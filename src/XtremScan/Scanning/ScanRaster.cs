namespace XtremScan.Scanning;

/// <summary>
/// Image lue par bandes. Les octets sont RVB ou gris, sans canal alpha.
/// </summary>
public sealed class ScanRaster
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int DpiX { get; init; }

    public required int DpiY { get; init; }

    public required int Components { get; init; }

    public required Func<int, int, byte[], Task> ReadRows { get; init; }
}
