using System.Globalization;
using System.Text;

namespace XtremScan.Scanning;

/// <summary>
/// Bail renvoyé par le scanner Brother sur le port 54921.
/// Les dpi sont ceux que l'appareil accepte d'exécuter, pas ceux demandés.
/// </summary>
public sealed class BrotherLease
{
    public int DpiX { get; init; }

    public int DpiY { get; init; }

    public int WidthMm { get; init; }

    public int HeightMm { get; init; }

    public int WidthPx { get; init; }

    public int HeightPx { get; init; }
}

/// <summary>
/// Plafonds lus sur une connexion : plancher, gris, puis couleur.
/// La couleur monte plus haut en horizontal (1200 contre 600 sur le DCP-J1050DW).
/// </summary>
public sealed class BrotherProfile
{
    public required string Host { get; init; }

    public required int MinX { get; init; }

    public required int MaxX { get; init; }

    public required int MinY { get; init; }

    public required int MaxY { get; init; }

    public required int GrayMaxX { get; init; }

    public required int GrayMaxY { get; init; }

    public ScanSourceCapabilities ToSource()
    {
        return new ScanSourceCapabilities
        {
            Kind = ScanSourceKind.Flatbed,
            Label = "Vitre",
            Horizontal = Axis(MinX, MaxX),
            Vertical = Axis(MinY, MaxY),
            OpticalX = MaxX,
            OpticalY = MaxY,
            GrayscaleHorizontal = Axis(MinX, GrayMaxX),
            GrayscaleVertical = Axis(MinY, GrayMaxY),
            ColorModes =
            [
                new NamedValue { Value = 3, Label = "Couleur" },
                new NamedValue { Value = 2, Label = "Niveaux de gris" },
            ],
            BitDepths =
            [
                new NamedValue { Value = 24, Label = "24 bits" },
                new NamedValue { Value = 8, Label = "8 bits" },
            ],
            Formats = ScanFormats.Brother,
        };
    }

    private static ResolutionAxis Axis(int minimum, int maximum)
    {
        IReadOnlyList<int> values = BrotherScanProtocol.StepsBetween(minimum, maximum);
        return new ResolutionAxis
        {
            Minimum = values[0],
            Maximum = values[^1],
            Step = 1,
            Values = values,
        };
    }
}

/// <summary>
/// Trame texte du scan réseau Brother. Pas de DLL constructeur.
/// </summary>
public static class BrotherScanProtocol
{
    public const int Port = 54921;

    /// <summary>Paliers observés sur ce protocole. Le bail en retire ceux que l'appareil refuse.</summary>
    public static readonly int[] Steps = [100, 200, 300, 600, 1200, 2400];

    public const int CeilingProbeDpi = 2400;

    public const int FloorProbeDpi = 100;

    public static byte[] CreateCommand(string command, params (string Key, string Value)[] parameters)
    {
        var builder = new StringBuilder();
        builder.Append(command);
        builder.Append('\n');
        foreach (var (key, value) in parameters)
        {
            builder.Append(key);
            builder.Append('=');
            builder.Append(value);
            builder.Append('\n');
        }

        var text = Encoding.ASCII.GetBytes(builder.ToString());
        var packet = new byte[text.Length + 2];
        packet[0] = 0x1B;
        text.CopyTo(packet.AsSpan(1));
        packet[^1] = 0x80;
        return packet;
    }

    public static bool TryParseLease(ReadOnlySpan<byte> packet, out BrotherLease? lease)
    {
        lease = null;
        var start = 0;
        while (start < packet.Length && !IsDigit(packet[start]))
        {
            start++;
        }

        if (start >= packet.Length)
        {
            return false;
        }

        var end = start;
        while (end < packet.Length && packet[end] != 0 && packet[end] != (byte)'\n' && packet[end] != (byte)'\r')
        {
            end++;
        }

        var text = Encoding.ASCII.GetString(packet[start..end]).Trim().TrimEnd(',');
        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 7)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dpiX)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dpiY)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var widthMm)
            || !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var widthPx)
            || !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var heightMm)
            || !int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var heightPx))
        {
            return false;
        }

        if (dpiX <= 0 || dpiY <= 0 || widthPx <= 0 || heightPx <= 0)
        {
            return false;
        }

        lease = new BrotherLease
        {
            DpiX = dpiX,
            DpiY = dpiY,
            WidthMm = widthMm,
            HeightMm = heightMm,
            WidthPx = widthPx,
            HeightPx = heightPx,
        };
        return true;
    }

    public static IReadOnlyList<int> StepsBetween(int minimum, int maximum)
    {
        if (minimum <= 0 || maximum <= 0)
        {
            return [];
        }

        if (minimum > maximum)
        {
            (minimum, maximum) = (maximum, minimum);
        }

        var values = new List<int>();
        foreach (var step in Steps)
        {
            if (step >= minimum && step <= maximum)
            {
                values.Add(step);
            }
        }

        if (values.Count == 0 || values[0] != minimum)
        {
            values.Insert(0, minimum);
        }

        if (values[^1] != maximum)
        {
            values.Add(maximum);
        }

        return values;
    }

    public static string ColorMode(int? wiaDataType)
    {
        return wiaDataType == 2 ? "GRAY64" : "CGRAY";
    }

    private static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';
}
