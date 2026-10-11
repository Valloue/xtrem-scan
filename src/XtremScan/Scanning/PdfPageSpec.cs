namespace XtremScan.Scanning;

public sealed class PdfTextRun
{
    public required string Text { get; init; }

    public double X { get; init; }

    public double Baseline { get; init; }

    public double Size { get; init; }
}

public sealed class PdfPageSpec
{
    public required string JpegPath { get; init; }

    public int CropX { get; init; }

    public int CropY { get; init; }

    public int CropWidth { get; init; }

    public int CropHeight { get; init; }

    public int QuarterTurns { get; init; }

    public double SkewDegrees { get; init; }

    public double FrameX { get; init; }

    public double FrameY { get; init; }

    public double FrameWidth { get; init; }

    public double FrameHeight { get; init; }

    public double Zoom { get; init; }

    public int FrameTurns { get; init; }

    public double AnchorX { get; init; } = double.NaN;

    public double AnchorY { get; init; } = double.NaN;

    public double ImageOffsetX { get; init; }

    public double ImageOffsetY { get; init; }

    public int FallbackDpiX { get; init; }

    public int FallbackDpiY { get; init; }

    public IReadOnlyList<PdfTextRun> Text { get; init; } = [];
}

public readonly record struct PdfImagePlacement(
    double PageWidth,
    double PageHeight,
    double A,
    double B,
    double C,
    double D,
    double E,
    double F);
