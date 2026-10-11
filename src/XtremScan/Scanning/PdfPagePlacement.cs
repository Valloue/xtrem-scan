namespace XtremScan.Scanning;

/// <summary>
/// Place le JPEG sur une page A4. Les octets de l'image ne changent pas.
/// La page fait toujours 210 × 297 mm. L'image garde la taille donnée par son dpi, au centre.
/// </summary>
public static class PdfPagePlacement
{
    public readonly record struct ContentBox(double X, double Y, double Width, double Height);

    public static PdfImagePlacement Place(JpegPicture picture, PdfPageSpec page)
    {
        var cropW = page.CropWidth > 0 ? page.CropWidth : picture.Width;
        var cropH = page.CropHeight > 0 ? page.CropHeight : picture.Height;
        var cropX = Math.Clamp(page.CropX, 0, Math.Max(0, picture.Width - 1));
        var cropY = Math.Clamp(page.CropY, 0, Math.Max(0, picture.Height - 1));
        cropW = Math.Clamp(cropW, 1, picture.Width - cropX);
        cropH = Math.Clamp(cropH, 1, picture.Height - cropY);
        var turns = page.QuarterTurns & 3;
        var (pageWidth, pageHeight) = IsoA4.Points((turns & 1) == 1);
        var box = ContentBounds(picture, page);
        var pagePixelWidth = turns % 2 == 0 ? cropW : cropH;
        var pagePixelHeight = turns % 2 == 0 ? cropH : cropW;
        var pageScaleX = box.Width / pagePixelWidth;
        var pageScaleY = box.Height / pagePixelHeight;

        var origin = Map(0, 0);
        var right = Map(1, 0);
        var up = Map(0, 1);
        var placed = new PdfImagePlacement(
            pageWidth,
            pageHeight,
            right.X - origin.X,
            right.Y - origin.Y,
            up.X - origin.X,
            up.Y - origin.Y,
            origin.X,
            origin.Y);
        // L'aperçu tourne dans le sens horaire. En PDF l'axe Y monte, donc le même angle change de signe.
        return Math.Abs(page.SkewDegrees) < 0.05 ? placed : Fit(placed, -page.SkewDegrees);

        (double X, double Y) Map(double unitX, double unitY)
        {
            var pixelX = unitX * picture.Width;
            var pixelY = (1 - unitY) * picture.Height;
            var localX = pixelX - cropX;
            var localY = pixelY - cropY;
            var (pageX, pageY) = turns switch
            {
                1 => (cropH - localY, localX),
                2 => (cropW - localX, cropH - localY),
                3 => (localY, cropW - localX),
                _ => (localX, localY),
            };
            return (box.X + (pageX * pageScaleX), box.Y + ((pagePixelHeight - pageY) * pageScaleY));
        }
    }

    /// <summary>Rectangle de l'image sur la page A4, avant l'inclinaison. Origine en bas à gauche.</summary>
    public static ContentBox ContentBounds(JpegPicture picture, PdfPageSpec page)
    {
        var cropW = page.CropWidth > 0 ? page.CropWidth : picture.Width;
        var cropH = page.CropHeight > 0 ? page.CropHeight : picture.Height;
        var cropX = Math.Clamp(page.CropX, 0, Math.Max(0, picture.Width - 1));
        var cropY = Math.Clamp(page.CropY, 0, Math.Max(0, picture.Height - 1));
        cropW = Math.Clamp(cropW, 1, picture.Width - cropX);
        cropH = Math.Clamp(cropH, 1, picture.Height - cropY);
        var dpiX = picture.DpiX ?? page.FallbackDpiX;
        var dpiY = picture.DpiY ?? page.FallbackDpiY;
        if (dpiX <= 0)
        {
            dpiX = dpiY;
        }

        if (dpiY <= 0)
        {
            dpiY = dpiX;
        }
        var turns = page.QuarterTurns & 3;
        var (pageWidth, pageHeight) = IsoA4.Points((turns & 1) == 1);
        double imageWidth;
        double imageHeight;
        if (dpiX > 0 && dpiY > 0)
        {
            var scaleX = 72d / dpiX;
            var scaleY = 72d / dpiY;
            var pageScaleX = turns % 2 == 0 ? scaleX : scaleY;
            var pageScaleY = turns % 2 == 0 ? scaleY : scaleX;
            imageWidth = (turns % 2 == 0 ? cropW : cropH) * pageScaleX;
            imageHeight = (turns % 2 == 0 ? cropH : cropW) * pageScaleY;
        }
        else
        {
            var pixelWidth = turns % 2 == 0 ? cropW : cropH;
            var pixelHeight = turns % 2 == 0 ? cropH : cropW;
            var scale = Math.Min(pageWidth / pixelWidth, pageHeight / pixelHeight);
            imageWidth = pixelWidth * scale;
            imageHeight = pixelHeight * scale;
        }

        return new ContentBox(
            (pageWidth - imageWidth) / 2d,
            (pageHeight - imageHeight) / 2d,
            imageWidth,
            imageHeight);
    }

    /// <summary>
    /// Tourne l'image dans la feuille déjà au format du quart de tour, sans la rétrécir. À l'échelle 1 elle a la largeur de la page.
    /// </summary>
    private static PdfImagePlacement Fit(PdfImagePlacement raw, double degreesClockwise)
    {
        var rad = degreesClockwise * Math.PI / 180d;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var scale = 1d;
        var cx = raw.PageWidth / 2d;
        var cy = raw.PageHeight / 2d;

        (double X, double Y) Turn(double x, double y)
        {
            var dx = (x - cx) * scale;
            var dy = (y - cy) * scale;
            return (cx + (dx * cos) - (dy * sin), cy + (dx * sin) + (dy * cos));
        }

        var origin = Turn(raw.E, raw.F);
        var right = Turn(raw.A + raw.E, raw.B + raw.F);
        var up = Turn(raw.C + raw.E, raw.D + raw.F);
        return new PdfImagePlacement(
            raw.PageWidth,
            raw.PageHeight,
            right.X - origin.X,
            right.Y - origin.Y,
            up.X - origin.X,
            up.Y - origin.Y,
            origin.X,
            origin.Y);
    }
}
