namespace XtremScan.Scanning;

/// <summary>
/// Sélection sur la feuille déjà au format, pas sur la photo du plateau.
/// Le zoom et le retournement restent dans ce rectangle : le format de la page ne change pas.
/// </summary>
public readonly record struct PageFrame(double X, double Y, double Width, double Height)
{
    public static PageFrame Full { get; } = new(0, 0, 1, 1);

    public bool IsFull => X <= 0.001 && Y <= 0.001 && Width >= 0.999 && Height >= 0.999;

    public (double X, double Y) Center => (X + (Width / 2d), Y + (Height / 2d));

    public static PageFrame Normalize(double x, double y, double width, double height)
    {
        if (width < 0)
        {
            x += width;
            width = -width;
        }

        if (height < 0)
        {
            y += height;
            height = -height;
        }

        width = Math.Max(0.04, width);
        height = Math.Max(0.04, height);
        // Le cadre peut sortir de la feuille, comme un recadrage. Il ne peut pas partir entièrement.
        x = Math.Clamp(x, -width + 0.02, 1.98);
        y = Math.Clamp(y, -height + 0.02, 1.98);
        return new PageFrame(x, y, width, height);
    }

    public static double NormalizeZoom(double zoom) => zoom < 0.05 ? 1 : Math.Clamp(zoom, 0.25, 4);

    /// <summary>
    /// Un quart de tour dans un rectangle qui n'est pas carré rétrécit pour tenir. Le zoom manuel vient ensuite.
    /// </summary>
    public double ContentScale(double pageWidth, double pageHeight, int turns, double zoom)
    {
        var scale = NormalizeZoom(zoom);
        if ((turns & 1) == 0)
        {
            return scale;
        }

        var selWidth = Math.Max(1, Width * pageWidth);
        var selHeight = Math.Max(1, Height * pageHeight);
        return scale * Math.Min(selWidth / selHeight, selHeight / selWidth);
    }

    public bool ChangesPixels(int turns, double zoom) =>
        (turns & 3) != 0 || Math.Abs(NormalizeZoom(zoom) - 1d) > 0.01;

    /// <summary>Point de la feuille d'où vient un pixel de la sélection, après zoom et retournement.</summary>
    public (double X, double Y) SourcePoint(double x, double y, double pageWidth, double pageHeight, int turns, double zoom)
    {
        var scale = ContentScale(pageWidth, pageHeight, turns, zoom);
        var centerX = Center.X * pageWidth;
        var centerY = Center.Y * pageHeight;
        var rad = -(turns & 3) * Math.PI / 2d;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var dx = x - centerX;
        var dy = y - centerY;
        var unrotatedX = centerX + (dx * cos) - (dy * sin);
        var unrotatedY = centerY + (dx * sin) + (dy * cos);
        return (centerX + ((unrotatedX - centerX) / scale), centerY + ((unrotatedY - centerY) / scale));
    }

    /// <summary>Rectangle de découpe PDF, origine en bas à gauche.</summary>
    public (double X, double Y, double Width, double Height) PdfRect(double pageWidth, double pageHeight)
    {
        return (X * pageWidth, (1d - Y - Height) * pageHeight, Width * pageWidth, Height * pageHeight);
    }

    /// <summary>Matrice qui tourne et zoome autour du centre de la sélection, en coordonnées PDF.</summary>
    public (double A, double B, double C, double D, double E, double F) PdfSpin(
        double pageWidth,
        double pageHeight,
        int turns,
        double zoom,
        double anchorX,
        double anchorY)
    {
        var scale = ContentScale(pageWidth, pageHeight, turns, zoom);
        var centerX = anchorX * pageWidth;
        var centerY = (1d - anchorY) * pageHeight;
        var rad = -(turns & 3) * Math.PI / 2d;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var a = scale * cos;
        var b = scale * sin;
        var c = -scale * sin;
        var d = scale * cos;
        var e = centerX - (a * centerX) - (c * centerY);
        var f = centerY - (b * centerX) - (d * centerY);
        return (a, b, c, d, e, f);
    }
}
