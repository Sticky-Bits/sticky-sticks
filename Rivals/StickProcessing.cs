static class StickProcessing
{
    public const int RightDeadzonePercent = 20;
    public static double RightOutput(double value) => LeftDeadzone(value, RightDeadzonePercent);
    public static double RightThresholdRawBoundary(int thresholdPercent) =>
        HardPressRawBoundary(RightDeadzonePercent, 100, thresholdPercent);
    public static double LeftDeadzoneThreshold(int percent) => Math.Clamp(percent / 100.0, 0, 1);
    public static double MechanicRawBoundary(int deadzonePercent, int sensitivityPercent, double gameThreshold)
    {
        double deadzone = LeftDeadzoneThreshold(deadzonePercent);
        if (sensitivityPercent <= 0 || deadzone >= 1) return double.PositiveInfinity;
        return deadzone + (1 - deadzone) * gameThreshold / (sensitivityPercent / 100.0);
    }
    public static double HardPressRawBoundary(int deadzonePercent, int sensitivityPercent, int hardPressPercent) =>
        MechanicRawBoundary(deadzonePercent, sensitivityPercent, hardPressPercent / 100.0);
    public static double LeftOutput(double value, int deadzonePercent, int sensitivityPercent) =>
        LeftDeadzone(value, deadzonePercent) * sensitivityPercent / 100.0;
    // Remove the deadzone, then linearly map the remaining travel to the full output range.
    public static double LeftDeadzone(double value, int percent)
    {
        double threshold = LeftDeadzoneThreshold(percent);
        if (threshold >= 1 || Math.Abs(value) <= threshold) return 0;
        return Math.CopySign(Math.Clamp((Math.Abs(value) - threshold) / (1 - threshold), 0, 1), value);
    }

    public static void SelfTest()
    {
        static void Check(double actual, double expected)
        {
            if (Math.Abs(actual - expected) > 1e-12 || !double.IsFinite(actual))
                throw new Exception($"Rescaled deadzone: expected {expected}, got {actual}.");
        }
        Check(LeftDeadzone(0.75, 50), 0.5);
        Check(LeftDeadzone(-0.75, 50), -0.5);
        Check(LeftDeadzone(0.85, 85), 0);
        Check(LeftDeadzone(-0.85, 85), 0);
        Check(LeftDeadzone(0.925, 85), 0.5);
        Check(LeftDeadzone(0.09, 8), 0.01 / 0.92);
        Check(LeftDeadzone(0.9, 10), 0.8 / 0.9);
        Check(LeftDeadzone(0.005, 10), 0);
        Check(LeftDeadzone(0, 10), 0);
        Check(LeftDeadzone(1, 85), 1);
        Check(LeftDeadzone(-1, 85), -1);
        Check(LeftDeadzone(0.4, 0), 0.4);
        Check(LeftDeadzone(1, 100), 0);
        Check(LeftOutput(0.4, 50, 200), 0);
        Check(LeftOutput(0.6, 50, 200), 0.4);
        Check(LeftOutput(1, 50, 200), 2);
        Check(LeftOutput(-1, 10, 300), -3);
        Check(HardPressRawBoundary(10, 120, 80), 0.7);
        Check(MechanicRawBoundary(10, 120, .8), .7);
        Check(MechanicRawBoundary(50, 100, .3), .65);
        Check(LeftOutput(-MechanicRawBoundary(50, 200, .8), 50, 200), -.8);
        Check(LeftOutput(HardPressRawBoundary(50, 200, 80), 50, 200), 0.8);
        Check(LeftOutput(-HardPressRawBoundary(50, 200, 80), 50, 200), -0.8);
        Check(HardPressRawBoundary(10, 50, 61), 1.198);
        Check(HardPressRawBoundary(85, 80, 80), 1);
        Check(RightThresholdRawBoundary(5), 0.24);
        Check(RightThresholdRawBoundary(50), 0.6);
        Check(RightThresholdRawBoundary(95), 0.96);
        Check(RightOutput(0.2), 0);
        Check(RightOutput(-0.1), 0);
        Check(RightOutput(0.24), 0.05);
        Check(RightOutput(-0.6), -0.5);
        Check(RightOutput(0.96), 0.95);
        Check(RightOutput(1), 1);
    }
}
