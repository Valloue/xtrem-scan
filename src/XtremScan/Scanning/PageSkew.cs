namespace XtremScan.Scanning;

/// <summary>
/// Petit angle de redressement. L'image enregistrée n'est pas réencodée : le PDF tourne le JPEG.
/// </summary>
public static class PageSkew
{
    public static (int Width, int Height) Bounds(int width, int height, double degreesClockwise)
    {
        if (width < 1 || height < 1 || Math.Abs(degreesClockwise) < 0.05)
        {
            return (Math.Max(1, width), Math.Max(1, height));
        }

        var corners = new[]
        {
            MapRaw(0, 0, width, height, degreesClockwise),
            MapRaw(width, 0, width, height, degreesClockwise),
            MapRaw(0, height, width, height, degreesClockwise),
            MapRaw(width, height, width, height, degreesClockwise),
        };
        var minX = corners.Min(point => point.X);
        var minY = corners.Min(point => point.Y);
        var maxX = corners.Max(point => point.X);
        var maxY = corners.Max(point => point.Y);
        return (Math.Max(1, (int)Math.Ceiling(maxX - minX)), Math.Max(1, (int)Math.Ceiling(maxY - minY)));
    }

    public static (double X, double Y) Map(double x, double y, int width, int height, double degreesClockwise)
    {
        var raw = MapRaw(x, y, width, height, degreesClockwise);
        var origin = Origin(width, height, degreesClockwise);
        return (raw.X - origin.X, raw.Y - origin.Y);
    }

    public static (double X, double Y) Unmap(double x, double y, int width, int height, double degreesClockwise)
    {
        var origin = Origin(width, height, degreesClockwise);
        return Inverse(x + origin.X, y + origin.Y, width, height, degreesClockwise);
    }

    /// <summary>Au-delà, l'angle fin n'est plus un petit redressement : la feuille est vraiment penchée.</summary>
    public const double MaxFineDegrees = 45;

    /// <summary>En dessous, la pente tient dans un pixel sur une page détaillée. On ne l'arrondit plus à zéro.</summary>
    public const double PixelDegrees = 0.01;

    /// <summary>
    /// Échelle de l'angle dans la feuille. Jusqu'à 12° le scan reste à taille réelle.
    /// Au-delà, l'image rétrécit pour tenir entière dans la feuille, sans couper le texte.
    /// </summary>
    public static double FitScale(double width, double height, double degreesClockwise)
    {
        if (width < 1 || height < 1)
        {
            return 1;
        }

        var distance = Math.Abs(degreesClockwise % 180d);
        if (distance > 90d)
        {
            distance = 180d - distance;
        }

        var rad = degreesClockwise * Math.PI / 180d;
        var cos = Math.Abs(Math.Cos(rad));
        var sin = Math.Abs(Math.Sin(rad));
        if (sin < 0.0001 || distance <= 12d)
        {
            return 1;
        }

        var boxWidth = (width * cos) + (height * sin);
        var boxHeight = (width * sin) + (height * cos);
        var contain = Math.Min(width / boxWidth, height / boxHeight);
        var blend = Math.Clamp((distance - 12d) / 8d, 0d, 1d);
        return 1d + ((contain - 1d) * blend);
    }

    /// <summary>
    /// Tourne un point dans la feuille. Un grand angle rétrécit l'image pour qu'elle tienne dedans.
    /// </summary>
    public static (double X, double Y) MapOnPage(double x, double y, int width, int height, double degreesClockwise)
    {
        var raw = MapRaw(x, y, width, height, degreesClockwise);
        var scale = FitScale(width, height, degreesClockwise);
        var cx = width / 2d;
        var cy = height / 2d;
        return (cx + ((raw.X - cx) * scale), cy + ((raw.Y - cy) * scale));
    }

    /// <summary>Inverse de la rotation d'aperçu, sans le rétrécissement des grands angles.</summary>
    public static (double X, double Y) UnmapView(double x, double y, int width, int height, double degreesClockwise)
    {
        return Inverse(x, y, width, height, degreesClockwise);
    }

    public static (double X, double Y) UnmapOnPage(double x, double y, int width, int height, double degreesClockwise)
    {
        var scale = FitScale(width, height, degreesClockwise);
        var cx = width / 2d;
        var cy = height / 2d;
        var unscaledX = cx + ((x - cx) / scale);
        var unscaledY = cy + ((y - cy) / scale);
        return Inverse(unscaledX, unscaledY, width, height, degreesClockwise);
    }

    private static (double X, double Y) Inverse(double x, double y, int width, int height, double degreesClockwise)
    {
        var rad = -degreesClockwise * Math.PI / 180d;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var cx = width / 2d;
        var cy = height / 2d;
        var dx = x - cx;
        var dy = y - cy;
        return (cx + (dx * cos) - (dy * sin), cy + (dx * sin) + (dy * cos));
    }

    public static double SuggestCorrection(bool[] ink, int width, int height)
    {
        if (TryTextCorrection(ink, width, height) is double text)
        {
            return text;
        }

        var best = BestAngle(ink, width, height);
        return Math.Abs(best.Angle) < PixelDegrees ? 0 : best.Angle;
    }

    /// <summary>
    /// Inclinaison à appliquer dans une orientation déjà choisie. Le quart de tour ne change pas.
    /// </summary>
    public static double SuggestSkew(bool[] ink, int width, int height, int quarterTurns)
    {
        var turned = RotateQuarter(ink, width, height, quarterTurns);
        var text = SuggestCorrection(turned.Ink, turned.Width, turned.Height);
        return KeepInFormat(text, MeasureRules(turned.Ink, turned.Width, turned.Height));
    }

    /// <summary>
    /// Inclinaison qui reste, une fois le quart de tour et l'angle déjà affichés appliqués.
    /// On l'ajoute à l'angle courant : une page déjà presque droite ne repart pas de 90°.
    /// </summary>
    public static double SuggestResidual(bool[] ink, int width, int height, int quarterTurns, double currentSkew)
    {
        var shown = RotateInView(ink, width, height, PageRotation.Combine(quarterTurns, currentSkew));
        return SuggestSkew(shown, width, height, 0);
    }

    /// <summary>Même rotation que l'aperçu : autour du centre, sans rétrécir.</summary>
    public static bool[] RotateInView(bool[] ink, int width, int height, double degreesClockwise)
    {
        if (Math.Abs(degreesClockwise) < 0.05 || width < 1 || height < 1 || ink.Length < width * height)
        {
            return ink;
        }

        var destination = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var source = Inverse(x, y, width, height, degreesClockwise);
                var sx = (int)Math.Round(source.X);
                var sy = (int)Math.Round(source.Y);
                if ((uint)sx >= (uint)width || (uint)sy >= (uint)height)
                {
                    continue;
                }

                destination[row + x] = ink[(sy * width) + sx];
            }
        }

        return destination;
    }

    /// <summary>
    /// Angle dans le format déjà affiché. 45° est le bord de la recherche : le vrai pic est souvent plus loin,
    /// et l'appliquer laisse la feuille en losange. On garde alors le filet, qui reste dans le format.
    /// </summary>
    public static double KeepInFormat(double textDegrees, RuledAngle ruled)
    {
        var rulesOk = ruled.Confidence >= 0.18 && Math.Abs(ruled.Degrees) < MaxFineDegrees - 0.2;
        var atWall = Math.Abs(textDegrees) > MaxFineDegrees - 0.2;
        if (!atWall)
        {
            var merged = Merge(textDegrees, ruled);
            if (Math.Abs(merged) < MaxFineDegrees - 0.2)
            {
                return merged;
            }
        }

        if (rulesOk)
        {
            return ruled.Degrees;
        }

        var folded = textDegrees;
        while (folded > MaxFineDegrees)
        {
            folded -= 90d;
        }

        while (folded < -MaxFineDegrees)
        {
            folded += 90d;
        }

        return Math.Abs(folded) < MaxFineDegrees - 0.2 ? folded : 0;
    }

    /// <summary>Angle d'un filet ou d'une colonne, et netteté du pic. 0 si aucun trait long.</summary>
    public readonly record struct RuledAngle(double Degrees, double Confidence);

    /// <summary>
    /// Correction horaire qui rend horizontaux les filets longs, par ajustement de droite.
    /// Le pic sur des rangées entières arrondit un petit angle : ici chaque trait vote avec sa pente.
    /// </summary>
    public static double? LongRuleCorrection(bool[] ink, int width, int height)
    {
        if (width < 32 || height < 32 || ink.Length < width * height)
        {
            return null;
        }

        var points = ThinHorizontalPoints(ink, width, height);
        if (points.Count < 80)
        {
            return null;
        }

        var used = new bool[points.Count];
        var slopes = new List<(double Degrees, double Span)>();
        var seed = 17;
        for (var line = 0; line < 10; line++)
        {
            List<int>? best = null;
            var bestSpan = 0d;
            for (var attempt = 0; attempt < 80; attempt++)
            {
                seed = unchecked((seed * 1103515245) + 12345);
                var first = (int)((uint)seed % (uint)points.Count);
                seed = unchecked((seed * 1103515245) + 12345);
                var second = (int)((uint)seed % (uint)points.Count);
                if (used[first] || used[second])
                {
                    continue;
                }

                var dx = points[second].X - points[first].X;
                if (Math.Abs(dx) < width * 0.4)
                {
                    continue;
                }

                var slope = (points[second].Y - points[first].Y) / (double)dx;
                if (Math.Abs(slope) > 0.7)
                {
                    continue;
                }

                var inliers = new List<int>();
                var minX = int.MaxValue;
                var maxX = int.MinValue;
                var norm = Math.Sqrt(1d + (slope * slope));
                foreach (var index in Enumerable.Range(0, points.Count))
                {
                    if (used[index])
                    {
                        continue;
                    }

                    var point = points[index];
                    var distance = Math.Abs((point.Y - points[first].Y) - (slope * (point.X - points[first].X))) / norm;
                    if (distance > 1.6)
                    {
                        continue;
                    }

                    inliers.Add(index);
                    minX = Math.Min(minX, point.X);
                    maxX = Math.Max(maxX, point.X);
                }

                var span = maxX - minX;
                if (inliers.Count >= 40 && span >= width * 0.45 && (best is null || span > bestSpan))
                {
                    best = inliers;
                    bestSpan = span;
                }
            }

            if (best is null)
            {
                break;
            }

            double count = 0;
            double sumX = 0;
            double sumY = 0;
            double sumXX = 0;
            double sumXY = 0;
            foreach (var index in best)
            {
                used[index] = true;
                var point = points[index];
                count++;
                sumX += point.X;
                sumY += point.Y;
                sumXX += point.X * (double)point.X;
                sumXY += point.X * (double)point.Y;
            }

            var variance = (count * sumXX) - (sumX * sumX);
            if (variance < 1)
            {
                continue;
            }

            var fitted = ((count * sumXY) - (sumX * sumY)) / variance;
            slopes.Add((Math.Atan(fitted) * 180d / Math.PI, bestSpan));
        }

        if (slopes.Count < 2)
        {
            return null;
        }

        var longest = slopes.Max(line => line.Span);
        if (longest < width * 0.5)
        {
            return null;
        }

        var kept = slopes
            .Where(line => line.Span >= longest * 0.8)
            .Select(line => line.Degrees)
            .OrderBy(degrees => degrees)
            .ToList();
        if (kept.Count < 2)
        {
            return null;
        }

        var median = kept[kept.Count / 2];
        var agreed = kept.Where(degrees => Math.Abs(degrees - median) <= 0.25).ToList();
        if (agreed.Count < 2)
        {
            return null;
        }

        var correction = -agreed.Average();
        return Math.Abs(correction) < PixelDegrees ? 0 : correction;
    }

    /// <summary>Mot déjà placé dans l'image, pour suivre sa ligne de base.</summary>
    public readonly record struct TextWord(int X, int Y, int Width, int Height);

    /// <summary>
    /// Correction horaire d'une page de texte, sans filet. La pente vient du bas des lettres,
    /// pas du rectangle du mot. Les lettres qui descendent (p, g, y) sont écartées.
    /// </summary>
    public static double? BaselineCorrection(bool[] ink, int width, int height, IReadOnlyList<TextWord> words)
    {
        if (width < 32 || height < 32 || ink.Length < width * height || words.Count < 8)
        {
            return null;
        }

        var placed = new List<(double CenterX, double Baseline, double Width, double CenterY, double Height)>();
        foreach (var word in words)
        {
            if (word.Width < 10 || word.Height < 8)
            {
                continue;
            }

            if (WordBaseline(ink, width, height, word) is not double baseline)
            {
                continue;
            }

            placed.Add((word.X + (word.Width / 2d), baseline, word.Width, word.Y + (word.Height / 2d), word.Height));
        }

        if (placed.Count < 8)
        {
            return null;
        }

        var lines = new List<List<(double CenterX, double Baseline, double Width, double CenterY, double Height)>>();
        foreach (var word in placed.OrderBy(item => item.CenterY))
        {
            List<(double CenterX, double Baseline, double Width, double CenterY, double Height)>? line = null;
            foreach (var candidate in lines)
            {
                var center = candidate.Average(item => item.CenterY);
                var size = candidate.Average(item => item.Height);
                if (Math.Abs(word.CenterY - center) <= size * 0.55)
                {
                    line = candidate;
                    break;
                }
            }

            if (line is null)
            {
                line = [];
                lines.Add(line);
            }

            line.Add(word);
        }

        var slopes = new List<double>();
        foreach (var line in lines)
        {
            if (line.Count < 4)
            {
                continue;
            }

            var minX = line.Min(word => word.CenterX - (word.Width / 2d));
            var maxX = line.Max(word => word.CenterX + (word.Width / 2d));
            if (maxX - minX < width * 0.4)
            {
                continue;
            }

            double count = 0;
            double sumX = 0;
            double sumY = 0;
            double sumXX = 0;
            double sumXY = 0;
            foreach (var word in line)
            {
                var weight = Math.Max(1d, word.Width);
                count += weight;
                sumX += word.CenterX * weight;
                sumY += word.Baseline * weight;
                sumXX += word.CenterX * word.CenterX * weight;
                sumXY += word.CenterX * word.Baseline * weight;
            }

            var variance = (count * sumXX) - (sumX * sumX);
            if (variance < 1)
            {
                continue;
            }

            var slope = ((count * sumXY) - (sumX * sumY)) / variance;
            slopes.Add(Math.Atan(slope) * 180d / Math.PI);
        }

        if (slopes.Count < 2)
        {
            return null;
        }

        slopes.Sort();
        var median = slopes[slopes.Count / 2];
        var agreed = slopes.Where(degrees => Math.Abs(degrees - median) <= 0.35).ToList();
        if (agreed.Count < 2)
        {
            return null;
        }

        var correction = -agreed.Average();
        return Math.Abs(correction) < PixelDegrees ? 0 : correction;
    }

    /// <summary>Ajoute un reste mesuré après le premier angle, seulement s'il est petit.</summary>
    public static double AddSmallRemainder(double angle, double? remainder)
    {
        if (remainder is not double extra || Math.Abs(extra) < PixelDegrees || Math.Abs(extra) > 0.35)
        {
            return angle;
        }

        var sum = angle + extra;
        return Math.Abs(sum) < PixelDegrees ? 0 : sum;
    }

    private static double? WordBaseline(bool[] ink, int width, int height, TextWord word)
    {
        var x0 = Math.Clamp(word.X, 0, width - 1);
        var x1 = Math.Clamp(word.X + word.Width, x0 + 1, width);
        var y0 = Math.Clamp(word.Y, 0, height - 1);
        var y1 = Math.Clamp(word.Y + word.Height, y0 + 1, height);
        var bottoms = new List<int>();
        for (var x = x0; x < x1; x++)
        {
            for (var y = y1 - 1; y >= y0; y--)
            {
                if (!ink[(y * width) + x])
                {
                    continue;
                }

                bottoms.Add(y);
                break;
            }
        }

        if (bottoms.Count < Math.Max(4, word.Width / 3))
        {
            return null;
        }

        bottoms.Sort();
        var keep = Math.Max(3, (int)Math.Round(bottoms.Count * 0.75));
        keep = Math.Min(keep, bottoms.Count);
        return bottoms[keep / 2];
    }

    private static List<(int X, int Y)> ThinHorizontalPoints(bool[] ink, int width, int height)
    {
        var points = new List<(int X, int Y)>();
        var step = width > 1600 ? 2 : 1;
        for (var x = 6; x < width - 6; x += step)
        {
            for (var y = 4; y < height - 4; y++)
            {
                var index = (y * width) + x;
                if (!ink[index])
                {
                    continue;
                }

                if (ink[index - (2 * width)] || ink[index + (2 * width)])
                {
                    continue;
                }

                if (!ink[index - 3] || !ink[index + 3])
                {
                    continue;
                }

                points.Add((x, y));
            }
        }

        return points;
    }

    /// <summary>
    /// Angle horaire qui aligne les traits longs : filets horizontaux et colonnes.
    /// Un aplat (logo, tampon, photo) est trop plein pour voter : un dessin peut être penché exprès.
    /// </summary>
    public static RuledAngle MeasureRules(bool[] ink, int width, int height)
    {
        if (width < 8 || height < 8 || ink.Length < width * height)
        {
            return new RuledAngle(0, 0);
        }

        var (mask, maskWidth, maskHeight) = ThinRules(ink, width, height);
        var points = new List<(int X, int Y)>();
        for (var y = 0; y < maskHeight; y++)
        {
            var row = y * maskWidth;
            for (var x = 0; x < maskWidth; x++)
            {
                if (mask[row + x])
                {
                    points.Add((x, y));
                }
            }
        }

        if (points.Count < 24)
        {
            return new RuledAngle(0, 0);
        }

        var steps = ((int)MaxFineDegrees * 20) + 1;
        var horizontalScores = new double[steps];
        var verticalScores = new double[steps];
        for (var step = 0; step < steps; step++)
        {
            var angle = (step - (steps / 2)) / 10d;
            (horizontalScores[step], verticalScores[step]) = RuleScores(points, maskWidth, maskHeight, angle);
        }

        var horizontal = Refine(Pick(horizontalScores), points, maskWidth, maskHeight, horizontal: true);
        var vertical = Refine(Pick(verticalScores), points, maskWidth, maskHeight, horizontal: false);
        var ruled = CombineRules(horizontal, vertical);
        if (LongRuleCorrection(ink, width, height) is double precise && Math.Abs(precise - ruled.Degrees) <= 1.5)
        {
            return Snap(precise, Math.Max(ruled.Confidence, 0.55));
        }

        return ruled;
    }

    /// <summary>
    /// Pente d'une ligne de mots, en degrés. Les centres suivent la ligne de base, pas le dessin autour.
    /// </summary>
    public static double? LineSlopeDegrees(IReadOnlyList<(double X, double Y, double Width)> words)
    {
        if (words.Count < 3)
        {
            return null;
        }

        var sumW = 0d;
        var sumX = 0d;
        var sumY = 0d;
        var sumXX = 0d;
        var sumXY = 0d;
        foreach (var word in words)
        {
            var weight = Math.Max(1d, word.Width);
            sumW += weight;
            sumX += word.X * weight;
            sumY += word.Y * weight;
            sumXX += word.X * word.X * weight;
            sumXY += word.X * word.Y * weight;
        }

        var variance = (sumW * sumXX) - (sumX * sumX);
        if (variance < 1)
        {
            return null;
        }

        var degrees = Math.Atan(((sumW * sumXY) - (sumX * sumY)) / variance) * 180d / Math.PI;
        return Math.Abs(degrees) > 20d ? null : degrees;
    }

    /// <summary>
    /// Correction horaire pour rendre horizontales les lignes de texte. Une ligne trop différente (tampon, logo) est écartée.
    /// </summary>
    public static double? CombineBaselines(IReadOnlyList<(double Degrees, double Width)> lines)
    {
        if (lines.Count == 0)
        {
            return null;
        }

        var median = lines.Select(line => line.Degrees).OrderBy(value => value).ElementAt(lines.Count / 2);
        var sum = 0d;
        var weight = 0d;
        foreach (var line in lines)
        {
            if (Math.Abs(line.Degrees - median) > 1.5)
            {
                continue;
            }

            var lineWeight = Math.Max(1d, line.Width);
            sum += line.Degrees * lineWeight;
            weight += lineWeight;
        }

        if (weight <= 0)
        {
            return null;
        }

        var correction = -(sum / weight);
        return Math.Abs(correction) < PixelDegrees ? 0 : correction;
    }

    /// <summary>
    /// Garde l'angle des traits longs quand il est proche du texte, ou quand le texte est resté plat.
    /// Les boîtes OCR sont horizontales : une pente de mots proche de 0 ne veut pas dire que la page est droite.
    /// </summary>
    public static double Merge(double textDegrees, RuledAngle ruled)
    {
        if (ruled.Confidence < 0.18 || Math.Abs(ruled.Degrees) > MaxFineDegrees)
        {
            return textDegrees;
        }

        var gap = Math.Abs(ruled.Degrees - textDegrees);
        if (gap <= 2.5 || (Math.Abs(textDegrees) <= 0.8 && Math.Abs(ruled.Degrees) > 1d))
        {
            return ruled.Degrees;
        }

        // Une boîte OCR coupe le mot penché : l'encre qui reste sous-estime la pente.
        // Le filet, du même côté et plus incliné, est le bord de la feuille.
        var sameSide = textDegrees * ruled.Degrees > 0;
        var flattened = Math.Abs(textDegrees) + 1.5 < Math.Abs(ruled.Degrees);
        if (sameSide && flattened && ruled.Confidence >= 0.4 && gap <= 8d)
        {
            return ruled.Degrees;
        }

        return textDegrees;
    }

    /// <summary>
    /// Aligne le texte sur l'horizontale. L'angle vient des lignes de caractères, pas d'une recherche sur toute l'encre.
    /// </summary>
    public static (int QuarterTurns, double SkewDegrees) SuggestOrientation(bool[] ink, int width, int height)
    {
        if (TryTextCorrection(ink, width, height) is double text)
        {
            return PageRotation.Split(text);
        }

        var upright = BestAngle(ink, width, height);
        var bestTurns = 0;
        var bestSkew = upright.Angle;
        var bestScore = upright.Score;
        for (var turn = 1; turn < 4; turn++)
        {
            var rotated = RotateQuarter(ink, width, height, turn);
            var candidate = BestAngle(rotated.Ink, rotated.Width, rotated.Height);
            if (candidate.Score > bestScore)
            {
                bestScore = candidate.Score;
                bestTurns = turn;
                bestSkew = candidate.Angle;
            }
        }

        if (bestTurns != 0 && upright.Score > 0 && bestScore < upright.Score * 1.25)
        {
            bestTurns = 0;
            bestSkew = upright.Angle;
        }

        if (Math.Abs(bestSkew) < PixelDegrees)
        {
            bestSkew = 0;
        }

        return (bestTurns, bestSkew);
    }

    private readonly record struct Glyph(double X, double Y, int Width, int Height);

    /// <summary>
    /// Angle horaire à appliquer pour rendre les lignes de texte horizontales.
    /// </summary>
    private static double? TryTextCorrection(bool[] ink, int width, int height)
    {
        var glyphs = ReadGlyphs(ink, width, height);
        if (glyphs.Count < 8)
        {
            return null;
        }

        var samples = new List<double>();
        for (var index = 0; index < glyphs.Count; index++)
        {
            var glyph = glyphs[index];
            var bestDistance = double.MaxValue;
            var bestAngle = 0d;
            var found = false;
            for (var other = 0; other < glyphs.Count; other++)
            {
                if (other == index)
                {
                    continue;
                }

                var neighbor = glyphs[other];
                var scale = (glyph.Height + neighbor.Height) / 2d;
                if (scale < 1)
                {
                    continue;
                }

                var heightGap = Math.Abs(glyph.Height - neighbor.Height) / scale;
                if (heightGap > 0.65)
                {
                    continue;
                }

                var dx = neighbor.X - glyph.X;
                var dy = neighbor.Y - glyph.Y;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));
                if (distance < scale * 0.8 || distance > scale * 14 || distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestAngle = NormalizeLine(Math.Atan2(dy, dx) * 180d / Math.PI);
                found = true;
            }

            if (found)
            {
                samples.Add(bestAngle);
            }
        }

        if (samples.Count < 6)
        {
            return null;
        }

        var sumSin = 0d;
        var sumCos = 0d;
        foreach (var sample in samples)
        {
            var doubled = sample * 2d * Math.PI / 180d;
            sumSin += Math.Sin(doubled);
            sumCos += Math.Cos(doubled);
        }

        var concentration = Math.Sqrt((sumSin * sumSin) + (sumCos * sumCos)) / samples.Count;
        if (concentration < 0.45)
        {
            return null;
        }

        var direction = 0.5 * Math.Atan2(sumSin, sumCos) * 180d / Math.PI;
        var correction = -direction - ResidualSlope(glyphs, width, height, -direction);
        return Math.Abs(correction) < PixelDegrees ? 0 : correction;
    }

    private static double ResidualSlope(List<Glyph> glyphs, int width, int height, double degreesClockwise)
    {
        var centerX = width / 2d;
        var centerY = height / 2d;
        var points = glyphs
            .Select(glyph => Turn(glyph.X, glyph.Y, degreesClockwise, centerX, centerY))
            .OrderBy(point => point.Y)
            .ToList();
        var medianHeight = glyphs.OrderBy(glyph => glyph.Height).ElementAt(glyphs.Count / 2).Height;
        var gap = Math.Max(3, medianHeight * 0.75);
        var lines = new List<List<(double X, double Y)>>();
        foreach (var point in points)
        {
            if (lines.Count == 0 || point.Y - lines[^1][^1].Y > gap)
            {
                lines.Add([]);
            }

            lines[^1].Add(point);
        }

        var weight = 0d;
        var slope = 0d;
        foreach (var line in lines)
        {
            if (line.Count < 4)
            {
                continue;
            }

            var meanX = line.Average(point => point.X);
            var meanY = line.Average(point => point.Y);
            var varianceX = 0d;
            var covariance = 0d;
            foreach (var point in line)
            {
                var dx = point.X - meanX;
                varianceX += dx * dx;
                covariance += dx * (point.Y - meanY);
            }

            if (varianceX < 4)
            {
                continue;
            }

            var degrees = Math.Atan(covariance / varianceX) * 180d / Math.PI;
            slope += degrees * line.Count;
            weight += line.Count;
        }

        return weight < 4 ? 0 : slope / weight;
    }

    private static List<Glyph> ReadGlyphs(bool[] ink, int width, int height)
    {
        var seen = new bool[ink.Length];
        var glyphs = new List<Glyph>();
        var stack = new Stack<int>();
        for (var start = 0; start < ink.Length; start++)
        {
            if (!ink[start] || seen[start])
            {
                continue;
            }

            seen[start] = true;
            stack.Push(start);
            var area = 0;
            var sumX = 0;
            var sumY = 0;
            var minX = width;
            var minY = height;
            var maxX = 0;
            var maxY = 0;
            var touchesBorder = false;
            while (stack.Count > 0)
            {
                var index = stack.Pop();
                var x = index % width;
                var y = index / width;
                area++;
                sumX += x;
                sumY += y;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                touchesBorder |= x == 0 || y == 0 || x == width - 1 || y == height - 1;
                Push(x - 1, y);
                Push(x + 1, y);
                Push(x, y - 1);
                Push(x, y + 1);
            }

            var boxWidth = maxX - minX + 1;
            var boxHeight = maxY - minY + 1;
            if (touchesBorder || area < 6 || boxWidth < 2 || boxHeight < 3)
            {
                continue;
            }

            if (boxHeight > height * 0.14 || boxWidth > width * 0.22)
            {
                continue;
            }

            var ratio = Math.Max(boxWidth, boxHeight) / (double)Math.Max(1, Math.Min(boxWidth, boxHeight));
            if (ratio > 6)
            {
                continue;
            }

            glyphs.Add(new Glyph(sumX / (double)area, sumY / (double)area, boxWidth, boxHeight));
        }

        return glyphs;

        void Push(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return;
            }

            var index = (y * width) + x;
            if (seen[index] || !ink[index])
            {
                return;
            }

            seen[index] = true;
            stack.Push(index);
        }
    }

    private static double NormalizeLine(double degrees)
    {
        while (degrees > 90d)
        {
            degrees -= 180d;
        }

        while (degrees <= -90d)
        {
            degrees += 180d;
        }

        return degrees;
    }

    private static (double X, double Y) Turn(double x, double y, double degreesClockwise, double centerX, double centerY)
    {
        var rad = degreesClockwise * Math.PI / 180d;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var dx = x - centerX;
        var dy = y - centerY;
        return (centerX + (dx * cos) - (dy * sin), centerY + (dx * sin) + (dy * cos));
    }

    private static (double Angle, double Score) BestAngle(bool[] ink, int width, int height)
    {
        if (width < 4 || height < 4)
        {
            return (0, 0);
        }

        var bestAngle = 0d;
        var bestScore = double.MinValue;
        for (var step = -180; step <= 180; step++)
        {
            var angle = step / 4d;
            var score = ProjectionVariance(ink, width, height, angle);
            if (score > bestScore || (score == bestScore && Math.Abs(angle) < Math.Abs(bestAngle)))
            {
                bestScore = score;
                bestAngle = angle;
            }
        }

        return (bestAngle, bestScore);
    }

    private static (bool[] Ink, int Width, int Height) RotateQuarter(bool[] ink, int width, int height, int turns)
    {
        turns &= 3;
        if (turns == 0)
        {
            return (ink, width, height);
        }

        var destinationWidth = turns % 2 == 0 ? width : height;
        var destinationHeight = turns % 2 == 0 ? height : width;
        var destination = new bool[destinationWidth * destinationHeight];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!ink[(y * width) + x])
                {
                    continue;
                }

                var (dx, dy) = turns switch
                {
                    1 => (height - 1 - y, x),
                    2 => (width - 1 - x, height - 1 - y),
                    _ => (y, width - 1 - x),
                };
                destination[(dy * destinationWidth) + dx] = true;
            }
        }

        return (destination, destinationWidth, destinationHeight);
    }

    private static (double X, double Y) MapRaw(double x, double y, int width, int height, double degreesClockwise)
    {
        var rad = degreesClockwise * Math.PI / 180d;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var cx = width / 2d;
        var cy = height / 2d;
        var dx = x - cx;
        var dy = y - cy;
        return (cx + (dx * cos) - (dy * sin), cy + (dx * sin) + (dy * cos));
    }

    private static (double X, double Y) Origin(int width, int height, double degreesClockwise)
    {
        var corners = new[]
        {
            MapRaw(0, 0, width, height, degreesClockwise),
            MapRaw(width, 0, width, height, degreesClockwise),
            MapRaw(0, height, width, height, degreesClockwise),
            MapRaw(width, height, width, height, degreesClockwise),
        };
        return (corners.Min(point => point.X), corners.Min(point => point.Y));
    }

    private static double ProjectionVariance(bool[] ink, int width, int height, double degreesClockwise)
    {
        var bins = new int[height + width + 4];
        var offset = width + 2;
        var count = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!ink[(y * width) + x])
                {
                    continue;
                }

                var mapped = MapRaw(x, y, width, height, degreesClockwise);
                var bin = offset + (int)Math.Round(mapped.Y);
                if (bin < 0 || bin >= bins.Length)
                {
                    continue;
                }

                bins[bin]++;
                count++;
            }
        }

        if (count == 0)
        {
            return 0;
        }

        var mean = count / (double)bins.Length;
        var variance = 0d;
        foreach (var value in bins)
        {
            var delta = value - mean;
            variance += delta * delta;
        }

        return variance;
    }

    private readonly record struct AxisPeak(double Degrees, double Score, double Confidence);

    /// <summary>Encre fine seulement : le cœur d'un aplat ne vote pas, le cadre du fichier non plus.</summary>
    private static (bool[] Mask, int Width, int Height) ThinRules(bool[] ink, int width, int height)
    {
        var factor = Math.Max(1, (int)Math.Ceiling(Math.Max(width, height) / 480d));
        var maskWidth = Math.Max(1, width / factor);
        var maskHeight = Math.Max(1, height / factor);
        var mask = new bool[maskWidth * maskHeight];
        for (var y = 0; y < maskHeight; y++)
        {
            for (var x = 0; x < maskWidth; x++)
            {
                var found = false;
                var yEnd = Math.Min(height, (y + 1) * factor);
                var xEnd = Math.Min(width, (x + 1) * factor);
                for (var sy = y * factor; sy < yEnd && !found; sy++)
                {
                    var row = sy * width;
                    for (var sx = x * factor; sx < xEnd; sx++)
                    {
                        if (ink[row + sx])
                        {
                            found = true;
                            break;
                        }
                    }
                }

                mask[(y * maskWidth) + x] = found;
            }
        }

        var stride = maskWidth + 1;
        var sat = new int[stride * (maskHeight + 1)];
        for (var y = 0; y < maskHeight; y++)
        {
            var rowSum = 0;
            var row = y * maskWidth;
            for (var x = 0; x < maskWidth; x++)
            {
                rowSum += mask[row + x] ? 1 : 0;
                sat[((y + 1) * stride) + x + 1] = sat[(y * stride) + x + 1] + rowSum;
            }
        }

        var margin = Math.Max(2, Math.Min(maskWidth, maskHeight) / 40);
        const int radius = 4;
        for (var y = 0; y < maskHeight; y++)
        {
            for (var x = 0; x < maskWidth; x++)
            {
                var index = (y * maskWidth) + x;
                if (!mask[index] || x < margin || y < margin || x >= maskWidth - margin || y >= maskHeight - margin)
                {
                    mask[index] = false;
                    continue;
                }

                var x0 = Math.Max(0, x - radius);
                var y0 = Math.Max(0, y - radius);
                var x1 = Math.Min(maskWidth - 1, x + radius);
                var y1 = Math.Min(maskHeight - 1, y + radius);
                var area = (x1 - x0 + 1) * (y1 - y0 + 1);
                var fill = (sat[((y1 + 1) * stride) + x1 + 1] - sat[((y1 + 1) * stride) + x0] - sat[(y0 * stride) + x1 + 1] + sat[(y0 * stride) + x0]) / (double)area;
                if (fill >= 0.72)
                {
                    mask[index] = false;
                }
            }
        }

        return (mask, maskWidth, maskHeight);
    }

    private static (double Horizontal, double Vertical) RuleScores(List<(int X, int Y)> points, int width, int height, double degreesClockwise)
    {
        var binCount = height + width + 8;
        var horizontalCount = new int[binCount];
        var verticalCount = new int[binCount];
        var horizontalOffset = width + 4;
        var verticalOffset = height + 4;
        foreach (var (x, y) in points)
        {
            var mapped = MapRaw(x, y, width, height, degreesClockwise);
            var horizontalBin = horizontalOffset + (int)Math.Round(mapped.Y);
            var verticalBin = verticalOffset + (int)Math.Round(mapped.X);
            if ((uint)horizontalBin < (uint)binCount)
            {
                horizontalCount[horizontalBin]++;
            }

            if ((uint)verticalBin < (uint)binCount)
            {
                verticalCount[verticalBin]++;
            }
        }

        // Le pic doit couvrir une vraie portion de page : le bord d'un tampon reste en dessous.
        var horizontalNeed = Math.Max(12, (int)Math.Round(width * 0.34));
        var verticalNeed = Math.Max(12, (int)Math.Round(height * 0.28));
        return (DensestBand(horizontalCount, horizontalNeed), DensestBand(verticalCount, verticalNeed));
    }

    /// <summary>Plus fort paquet de pixels sur trois rangées. Un filet aligné s'y empile ; un arc de tampon, non.</summary>
    private static double DensestBand(int[] count, int minCount)
    {
        var best = 0;
        for (var index = 1; index < count.Length - 1; index++)
        {
            var window = count[index - 1] + count[index] + count[index + 1];
            if (window > best)
            {
                best = window;
            }
        }

        return best >= minCount ? best : 0;
    }

    private static AxisPeak Pick(double[] scores)
    {
        var bestIndex = 0;
        for (var index = 1; index < scores.Length; index++)
        {
            if (scores[index] > scores[bestIndex])
            {
                bestIndex = index;
            }
        }

        var best = scores[bestIndex];
        if (best <= 0)
        {
            return new AxisPeak(0, 0, 0);
        }

        var ordered = (double[])scores.Clone();
        Array.Sort(ordered);
        var median = ordered[ordered.Length / 2];
        var confidence = Math.Clamp((best - median) / best, 0, 1);
        var degrees = (bestIndex - (scores.Length / 2)) / 10d;
        return new AxisPeak(Math.Abs(degrees) < PixelDegrees ? 0 : degrees, best, confidence);
    }

    private static AxisPeak Refine(AxisPeak peak, List<(int X, int Y)> points, int width, int height, bool horizontal)
    {
        if (peak.Score <= 0)
        {
            return peak;
        }

        var bestDegrees = peak.Degrees;
        var bestScore = peak.Score;
        for (var step = -40; step <= 40; step++)
        {
            var angle = peak.Degrees + (step * 0.005d);
            if (angle < -MaxFineDegrees || angle > MaxFineDegrees)
            {
                continue;
            }

            var (horizontalScore, verticalScore) = RuleScores(points, width, height, angle);
            var score = horizontal ? horizontalScore : verticalScore;
            if (score > bestScore || (score == bestScore && Math.Abs(angle) < Math.Abs(bestDegrees)))
            {
                bestScore = score;
                bestDegrees = angle;
            }
        }

        if (Math.Abs(bestDegrees) < PixelDegrees)
        {
            bestDegrees = 0;
        }

        return peak with { Degrees = bestDegrees, Score = bestScore };
    }

    private static RuledAngle CombineRules(AxisPeak horizontal, AxisPeak vertical)
    {
        var horizontalOk = horizontal.Confidence >= 0.18 && horizontal.Score > 0;
        var verticalOk = vertical.Confidence >= 0.18 && vertical.Score > 0;
        if (horizontalOk && verticalOk && Math.Abs(horizontal.Degrees - vertical.Degrees) <= 0.5)
        {
            var score = horizontal.Score + vertical.Score;
            var degrees = ((horizontal.Degrees * horizontal.Score) + (vertical.Degrees * vertical.Score)) / score;
            var confidence = Math.Min(1d, ((horizontal.Confidence + vertical.Confidence) / 2d) + 0.1);
            return Snap(degrees, confidence);
        }

        var best = horizontal.Score >= vertical.Score ? horizontal : vertical;
        if (best.Confidence < 0.18 || best.Score <= 0)
        {
            return new RuledAngle(0, 0);
        }

        return Snap(best.Degrees, best.Confidence * 0.85);
    }

    private static RuledAngle Snap(double degrees, double confidence)
    {
        if (Math.Abs(degrees) < PixelDegrees)
        {
            degrees = 0;
        }

        return new RuledAngle(degrees, confidence);
    }
}
