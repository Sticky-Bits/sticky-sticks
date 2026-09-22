sealed record PollingSnapshot(double Progress, string Result, string Guidance, bool Started = false);

sealed class PollingRate(bool reportStream = false) : IAnalyzerStage
{
    readonly object sync = new();
    double start = double.NaN, lastTime = double.NaN;
    double lx, ly, rx, ry;
    long updates;
    bool hasPrevious, complete, leftReady, rightReady;
    public string Title => "Fast Movement";
    public string Instructions => "Move your sticks as quickly as possible in circles.";
    public bool Complete { get { lock (sync) return complete; } }
    public PollingSnapshot Snapshot()
    {
        lock (sync)
        {
            double elapsed = hasPrevious ? lastTime - start : 0;
            double hz = elapsed > 0 ? updates / elapsed : 0;
            return new(complete ? 1 : Math.Clamp(elapsed / 8, 0, 1),
                $"Estimated polling rate: {hz:F0} Hz", hasPrevious ? "Keep both sticks spinning · measuring for 8 seconds"
                    : "Move both sticks beyond 0.8 to start measuring", hasPrevious);
        }
    }
    public void Observe(AnalyzerReading reading)
    {
        lock (sync)
        {
            double time = reading.Time;
            if (complete || !double.IsFinite(time) || time < lastTime) return;
            if (!hasPrevious)
            {
                lastTime = time;
                leftReady |= Math.Sqrt(reading.LX * reading.LX + reading.LY * reading.LY) > .8;
                rightReady |= Math.Sqrt(reading.RX * reading.RX + reading.RY * reading.RY) > .8;
                if (!leftReady || !rightReady) return;
                start = time;
            }
            // Count a changed four-axis state once, not once per axis. Unchanged
            // query heartbeats add elapsed time but never count as updates.
            bool changed = reading.LX != lx || reading.LY != ly || reading.RX != rx || reading.RY != ry;
            if (hasPrevious && (reportStream || changed)) updates++;
            lx = reading.LX; ly = reading.LY; rx = reading.RX; ry = reading.RY;
            hasPrevious = true; lastTime = time;
            complete = time - start >= 8;
        }
    }
    public static void SelfTest()
    {
        var stage = new PollingRate();
        stage.Observe(new(.8, 0, .8, 0, true, true, -30));
        stage.Observe(new(1, 0, 0, 0, true, true, -20));
        stage.Observe(new(0, 0, 0, 0, false, false, -10));
        if (stage.Snapshot().Started || stage.Snapshot().Progress != 0 || stage.Complete)
            throw new Exception("Polling must wait for both sticks to exceed .8, excluding all waiting time.");
        // Poll at 1000 Hz; new, deliberately irregular states arrive 200 times/sec.
        for (int i = 0; i <= 8000; i++)
        {
            int state = 1 + i / 10 * 2 + (i % 10 >= 3 ? 1 : 0);
            stage.Observe(new(state, state, state, state, true, true, i * .001));
        }
        if (!stage.Complete || !stage.Snapshot().Result.Contains("200 Hz"))
            throw new Exception("Polling rate must count changed states per second, irrespective of timing regularity.");
        var idle = new PollingRate();
        var reports = new PollingRate(true);
        for (int i = 0; i <= 2000; i++)
        {
            var reading = new AnalyzerReading(1, 0, 1, 0, false, false, i * .004);
            idle.Observe(reading); reports.Observe(reading);
        }
        if (!idle.Complete || !idle.Snapshot().Result.Contains(": 0 Hz") || !reports.Snapshot().Result.Contains("250 Hz"))
            throw new Exception("Duplicate queries must be excluded; actual unchanged USB reports must count.");
    }
}
