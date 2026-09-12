readonly record struct AnalyzerReading(double LX, double LY, double RX, double RY, bool FreshLeft, bool FreshRight,
    double Time = 0, bool SampleLeft = true, bool SampleRight = true);

interface IAnalyzerStage
{
    string Title { get; }
    string Instructions { get; }
    bool Complete { get; }
    void Observe(AnalyzerReading reading);
}

// Stage completion advances here rather than in the UI. More stages can be appended
// without adding buttons or changing the recording lifecycle.
sealed class AnalyzerRun(params IAnalyzerStage[] stages)
{
    readonly object sync = new();
    int index;
    public bool Complete { get { lock (sync) return index >= stages.Length; } }
    public IAnalyzerStage? Current { get { lock (sync) return index < stages.Length ? stages[index] : null; } }
    public void Observe(AnalyzerReading reading)
    {
        lock (sync)
        {
            if (index >= stages.Length) return;
            stages[index].Observe(reading);
            if (stages[index].Complete) index++;
        }
    }
}

sealed record PerimeterStickSnapshot(double X, double Y, double[] Radii, long[] Attempts, int StableIntervals = 0, int Direction = 0)
{
    public bool Clockwise => StableIntervals < PerimeterSweeps.RequiredIntervals / 2;
    public string DirectionLabel => Clockwise ? "Clockwise" : "Counter clockwise";
    public int Filled => Attempts.Count(n => n > 0);
    public long TotalFills => Attempts.Sum();
    public bool Ready => Filled == PerimeterSweeps.BucketCount && StableIntervals >= PerimeterSweeps.RequiredIntervals;
    public double Completion => Ready ? 1 : .05 * (Filled / (double)PerimeterSweeps.BucketCount) + .95 * (StableIntervals / (double)PerimeterSweeps.RequiredIntervals);
    public double MissingPenalty => (PerimeterSweeps.BucketCount - Filled) / (double)PerimeterSweeps.BucketCount;
    // Score observed radial error and missing coverage separately: a missing angle
    // is not an observed zero radius. Each 1% missing adds one percentage point.
    public double RadialError
    {
        get
        {
            var losses = Radii.Where(double.IsFinite).Select(r => (r - 1) * (r < 1 ? 2 : 1)).ToArray();
            return losses.Length == 0 ? double.NaN : Math.Sqrt(losses.Average(e => e * e));
        }
    }
    public double Error => (double.IsFinite(RadialError) ? RadialError : 0) + MissingPenalty;
    public string Grade => Error switch {
        <= .01 => "A+", <= .02 => "A", <= .04 => "B", <= .07 => "C", <= .12 => "D", _ => "F" };
}

sealed class PerimeterSweeps : IAnalyzerStage
{
    
    public const int BucketCount = 36, RequiredIntervals = 50;
    public static int Bucket(double x, double y)
    {
        double degrees = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        return (int)Math.Floor((degrees + 5) / 10) % BucketCount;
    }
    readonly object sync = new();
    readonly StickBins left = new(), right = new();
    public string Title => "Circular Sweeps";
    public string Instructions => "Roll your stick at medium speed around the perimeter of the gate to record the maximum values produced. Use a mix of clockwise and counter clockwise movement as directed.";
    public bool Complete { get { lock (sync) return left.Ready && right.Ready; } }
    public void Observe(AnalyzerReading reading)
    {
        lock (sync)
        {
            if (reading.SampleLeft) left.Observe(reading.Time, reading.LX, reading.LY, reading.FreshLeft);
            if (reading.SampleRight) right.Observe(reading.Time, reading.RX, reading.RY, reading.FreshRight);
        }
    }
    public (PerimeterStickSnapshot Left, PerimeterStickSnapshot Right) Snapshot()
    { lock (sync) return (left.Snapshot(), right.Snapshot()); }

    sealed class StickBins
    {
        readonly double[] radii = Enumerable.Repeat(double.NaN, BucketCount).ToArray();
        readonly long[] attempts = new long[BucketCount];
        double x, y;
        int filled, stableIntervals, previousBin = -1;
        double windowStart = double.NaN, lastTime = double.NaN;
        bool moved, outer, improved;
        int quadrant = -1, direction;
        public bool Ready => filled == BucketCount && stableIntervals >= RequiredIntervals;
        public void Observe(double time, double inputX, double inputY, bool fresh)
        {
            if (Ready) return;
            if (!double.IsFinite(time) || !double.IsFinite(inputX) || !double.IsFinite(inputY) || time < lastTime) return;
            // Never award elapsed intervals during a gap in input.
            if (!double.IsNaN(lastTime) && time - lastTime > .25)
            { ResetDirection(time); }
            lastTime = time;
            x = inputX; y = inputY;
            double radius = Math.Sqrt(x * x + y * y);
            if (!double.IsFinite(radius)) return;
            if (radius < .5) { ResetDirection(time); return; }
            if (fresh)
            {
                double degrees = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
                int nextQuadrant = (int)(degrees / 90);
                if (quadrant >= 0 && quadrant != nextQuadrant)
                {
                    int step = (nextQuadrant - quadrant + 4) % 4;
                    int expected = stableIntervals < RequiredIntervals / 2 ? 3 : 1;
                    direction = step == 2 ? 0 : step == expected ? 1 : -1;
                }
                quadrant = nextQuadrant;
            }
            if (direction != 1)
            {
                windowStart = filled == BucketCount ? time : double.NaN;
                moved = outer = improved = false; previousBin = -1;
                return;
            }
            if (!double.IsNaN(windowStart) && time - windowStart >= .1 - 1e-9)
            {
                if (moved && outer && !improved)
                {
                    stableIntervals++;
                    if (stableIntervals == RequiredIntervals / 2) { ResetDirection(time); return; }
                }
                windowStart = time; moved = outer = improved = false;
                if (Ready) return;
            }
            if (!double.IsNaN(windowStart) && radius > .9) outer = true;
            if (!fresh || radius == 0) return; // Exact center has no meaningful angle.
            int bin = Bucket(x, y);
            if (!double.IsNaN(windowStart) && previousBin >= 0 && bin != previousBin) moved = true;
            previousBin = bin;
            attempts[bin]++;
            if (double.IsNaN(radii[bin]) || radius > radii[bin])
            {
                if (double.IsNaN(radii[bin])) filled++;
                radii[bin] = radius; improved = true;
            }
            // Coverage earns 5%; confidence starts in a fresh interval afterwards.
            if (filled == BucketCount && double.IsNaN(windowStart))
            { windowStart = time; moved = outer = improved = false; }
        }
        void ResetDirection(double time)
        {
            quadrant = -1; direction = 0; previousBin = -1;
            windowStart = filled == BucketCount ? time : double.NaN;
            moved = outer = improved = false;
        }
        public PerimeterStickSnapshot Snapshot() => new(x, y, radii.ToArray(), attempts.ToArray(), stableIntervals, direction);
    }

    public static void SelfTest()
    {
        static void Require(bool value, string message) { if (!value) throw new Exception("Perimeter: " + message); }
        var stage = new PerimeterSweeps();
        double time = 0, angle = 45;
        void Feed(double radius = 1)
        {
            double a = angle * Math.PI / 180;
            stage.Observe(new(Math.Cos(a) * radius, Math.Sin(a) * radius, 0, 0, true, false, time));
            time += .01;
        }
        Feed(); angle = 135; Feed(1.2);
        Require(stage.Snapshot().Left.Direction == -1 && stage.Snapshot().Left.Filled == 0, "wrong quadrant order must not record");
        angle = 45; Feed();
        Require(stage.Snapshot().Left.Direction == 1, "clockwise adjacent quadrant order");
        // Reverse while still in the same bucket: once direction is confirmed, maxima still work.
        Feed(1.1);
        Require(stage.Snapshot().Left.Radii.Any(r => Math.Abs(r - 1.1) < 1e-12), "same-bucket maximum captured");
        for (int i = 0; i < 2000 && stage.Snapshot().Left.Clockwise; i++) { angle -= 10; Feed(); }
        var halfway = stage.Snapshot().Left;
        Require(halfway.Filled == 36 && halfway.StableIntervals == 25 && !halfway.Ready && !halfway.Clockwise, "clockwise half finishes after 25 intervals");
        Require(Math.Abs(halfway.Completion - .525) < 1e-12, "5 percent coverage plus half of timer");
        for (int i = 0; i < 100; i++) { angle -= 10; Feed(1.3); }
        var wrong = stage.Snapshot().Left;
        Require(wrong.Direction == -1 && wrong.StableIntervals == 25 && wrong.Radii.SequenceEqual(halfway.Radii), "wrong-way sweep must freeze timer and maxima");
        for (int i = 0; i < 2000 && !stage.Snapshot().Left.Ready; i++) { angle += 10; Feed(); }
        Require(stage.Snapshot().Left.Ready && !stage.Complete, "counter-clockwise half and independent sticks");
        var next = new TestStage(); var run = new AnalyzerRun(stage, next);
        for (int i = 0; i < 5000 && !stage.Complete; i++)
        {
            angle += stage.Snapshot().Right.Clockwise ? -10 : 10;
            double a = angle * Math.PI / 180;
            run.Observe(new(0, 0, Math.Cos(a), Math.Sin(a), false, true, time)); time += .01;
        }
        Require(stage.Complete && run.Current == next, "both sticks advance automatically");
        run.Observe(default); Require(run.Complete, "final stage progression");
    }
    sealed class TestStage : IAnalyzerStage
    {
        public string Title => "Test";
        public string Instructions => "Test";
        public bool Complete { get; private set; }
        public void Observe(AnalyzerReading reading) => Complete = true;
    }
}

