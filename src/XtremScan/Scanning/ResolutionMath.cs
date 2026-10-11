namespace XtremScan.Scanning;

/// <summary>
/// Aligne une résolution sur la plage réellement acceptée par le pilote.
/// </summary>
public static class ResolutionMath
{
    /// <summary>
    /// Au-delà de ce nombre, la liste reste valide mais l'UI passe sur un champ numérique
    /// (un pas de 1 entre 50 et 4800 ferait des milliers d'entrées). Ce n'est pas un plafond de dpi.
    /// </summary>
    public const int ComboBoxValueLimit = 48;

    public static int SnapToStep(int value, int minimum, int maximum, int step)
    {
        if (maximum < minimum)
        {
            (minimum, maximum) = (maximum, minimum);
        }

        if (step < 1)
        {
            step = 1;
        }

        if (value <= minimum)
        {
            return minimum;
        }

        if (value >= maximum)
        {
            return maximum;
        }

        var steps = (value - minimum) / step;
        return minimum + (steps * step);
    }

    public static IReadOnlyList<int>? Expand(int minimum, int maximum, int step)
    {
        if (step < 1)
        {
            step = 1;
        }

        if (maximum < minimum)
        {
            return [minimum];
        }

        var count = ((maximum - minimum) / step) + 1;
        if (count > ComboBoxValueLimit)
        {
            return null;
        }

        var values = new List<int>(count);
        for (var dpi = minimum; dpi <= maximum; dpi += step)
        {
            values.Add(dpi);
        }

        return values;
    }
}
