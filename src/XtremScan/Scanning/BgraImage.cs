namespace XtremScan.Scanning;

public static class BgraImage
{
    /// <summary>Tourne dans le même cadre. Un grand angle rétrécit l'image pour qu'elle tienne dans la feuille.</summary>
    public static (byte[] Pixels, int Width, int Height) RotateClockwise(byte[] pixels, int width, int height, double degrees)
    {
        if (Math.Abs(degrees) < 0.05 || width < 1 || height < 1)
        {
            return (pixels, width, height);
        }

        var destination = new byte[width * height * 4];
        for (var index = 0; index < destination.Length; index += 4)
        {
            destination[index] = 255;
            destination[index + 1] = 255;
            destination[index + 2] = 255;
            destination[index + 3] = 255;
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var source = PageSkew.UnmapOnPage(x, y, width, height, degrees);
                var sx = (int)Math.Round(source.X);
                var sy = (int)Math.Round(source.Y);
                if (sx < 0 || sy < 0 || sx >= width || sy >= height)
                {
                    continue;
                }

                System.Buffer.BlockCopy(pixels, ((sy * width) + sx) * 4, destination, ((y * width) + x) * 4, 4);
            }
        }

        return (destination, width, height);
    }

    /// <summary>
    /// Même rotation, avec un mélange des pixels voisins. Réservé à la lecture du texte :
    /// le plus proche voisin casse les traits fins.
    /// </summary>
    public static (byte[] Pixels, int Width, int Height) RotateForReading(byte[] pixels, int width, int height, double degrees)
    {
        if (Math.Abs(degrees) < 0.05 || width < 1 || height < 1)
        {
            return (pixels, width, height);
        }

        var destination = new byte[width * height * 4];
        for (var index = 0; index < destination.Length; index += 4)
        {
            destination[index] = 255;
            destination[index + 1] = 255;
            destination[index + 2] = 255;
            destination[index + 3] = 255;
        }

        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var source = PageSkew.UnmapOnPage(x, y, width, height, degrees);
                var x0 = (int)Math.Floor(source.X);
                var y0 = (int)Math.Floor(source.Y);
                var fx = source.X - x0;
                var fy = source.Y - y0;
                var target = row + (x * 4);
                for (var channel = 0; channel < 3; channel++)
                {
                    var sample =
                        (Sample(x0, y0, channel) * (1 - fx) * (1 - fy))
                        + (Sample(x0 + 1, y0, channel) * fx * (1 - fy))
                        + (Sample(x0, y0 + 1, channel) * (1 - fx) * fy)
                        + (Sample(x0 + 1, y0 + 1, channel) * fx * fy);
                    destination[target + channel] = (byte)Math.Clamp((int)Math.Round(sample), 0, 255);
                }
            }
        }

        return (destination, width, height);

        int Sample(int sx, int sy, int channel)
        {
            if ((uint)sx >= (uint)width || (uint)sy >= (uint)height)
            {
                return 255;
            }

            return pixels[((sy * width) + sx) * 4 + channel];
        }
    }

    /// <summary>Même cadre que l'aperçu : rotation autour du centre, sans rétrécir.</summary>
    public static (byte[] Pixels, int Width, int Height) RotateInView(byte[] pixels, int width, int height, double degrees)
    {
        if (Math.Abs(degrees) < 0.05 || width < 1 || height < 1)
        {
            return (pixels, width, height);
        }

        var destination = new byte[width * height * 4];
        for (var index = 0; index < destination.Length; index += 4)
        {
            destination[index] = 255;
            destination[index + 1] = 255;
            destination[index + 2] = 255;
            destination[index + 3] = 255;
        }

        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var source = PageSkew.UnmapView(x, y, width, height, degrees);
                var x0 = (int)Math.Floor(source.X);
                var y0 = (int)Math.Floor(source.Y);
                var fx = source.X - x0;
                var fy = source.Y - y0;
                var target = row + (x * 4);
                for (var channel = 0; channel < 3; channel++)
                {
                    var sample =
                        (ViewSample(x0, y0, channel) * (1 - fx) * (1 - fy))
                        + (ViewSample(x0 + 1, y0, channel) * fx * (1 - fy))
                        + (ViewSample(x0, y0 + 1, channel) * (1 - fx) * fy)
                        + (ViewSample(x0 + 1, y0 + 1, channel) * fx * fy);
                    destination[target + channel] = (byte)Math.Clamp((int)Math.Round(sample), 0, 255);
                }
            }
        }

        return (destination, width, height);

        int ViewSample(int sx, int sy, int channel)
        {
            if ((uint)sx >= (uint)width || (uint)sy >= (uint)height)
            {
                return 255;
            }

            return pixels[((sy * width) + sx) * 4 + channel];
        }
    }

    /// <summary>
    /// Zoom et retournement limités à la sélection. Le reste de la feuille et sa taille ne changent pas.
    /// </summary>
    public static byte[] ApplyFrame(byte[] pixels, int width, int height, PageFrame frame, int turns, double zoom)
    {
        if (!frame.ChangesPixels(turns, zoom) || width < 1 || height < 1)
        {
            return pixels;
        }

        var left = Math.Clamp((int)Math.Floor(frame.X * width), 0, width - 1);
        var top = Math.Clamp((int)Math.Floor(frame.Y * height), 0, height - 1);
        var right = Math.Clamp((int)Math.Ceiling((frame.X + frame.Width) * width), left + 1, width);
        var bottom = Math.Clamp((int)Math.Ceiling((frame.Y + frame.Height) * height), top + 1, height);
        var destination = new byte[pixels.Length];
        System.Buffer.BlockCopy(pixels, 0, destination, 0, pixels.Length);
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var source = frame.SourcePoint(x, y, width, height, turns, zoom);
                var sx = (int)Math.Round(source.X);
                var sy = (int)Math.Round(source.Y);
                var offset = ((y * width) + x) * 4;
                if (sx < 0 || sy < 0 || sx >= width || sy >= height)
                {
                    destination[offset] = 255;
                    destination[offset + 1] = 255;
                    destination[offset + 2] = 255;
                    destination[offset + 3] = 255;
                    continue;
                }

                System.Buffer.BlockCopy(pixels, ((sy * width) + sx) * 4, destination, offset, 4);
            }
        }

        return destination;
    }
}
