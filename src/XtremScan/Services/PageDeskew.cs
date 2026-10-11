using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using XtremScan.Scanning;

namespace XtremScan.Services;

/// <summary>
/// Estime l'orientation avec Tesseract.
/// L'angle fin suit les filets qui traversent la page. Sans filet, il suit la ligne de base du texte.
/// Repli : recherche d'inclinaison sur l'encre. Le JPEG d'origine n'est pas modifié.
/// </summary>
public static class PageDeskew
{
    private static readonly Serilog.ILogger Log = AppLog.For(nameof(PageDeskew));

    public static async Task<(int QuarterTurns, double SkewDegrees)> SuggestOrientationAsync(string path)
    {
        var file = Path.GetFileName(path);
        if (PageOcr.LanguageReady())
        {
            var suggestion = await SuggestByOcrAsync(path);
            if (suggestion is not null)
            {
                Log.Information(
                    "Orientation OCR {File} → quart {Quarter}, inclinaison {Skew}°",
                    file,
                    suggestion.Value.QuarterTurns,
                    suggestion.Value.SkewDegrees.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                return suggestion.Value;
            }
        }

        var (ink, width, height) = await ReadInkAsync(path);
        var orientation = PageSkew.SuggestOrientation(ink, width, height);
        var fine = PageSkew.Merge(orientation.SkewDegrees, PageSkew.MeasureRules(ink, width, height));
        if (Math.Abs(fine) > PageSkew.MaxFineDegrees)
        {
            fine = orientation.SkewDegrees;
        }

        Log.Information(
            "Orientation encre {File} → quart {Quarter}, inclinaison {Skew}°",
            file,
            orientation.QuarterTurns,
            fine.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        return (orientation.QuarterTurns, fine);
    }

    /// <summary>
    /// Inclinaison dans le format déjà affiché. Portrait reste portrait, paysage reste paysage.
    /// </summary>
    /// <summary>
    /// Reste d'inclinaison dans l'image déjà affichée. On l'ajoute à l'angle courant.
    /// </summary>
    public static async Task<double> SuggestSkewAsync(string path, int quarterTurns, double currentSkew = 0)
    {
        quarterTurns &= 3;
        var file = Path.GetFileName(path);
        var decoded = await DecodePreviewAsync(path);
        var angle = PageRotation.Combine(quarterTurns, currentSkew);
        var shown = BgraImage.RotateInView(decoded.Pixels, decoded.Width, decoded.Height, angle);
        var ink = InkMask(shown.Pixels, shown.Width, shown.Height);
        var fromRules = PageSkew.SuggestSkew(ink, shown.Width, shown.Height, 0);
        if (!PageOcr.LanguageReady())
        {
            Log.Information(
                "Reste {File} quart {Quarter}, angle affiché {Shown}° → {Skew}° (encre seule)",
                file,
                quarterTurns,
                angle.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                fromRules.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            return fromRules;
        }

        var fromText = await SuggestSkewOnShownAsync(shown.Pixels, shown.Width, shown.Height);
        if (fromText is null || Math.Abs(Math.Abs(fromText.Value) - PageSkew.MaxFineDegrees) <= 0.25)
        {
            Log.Information(
                "Reste {File} quart {Quarter}, angle affiché {Shown}° → {Skew}° (filet ; OCR {Ocr})",
                file,
                quarterTurns,
                angle.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                fromRules.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                fromText is null ? "indisponible" : fromText.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            return fromRules;
        }

        var chosen = Math.Abs(fromText.Value - fromRules) <= 1.2 ? fromText.Value : fromRules;
        Log.Information(
            "Reste {File} quart {Quarter}, angle affiché {Shown}° → {Skew}° (OCR {Ocr}, filet {Rules})",
            file,
            quarterTurns,
            angle.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            chosen.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            fromText.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            fromRules.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        return chosen;
    }

    private static async Task<double?> SuggestSkewOnShownAsync(byte[] pixels, int width, int height)
    {
        var words = await Task.Run(() => PageOcr.ReadWords(pixels, width, height));
        if (words.Count < 10)
        {
            return null;
        }

        return TextAngleInBoxes(pixels, width, height, words, lockFormat: true);
    }

    /// <summary>
    /// Lit la page dans les quatre orientations et garde celle où l'OCR trouve le plus de mots.
    /// L'angle fin vient des filets, des colonnes et des lignes de texte. Les aplats ne votent pas.
    /// </summary>
    private static async Task<(int QuarterTurns, double SkewDegrees)?> SuggestByOcrAsync(string path)
    {
        var decoded = await DecodePreviewAsync(path);
        var pixels = decoded.Pixels;
        var decodeWidth = decoded.Width;
        var decodeHeight = decoded.Height;

        var upright = PageOcr.ReadUprightTurns(pixels, decodeWidth, decodeHeight);
        var bestScore = 0d;
        var bestTurns = upright?.QuarterTurns ?? 0;
        (byte[] Pixels, int Width, int Height) best = default;
        IReadOnlyList<PageOcr.PixelWord> bestWords = [];
        if (upright is null)
        {
            for (var turn = 0; turn < 4; turn++)
            {
                var oriented = PagePreview.Orient(pixels, decodeWidth, decodeHeight, turn, 0);
                var score = await Task.Run(() => PageOcr.ScoreWords(oriented.Pixels, oriented.Width, oriented.Height));
                if (score.ConfidenceSum > bestScore)
                {
                    bestScore = score.ConfidenceSum;
                    bestTurns = turn;
                    best = oriented;
                    bestWords = score.Words;
                }
            }
        }
        else
        {
            best = PagePreview.Orient(pixels, decodeWidth, decodeHeight, bestTurns, 0);
            bestWords = await Task.Run(() => PageOcr.ReadWords(best.Pixels, best.Width, best.Height));
            bestScore = bestWords.Count;
        }

        if ((upright is null && bestScore < 10) || best.Pixels is null)
        {
            return null;
        }

        var ink = InkMask(best.Pixels, best.Width, best.Height);
        var rules = PageSkew.LongRuleCorrection(ink, best.Width, best.Height);
        double fine;
        if (rules is double fromRules)
        {
            var turned = BgraImage.RotateInView(best.Pixels, best.Width, best.Height, fromRules);
            var again = PageSkew.LongRuleCorrection(
                InkMask(turned.Pixels, turned.Width, turned.Height),
                turned.Width,
                turned.Height);
            fine = PageSkew.AddSmallRemainder(fromRules, again);
        }
        else if (PageSkew.BaselineCorrection(ink, best.Width, best.Height, ToWords(bestWords)) is double fromText)
        {
            var turned = BgraImage.RotateInView(best.Pixels, best.Width, best.Height, fromText);
            var words = await Task.Run(() => PageOcr.ReadWords(turned.Pixels, turned.Width, turned.Height));
            var again = PageSkew.BaselineCorrection(
                InkMask(turned.Pixels, turned.Width, turned.Height),
                turned.Width,
                turned.Height,
                ToWords(words));
            fine = PageSkew.AddSmallRemainder(fromText, again);
        }
        else
        {
            fine = TextAngleInBoxes(best.Pixels, best.Width, best.Height, bestWords, lockFormat: false);
        }

        if (Math.Abs(fine) > PageSkew.MaxFineDegrees)
        {
            fine = 0;
        }

        return (bestTurns, fine);
    }

    private static IReadOnlyList<PageSkew.TextWord> ToWords(IReadOnlyList<PageOcr.PixelWord> words)
    {
        var mapped = new List<PageSkew.TextWord>(words.Count);
        foreach (var word in words)
        {
            mapped.Add(new PageSkew.TextWord(word.X, word.Y, word.Width, word.Height));
        }

        return mapped;
    }

    private static async Task<(byte[] Pixels, int Width, int Height)> DecodePreviewAsync(string path)
    {
        await using var file = File.OpenRead(path);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var pixelWidth = (int)decoder.PixelWidth;
        var pixelHeight = (int)decoder.PixelHeight;
        var scale = Math.Min(1d, 2800d / (double)Math.Max(1, Math.Max(pixelWidth, pixelHeight)));
        var decodeWidth = Math.Max(1, (int)Math.Round(pixelWidth * scale));
        var decodeHeight = Math.Max(1, (int)Math.Round(pixelHeight * scale));
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

    /// <summary>
    /// Angle à appliquer. L'encre dans les boîtes de mots donne le texte : les rectangles OCR sont horizontaux et mentent sur la pente.
    /// Les filets et les colonnes précisent cet angle. Les aplats hors des boîtes ne comptent pas dans le texte.
    /// </summary>
    private static double TextAngleInBoxes(byte[] pixels, int width, int height, IReadOnlyList<PageOcr.PixelWord> words, bool lockFormat)
    {
        var ink = new bool[width * height];
        for (var index = 0; index < ink.Length; index++)
        {
            var offset = index * 4;
            if (offset + 2 >= pixels.Length)
            {
                break;
            }

            var luma = ((pixels[offset + 2] * 2) + (pixels[offset + 1] * 5) + pixels[offset]) / 8;
            ink[index] = luma < 200;
        }

        var allowed = new bool[width * height];
        foreach (var word in words)
        {
            var pad = Math.Max(2, (int)(word.Height * 0.4));
            var x0 = Math.Max(0, word.X - pad);
            var y0 = Math.Max(0, word.Y - pad);
            var x1 = Math.Min(width, word.X + word.Width + pad);
            var y1 = Math.Min(height, word.Y + word.Height + pad);
            for (var y = y0; y < y1; y++)
            {
                var row = y * width;
                for (var x = x0; x < x1; x++)
                {
                    allowed[row + x] = true;
                }
            }
        }

        var textInk = new bool[width * height];
        var kept = 0;
        for (var index = 0; index < textInk.Length; index++)
        {
            textInk[index] = ink[index] && allowed[index];
            kept |= textInk[index] ? 1 : 0;
        }

        var text = kept == 0 ? 0d : PageSkew.SuggestCorrection(textInk, width, height);
        var ruled = PageSkew.MeasureRules(ink, width, height);
        return lockFormat ? PageSkew.KeepInFormat(text, ruled) : PageSkew.Merge(text, ruled);
    }

    public static async Task<double> SuggestAsync(string path)
    {
        var orientation = await SuggestOrientationAsync(path);
        return (orientation.QuarterTurns * 90d) + orientation.SkewDegrees;
    }

    private static async Task<(bool[] Ink, int Width, int Height)> ReadInkAsync(string path)
    {
        const int limit = 240;
        await using var file = File.OpenRead(path);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var width = (int)decoder.PixelWidth;
        var height = (int)decoder.PixelHeight;
        var scale = Math.Min(1d, limit / (double)Math.Max(1, Math.Max(width, height)));
        var decodeWidth = Math.Max(1, (int)Math.Round(width * scale));
        var decodeHeight = Math.Max(1, (int)Math.Round(height * scale));
        var provider = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            new BitmapTransform { ScaledWidth = (uint)decodeWidth, ScaledHeight = (uint)decodeHeight },
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        return (InkMask(provider.DetachPixelData(), decodeWidth, decodeHeight), decodeWidth, decodeHeight);
    }

    private static bool[] InkMask(byte[] pixels, int width, int height)
    {
        var ink = new bool[width * height];
        for (var index = 0; index < ink.Length; index++)
        {
            var offset = index * 4;
            if (offset + 2 >= pixels.Length)
            {
                break;
            }

            var luma = ((pixels[offset + 2] * 2) + (pixels[offset + 1] * 5) + pixels[offset]) / 8;
            ink[index] = luma < 200;
        }

        return ink;
    }
}
