using System.Buffers.Binary;
using System.IO.Compression;

namespace XtremScan.Scanning;

/// <summary>
/// PNG écrit bande par bande. Le fichier final n'est pas réduit.
/// </summary>
public static class PngWriter
{
    private const int BandRows = 16;

    public static async Task WriteAsync(ScanRaster raster, string path)
    {
        var compressed = path + ".idat";
        try
        {
            await using (var raw = new FileStream(compressed, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await using var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true);
                var rowBytes = raster.Width * raster.Components;
                var band = new byte[(rowBytes + 1) * BandRows];
                var pixels = new byte[rowBytes * BandRows];
                for (var y = 0; y < raster.Height; y += BandRows)
                {
                    var rows = Math.Min(BandRows, raster.Height - y);
                    await raster.ReadRows(y, rows, pixels);
                    var used = 0;
                    for (var row = 0; row < rows; row++)
                    {
                        band[used++] = 0;
                        pixels.AsSpan(row * rowBytes, rowBytes).CopyTo(band.AsSpan(used));
                        used += rowBytes;
                    }

                    zlib.Write(band, 0, used);
                }
            }

            await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            WriteChunk(output, "IHDR", Header(raster));
            WriteChunk(output, "pHYs", Physical(raster));
            await using (var idat = File.OpenRead(compressed))
            {
                var buffer = new byte[1024 * 1024];
                int read;
                while ((read = idat.Read(buffer)) > 0)
                {
                    WriteChunk(output, "IDAT", buffer.AsSpan(0, read));
                }
            }

            WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        }
        finally
        {
            TryDelete(compressed);
        }
    }

    private static byte[] Header(ScanRaster raster)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), raster.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), raster.Height);
        header[8] = 8;
        header[9] = (byte)(raster.Components == 1 ? 0 : 2);
        return header;
    }

    private static byte[] Physical(ScanRaster raster)
    {
        var chunk = new byte[9];
        BinaryPrimitives.WriteInt32BigEndian(chunk.AsSpan(0), PixelsPerMeter(raster.DpiX));
        BinaryPrimitives.WriteInt32BigEndian(chunk.AsSpan(4), PixelsPerMeter(raster.DpiY));
        chunk[8] = 1;
        return chunk;
    }

    private static int PixelsPerMeter(int dpi)
    {
        var safe = dpi > 0 ? dpi : 72;
        return (int)Math.Round(safe / 0.0254);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32.Compute(typeBytes, data));
        output.Write(crc);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
