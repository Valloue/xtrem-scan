namespace XtremScan.Scanning;

/// <summary>
/// Angle de page en degrés. Les quarts de tour restent exacts ; le reste est un petit angle dans le PDF.
/// </summary>
public static class PageRotation
{
    public static double Wrap(double degrees)
    {
        var wrapped = degrees % 360d;
        if (wrapped > 180d)
        {
            wrapped -= 360d;
        }

        if (wrapped <= -180d)
        {
            wrapped += 360d;
        }

        return wrapped;
    }

    public static (int QuarterTurns, double SkewDegrees) Split(double degrees)
    {
        var wrapped = Wrap(degrees);
        var turns = (int)Math.Round(wrapped / 90d);
        var skew = wrapped - (turns * 90d);
        var quarter = turns % 4;
        if (quarter < 0)
        {
            quarter += 4;
        }

        return (quarter, skew);
    }

    public static double Combine(int quarterTurns, double skewDegrees)
    {
        return Wrap(((quarterTurns & 3) * 90d) + skewDegrees);
    }
}
