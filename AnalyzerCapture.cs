using System.Diagnostics;

// Owns native reads while active. UI rendering observes Latest, never polls alongside
// this worker. Aggregation is bounded (360 bins per stick), even during a long sweep.
sealed class AnalyzerCapture
{
    readonly ControllerSource source;
    readonly int[] mapping;
    readonly bool[] inversion;
    readonly Action<AnalyzerReading> observe;
    readonly CancellationTokenSource stop = new();
    readonly object sync = new();
    readonly long started = Stopwatch.GetTimestamp();
    Task worker = Task.CompletedTask;
    Task? stopping;
    bool closed, hasPrevious;
    double previousLX, previousLY, previousRX, previousRY;
    long reads, changes;
    double firstRead = -1, lastRead;
    double lastLeftSample = double.NegativeInfinity, lastRightSample = double.NegativeInfinity;
    ControllerFrame? latest;
    string? error;
    public ControllerFrame? Latest => Volatile.Read(ref latest);
    public string? Error => Volatile.Read(ref error);
    public double Elapsed => (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
    public bool IsReportStream => source.HasReportStream;

    public AnalyzerCapture(ControllerSource source, int[] mapping, bool[] inversion, Action<AnalyzerReading> observe)
    {
        if (mapping.Length != 4 || inversion.Length != 4) throw new ArgumentException("Both sticks require four mappings.");
        this.source = source; this.mapping = mapping.ToArray(); this.inversion = inversion.ToArray(); this.observe = observe;
        if (source.HasReportStream) source.ReportReceived += Accept;
        else worker = Task.Factory.StartNew(() =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var frame = source.Read();
                    if (frame == null) { Volatile.Write(ref latest, null); Thread.Sleep(1); }
                    else Accept(frame);
                    Thread.Yield();
                }
            }
            catch (Exception ex) { Volatile.Write(ref error, ex.Message); }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    void Accept(ControllerFrame frame)
    {
        if (mapping.Any(i => i < 0 || i >= frame.Values.Length))
        { Volatile.Write(ref error, "Both sticks need valid X and Y axis mappings."); return; }
        double Axis(int i) => frame.Values[mapping[i]] * (inversion[i] ? -1 : 1);
        double lx = Axis(0), ly = Axis(1), rx = Axis(2), ry = Axis(3);
        if (!double.IsFinite(lx) || !double.IsFinite(ly) || !double.IsFinite(rx) || !double.IsFinite(ry)) return;
        lock (sync)
        {
            if (closed) return;
            Volatile.Write(ref latest, frame);
            reads++;
            double now = (frame.Timestamp - started) / (double)Stopwatch.Frequency;
            if (firstRead < 0) firstRead = now;
            lastRead = now;
            bool freshLeft = !hasPrevious || lx != previousLX || ly != previousLY;
            bool freshRight = !hasPrevious || rx != previousRX || ry != previousRY;
            if (hasPrevious && (freshLeft || freshRight)) changes++;
            // Keep every changed value/report, plus a 1 ms heartbeat for unchanged API
            // state. Holding still must advance timed stages and contribute resting data,
            // without treating millions of identical API polls as physical reports.
            bool sampleLeft = freshLeft || source.HasReportStream || now - lastLeftSample >= .001;
            bool sampleRight = freshRight || source.HasReportStream || now - lastRightSample >= .001;
            if (sampleLeft || sampleRight)
                observe(new(lx, ly, rx, ry, freshLeft || source.HasReportStream, freshRight || source.HasReportStream,
                    now, sampleLeft, sampleRight));
            if (sampleLeft) lastLeftSample = now;
            if (sampleRight) lastRightSample = now;
            previousLX = lx; previousLY = ly; previousRX = rx; previousRY = ry; hasPrevious = true;
        }
    }
    public (double ReadHz, double ChangeHz) Rates()
    {
        lock (sync)
        {
            double duration = lastRead - firstRead;
            return duration > 0 ? ((reads - 1) / duration, changes / duration) : (0, 0);
        }
    }
    public Task StopAsync() { lock (sync) return stopping ??= StopCore(); }
    async Task StopCore()
    {
        source.ReportReceived -= Accept;
        lock (sync) closed = true;
        stop.Cancel(); await worker; stop.Dispose();
    }

    public static async Task SelfTest()
    {
        var source = new TestSource();
        var readings = new List<AnalyzerReading>();
        var capture = new AnalyzerCapture(source, [0, 1, 2, 3], [false, true, false, false], readings.Add);
        await Task.Delay(50); await capture.StopAsync(); await capture.StopAsync();
        if (readings.Count < 2 || readings[0].LY != -.5 || readings[0].RX != -.25 ||
            readings.Skip(1).Any(r => r.FreshLeft || r.FreshRight) || readings[^1].Time <= readings[0].Time)
            throw new Exception("Analyzer: full mapping or duplicate filtering failed.");
        int count = source.Reads; await Task.Delay(10);
        if (source.Reads != count) throw new Exception("Analyzer polling did not stop.");
        var reports = new TestReports(); readings.Clear();
        var stream = new AnalyzerCapture(reports, [0, 1, 2, 3], new bool[4], readings.Add);
        for (int i = 0; i < 20; i++) reports.Emit();
        await stream.StopAsync(); reports.Emit();
        if (readings.Count != 20 || readings.Any(r => !r.FreshLeft || !r.FreshRight))
            throw new Exception("Analyzer USB report subscription failed.");
    }
    sealed class TestSource : ControllerSource
    {
        public int Reads;
        public override string[] Axes => ["LX", "LY", "RX", "RY"];
        public override ControllerFrame Read() { Reads++; return new([.25, .5, -.25, -.5], [1, 2, 3, 4], "Test"); }
    }
    sealed class TestReports : ControllerSource
    {
        public override bool HasReportStream => true;
        public override string[] Axes => ["LX", "LY", "RX", "RY"];
        public override ControllerFrame? Read() => throw new Exception("Do not poll report streams.");
        public void Emit() => Report(new([0, 0, 0, 0], [0, 0, 0, 0], "Test"));
    }
}
