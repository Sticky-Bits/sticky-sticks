sealed record AnalyzerScore(double Perimeter, double Snapback, double Jitter, double Centering)
{
    public double Overall => .4 * Perimeter + .4 * Snapback + .1 * Jitter + .1 * Centering;
    public string Grade => Letter(Overall);
    public static string Letter(double score) => score switch
    { >= 95 => "A+", >= 90 => "A", >= 80 => "B", >= 70 => "C", >= 60 => "D", _ => "F" };
    public static AnalyzerScore From(PerimeterStickSnapshot perimeter, FlickStickSnapshot flick) => new(
        Scale(perimeter.Error, [.01, .02, .04, .07, .12]),
        Scale(flick.WorstSnapback, [.01, .03, .06, .10, .20]),
        Scale(flick.AverageJitter, [.00025, .0005, .0015, .0047, .008]),
        Scale(flick.WorstResting, [.005, .01, .02, .04, .08]));
    public static AnalyzerScore Average(AnalyzerScore left, AnalyzerScore right) => new(
        (left.Perimeter + right.Perimeter) / 2, (left.Snapback + right.Snapback) / 2,
        (left.Jitter + right.Jitter) / 2, (left.Centering + right.Centering) / 2);
    // Continuous interpolation preserves the previous rubric's cutoffs without score jumps.
    static double Scale(double error, double[] limits)
    {
        if (!double.IsFinite(error)) return 0;
        double[] values = [0, .. limits, limits[^1] * 2];
        double[] scores = [100, 95, 90, 80, 70, 60, 0];
        error = Math.Max(0, error);
        for (int i = 1; i < values.Length; i++)
            if (error <= values[i])
                return Math.Clamp(scores[i - 1] + (scores[i] - scores[i - 1]) * ((error - values[i - 1]) / (values[i] - values[i - 1])), 0, 100);
        return 0;
    }
    public static void SelfTest()
    {
        if (new AnalyzerScore(100, 0, 100, 100).Overall != 60 || new AnalyzerScore(0, 100, 0, 0).Overall != 40)
            throw new Exception("Analyzer scoring weights failed.");
        if (Scale(0, [.01, .02, .04, .07, .12]) != 100 || Scale(.04, [.01, .02, .04, .07, .12]) != 80 ||
            Scale(.24, [.01, .02, .04, .07, .12]) != 0 || Letter(95) != "A+" || Letter(59.9) != "F")
            throw new Exception("Analyzer score boundaries failed.");
    }
}
