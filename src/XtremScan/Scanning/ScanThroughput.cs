namespace XtremScan.Scanning;

/// <summary>
/// Pourcentage d'après les octets reçus. Le scanner n'annonce pas la taille finale.
/// </summary>
public static class ScanThroughput
{
    public static int Percent(long received, int width, int height)
    {
        var estimate = Math.Max(32_768L, (long)Math.Max(1, width) * Math.Max(1, height) / 4);
        if (received <= 0)
        {
            return 8;
        }

        var ratio = received / (double)estimate;
        var percent = (int)Math.Round(100 * (1 - Math.Exp(-ratio)));
        return Math.Clamp(percent, 8, 99);
    }
}
