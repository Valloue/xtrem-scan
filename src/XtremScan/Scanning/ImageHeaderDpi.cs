namespace XtremScan.Scanning;

/// <summary>
/// Relit la résolution annoncée dans l'en-tête du fichier, sans le décoder.
/// </summary>
public static class ImageHeaderDpi
{
    public static int? TryRead(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> signature = stackalloc byte[8];
            if (stream.Read(signature) < 2)
            {
                return null;
            }

            stream.Position = 0;
            if (signature[0] == (byte)'B' && signature[1] == (byte)'M')
            {
                return ReadBmp(stream);
            }

            if (signature[0] == 0x89 && signature[1] == (byte)'P')
            {
                return ReadPng(stream);
            }

            if (signature[0] == 0xFF && signature[1] == 0xD8)
            {
                return ReadJpeg(stream);
            }

            if ((signature[0] == (byte)'I' && signature[1] == (byte)'I')
                || (signature[0] == (byte)'M' && signature[1] == (byte)'M'))
            {
                return ReadTiff(stream);
            }
        }
        catch (IOException)
        {
            return null;
        }

        return null;
    }

    private static int? ReadBmp(Stream stream)
    {
        if (stream.Length < 46)
        {
            return null;
        }

        Span<byte> header = stackalloc byte[46];
        stream.ReadExactly(header);
        var ppm = BitConverter.ToInt32(header[38..]);
        return PixelsPerMeterToDpi(ppm);
    }

    private static int? ReadPng(Stream stream)
    {
        Span<byte> signature = stackalloc byte[8];
        stream.ReadExactly(signature);
        Span<byte> chunkHead = stackalloc byte[8];
        while (stream.Position + 8 < stream.Length)
        {
            if (stream.Read(chunkHead) < 8)
            {
                break;
            }

            var length = ReadInt32BigEndian(chunkHead);
            var type = System.Text.Encoding.ASCII.GetString(chunkHead[4..]);
            if (type == "pHYs" && length >= 9)
            {
                Span<byte> data = stackalloc byte[9];
                stream.ReadExactly(data);
                if (data[8] != 1)
                {
                    return null;
                }

                var ppm = ReadInt32BigEndian(data);
                return PixelsPerMeterToDpi(ppm);
            }

            if (type == "IEND")
            {
                break;
            }

            stream.Position += length + 4;
        }

        return null;
    }

    private static int? ReadJpeg(Stream stream)
    {
        Span<byte> marker = stackalloc byte[4];
        stream.Position = 2;
        while (stream.Position + 4 < stream.Length)
        {
            if (stream.Read(marker) < 4 || marker[0] != 0xFF)
            {
                break;
            }

            var segment = marker[1];
            var length = (marker[2] << 8) | marker[3];
            if (length < 2)
            {
                break;
            }

            if (segment == 0xE0 && length >= 16)
            {
                Span<byte> app0 = stackalloc byte[14];
                stream.ReadExactly(app0);
                if (app0[0] == (byte)'J' && app0[1] == (byte)'F' && app0[2] == (byte)'I' && app0[3] == (byte)'F')
                {
                    var units = app0[7];
                    var xDensity = (app0[8] << 8) | app0[9];
                    if (units == 1)
                    {
                        return xDensity;
                    }

                    if (units == 2)
                    {
                        return (int)Math.Round(xDensity * 2.54);
                    }
                }

                return null;
            }

            stream.Position += length - 2;
        }

        return null;
    }

    private static int? ReadTiff(Stream stream)
    {
        Span<byte> header = stackalloc byte[8];
        stream.ReadExactly(header);
        var little = header[0] == (byte)'I';
        var ifd = ReadInt32(header[4..], little);
        if (ifd <= 0 || ifd >= stream.Length)
        {
            return null;
        }

        stream.Position = ifd;
        Span<byte> countBytes = stackalloc byte[2];
        stream.ReadExactly(countBytes);
        var count = little
            ? BitConverter.ToUInt16(countBytes)
            : (ushort)((countBytes[0] << 8) | countBytes[1]);

        Span<byte> entry = stackalloc byte[12];
        for (var i = 0; i < count; i++)
        {
            stream.ReadExactly(entry);
            var tag = little
                ? BitConverter.ToUInt16(entry)
                : (ushort)((entry[0] << 8) | entry[1]);
            if (tag != 282)
            {
                continue;
            }

            var type = little
                ? BitConverter.ToUInt16(entry[2..])
                : (ushort)((entry[2] << 8) | entry[3]);
            if (type != 5)
            {
                continue;
            }

            var offset = ReadInt32(entry[8..], little);
            if (offset <= 0 || offset + 8 > stream.Length)
            {
                return null;
            }

            stream.Position = offset;
            Span<byte> rational = stackalloc byte[8];
            stream.ReadExactly(rational);
            var numerator = ReadInt32(rational, little);
            var denominator = ReadInt32(rational[4..], little);
            if (denominator == 0)
            {
                return null;
            }

            return (int)Math.Round(numerator / (double)denominator);
        }

        return null;
    }

    private static int? PixelsPerMeterToDpi(int pixelsPerMeter)
    {
        if (pixelsPerMeter <= 0)
        {
            return null;
        }

        return (int)Math.Round(pixelsPerMeter * 0.0254);
    }

    private static int ReadInt32BigEndian(ReadOnlySpan<byte> bytes)
        => (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

    private static int ReadInt32(ReadOnlySpan<byte> bytes, bool little)
        => little ? BitConverter.ToInt32(bytes) : ReadInt32BigEndian(bytes);
}
