using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using XtremScan.Scanning;

namespace XtremScan.Services;

/// <summary>
/// Produit PDF, PNG ou TIFF à partir du JPEG du scanner, sans changer la résolution.
/// </summary>
public static class ScanExporter
{
    private const long PngTiffPixelLimit = 48_000_000;

    public static async Task ExportAsync(string jpegPath, string destinationPath, string formatId, int fallbackDpiX, int fallbackDpiY)
    {
        AppLog.For(nameof(ScanExporter)).Information(
            "Export {Format} : {Source} → {Destination}",
            formatId,
            Path.GetFileName(jpegPath),
            Path.GetFileName(destinationPath));
        if (formatId == ScanFormats.Pdf)
        {
            PdfJpegWriter.Write(jpegPath, destinationPath, fallbackDpiX, fallbackDpiY);
            return;
        }

        if (formatId is not (ScanFormats.Png or ScanFormats.Tiff))
        {
            throw new InvalidOperationException("Ce format n'est pas disponible.");
        }

        var raster = await OpenRasterAsync(jpegPath, fallbackDpiX, fallbackDpiY);
        if (formatId == ScanFormats.Png)
        {
            await PngWriter.WriteAsync(raster, destinationPath);
            return;
        }

        await TiffWriter.WriteAsync(raster, destinationPath);
    }

    private static async Task<ScanRaster> OpenRasterAsync(string jpegPath, int fallbackDpiX, int fallbackDpiY)
    {
        var picture = JpegPicture.Read(jpegPath);
        if ((long)picture.Width * picture.Height > PngTiffPixelLimit)
        {
            throw new InvalidOperationException(
                "Cette page est trop grande pour un PNG ou un TIFF. Choisis JPEG ou PDF : la résolution du scan reste la même.");
        }

        await using var file = File.OpenRead(jpegPath);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var gray = picture.Components == 1;
        var provider = await decoder.GetPixelDataAsync(
            gray ? BitmapPixelFormat.Gray8 : BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var pixels = provider.DetachPixelData();
        var components = gray ? 1 : 3;
        var stride = picture.Width * (gray ? 1 : 4);
        return new ScanRaster
        {
            Width = picture.Width,
            Height = picture.Height,
            DpiX = picture.DpiX ?? (fallbackDpiX > 0 ? fallbackDpiX : 72),
            DpiY = picture.DpiY ?? (fallbackDpiY > 0 ? fallbackDpiY : 72),
            Components = components,
            ReadRows = (y, count, destination) =>
            {
                var rowBytes = picture.Width * components;
                for (var row = 0; row < count; row++)
                {
                    var source = pixels.AsSpan(((y + row) * stride), stride);
                    var target = destination.AsSpan(row * rowBytes, rowBytes);
                    if (gray)
                    {
                        source[..rowBytes].CopyTo(target);
                    }
                    else
                    {
                        for (var x = 0; x < picture.Width; x++)
                        {
                            target[x * 3] = source[x * 4 + 2];
                            target[x * 3 + 1] = source[x * 4 + 1];
                            target[x * 3 + 2] = source[x * 4];
                        }
                    }
                }

                return Task.CompletedTask;
            },
        };
    }
}
