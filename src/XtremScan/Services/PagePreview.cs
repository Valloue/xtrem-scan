using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using XtremScan.Scanning;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace XtremScan.Services;

/// <summary>
/// Aperçu orienté, assez petit pour l'écran. Un quart de tour passe la feuille en paysage. Le JPEG enregistré n'est pas tourné.
/// </summary>
public static class PagePreview
{
    public const int MaxEdge = 1400;

    public static (int Width, int Height) DisplaySize(int pixelWidth, int pixelHeight, int quarterTurns, double skewDegrees = 0)
    {
        _ = skewDegrees;
        var swapped = (quarterTurns & 1) == 1;
        var width = swapped ? pixelHeight : pixelWidth;
        var height = swapped ? pixelWidth : pixelHeight;
        var scale = Math.Min(1d, MaxEdge / (double)Math.Max(1, Math.Max(pixelWidth, pixelHeight)));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    public static async Task<ImageSource> LoadAsync(string path, int pixelWidth, int pixelHeight, int quarterTurns, double skewDegrees = 0)
    {
        var decoded = await DecodeAsync(path, pixelWidth, pixelHeight);
        var oriented = Orient(decoded.Pixels, decoded.Width, decoded.Height, quarterTurns, skewDegrees);
        return await ToImageAsync(oriented.Pixels, oriented.Width, oriented.Height);
    }

    public static async Task<(byte[] Pixels, int Width, int Height)> DecodeAsync(string path, int pixelWidth, int pixelHeight)
    {
        var scale = Math.Min(1d, MaxEdge / (double)Math.Max(1, Math.Max(pixelWidth, pixelHeight)));
        var decodeWidth = Math.Max(1, (int)Math.Round(pixelWidth * scale));
        var decodeHeight = Math.Max(1, (int)Math.Round(pixelHeight * scale));
        await using var file = File.OpenRead(path);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var provider = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform
            {
                ScaledWidth = (uint)decodeWidth,
                ScaledHeight = (uint)decodeHeight,
                InterpolationMode = BitmapInterpolationMode.Fant,
            },
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.ColorManageToSRgb);
        return (provider.DetachPixelData(), decodeWidth, decodeHeight);
    }

    public static (byte[] Pixels, int Width, int Height) Orient(byte[] pixels, int width, int height, int quarterTurns, double skewDegrees)
    {
        var turned = RotateQuarter(pixels, width, height, quarterTurns);
        if (Math.Abs(skewDegrees) < 0.05)
        {
            return turned;
        }

        return BgraImage.RotateClockwise(turned.Pixels, turned.Width, turned.Height, skewDegrees);
    }

    private static (byte[] Pixels, int Width, int Height) RotateQuarter(byte[] pixels, int width, int height, int quarterTurns)
    {
        var turns = quarterTurns & 3;
        if (turns == 0)
        {
            return (pixels, width, height);
        }

        var destinationWidth = turns % 2 == 0 ? width : height;
        var destinationHeight = turns % 2 == 0 ? height : width;
        var destination = new byte[destinationWidth * destinationHeight * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (dx, dy) = turns switch
                {
                    1 => (height - 1 - y, x),
                    2 => (width - 1 - x, height - 1 - y),
                    _ => (y, width - 1 - x),
                };
                System.Buffer.BlockCopy(pixels, ((y * width) + x) * 4, destination, ((dy * destinationWidth) + dx) * 4, 4);
            }
        }

        return (destination, destinationWidth, destinationHeight);
    }

    public static async Task<ImageSource> ToImageAsync(byte[] pixels, int width, int height)
    {
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            pixels.AsBuffer(),
            BitmapPixelFormat.Bgra8,
            width,
            height,
            BitmapAlphaMode.Premultiplied);
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(bitmap);
        return source;
    }
}
