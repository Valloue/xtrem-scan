namespace XtremScan.Scanning;

public sealed class OcrWordBox
{
    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }
}

public sealed class OcrPage
{
    public required IReadOnlyList<PdfTextRun> Runs { get; init; }

    public required IReadOnlyList<OcrWordBox> Boxes { get; init; }
}
