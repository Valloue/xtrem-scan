namespace XtremScan.Scanning;

/// <summary>
/// PDF d'une page dont l'image est le JPEG d'origine. Aucun décodage, la résolution reste celle du scan.
/// </summary>
public static class PdfJpegWriter
{
    public static void Write(string jpegPath, string pdfPath, int fallbackDpiX, int fallbackDpiY)
    {
        PdfDocumentWriter.Write(
            pdfPath,
            [
                new PdfPageSpec
                {
                    JpegPath = jpegPath,
                    FallbackDpiX = fallbackDpiX,
                    FallbackDpiY = fallbackDpiY,
                },
            ]);
    }
}
