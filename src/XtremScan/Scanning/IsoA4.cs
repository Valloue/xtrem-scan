namespace XtremScan.Scanning;

/// <summary>
/// Feuille A4 ISO, 210 × 297 mm. Les pixels suivent le dpi du scan, pas une liste figée.
/// </summary>
public static class IsoA4
{
    public const double WidthMillimetres = 210;

    public const double HeightMillimetres = 297;

    public static (double Width, double Height) Points(bool landscape)
    {
        var width = WidthMillimetres / 25.4d * 72d;
        var height = HeightMillimetres / 25.4d * 72d;
        return landscape ? (height, width) : (width, height);
    }

    public static (int Width, int Height) Pixels(int dpiX, int dpiY)
    {
        var width = Math.Max(1, (int)Math.Round(WidthMillimetres / 25.4d * Math.Max(1, dpiX)));
        var height = Math.Max(1, (int)Math.Round(HeightMillimetres / 25.4d * Math.Max(1, dpiY)));
        return (width, height);
    }

    public static (int Width, int Height) SheetPixels(bool landscape, int maxEdge)
    {
        var scale = Math.Max(1, maxEdge) / HeightMillimetres;
        var width = WidthMillimetres * scale;
        var height = HeightMillimetres * scale;
        return landscape
            ? (Math.Max(1, (int)Math.Round(height)), Math.Max(1, (int)Math.Round(width)))
            : (Math.Max(1, (int)Math.Round(width)), Math.Max(1, (int)Math.Round(height)));
    }

    /// <summary>
    /// Taille d'affichage de l'image avant quart de tour, dans le même échelle que <see cref="SheetPixels"/>.
    /// Le dpi vient du fichier : un axe à 2400 n'étire pas l'autre.
    /// </summary>
    public static (double Width, double Height) ImageDisplay(int pixelWidth, int pixelHeight, int dpiX, int dpiY, int maxEdge)
    {
        var sheet = SheetPixels(false, maxEdge);
        if (dpiX <= 0 || dpiY <= 0)
        {
            var scale = Math.Min(sheet.Width / (double)Math.Max(1, pixelWidth), sheet.Height / (double)Math.Max(1, pixelHeight));
            return (pixelWidth * scale, pixelHeight * scale);
        }

        var millimetreX = pixelWidth / (double)dpiX * 25.4d;
        var millimetreY = pixelHeight / (double)dpiY * 25.4d;
        return (sheet.Width * millimetreX / WidthMillimetres, sheet.Height * millimetreY / HeightMillimetres);
    }
}
