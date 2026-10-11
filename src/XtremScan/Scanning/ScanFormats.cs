namespace XtremScan.Scanning;

public static class ScanFormats
{
    public const string Jpeg = "image/jpeg";

    public const string Pdf = "application/pdf";

    public const string Png = "image/png";

    public const string Tiff = "image/tiff";

    public const string WiaJpeg = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}";

    public static ScanFormat JpegFormat { get; } = new() { Id = Jpeg, Label = "JPEG", Extension = ".jpg" };

    public static ScanFormat PdfFormat { get; } = new() { Id = Pdf, Label = "PDF", Extension = ".pdf" };

    public static ScanFormat PngFormat { get; } = new() { Id = Png, Label = "PNG", Extension = ".png" };

    public static ScanFormat TiffFormat { get; } = new() { Id = Tiff, Label = "TIFF", Extension = ".tif" };

    public static IReadOnlyList<ScanFormat> Brother { get; } = [JpegFormat, PdfFormat, PngFormat, TiffFormat];

    public static bool IsDerived(string? formatId)
    {
        return formatId is Pdf or Png or Tiff;
    }
}
