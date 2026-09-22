enum FlickPhase { Hold, Release, Recording, Complete }
readonly record struct FlickPoint(double Time, double X, double Y)
{
    public double Radius => Math.Sqrt(X * X + Y * Y);
}
sealed record FlickResult(int Target, FlickPoint[] Samples, double Snapback, double RestingDistance, double Jitter,
    FlickPoint? SnapbackPoint = null, double JitterX = 0, double JitterY = 0, FlickPoint SettledCenter = default)
{
    public bool HasDetectedSnapback => Snapback > .1 && SnapbackPoint.HasValue;
}
sealed record FlickStickSnapshot(FlickPhase Phase, int Completed, int Target, double HoldProgress, string Notice, FlickResult[] Results)
{
    public double Completion => Completed / 16.0;
    public double WorstSnapback => Results.Length == 0 ? 0 : Results.Max(r => r.Snapback);
    public FlickResult? WorstCentering => Results.MaxBy(r => r.RestingDistance);
    public double WorstResting => WorstCentering?.RestingDistance ?? 0;
    public double AverageJitterX => Results.Length == 0 ? 0 : Results.Average(r => r.JitterX);
    public double AverageJitterY => Results.Length == 0 ? 0 : Results.Average(r => r.JitterY);
    public double AverageJitter => Math.Max(AverageJitterX, AverageJitterY);
}

sealed class Flicks : IAnalyzerStage
{
    public static readonly string[] Directions = ["Top", "Bottom", "Left", "Right", "Top Left", "Bottom Left", "Top Right", "Bottom Right",
        "NNW", "NNE", "ENE", "ESE", "SSE", "SSW", "WSW", "WNW"];
    static readonly double[] Angles = [90, 270, 180, 0, 135, 225, 45, 315, 112.5, 67.5, 22.5, 337.5, 292.5, 247.5, 202.5, 157.5];
    readonly object sync = new();
    readonly StickFlicks left = new(), right = new();
    public string Title => "Perimeter Releases";
    public string Instructions => "Hold your stick inside the circle until it turns green, then release the stick and do not touch it until the next circle appears.";
    public bool Complete { get { lock (sync) return left.Complete && right.Complete; } }
    public static (double X, double Y) TargetVector(int target)
    { double a = Angles[target] * Math.PI / 180; return (Math.Cos(a), Math.Sin(a)); }
    public void Observe(AnalyzerReading reading)
    {
        lock (sync)
        {
            if (reading.SampleLeft) left.Observe(reading.Time, reading.LX, reading.LY);
            if (reading.SampleRight) right.Observe(reading.Time, reading.RX, reading.RY);
        }
    }
    public (FlickStickSnapshot Left, FlickStickSnapshot Right) Snapshot()
    { lock (sync) return (left.Snapshot(), right.Snapshot()); }

    sealed class StickFlicks
    {
        readonly List<FlickResult> results = new();
        readonly List<FlickPoint> recording = new();
        FlickPhase phase;
        double holdStart = double.NaN, previousTime = double.NaN, startRecording, heldX, heldY, progress;
        string notice = "";
        int Target => Math.Min(results.Count, Directions.Length - 1);
        public bool Complete => results.Count == 16;
        public FlickStickSnapshot Snapshot() => new(phase, results.Count, Target, progress, notice, results.ToArray());

        public void Observe(double time, double x, double y)
        {
            if (Complete || !double.IsFinite(time) || !double.IsFinite(x) || !double.IsFinite(y)) return;
            if (!double.IsNaN(previousTime) && time < previousTime) return;
            // A long input gap cannot establish a continuous hold or a fully recorded release.
            if (!double.IsNaN(previousTime) && time - previousTime > .25)
                Reset("Input gap · repeat this direction");
            previousTime = time;
            double radius = Math.Sqrt(x * x + y * y);
            if (phase == FlickPhase.Recording)
            {
                if (time >= startRecording + 1)
                {
                    if (recording.Count < 10 || recording[^1].Time - startRecording < .75)
                    { Reset("Too few samples · repeat this direction"); return; }
                    results.Add(Analyze(recording.ToArray(), heldX, heldY, Target));
                    recording.Clear(); holdStart = double.NaN; progress = 0; notice = "";
                    phase = Complete ? FlickPhase.Complete : FlickPhase.Hold;
                    return;
                }
                if (recording.Count >= 250_000) { Reset("Sample limit · repeat this direction"); return; }
                recording.Add(new(time, x, y)); return;
            }
            if (phase == FlickPhase.Release && radius < .9)
            {
                phase = FlickPhase.Recording; startRecording = time;
                recording.Clear(); recording.Add(new(time, x, y)); return;
            }
            // Exactly .9 has not crossed the recording boundary yet.
            if (phase == FlickPhase.Release && radius == .9) return;
            var target = TargetVector(Target);
            bool inSector = radius > .9 && (x * target.X + y * target.Y) / radius >= Math.Cos(Math.PI / 16);
            if (!inSector) { Reset(notice); return; }
            notice = "";
            heldX = x / radius; heldY = y / radius;
            if (double.IsNaN(holdStart)) holdStart = time;
            progress = Math.Clamp(time - holdStart, 0, 1);
            if (progress >= 1) phase = FlickPhase.Release;
        }
        void Reset(string message)
        { phase = FlickPhase.Hold; holdStart = double.NaN; progress = 0; recording.Clear(); notice = message; }
    }

    public static FlickResult Analyze(FlickPoint[] samples, double heldX, double heldY, int target)
    {
        // The opposite open hemisphere is defined by the actual last held direction.
        FlickPoint? peak = samples.Where(s => s.X * heldX + s.Y * heldY < 0)
            .OrderByDescending(s => s.Radius).Select(s => (FlickPoint?)s).FirstOrDefault();
        double snapback = peak?.Radius ?? 0;
        double recordingStart = samples[0].Time;
        var settled = samples.Where(s => s.Time >= recordingStart + .5 && s.Time < recordingStart + 1).ToArray();
        if (settled.Length == 0) throw new ArgumentException("Flick recording has no settled samples.");
        var center = new FlickPoint(recordingStart + .75, settled.Average(s => s.X), settled.Average(s => s.Y));
        double jitterX = settled.Max(s => s.X) - settled.Min(s => s.X);
        double jitterY = settled.Max(s => s.Y) - settled.Min(s => s.Y);
        return new(target, samples, snapback, center.Radius, Math.Max(jitterX, jitterY), peak, jitterX, jitterY, center);
    }

    public static void SelfTest()
    {
        static void Require(bool condition, string message) { if (!condition) throw new Exception("Flicks: " + message); }
        var samples = Enumerable.Range(0, 100).Select(i => new FlickPoint(i / 100.0,
            i >= 10 ? (i % 2 == 0 ? .01 : -.01) : 0, i < 9 ? .8 : i == 9 ? -.4 : 0)).ToArray();
        var metrics = Analyze(samples, 0, 1, 0);
        Require(Math.Abs(metrics.Snapback - .4) < 1e-12, "snapback must be opposite and untrimmed");
        Require(metrics.HasDetectedSnapback && metrics.SnapbackPoint?.Y == -.4, "retain the actual opposite-hemisphere endpoint");
        Require(!(metrics with { Snapback = .1 }).HasDetectedSnapback, "graphic excludes snapback at or below .1");
        Require(Math.Abs(metrics.RestingDistance) < 1e-12, "settled centering must average the position within each flick");
        Require(Math.Abs(metrics.JitterX - .02) < 1e-12 && metrics.JitterY == 0, "jitter range must span positive and negative values");
        var diagonal = Analyze([new(0, -.2, .3), new(.6, -.3, -.4)], Math.Sqrt(.5), Math.Sqrt(.5), 6);
        Require(Math.Abs(diagonal.Snapback - .5) < 1e-12, "diagonal hemisphere test");
        var f = new Flicks();
        void Feed(double t, double lx, double ly, double rx = 0, double ry = 0) => f.Observe(new(lx, ly, rx, ry, true, true, t));
        for (int i = 0; i <= 50; i++) Feed(i * .01, 0, 1);
        Feed(.51, 1, 0); // Wrong octant resets the hold.
        for (int i = 52; i <= 120; i++) Feed(i * .01, 0, 1);
        Require(f.Snapshot().Left.Phase == FlickPhase.Hold, "hold must be continuous in the target octant");
        for (int i = 121; i <= 154; i++) Feed(i * .01, 0, 1);
        Require(f.Snapshot().Left.Phase == FlickPhase.Release, "one-second hold must arm release");
        Feed(1.55, 0, .89);
        Require(f.Snapshot().Left.Phase == FlickPhase.Recording, "crossing below .9 must start recording immediately");
        for (int i = 156; i <= 256; i++) Feed(i * .01, 0, 0);
        Require(f.Snapshot().Left.Completed == 1 && f.Snapshot().Right.Completed == 0, "sticks must progress independently");
        Require(f.Snapshot().Left.Results[0].Samples[0].Y == .89, "release-crossing sample must be retained");
        // Complete all directions on the left before moving the right.
        double time = 2.57;
        for (int side = 0; side < 2; side++)
        {
            while ((side == 0 ? f.Snapshot().Left : f.Snapshot().Right).Phase != FlickPhase.Complete)
            {
                var state = side == 0 ? f.Snapshot().Left : f.Snapshot().Right;
                var v = TargetVector(state.Target);
                for (int i = 0; i < 110; i++, time += .01)
                    Feed(time, side == 0 ? v.X : 0, side == 0 ? v.Y : 0, side == 1 ? v.X : 0, side == 1 ? v.Y : 0);
                for (int i = 0; i < 110; i++, time += .01) Feed(time, 0, 0);
            }
            if (side == 0) Require(!f.Complete, "both sticks need 16 releases");
        }
        Require(f.Complete && f.Snapshot().Left.Results.Select(r => r.Target).SequenceEqual(Enumerable.Range(0, 16)), "sixteen directions once in order");
        Require(Angles.Distinct().Count() == 16 && TargetVector(8).X < 0 && TargetVector(8).Y > 0, "unique targets and NNW orientation");
        var summary = new FlickStickSnapshot(FlickPhase.Complete, 16, 0, 0, "",
            [metrics, metrics with { Snapback = .01, RestingDistance = .03, Jitter = 0 }]);
        Require(summary.WorstSnapback == .4 && Math.Abs(summary.WorstResting - .03) < 1e-12, "summary must use worst snapback and worst centering");
    }
}
