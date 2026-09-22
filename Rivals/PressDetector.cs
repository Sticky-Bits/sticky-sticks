// Called once per sampled frame, never from painting. Values are unrounded final axes.
sealed class PressDetector(int? travelFrames)
{
    sealed class Axis
    {
        int sinceZero = int.MaxValue;
        bool armed;

        public int Sample(double value, double threshold, int? limit)
        {
            if (!double.IsFinite(value)) { Reset(); return 0; }
            if (value == 0)
            {
                sinceZero = 0;
                armed = true;
                return 0;
            }
            if (sinceZero < int.MaxValue) sinceZero++;
            if (!armed) return 0;
            // The four samples after the most recent zero are eligible, including sample four.
            if (limit.HasValue && sinceZero > limit.Value) { armed = false; return 0; }
            if (Math.Abs(value) < threshold) return 0;
            armed = false;
            return Math.Sign(value);
        }

        public void Reset() { sinceZero = int.MaxValue; armed = false; }
    }

    readonly Axis x = new(), y = new();
    int remaining;
    public string Text { get; private set; } = "None";
    public bool Ambiguous { get; private set; }

    public void Reset()
    {
        x.Reset(); y.Reset(); remaining = 0; Text = "None"; Ambiguous = false;
    }

    public void Sample(double finalX, double finalY, double threshold)
    {
        if (remaining > 0 && --remaining == 0) { Text = "None"; Ambiguous = false; }
        int horizontal = travelFrames.HasValue ? x.Sample(finalX, threshold, travelFrames) : HeldDirection(finalX, threshold);
        int vertical = travelFrames.HasValue ? y.Sample(finalY, threshold, travelFrames) : HeldDirection(finalY, threshold);
        if (horizontal == 0 && vertical == 0) return;
        // Keep tracking axes during the hold, but never overwrite or queue later events.
        if (remaining > 0) return;
        string verticalName = vertical > 0 ? "Up" : "Down";
        string horizontalName = horizontal > 0 ? "Right" : "Left";
        // Only compare eligible directions, using exact, unrounded final magnitudes.
        Ambiguous = horizontal != 0 && vertical != 0 && Math.Abs(finalX) == Math.Abs(finalY);
        Text = Ambiguous ? $"{verticalName} {horizontalName} AMBIGUOUS".ToUpperInvariant()
            : horizontal != 0 && (vertical == 0 || Math.Abs(finalX) > Math.Abs(finalY))
                ? horizontalName : verticalName;
        remaining = 30;
    }

    static int HeldDirection(double value, double threshold) =>
        double.IsFinite(value) && Math.Abs(value) >= threshold ? Math.Sign(value) : 0;

    public static void SelfTest()
    {
        static void Check(PressDetector detector, string expected)
        {
            if (detector.Text != expected) throw new Exception($"Press detection: expected {expected}, got {detector.Text}.");
        }
        var d = new PressDetector(4);
        d.Sample(1, 1, .8); Check(d, "None"); // Must first observe zero.
        d.Sample(0, 0, .8);
        d.Sample(.1, .1, .8); d.Sample(.2, .2, .8); d.Sample(.4, .4, .8);
        d.Sample(-.8, .8, .8); Check(d, "UP LEFT AMBIGUOUS"); // Inclusive fourth frame and threshold.
        for (int i = 0; i < 29; i++) d.Sample(-1, 1, .8);
        Check(d, "UP LEFT AMBIGUOUS");
        d.Sample(-1, 1, .8); Check(d, "None");
        d.Sample(-1, 0, .8); d.Sample(-1, -1, .8); Check(d, "Down");
        d.Sample(0, -1, .8); d.Sample(1, -1, .8); Check(d, "Down"); // Never overwrite during the hold.
        for (int i = 0; i < 30; i++) d.Sample(1, -1, .8);
        Check(d, "None");
        d.Sample(0, -1, .8); d.Sample(1, -1, .8); Check(d, "Right");
        d.Reset(); d.Sample(0, 1, .8);
        for (int i = 0; i < 4; i++) d.Sample(.2, 1, .8);
        d.Sample(1, 1, .8); Check(d, "None"); // Too slow; Y never saw zero.
        d.Sample(1, 0, .8); d.Sample(-1, 0, .8); Check(d, "None"); // Y cannot rearm X.
        d.Reset(); d.Sample(0, 0, .8); d.Sample(1, .2, .8); Check(d, "Right");
        d.Sample(1, 1, .8); Check(d, "Right"); // First crossing wins.
        d.Sample(1, 0, .8); d.Sample(1, 1, .8); Check(d, "Right");
        d.Reset(); d.Sample(0, 0, .8); d.Sample(.79999, 0, .8); Check(d, "None");
        var right = new PressDetector(null);
        right.Sample(0, 0, .5);
        for (int i = 0; i < 10; i++) right.Sample(.1, 0, .5);
        right.Sample(.5, 0, .5); Check(right, "Right");
        for (int i = 0; i < 90; i++) right.Sample(.5, 0, .5);
        Check(right, "Right"); // Held right-stick input continuously renews the display.
        right.Sample(0, 1, .5); Check(right, "Right");
        for (int i = 0; i < 29; i++) right.Sample(0, 1, .5);
        Check(right, "Up");
        for (int i = 0; i < 30; i++) right.Sample(0, 0, .5);
        Check(right, "None");
        foreach (int? limit in new int?[] { 4, null })
        {
            var detector = new PressDetector(limit);
            foreach (var (finalX, finalY, expected) in new[] {
                (.9, -.8, "Right"), (-.8, .9, "Up"), (-.9, .8, "Left"),
                (.8, -.9, "Down"), (.8, .8, "UP RIGHT AMBIGUOUS"),
                (-.8, -.8, "DOWN LEFT AMBIGUOUS"), (.800001, .8, "Right") })
            {
                detector.Reset(); detector.Sample(0, 0, .8);
                detector.Sample(finalX, finalY, .8); Check(detector, expected);
                if (detector.Ambiguous != expected.Contains("AMBIGUOUS")) throw new Exception("Incorrect ambiguity flag.");
                detector.Sample(-1, 0, .8); Check(detector, expected); // Preserve the hold, including tie state.
                for (int i = 0; i < 30; i++) detector.Sample(0, 0, .8);
                Check(detector, "None");
                if (detector.Ambiguous) throw new Exception("Ambiguity did not expire.");
            }
        }
    }
}
