using System.Globalization;
static class DisplayNumbers
{
    public static string Format(double value, bool roundToThree, bool valid = true)
    {
        if (!valid) return "—";
        int decimals = roundToThree ? 3 : 5;
        double rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0;
        return rounded.ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }
}
