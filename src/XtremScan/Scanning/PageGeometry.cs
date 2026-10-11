namespace XtremScan.Scanning;

/// <summary>
/// Recadrage et quarts de tour, en pixels de l'image d'origine. Le fichier n'est pas rééchantillonné.
/// </summary>
public static class PageGeometry
{
    public static (int X, int Y, int Width, int Height) Normalize(int x1, int y1, int x2, int y2, int imageWidth, int imageHeight)
    {
        var left = Math.Clamp(Math.Min(x1, x2), 0, Math.Max(0, imageWidth - 1));
        var top = Math.Clamp(Math.Min(y1, y2), 0, Math.Max(0, imageHeight - 1));
        var right = Math.Clamp(Math.Max(x1, x2), left + 1, imageWidth);
        var bottom = Math.Clamp(Math.Max(y1, y2), top + 1, imageHeight);
        return (left, top, right - left, bottom - top);
    }

    public static (int X, int Y) Move(int cropX, int cropY, int cropWidth, int cropHeight, int dx, int dy, int imageWidth, int imageHeight)
    {
        var x = Math.Clamp(cropX + dx, 0, Math.Max(0, imageWidth - cropWidth));
        var y = Math.Clamp(cropY + dy, 0, Math.Max(0, imageHeight - cropHeight));
        return (x, y);
    }

    public static (double X, double Y) SourceFromRotated(double viewX, double viewY, int imageWidth, int imageHeight, int quarterTurns)
    {
        var turns = quarterTurns & 3;
        return turns switch
        {
            1 => (viewY, imageHeight - viewX),
            2 => (imageWidth - viewX, imageHeight - viewY),
            3 => (imageWidth - viewY, viewX),
            _ => (viewX, viewY),
        };
    }

    public static (double X, double Y, double Width, double Height) RotatedBounds(
        int cropX,
        int cropY,
        int cropWidth,
        int cropHeight,
        int imageWidth,
        int imageHeight,
        int quarterTurns)
    {
        var corners = new[]
        {
            RotatedPoint(cropX, cropY, imageWidth, imageHeight, quarterTurns),
            RotatedPoint(cropX + cropWidth, cropY, imageWidth, imageHeight, quarterTurns),
            RotatedPoint(cropX, cropY + cropHeight, imageWidth, imageHeight, quarterTurns),
            RotatedPoint(cropX + cropWidth, cropY + cropHeight, imageWidth, imageHeight, quarterTurns),
        };
        var minX = corners.Min(point => point.X);
        var minY = corners.Min(point => point.Y);
        var maxX = corners.Max(point => point.X);
        var maxY = corners.Max(point => point.Y);
        return (minX, minY, maxX - minX, maxY - minY);
    }

    private static (double X, double Y) RotatedPoint(double sourceX, double sourceY, int imageWidth, int imageHeight, int quarterTurns)
    {
        var turns = quarterTurns & 3;
        return turns switch
        {
            1 => (imageHeight - sourceY, sourceX),
            2 => (imageWidth - sourceX, imageHeight - sourceY),
            3 => (sourceY, imageWidth - sourceX),
            _ => (sourceX, sourceY),
        };
    }
}
