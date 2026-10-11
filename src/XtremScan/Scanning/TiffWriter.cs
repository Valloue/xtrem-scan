using System.IO.Compression;

namespace XtremScan.Scanning;

/// <summary>
/// TIFF 8 bits, bandes compressées. Le fichier final n'est pas réduit.
/// </summary>
public static class TiffWriter
{
    private const int BandRows = 32;

    public static async Task WriteAsync(ScanRaster raster, string path)
    {
        var strips = (raster.Height + BandRows - 1) / BandRows;
        var offsets = new uint[strips];
        var counts = new uint[strips];
        await using var output = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        output.Write(new byte[] { (byte)'I', (byte)'I', 42, 0, 0, 0, 0, 0 });
        var rowBytes = raster.Width * raster.Components;
        var pixels = new byte[rowBytes * BandRows];
        for (var strip = 0; strip < strips; strip++)
        {
            var y = strip * BandRows;
            var rows = Math.Min(BandRows, raster.Height - y);
            await raster.ReadRows(y, rows, pixels);
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(pixels, 0, rowBytes * rows);
            }

            offsets[strip] = (uint)output.Position;
            var data = compressed.ToArray();
            counts[strip] = (uint)data.Length;
            output.Write(data);
        }

        var ifd = (uint)output.Position;
        WriteIfd(output, raster, offsets, counts);
        output.Position = 4;
        output.Write(BitConverter.GetBytes(ifd));
    }

    private static void WriteIfd(Stream output, ScanRaster raster, uint[] offsets, uint[] counts)
    {
        var strips = offsets.Length;
        var bitsPerSample = raster.Components == 1 ? (ushort)8 : (ushort)0;
        var entries = new List<(ushort Tag, ushort Type, uint Count, uint Value)>
        {
            (256, 4, 1, (uint)raster.Width),
            (257, 4, 1, (uint)raster.Height),
            (258, 3, (uint)raster.Components, bitsPerSample),
            (259, 3, 1, 8),
            (262, 3, 1, raster.Components == 1 ? 1u : 2u),
            (273, 4, (uint)strips, 0),
            (277, 3, 1, (uint)raster.Components),
            (278, 4, 1, (uint)Math.Min(BandRows, raster.Height)),
            (279, 4, (uint)strips, 0),
            (282, 5, 1, 0),
            (283, 5, 1, 0),
            (284, 3, 1, 1),
            (296, 3, 1, 2),
        };

        var dataStart = (uint)output.Position + 2 + (uint)(entries.Count * 12) + 4;
        var cursor = dataStart;
        uint bitsOffset = 0;
        if (raster.Components != 1)
        {
            bitsOffset = cursor;
            entries[2] = (258, 3, (uint)raster.Components, bitsOffset);
            cursor += (uint)(raster.Components * 2);
        }

        var stripOffset = cursor;
        entries[5] = (273, 4, (uint)strips, strips == 1 ? offsets[0] : stripOffset);
        cursor += strips == 1 ? 0 : (uint)(strips * 4);
        var countOffset = cursor;
        entries[8] = (279, 4, (uint)strips, strips == 1 ? counts[0] : countOffset);
        cursor += strips == 1 ? 0 : (uint)(strips * 4);
        var xResolution = cursor;
        entries[9] = (282, 5, 1, xResolution);
        cursor += 8;
        var yResolution = cursor;
        entries[10] = (283, 5, 1, yResolution);

        Span<byte> countBytes = stackalloc byte[2];
        BitConverter.TryWriteBytes(countBytes, (ushort)entries.Count);
        output.Write(countBytes);
        Span<byte> record = stackalloc byte[12];
        foreach (var entry in entries)
        {
            record.Clear();
            BitConverter.TryWriteBytes(record, entry.Tag);
            BitConverter.TryWriteBytes(record[2..], entry.Type);
            BitConverter.TryWriteBytes(record[4..], entry.Count);
            BitConverter.TryWriteBytes(record[8..], entry.Value);
            output.Write(record);
        }

        output.Write(new byte[4]);
        if (raster.Components != 1)
        {
            for (var channel = 0; channel < raster.Components; channel++)
            {
                output.Write(BitConverter.GetBytes((ushort)8));
            }
        }

        if (strips != 1)
        {
            foreach (var offset in offsets)
            {
                output.Write(BitConverter.GetBytes(offset));
            }

            foreach (var count in counts)
            {
                output.Write(BitConverter.GetBytes(count));
            }
        }

        output.Write(BitConverter.GetBytes((uint)Math.Max(1, raster.DpiX)));
        output.Write(BitConverter.GetBytes(1u));
        output.Write(BitConverter.GetBytes((uint)Math.Max(1, raster.DpiY)));
        output.Write(BitConverter.GetBytes(1u));
    }
}
