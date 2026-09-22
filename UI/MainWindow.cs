using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

static class MainWindow
{
    public static void Run(int seconds)
    {
        Application.EnableVisualStyles();
        using var window = new Form
        {
            Text = "Sticky Sticks",
            ClientSize = new Size(1440, 1080), MinimumSize = new Size(1200, 930),
            StartPosition = FormStartPosition.CenterScreen
        };
        DarkTheme.StyleWindow(window);
        var session = new ControllerSession();
        using var view = new StickView(session) { Dock = DockStyle.Fill };
        using var tuning = new TuningControls { Dock = DockStyle.Bottom };
        using var pages = new UtilityPages(view, tuning) { Dock = DockStyle.Fill };
        window.Controls.Add(pages);
        using var selector = new ControllerToolbar(view) { Dock = DockStyle.Top };
        window.Controls.Add(selector);
        using var stop = new CancellationTokenSource();
        bool closing = false, cleaned = false;
        window.FormClosing += async (_, e) =>
        {
            if (cleaned) return;
            e.Cancel = true;
            if (closing) return;
            closing = true; stop.Cancel();
            await selector.CloseSource();
            SdlSource.Shutdown();
            cleaned = true; window.Close();
        };
        window.Shown += async (_, _) =>
        {
            _ = selector.RefreshControllers();
            bool highResolution = timeBeginPeriod(1) == 0;
            var clock = Stopwatch.StartNew();
            string? timingPath = Environment.GetEnvironmentVariable("STICKYSTICKS_FRAME_LOG");
            var timings = timingPath == null ? null : new List<string> { "start_ms,read_and_calc_ms,paint_ms,total_work_ms,paints,connected" };
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    double startMs = clock.Elapsed.TotalMilliseconds;
                    long paintsBefore = view.PaintCount;
                    // One state read drives both dots and all numeric values for this frame.
                    view.LeftDeadzonePercent = tuning.LeftDeadzonePercent;
                    view.LeftSensitivityPercent = tuning.LeftSensitivityPercent;
                    view.HardPressPercent = tuning.HardPressPercent;
                    view.RightThresholdPercent = tuning.RightThresholdPercent;
                    selector.Enabled = !view.Session.AnalyzerBusy;
                    view.Sample();
                    double sampledMs = clock.Elapsed.TotalMilliseconds;
                    view.Invalidate();
                    view.Update();
                    double paintedMs = clock.Elapsed.TotalMilliseconds;
                    view.RecordRenderedFrame(view.PaintCount > paintsBefore);
                    timings?.Add(FormattableString.Invariant($"{startMs:F6},{sampledMs - startMs:F6},{paintedMs - sampledMs:F6},{paintedMs - startMs:F6},{view.PaintCount - paintsBefore},{view.HasFrame}"));
                    if (seconds > 0 && clock.Elapsed.TotalSeconds >= seconds) { window.Close(); break; }
                    // Schedule against a fixed 60 Hz clock; skip missed frames instead of queuing them.
                    double next = (Math.Floor(clock.Elapsed.TotalSeconds * 60) + 1) / 60;
                    // Millisecond timer rounding can wake early. Never sample twice in one slot.
                    double remainingSeconds;
                    while ((remainingSeconds = next - clock.Elapsed.TotalSeconds) > 0)
                        await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(remainingSeconds * 1000))), stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            finally
            {
                if (highResolution) timeEndPeriod(1);
                if (timings != null) File.WriteAllLines(timingPath!, timings);
            }
        };
        Application.Run(window);
    }

    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint period);
}
