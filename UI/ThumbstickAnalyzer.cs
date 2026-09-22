using System.Diagnostics;

sealed class ThumbstickAnalyzer : Panel
{
    readonly ControllerSession session;
    readonly Button start = new() { Text = "Start Test", Width = 120, Height = 36, Dock = DockStyle.Right };
    readonly Label stageTitle = new() { Text = "Circular Sweeps", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = Color.White };
    readonly Label instructions = new() { Text = "Roll your stick at medium speed around the perimeter of the gate to record the maximum values produced. Use a mix of clockwise and counter clockwise movement as directed.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(192, 204, 223) };
    readonly Label status = new() { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(255, 210, 90) };
    readonly AnalyzerPlot left = new("LEFT STICK") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
    readonly AnalyzerPlot right = new("RIGHT STICK") { Dock = DockStyle.Fill, Margin = new Padding(10, 0, 0, 0) };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    readonly TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
    readonly Font resultGradeFont = new("Segoe UI", 30, FontStyle.Bold);
    PerimeterSweeps perimeter = new();
    Flicks flicks = new();
    PollingRate polling = new();
    AnalyzerRun? run;
    Task? stopping;
    bool finishing;
    readonly TriggerStartLatch triggerStart = new();
    ControllerSource? triggerSource;

    public ThumbstickAnalyzer(ControllerSession session)
    {
        this.session = session; session.StopAnalyzer = StopAsync;
        session.Sampled += OnSample;
        BackColor = DarkTheme.Background; Padding = new Padding(28, 20, 28, 16);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (float height in new[] { 66f, 42, 52, 42 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new Label { Text = "Thumbstick Analyzer", Font = new Font("Segoe UI", 25, FontStyle.Bold),
            Dock = DockStyle.Fill, ForeColor = Color.White });
        DarkTheme.StyleButton(start); header.Controls.Add(start);
        layout.Controls.Add(header, 0, 0); layout.Controls.Add(stageTitle, 0, 1);
        layout.Controls.Add(instructions, 0, 2); layout.Controls.Add(status, 0, 3);
        var plots = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        plots.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); plots.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        plots.Controls.Add(left, 0, 0); plots.Controls.Add(right, 1, 0); layout.Controls.Add(plots, 0, 4);
        Controls.Add(layout);
        start.Click += async (_, _) => { if (session.AnalyzerBusy) await StopAsync(); else StartTest(); };
        timer.Tick += async (_, _) => await UpdateRecording();
        status.Text = "Press Start Test or RT to begin.";
    }

    void OnSample(ControllerFrame? frame)
    {
        start.Enabled = session.Source is not KeyboardSource;
        if (session.Source is KeyboardSource) status.Text = "Select a controller to run the thumbstick tests.";
        if (triggerSource != session.Source) { triggerSource = session.Source; triggerStart.Reset(); }
        bool canStart = session.Source is not KeyboardSource && Visible && !session.AnalyzerBusy && !finishing && stopping == null && !session.IsScanning && !session.IsConnecting;
        bool shortcut = triggerStart.Update(frame?.RightTrigger, canStart);
        if (Visible)
        {
            double Axis(int axis)
            {
                double value = AxisMapping.Read(frame, axis, session.Mapping, session.Invert);
                return double.IsFinite(value) ? value : 0;
            }
            // Preview only: don't send idle values to the binning or stage runner.
            left.Data = left.Data with { X = Axis(0), Y = Axis(1) };
            right.Data = right.Data with { X = Axis(2), Y = Axis(3) };
            left.Invalidate(); right.Invalidate();
        }
        if (shortcut) StartTest();
    }

    void StartTest()
    {
        if (stopping != null || finishing) return;
        if (session.Source is KeyboardSource) { status.Text = "Select a controller to run the thumbstick tests."; return; }
        if (session.Source == null || session.IsScanning || session.IsConnecting)
        { status.Text = "Select a connected controller before starting."; return; }
        if (session.Mapping.Any(i => i < 0 || i >= session.Source.Axes.Length))
        { status.Text = "Select valid X and Y mappings for both sticks in Settings."; return; }
        perimeter = new();
        SetResultsLayout(false);
        flicks = new();
        polling = new(session.Source.HasReportStream);
        run = new AnalyzerRun(perimeter, flicks, polling); // Append future stages here.
        left.FlickData = right.FlickData = null;
        left.ShowResults = right.ShowResults = false;
        session.StartCapture(run.Observe);
        start.Text = "Stop Test";
        stageTitle.Text = perimeter.Title; instructions.Text = perimeter.Instructions;
        status.Text = "Recording both sticks · rotate around the full perimeter.";
        RefreshPlots(); timer.Start();
    }
    void RefreshPlots()
    {
        var data = perimeter.Snapshot();
        // Dot motion is a live preview even after a stick's recorded data has frozen.
        left.Data = data.Left with { X = left.Data.X, Y = left.Data.Y };
        right.Data = data.Right with { X = right.Data.X, Y = right.Data.Y };
        if (run?.Current == polling)
        {
            left.FlickData = right.FlickData = null;
        }
        else if (run?.Current == flicks || run?.Complete == true)
        {
            var flickData = flicks.Snapshot(); left.FlickData = flickData.Left; right.FlickData = flickData.Right;
        }
        left.IsRecording = right.IsRecording = session.AnalyzerBusy;
        left.PollingData = right.PollingData = run?.Current == polling ? polling.Snapshot() : null;
        left.Invalidate(); right.Invalidate();
    }
    async Task UpdateRecording()
    {
        if (finishing || stopping != null) return;
        var capture = session.AnalyzerRecording;
        if (capture == null || run == null) return;
        RefreshPlots();
        if (capture.Error != null || (capture.Elapsed > 3 && capture.Latest == null) ||
            (capture.Latest != null && (Stopwatch.GetTimestamp() - capture.Latest.Timestamp) / (double)Stopwatch.Frequency > 2))
        {
            string error = capture.Error ?? "Input stopped. Reconnect the controller and start again.";
            await StopAsync(); status.Text = error; return;
        }
        var active = run.Current;
        if (active != null)
        {
            stageTitle.Text = active.Title; instructions.Text = active.Instructions;
            if (active == polling)
            {
                var progress = polling.Snapshot();
                status.Text = $"{Math.Floor(progress.Progress * 100):0}% complete · {progress.Guidance}";
            }
            else if (active == flicks)
            {
                var progress = flicks.Snapshot();
                status.Text = $"Left {Math.Floor(progress.Left.Completion * 100):0}% complete · Right {Math.Floor(progress.Right.Completion * 100):0}% complete · Sticks progress independently";
            }
            else status.Text = $"Recording · Left {Math.Floor(left.Data.Completion * 100):0}% complete · Right {Math.Floor(right.Data.Completion * 100):0}% complete";
            return;
        }
        finishing = true;
        await StopAsync();
        left.ShowResults = right.ShowResults = true; RefreshPlots();
        var overall = AnalyzerScore.Average(AnalyzerScore.From(left.Data, left.FlickData!), AnalyzerScore.From(right.Data, right.FlickData!));
        SetResultsLayout(true);
        stageTitle.Text = $"Final Score: {overall.Overall:F1}/100";
        instructions.Text = overall.Grade;
        instructions.ForeColor = AnalyzerPlot.GradeColor(overall.Overall);
        var rate = System.Text.RegularExpressions.Regex.Match(polling.Snapshot().Result, @"~?\d+ Hz");
        status.Text = $"Estimated Polling Rate: {(rate.Success ? rate.Value : "Unavailable")}";
        start.Text = "Run test again";
        finishing = false;
    }
    public Task StopAsync() => stopping ??= StopCore();
    void SetResultsLayout(bool results)
    {
        stageTitle.TextAlign = instructions.TextAlign = status.TextAlign = results ? ContentAlignment.MiddleCenter : ContentAlignment.TopLeft;
        instructions.Font = results ? resultGradeFont : Font;
        instructions.ForeColor = Color.FromArgb(192, 204, 223);
        status.ForeColor = results ? Color.FromArgb(192, 204, 223) : Color.FromArgb(255, 210, 90);
        layout.RowStyles[2].Height = results ? 64 : 52;
    }
    async Task StopCore()
    {
        timer.Stop();
        await session.StopCapture();
        if (session.AnalyzerBusy && !finishing) status.Text = "Stopped · incomplete results are not graded. Press Start Test to begin again.";
        session.AnalyzerBusy = false; start.Text = "Start Test";
        left.IsRecording = right.IsRecording = false; left.Invalidate(); right.Invalidate();
        await Task.Yield(); stopping = null;
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { session.Sampled -= OnSample; timer.Dispose(); stageTitle.Font.Dispose(); resultGradeFont.Dispose(); } base.Dispose(disposing); }

    public static void Preview(string path, bool showFlicks = false)
    {
        Application.EnableVisualStyles();
        var session = new ControllerSession();
        using var page = new ThumbstickAnalyzer(session) { Dock = DockStyle.Fill };
        using var form = new Form { ClientSize = new Size(1100, 900), Text = "Sticky Sticks · Synthetic perimeter preview" };
        DarkTheme.StyleWindow(form); form.Controls.Add(page);
        for (int n = 0; n < 5; n++)
            for (int i = 0; i < 360; i++)
            {
                if (n > 0 && i > 160 && i < 210) continue;
                double a = i * Math.PI / 180;
                page.perimeter.Observe(new(.98 * Math.Cos(a), .98 * Math.Sin(a), 1.02 * Math.Cos(a + .2), 1.02 * Math.Sin(a + .2), true, i < 310));
            }
        page.RefreshPlots(); page.status.Text = "Synthetic preview · rotate around the full perimeter.";
        if (showFlicks)
        {
            page.run = new AnalyzerRun(page.flicks); session.AnalyzerBusy = true;
            for (int i = 0; i <= 110; i++) page.flicks.Observe(new(0, 1, 0, 1, true, true, i * .01));
            page.flicks.Observe(new(0, 1, 0, -.12, true, true, 1.11));
            page.stageTitle.Text = page.flicks.Title; page.instructions.Text = page.flicks.Instructions;
            page.status.Text = "Synthetic preview · independent Release! and Recording states";
            page.RefreshPlots();
        }
        form.Shown += (_, _) => form.BeginInvoke((Action)(() =>
        {
            page.Refresh();
            using var bitmap = new Bitmap(page.Width, page.Height); page.DrawToBitmap(bitmap, page.ClientRectangle); bitmap.Save(path); form.Close();
        }));
        Application.Run(form);
    }
    public static void SmokeTest(string path)
    {
        StickScrollPanel.VerifyResize();
        var source = new SweepSource();
        var session = new ControllerSession();
        session.Source = source;
        using var page = new ThumbstickAnalyzer(session) { Dock = DockStyle.Fill };
        source.SweepState = () => page.perimeter.Snapshot();
        source.FlickState = () => page.run?.Current == page.flicks ? page.flicks.Snapshot() : null;
        using var form = new Form { ClientSize = new Size(1000, 850), Text = "Sticky Sticks · Automatic sweep test" };
        form.Controls.Add(page);
        form.Shown += async (_, _) =>
        {
            try
            {
                var idle = new ControllerFrame([.65, -.3, -.4, .7], [0, 0, 0, 0], "Synthetic preview") { RightTrigger = 0 };
                page.OnSample(idle);
                if (page.left.Data.X != .65 || page.right.Data.Y != .7 || page.perimeter.Snapshot().Left.Filled != 0)
                    throw new Exception("Idle preview changed bins or failed to move dots.");
                page.OnSample(idle with { RightTrigger = 26 });
                if (!session.AnalyzerBusy) throw new Exception("RT did not start the test.");
                var startedRun = page.run;
                page.OnSample(idle with { RightTrigger = 255 });
                if (!session.AnalyzerBusy || !ReferenceEquals(startedRun, page.run)) throw new Exception("Held RT restarted or stopped capture.");
                var timeout = Stopwatch.StartNew();
                while (session.AnalyzerBusy && timeout.Elapsed.TotalSeconds < 8) await Task.Delay(20);
                if (session.AnalyzerBusy || page.run?.Complete != true || !page.left.ShowResults || !page.right.ShowResults)
                    throw new Exception("Automatic completion/results failed.");
                using (var bitmap = new Bitmap(page.Width, page.Height))
                { page.DrawToBitmap(bitmap, page.ClientRectangle); bitmap.Save(path + ".png"); }
                page.OnSample(idle with { RightTrigger = 255 });
                if (session.AnalyzerBusy) throw new Exception("Held RT restarted a completed test.");
                page.StartTest(); await page.StopAsync(); await page.StopAsync();
                if (session.AnalyzerRecording != null || session.AnalyzerBusy) throw new Exception("Stop did not release capture.");
                File.WriteAllText(path, "PASS: idle preview without capture, RT start, held RT ignored during/after capture, automatic results and cancellation.");
            }
            catch (Exception ex) { File.WriteAllText(path, "FAIL: " + ex); }
            finally { await page.StopAsync(); form.Close(); }
        };
        Application.Run(form);
    }
    sealed class SweepSource : ControllerSource
    {
        int sample;
        double leftAngle, rightAngle;
        public Func<(PerimeterStickSnapshot Left, PerimeterStickSnapshot Right)>? SweepState;
        readonly long started = Stopwatch.GetTimestamp();
        public Func<(FlickStickSnapshot Left, FlickStickSnapshot Right)?>? FlickState;
        public override string[] Axes => ["LX", "LY", "RX", "RY"];
        public override ControllerFrame Read()
        {
            double a = sample++ % 360 * Math.PI / 180;
            var sweep = SweepState?.Invoke();
            leftAngle += sweep?.Left.Clockwise == true ? -.07 : .07;
            rightAngle += sweep?.Right.Clockwise == true ? -.07 : .07;
            double[] values = [Math.Cos(leftAngle), Math.Sin(leftAngle), Math.Cos(rightAngle), Math.Sin(rightAngle)];
            var flick = FlickState?.Invoke();
            if (flick.HasValue)
            {
                foreach (int side in new[] { 0, 1 })
                {
                    var state = side == 0 ? flick.Value.Left : flick.Value.Right;
                    var target = Flicks.TargetVector(state.Target);
                    double magnitude = state.Phase == FlickPhase.Hold ? 1 : state.Phase == FlickPhase.Release ? -.25 : 0;
                    values[side * 2] = magnitude * target.X;
                    values[side * 2 + 1] = magnitude * target.Y;
                }
            }
            // Accelerated, deterministic timestamps for this synthetic diagnostic only.
            return new(values, [0, 0, 0, 0], "Synthetic sweep/flick") { Timestamp = started + (long)(sample * .01 * Stopwatch.Frequency) };
        }
    }
}


