using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

static class XInputWindow
{
    public static void Run(int seconds)
    {
        Application.EnableVisualStyles();
        using var window = new Form
        {
            Text = "Sticky Sticks",
            ClientSize = new Size(1100, 1080), MinimumSize = new Size(1000, 930),
            StartPosition = FormStartPosition.CenterScreen
        };
        DarkTheme.StyleWindow(window);
        using var view = new StickView { Dock = DockStyle.Fill };
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
            await selector.RefreshControllers();
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
                    selector.Enabled = !view.AnalyzerBusy;
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

sealed class StickView : Control
{
    readonly Font title = new("Segoe UI", 27, FontStyle.Bold);
    readonly Font label = new("Segoe UI", 11, FontStyle.Bold);
    readonly Font numbers = new("Consolas", 16);
    readonly Font small = new("Consolas", 10);
    readonly Color muted = Color.FromArgb(148, 160, 181);
    readonly Color border = Color.FromArgb(49, 58, 75);
    ControllerFrame? frame;
    public AnalyzerCapture? AnalyzerRecording;
    public Func<Task>? StopAnalyzer;
    public bool AnalyzerBusy;
    public event Action<ControllerFrame?>? Sampled;
    public long PaintCount { get; private set; }
    public bool HasFrame => frame != null;
    readonly Stopwatch fpsClock = Stopwatch.StartNew();
    int renderedFrames;
    double actualFps;
    public void RecordRenderedFrame(bool painted)
    {
        if (painted) renderedFrames++;
        double elapsed = fpsClock.Elapsed.TotalSeconds;
        if (elapsed < 1) return;
        actualFps = renderedFrames / elapsed;
        renderedFrames = 0;
        fpsClock.Restart();
    }
    readonly PressDetector leftPress = new(4), rightPress = new(null);
    ControllerSource? detectionSource;
    string detectionSettings = "";
    public int LeftDeadzonePercent = 10;
    public int LeftSensitivityPercent = 120;
    public int HardPressPercent = 80;
    public int RightThresholdPercent = 35;
    public bool RoundDisplay = true;
    public bool OctagonalGate;
    public bool IsScanning;
    public bool IsConnecting;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ControllerSource? Source { get; set; }
    public int[] Mapping { get; } = [0, 1, 2, 3];
    public bool[] Invert { get; } = new bool[4];
    bool focused;
    public StickView()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(10, 13, 19);
        ForeColor = Color.FromArgb(242, 245, 251);
    }

    public void Sample()
    {
        // Discovery and opening can touch the same native backend; don't poll concurrently.
        frame = IsScanning || IsConnecting ? null : AnalyzerRecording != null ? AnalyzerRecording.Latest : Source?.Read();
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        focused = pid == Environment.ProcessId;
        UpdatePressDetection();
        Sampled?.Invoke(frame);
    }

    double RawAxis(int axis)
    {
        int index = Mapping[axis];
        return frame != null && index >= 0 && index < frame.Values.Length
            ? frame.Values[index] * (Invert[axis] ? -1 : 1) : double.NaN;
    }

    double FinalAxis(int axis, double raw) => axis < 2
        ? StickProcessing.LeftOutput(raw, LeftDeadzonePercent, LeftSensitivityPercent)
        : StickProcessing.RightOutput(raw);

    void UpdatePressDetection()
    {
        string settings = $"{LeftDeadzonePercent}/{LeftSensitivityPercent}/{HardPressPercent}/{RightThresholdPercent}/{string.Join(',', Mapping)}/{string.Join(',', Invert)}";
        if (Source != detectionSource || settings != detectionSettings || frame == null)
        {
            leftPress.Reset(); rightPress.Reset();
            detectionSource = Source; detectionSettings = settings;
        }
        if (frame == null) return;
        Update(leftPress, 0, HardPressPercent / 100.0);
        Update(rightPress, 2, RightThresholdPercent / 100.0);

        void Update(PressDetector detector, int axis, double threshold)
        {
            double x = RawAxis(axis), y = RawAxis(axis + 1);
            if (!double.IsFinite(x) || !double.IsFinite(y)) { detector.Reset(); return; }
            detector.Sample(FinalAxis(axis, x), FinalAxis(axis + 1, y), threshold);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintCount++;
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        DrawText(g, "Rivals of Aether 2", title, ForeColor, 30, 24);
        bool loading = IsScanning || IsConnecting;
        string status = loading ? "Loading devices…" : frame == null ? "Disconnected" : focused ? "Connected · Focused" : "Connected · Unfocused";
        DrawText(g, status, small,
            loading || (frame != null && !focused) ? Color.FromArgb(255, 210, 90)
                : frame == null ? Color.FromArgb(255, 83, 90) : Color.Aquamarine, Width - 310, 42);
        using var line = new Pen(border);
        g.DrawLine(line, 30, 94, Width - 30, 94);
        float gap = 24, cardWidth = (Width - 60 - gap) / 2f;
        float cardHeight = Height - 178;
        DrawStick(g, new RectangleF(30, 116, cardWidth, cardHeight), "LEFT STICK", 0, 1);
        DrawStick(g, new RectangleF(30 + cardWidth + gap, 116, cardWidth, cardHeight), "RIGHT STICK", 2, 3);
        DrawText(g, FormattableString.Invariant($"Target FPS: 60   ·   Actual FPS: {actualFps:F1}"), small, muted, 30, Height - 48);
        DrawText(g, frame?.Detail ?? Source?.Status ?? "Connect a controller and click Refresh.", small, muted, 30, Height - 27);
    }

    void DrawStick(Graphics g, RectangleF card, string heading, int axisX, int axisY)
    {
        using var background = new SolidBrush(Color.FromArgb(18, 23, 33));
        using var outline = new Pen(border, 1);
        g.FillRectangle(background, card);
        g.DrawRectangle(outline, card.X, card.Y, card.Width, card.Height);
        DrawText(g, heading, label, ForeColor, card.X + 25, card.Y + 22);
        double rawBoundary = axisX == 0
            ? StickProcessing.HardPressRawBoundary(LeftDeadzonePercent, LeftSensitivityPercent, HardPressPercent)
            : StickProcessing.RightThresholdRawBoundary(RightThresholdPercent);
        var hardColor = Color.FromArgb(255, 177, 74);
        var plot = new RectangleF(card.X + 14, card.Y + 80, card.Width - 28, Math.Max(24, card.Height - 296));
        var detector = axisX == 0 ? leftPress : rightPress;
        string detectedLabel = axisX == 0 ? "Hard press detected: " : "Threshold detected: ";
        DrawText(g, detectedLabel, small, muted, card.X + 25, card.Y + 50);
        float detectedX = card.X + 25 + g.MeasureString(detectedLabel, small).Width + 4;
        DrawText(g, detector.Text, small, detector.Text == "None" ? muted
            : detector.Ambiguous ? Color.FromArgb(255, 210, 90) : Color.Aquamarine, detectedX, card.Y + 50);
        float cx = plot.X + plot.Width / 2, cy = plot.Y + plot.Height / 2;
        float radius = Math.Min(plot.Width, plot.Height) * 0.32f;
        var plotState = g.Save();
        g.SetClip(plot, CombineMode.Intersect);
        {
            int deadzonePercent = axisX == 0 ? LeftDeadzonePercent : StickProcessing.RightDeadzonePercent;
            float extent = radius * (float)StickProcessing.LeftDeadzoneThreshold(deadzonePercent);
            using var horizontal = new SolidBrush(Color.FromArgb(70, 65, 150, 255));
            using var vertical = new SolidBrush(Color.FromArgb(70, 55, 220, 120));
            using var overlap = new SolidBrush(Color.FromArgb(100, 255, 70, 80));
            // Exclude the intersection from both bands so it stays red, without blended layers.
            g.FillRectangle(horizontal, cx - extent, plot.Top, extent * 2, cy - extent - plot.Top);
            g.FillRectangle(horizontal, cx - extent, cy + extent, extent * 2, plot.Bottom - cy - extent);
            g.FillRectangle(vertical, plot.Left, cy - extent, cx - extent - plot.Left, extent * 2);
            g.FillRectangle(vertical, cx + extent, cy - extent, plot.Right - cx - extent, extent * 2);
            g.FillRectangle(overlap, cx - extent, cy - extent, extent * 2, extent * 2);
        }
        using var circle = new Pen(Color.FromArgb(64, 75, 96), 2);
        if (OctagonalGate)
        {
            var points = Enumerable.Range(0, 8).Select(i => new PointF(
                cx + radius * (float)Math.Cos(i * Math.PI / 4),
                cy + radius * (float)Math.Sin(i * Math.PI / 4))).ToArray();
            g.DrawPolygon(circle, points);
        }
        else g.DrawEllipse(circle, cx - radius, cy - radius, radius * 2, radius * 2);
        using var square = new Pen(Color.FromArgb(108, 121, 146), 1);
        g.DrawRectangle(square, cx - radius, cy - radius, radius * 2, radius * 2);
        using var cross = new Pen(Color.FromArgb(34, 43, 58), 1);
        g.DrawLine(cross, cx - radius, cy, cx + radius, cy);
        g.DrawLine(cross, cx, cy - radius, cx, cy + radius);
        // The orange guide uses the same final-value coordinates as the blue dot
        // and detector. The inverse-mapped raw boundary is only a numeric statistic.
        {
            float extent = radius * (axisX == 0 ? HardPressPercent : RightThresholdPercent) / 100f;
            // Shade the union of the four axis threshold regions, without darkening corners twice.
            using var outside = new Region(plot);
            outside.Exclude(new RectangleF(cx - extent, cy - extent, extent * 2, extent * 2));
            using var shade = new SolidBrush(Color.FromArgb(24, hardColor));
            g.FillRegion(shade, outside);
            using var guide = new Pen(hardColor, 1.5f) { DashStyle = DashStyle.Dash };
            g.DrawLine(guide, cx - extent, plot.Top, cx - extent, plot.Bottom);
            g.DrawLine(guide, cx + extent, plot.Top, cx + extent, plot.Bottom);
            g.DrawLine(guide, plot.Left, cy - extent, plot.Right, cy - extent);
            g.DrawLine(guide, plot.Left, cy + extent, plot.Right, cy + extent);
        }
        int ix = Mapping[axisX], iy = Mapping[axisY];
        bool validX = frame != null && ix >= 0 && ix < frame.Values.Length;
        bool validY = frame != null && iy >= 0 && iy < frame.Values.Length;
        double x = validX ? RawAxis(axisX) : 0;
        double y = validY ? RawAxis(axisY) : 0;
        double finalX = FinalAxis(axisX, x);
        double finalY = FinalAxis(axisY, y);
        string rawX = validX ? frame!.Raw[ix].ToString() : "—", rawY = validY ? frame!.Raw[iy].ToString() : "—";
        if (validX && validY)
        {
            // Positive Y is up. Preserve independent axes, including diagonal values outside the circle.
            DrawDot(g, cx + (float)x * radius, cy - (float)y * radius, Color.FromArgb(255, 83, 90), 7);
            DrawDot(g, cx + (float)finalX * radius, cy - (float)finalY * radius, Color.FromArgb(65, 150, 255), 5);
        }
        g.Restore(plotState);
        float valuesY = card.Bottom - 204;
        DrawText(g, $"Final  X {Format(finalX, validX),8}  Y {Format(finalY, validY),8}", small, Color.FromArgb(65, 150, 255), card.X + 25, valuesY);
        DrawText(g, $"Raw    X {Format(x, validX),8}  Y {Format(y, validY),8}", small, Color.FromArgb(255, 83, 90), card.X + 25, valuesY + 22);
        DrawText(g, $"Device X: {rawX,6}  Y: {rawY,6}", small, muted, card.X + 25, valuesY + 44);
        if (Source is SwitchSource)
            DrawText(g, "Stored calibration · direct USB", small, muted, card.X + 25, valuesY + 64);
        float statsY = valuesY + 92;
        g.DrawLine(outline, card.X + 25, statsY - 6, card.Right - 25, statsY - 6);
        bool left = axisX == 0;
        double deadzone = StickProcessing.LeftDeadzoneThreshold(left ? LeftDeadzonePercent : StickProcessing.RightDeadzonePercent);
        DrawText(g, $"Raw to escape deadzone: > {Format(deadzone)}", small, muted, card.X + 25, statsY);
        DrawText(g, $"Raw for {(left ? "hard press" : "threshold")}: {Format(rawBoundary)}", small, muted, card.X + 25, statsY + 22);
        double maximum = left ? LeftSensitivityPercent / 100.0 : 1;
        DrawText(g, $"Maximum theoretical final: {Format(maximum)}", small, muted, card.X + 25, statsY + 44);
        // Compare the actual settings, not rounded display values. Equality is reachable at raw 1.
        if (left && LeftSensitivityPercent < HardPressPercent)
        {
            var warning = Color.FromArgb(255, 83, 90);
            DrawText(g, "Warning: Hard press cannot be reached.", small, warning, card.X + 25, statsY + 66);
            DrawText(g, $"Maximum {Format(maximum)} < threshold {Format(HardPressPercent / 100.0)}", small, warning, card.X + 25, statsY + 84);
        }
    }

    string Format(double value, bool valid = true)
    {
        if (!valid) return "—";
        int decimals = RoundDisplay ? 3 : 5;
        double rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0; // Avoid displaying negative zero.
        return rounded.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture);
    }

    static void DrawDot(Graphics g, float x, float y, Color color, float radius)
    {
        using var glow = new SolidBrush(Color.FromArgb(35, color));
        using var dot = new SolidBrush(color);
        g.FillEllipse(glow, x - 11, y - 11, 22, 22);
        g.FillEllipse(dot, x - radius, y - radius, radius * 2, radius * 2);
    }

    static void DrawText(Graphics g, string text, Font font, Color color, float x, float y)
    {
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, x, y);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { title.Dispose(); label.Dispose(); numbers.Dispose(); small.Dispose(); }
        base.Dispose(disposing);
    }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}



