namespace XtremScan.Scanning;

/// <summary>
/// Taille et mode d'un JPEG, lus dans les marqueurs, sans le décoder.
/// </summary>
public sealed class JpegPicture
{
    public int Width { get; init; }

    public int Height { get; init; }

    public int Components { get; init; }

    public int? DpiX { get; init; }

    public int? DpiY { get; init; }

    public static JpegPicture Read(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> signature = stackalloc byte[2];
        if (stream.Read(signature) < 2 || signature[0] != 0xFF || signature[1] != 0xD8)
        {
            throw new InvalidOperationException("Le fichier reçu n'est pas un JPEG.");
        }

        int? dpiX = null;
        int? dpiY = null;
        int width = 0;
        int height = 0;
        int components = 0;
        Span<byte> marker = stackalloc byte[4];
        while (stream.Position + 4 < stream.Length)
        {
            if (stream.Read(marker) < 4 || marker[0] != 0xFF)
            {
                break;
            }

            var segment = marker[1];
            if (segment == 0xD9 || segment == 0xDA)
            {
                break;
            }

            var length = (marker[2] << 8) | marker[3];
            if (length < 2)
            {
                break;
            }

            var payload = length - 2;
            if (segment == 0xE0 && payload >= 14)
            {
                var app0 = new byte[payload];
                stream.ReadExactly(app0);
                if (app0[0] == (byte)'J' && app0[1] == (byte)'F' && app0[2] == (byte)'I' && app0[3] == (byte)'F')
                {
                    var units = app0[7];
                    var xDensity = (app0[8] << 8) | app0[9];
                    var yDensity = (app0[10] << 8) | app0[11];
                    dpiX = DensityToDpi(units, xDensity);
                    dpiY = DensityToDpi(units, yDensity);
                }

                continue;
            }

            if (segment is 0xC0 or 0xC1 or 0xC2 && payload >= 8)
            {
                var frame = new byte[payload];
                stream.ReadExactly(frame);
                height = (frame[1] << 8) | frame[2];
                width = (frame[3] << 8) | frame[4];
                components = frame[5];
                continue;
            }

            stream.Position += payload;
        }

        if (width <= 0 || height <= 0 || components is not (1 or 3))
        {
            throw new InvalidOperationException("Impossible de lire la taille du JPEG.");
        }

        return new JpegPicture
        {
            Width = width,
            Height = height,
            Components = components,
            DpiX = dpiX,
            DpiY = dpiY,
        };
    }

    private static int? DensityToDpi(int units, int density)
    {
        if (density <= 0)
        {
            return null;
        }

        return units switch
        {
            1 => density,
            2 => (int)Math.Round(density * 2.54),
            _ => null,
        };
    }
}
