using System.Runtime.InteropServices.WindowsRuntime;
using Tesseract;
using XtremScan.Scanning;

namespace XtremScan.Services;

/// <summary>
/// Couche de texte d'un PDF via Tesseract 5, en local. L'image enregistrée n'est pas réduite.
/// Le texte reconnu n'est pas journalisé. La lecture se fait là où le texte est horizontal,
/// puis les boîtes reviennent dans la feuille affichée.
/// </summary>
public static class PageOcr
{
    /// <summary>Grand côté de la copie lue. Autour de 300 dpi pour une page A4, Tesseract voit le petit texte.</summary>
    private const int MaxEdge = 3500;

    private static readonly object Gate = new();
    private static TesseractEngine? _engine;
    private static TesseractEngine? _orientation;
    private static bool _engineFailed;
    private static bool _orientationFailed;

    public readonly record struct PixelWord(int X, int Y, int Width, int Height);

    public static bool LanguageReady()
    {
        return !_engineFailed && File.Exists(TrainedDataPath);
    }

    public static async Task<OcrPage> RecognizeAsync(PdfPageSpec page)
    {
        if (!LanguageReady())
        {
            throw new InvalidOperationException(
                "Le français Tesseract est introuvable. Le PDF sera enregistré sans texte.");
        }

        var picture = JpegPicture.Read(page.JpegPath);
        AppLog.For(nameof(PageOcr)).Information(
            "Tesseract page {File} — jpeg {W}x{H}, quart {Quarter}, skew {Skew}°",
            Path.GetFileName(page.JpegPath),
            picture.Width,
            picture.Height,
            page.QuarterTurns,
            page.SkewDegrees.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        var (pixels, width, height) = await VisiblePixelsAsync(page, picture);
        AppLog.For(nameof(PageOcr)).Information("Tesseract bitmap {Width}x{Height} prêt", width, height);
        var (readTurns, read) = await Task.Run(() => ReadBest(pixels, width, height));
        if (readTurns != 0)
        {
            AppLog.For(nameof(PageOcr)).Information("Tesseract lu tourné de {Turns} quart(s) puis remappé", readTurns);
            read = Remap(read, readTurns);
        }

        var content = PdfPagePlacement.ContentBounds(picture, page);
        var runs = new List<PdfTextRun>(read.Runs.Count);
        for (var index = 0; index < read.Runs.Count; index++)
        {
            var run = read.Runs[index];
            var box = read.Boxes[index];
            runs.Add(new PdfTextRun
            {
                Text = run.Text,
                X = content.X + (box.X * content.Width),
                Baseline = content.Y + ((1d - box.Y - box.Height) * content.Height),
                Size = Math.Max(4, box.Height * content.Height),
            });
        }

        AppLog.For(nameof(PageOcr)).Information(
            "Tesseract page {File} → {Words} mot(s)",
            Path.GetFileName(page.JpegPath),
            read.Boxes.Count);
        return new OcrPage { Runs = runs, Boxes = read.Boxes };
    }

    /// <summary>Rectangles de mots, en pixels, pour le redressement. Liste vide si Tesseract ne répond pas.</summary>
    public static IReadOnlyList<PixelWord> ReadWords(byte[] pixels, int width, int height)
    {
        return Recognize(pixels, width, height, PageSegMode.Auto).Words;
    }

    /// <summary>
    /// Quart de tour pour mettre le texte à l'endroit. C'est le modèle d'orientation déjà livré avec Tesseract, pas un réseau entraîné ici.
    /// </summary>
    public static (int QuarterTurns, float Confidence)? ReadUprightTurns(byte[] pixels, int width, int height)
    {
        if (width < 32 || height < 32 || pixels.Length < width * height * 4 || !File.Exists(OrientationDataPath))
        {
            return null;
        }

        lock (Gate)
        {
            var engine = OrientationEngine();
            if (engine is null)
            {
                return null;
            }

            try
            {
                using var pix = Pix.LoadFromMemory(ToBmp(pixels, width, height));
                using var page = engine.Process(pix, PageSegMode.OsdOnly);
                page.DetectBestOrientation(out int degrees, out float confidence);
                var turns = ((degrees % 360) + 360) % 360 / 90;
                AppLog.For(nameof(PageOcr)).Information(
                    "Orientation Tesseract → quart {Turns}, confiance {Confidence}",
                    turns,
                    confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                return confidence < 1.5f ? null : (turns, confidence);
            }
            catch (Exception exception)
            {
                AppLog.For(nameof(PageOcr)).Warning(exception, "Orientation Tesseract indisponible");
                return null;
            }
        }
    }

    public readonly record struct WordScore(IReadOnlyList<PixelWord> Words, double ConfidenceSum);

    public static WordScore ScoreWords(byte[] pixels, int width, int height)
    {
        var read = Recognize(pixels, width, height, PageSegMode.Auto);
        return new WordScore(read.Words, read.ConfidenceSum);
    }

    private static (int Turns, OcrPage Page) ReadBest(byte[] pixels, int width, int height)
    {
        var upright = ToPage(Recognize(pixels, width, height, PageSegMode.Auto));
        if (upright.Boxes.Count >= 8)
        {
            return (0, upright);
        }

        var best = (0, upright);
        foreach (var turn in new[] { 1, 3, 2 })
        {
            var turned = PagePreview.Orient(pixels, width, height, turn, 0);
            var candidate = ToPage(Recognize(turned.Pixels, turned.Width, turned.Height, PageSegMode.Auto));
            if (candidate.Boxes.Count > best.Item2.Boxes.Count)
            {
                best = (turn, candidate);
            }
        }

        return best;
    }

    private static OcrPage ToPage((IReadOnlyList<PixelWord> Words, IReadOnlyList<string> Texts, int Width, int Height, double ConfidenceSum) read)
    {
        var boxes = new List<OcrWordBox>(read.Words.Count);
        var runs = new List<PdfTextRun>(read.Words.Count);
        var width = Math.Max(1, read.Width);
        var height = Math.Max(1, read.Height);
        for (var index = 0; index < read.Words.Count; index++)
        {
            var word = read.Words[index];
            boxes.Add(new OcrWordBox
            {
                X = word.X / (double)width,
                Y = word.Y / (double)height,
                Width = word.Width / (double)width,
                Height = word.Height / (double)height,
            });
            runs.Add(new PdfTextRun { Text = read.Texts[index], X = 0, Baseline = 0, Size = 1 });
        }

        return new OcrPage { Runs = runs, Boxes = boxes };
    }

    private static OcrPage Remap(OcrPage read, int turns)
    {
        var boxes = new List<OcrWordBox>(read.Boxes.Count);
        foreach (var box in read.Boxes)
        {
            var (x, y) = turns switch
            {
                1 => (box.Y, 1d - box.X - box.Width),
                2 => (1d - box.X - box.Width, 1d - box.Y - box.Height),
                3 => (1d - box.Y - box.Height, box.X),
                _ => (box.X, box.Y),
            };
            var width = (turns & 1) == 1 ? box.Height : box.Width;
            var height = (turns & 1) == 1 ? box.Width : box.Height;
            boxes.Add(new OcrWordBox { X = x, Y = y, Width = width, Height = height });
        }

        return new OcrPage { Runs = read.Runs, Boxes = boxes };
    }

    private static (IReadOnlyList<PixelWord> Words, IReadOnlyList<string> Texts, int Width, int Height, double ConfidenceSum) Recognize(
        byte[] pixels,
        int width,
        int height,
        PageSegMode mode)
    {
        if (width < 8 || height < 8 || pixels.Length < width * height * 4)
        {
            return ([], [], width, height, 0);
        }

        lock (Gate)
        {
            var engine = Engine();
            if (engine is null)
            {
                return ([], [], width, height, 0);
            }

            try
            {
                using var pix = Pix.LoadFromMemory(ToBmp(pixels, width, height));
                pix.XRes = 300;
                pix.YRes = 300;
                using var recognized = engine.Process(pix, mode);
                using var iterator = recognized.GetIterator();
                var words = new List<PixelWord>();
                var texts = new List<string>();
                var confidence = 0d;
                if (iterator is not null)
                {
                    iterator.Begin();
                    do
                    {
                        var text = iterator.GetText(PageIteratorLevel.Word);
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        var score = iterator.GetConfidence(PageIteratorLevel.Word);
                        if (score < 0 || !iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds))
                        {
                            continue;
                        }

                        var boxWidth = Math.Max(1, bounds.Width);
                        var boxHeight = Math.Max(1, bounds.Height);
                        words.Add(new PixelWord(bounds.X1, bounds.Y1, boxWidth, boxHeight));
                        texts.Add(text.Trim());
                        confidence += score;
                    }
                    while (iterator.Next(PageIteratorLevel.Word));
                }

                var mean = words.Count == 0 ? 0 : confidence / words.Count;
                AppLog.For(nameof(PageOcr)).Information(
                    "Tesseract : {Count} mot(s), image {Width}x{Height}, confiance moyenne {Confidence}",
                    words.Count,
                    width,
                    height,
                    mean.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
                return (words, texts, width, height, confidence);
            }
            catch (Exception exception)
            {
                AppLog.For(nameof(PageOcr)).Warning(exception, "Tesseract n'a pas lu cette image");
                return ([], [], width, height, 0);
            }
        }
    }

    private static TesseractEngine? Engine()
    {
        if (_engine is not null)
        {
            return _engine;
        }

        if (_engineFailed || !File.Exists(TrainedDataPath))
        {
            _engineFailed = true;
            AppLog.For(nameof(PageOcr)).Warning("Tesseract : français absent ({Path})", TrainedDataPath);
            return null;
        }

        try
        {
            _engine = new TesseractEngine(DataDirectory, "fra", EngineMode.LstmOnly);
            AppLog.For(nameof(PageOcr)).Information("Tesseract 5 français chargé ({Path})", TrainedDataPath);
            return _engine;
        }
        catch (Exception exception)
        {
            _engineFailed = true;
            AppLog.For(nameof(PageOcr)).Error(exception, "Tesseract impossible à charger");
            return null;
        }
    }

    private static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "tessdata");

    private static string TrainedDataPath => Path.Combine(DataDirectory, "fra.traineddata");

    private static string OrientationDataPath => Path.Combine(DataDirectory, "osd.traineddata");

    private static TesseractEngine? OrientationEngine()
    {
        if (_orientation is not null)
        {
            return _orientation;
        }

        if (_orientationFailed || !File.Exists(OrientationDataPath))
        {
            _orientationFailed = true;
            AppLog.For(nameof(PageOcr)).Warning("Tesseract : modèle d'orientation absent ({Path})", OrientationDataPath);
            return null;
        }

        try
        {
            _orientation = new TesseractEngine(DataDirectory, "osd", EngineMode.TesseractOnly);
            AppLog.For(nameof(PageOcr)).Information("Orientation Tesseract chargée ({Path})", OrientationDataPath);
            return _orientation;
        }
        catch (Exception exception)
        {
            _orientationFailed = true;
            AppLog.For(nameof(PageOcr)).Error(exception, "Orientation Tesseract impossible à charger");
            return null;
        }
    }

    private static async Task<(byte[] Pixels, int Width, int Height)> VisiblePixelsAsync(PdfPageSpec page, JpegPicture picture)
    {
        var longSide = Math.Max(picture.Width, picture.Height);
        var decodeLong = Math.Min(MaxEdge, longSide);
        var scale = decodeLong / (double)longSide;
        var decodeWidth = Math.Max(1, (int)Math.Round(picture.Width * scale));
        var decodeHeight = Math.Max(1, (int)Math.Round(picture.Height * scale));
        await using var file = File.OpenRead(page.JpegPath);
        using var stream = file.AsRandomAccessStream();
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        var provider = await decoder.GetPixelDataAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
            new Windows.Graphics.Imaging.BitmapTransform
            {
                ScaledWidth = (uint)decodeWidth,
                ScaledHeight = (uint)decodeHeight,
            },
            Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
            Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
        var decoded = provider.DetachPixelData();
        var cropX = page.CropWidth > 0 ? (int)Math.Round(page.CropX * decodeWidth / (double)picture.Width) : 0;
        var cropY = page.CropHeight > 0 ? (int)Math.Round(page.CropY * decodeHeight / (double)picture.Height) : 0;
        var cropW = page.CropWidth > 0 ? Math.Max(1, (int)Math.Round(page.CropWidth * decodeWidth / (double)picture.Width)) : decodeWidth;
        var cropH = page.CropHeight > 0 ? Math.Max(1, (int)Math.Round(page.CropHeight * decodeHeight / (double)picture.Height)) : decodeHeight;
        cropX = Math.Clamp(cropX, 0, decodeWidth - 1);
        cropY = Math.Clamp(cropY, 0, decodeHeight - 1);
        cropW = Math.Clamp(cropW, 1, decodeWidth - cropX);
        cropH = Math.Clamp(cropH, 1, decodeHeight - cropY);
        var cropped = new byte[cropW * cropH * 4];
        for (var row = 0; row < cropH; row++)
        {
            System.Buffer.BlockCopy(decoded, ((cropY + row) * decodeWidth + cropX) * 4, cropped, row * cropW * 4, cropW * 4);
        }

        var upright = PagePreview.Orient(cropped, cropW, cropH, page.QuarterTurns, 0);
        return BgraImage.RotateForReading(upright.Pixels, upright.Width, upright.Height, page.SkewDegrees);
    }

    /// <summary>BMP 24 bits, lu par Leptonica sans dépendance d'encodage.</summary>
    private static byte[] ToBmp(byte[] bgra, int width, int height)
    {
        var stride = (width * 3 + 3) & ~3;
        var pixels = stride * height;
        var file = new byte[54 + pixels];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BitConverter.GetBytes(file.Length).CopyTo(file, 2);
        BitConverter.GetBytes(54).CopyTo(file, 10);
        BitConverter.GetBytes(40).CopyTo(file, 14);
        BitConverter.GetBytes(width).CopyTo(file, 18);
        BitConverter.GetBytes(height).CopyTo(file, 22);
        BitConverter.GetBytes((short)1).CopyTo(file, 26);
        BitConverter.GetBytes((short)24).CopyTo(file, 28);
        BitConverter.GetBytes(pixels).CopyTo(file, 34);
        for (var y = 0; y < height; y++)
        {
            var source = (height - 1 - y) * width * 4;
            var destination = 54 + (y * stride);
            for (var x = 0; x < width; x++)
            {
                file[destination] = bgra[source];
                file[destination + 1] = bgra[source + 1];
                file[destination + 2] = bgra[source + 2];
                source += 4;
                destination += 3;
            }
        }

        return file;
    }
}
