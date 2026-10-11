namespace XtremScan.Scanning;

public enum ScanSourceKind
{
    Flatbed,
    Feeder,
    Duplex,
}

public sealed class ScannerDevice
{
    public required string Id { get; init; }

    public required string Name { get; init; }
}

public sealed class ResolutionAxis
{
    public int Minimum { get; init; }

    public int Maximum { get; init; }

    public int Step { get; init; } = 1;

    /// <summary>Valeurs discrètes. Null si la plage est trop fine pour une liste : le pas reste valable.</summary>
    public IReadOnlyList<int>? Values { get; init; }

    public bool Contains(int dpi)
    {
        if (Values is not null)
        {
            return Values.Contains(dpi);
        }

        if (dpi < Minimum || dpi > Maximum)
        {
            return false;
        }

        var step = Step < 1 ? 1 : Step;
        return (dpi - Minimum) % step == 0;
    }

    public int Snap(int dpi)
    {
        if (Values is { Count: > 0 })
        {
            return Values.OrderBy(value => Math.Abs(value - dpi)).First();
        }

        return ResolutionMath.SnapToStep(dpi, Minimum, Maximum, Step);
    }
}

public sealed class NamedValue
{
    public required int Value { get; init; }

    public required string Label { get; init; }
}

public sealed class ScanFormat
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public required string Extension { get; init; }
}

public sealed class ScanSourceCapabilities
{
    public required ScanSourceKind Kind { get; init; }

    public required string Label { get; init; }

    public required ResolutionAxis Horizontal { get; init; }

    public required ResolutionAxis Vertical { get; init; }

    public int? OpticalX { get; init; }

    public int? OpticalY { get; init; }

    /// <summary>Plafond du mode gris quand il est plus bas que la couleur. Null : mêmes axes.</summary>
    public ResolutionAxis? GrayscaleHorizontal { get; init; }

    public ResolutionAxis? GrayscaleVertical { get; init; }

    public required IReadOnlyList<NamedValue> ColorModes { get; init; }

    public required IReadOnlyList<NamedValue> BitDepths { get; init; }

    public required IReadOnlyList<ScanFormat> Formats { get; init; }
}

public sealed class ScanRequest
{
    public required string DeviceId { get; init; }

    public required ScanSourceKind Source { get; init; }

    public required int DpiX { get; init; }

    public required int DpiY { get; init; }

    public int? ColorMode { get; init; }

    public int? BitDepth { get; init; }

    public string? FormatId { get; init; }

    public required string OutputDirectory { get; init; }

    public bool Preview { get; init; }

    public IProgress<int>? Progress { get; init; }
}

public sealed class ScanResult
{
    public required string FilePath { get; init; }

    public int RequestedDpiX { get; init; }

    public int RequestedDpiY { get; init; }

    public int? FileDpi { get; init; }

    /// <summary>Image affichable quand le fichier enregistré est un PDF.</summary>
    public string? DisplayPath { get; init; }
}
