using System.Drawing.Drawing2D;

sealed class AnalyzerPlot(string heading) : Control
{
    public PerimeterStickSnapshot Data = new(0, 0, Enumerable.Repeat(double.NaN, PerimeterSweeps.BucketCount).ToArray(), new long[PerimeterSweeps.BucketCount]);
    public bool ShowResults;
    public FlickStickSnapshot? FlickData;
    public PollingSnapshot? PollingData;
    public bool IsRecording;
    readonly Font title = new("Segoe UI", 12, FontStyle.Bold);
    readonly Font numbers = new("Consolas", 11);
    readonly Font small = new("Segoe UI", 10);
    readonly Font grade = new("Segoe UI", 30, FontStyle.Bold);
    static readonly Color[] Heat = [Color.FromArgb(223, 85, 96), Color.FromArgb(225, 132, 72),
        Color.FromArgb(227, 168, 79), Color.FromArgb(214, 191, 84), Color.FromArgb(154, 196, 98), Color.FromArgb(63, 205, 152)];
    public AnalyzerPlot() : this("STICK") { }
    protected override void OnCreateControl()
    {
        base.OnCreateControl(); DoubleBuffered = true; ResizeRedraw = true;
        BackColor = Color.FromArgb(18, 23, 33); ForeColor = Color.FromArgb(230, 236, 246);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(DarkTheme.Border);
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        Label(g, heading, title, ForeColor, 22, 20);
        var flick = FlickData;
        double completion = PollingData?.Progress ?? flick?.Completion ?? Data.Completion;
        if (!ShowResults) Label(g, $"{Math.Floor(completion * 100):0}% complete", small, completion == 1 ? Heat[5] : Color.FromArgb(172, 184, 203), 22, 52);
        float plotBottom = Math.Max(200, Height - (flick == null ? 170 : 230));
        float top = ShowResults ? 60 : 126;
        if (!ShowResults && flick == null && PollingData == null && !Data.Ready)
            Label(g, Data.DirectionLabel, title, Data.Direction > 0 ? Color.Lime : Data.Direction < 0 ? Color.Red : Color.FromArgb(172, 184, 203), 22, 82);
        float availableHeight = plotBottom - top;
        bool showReach = ShowResults;
        bool showSnapback = ShowResults && flick != null;
        double reach = showReach ? Math.Max(1.13, Data.Radii.Where(double.IsFinite).DefaultIfEmpty(1).Max() * 1.08) : 1.13;
        float plotWidth = showSnapback ? Width / 2f - 42 : Width - 100;
        float radius = Math.Max(10, Math.Min(plotWidth, availableHeight - 35) / 2 / (float)reach);
        float cx = showSnapback ? Width / 4f : Width / 2f, cy = top + availableHeight / 2;
        if (showSnapback)
        {
            Label(g, "Perimeter", small, ForeColor, 22, top);
            DrawSnapbacks(g, flick!, Width * .75f, cy, plotWidth, availableHeight, top);
        }
        if (showReach)
        {
            for (int i = 0; i < Data.Radii.Length; i++)
            {
                if (!double.IsFinite(Data.Radii[i]) || Data.Radii[i] <= 0) continue;
                float r = (float)Data.Radii[i] * radius;
                var color = ReachColor(Data.Radii[i]);
                using var fill = new SolidBrush(color);
                using var edge = new Pen(Color.FromArgb(110, 0, 0, 0));
                var bounds = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);
                g.FillPie(fill, bounds.X, bounds.Y, bounds.Width, bounds.Height, -i * 10 - 5, 10);
                g.DrawPie(edge, bounds.X, bounds.Y, bounds.Width, bounds.Height, -i * 10 - 5, 10);
            }
        }
        using var reference = new Pen(Color.FromArgb(78, 90, 112), 1.5f);
        using var axis = new Pen(Color.FromArgb(38, 48, 65));
        g.DrawLine(axis, cx - radius, cy, cx + radius, cy);
        g.DrawLine(axis, cx, cy - radius, cx, cy + radius);
        g.DrawEllipse(reference, cx - radius, cy - radius, radius * 2, radius * 2);
        if (flick != null && !ShowResults)
        {
            string prompt = flick.Phase == FlickPhase.Complete ? "Complete" : !IsRecording ? "Stopped" : flick.Phase switch
            {
                FlickPhase.Release => "Release!",
                FlickPhase.Recording => "DON'T TOUCH!",
                _ => $"Hold {Flicks.Directions[flick.Target]} · {flick.HoldProgress:F1} / 1.0 s"
            };
            var color = flick.Phase == FlickPhase.Release ? Heat[5] : flick.Phase == FlickPhase.Recording ? Color.Red : Color.FromArgb(255, 177, 74);
            Label(g, prompt, title, color, 22, 82);
            if (flick.Phase != FlickPhase.Complete)
            {
                var target = Flicks.TargetVector(flick.Target);
                float tx = cx + (float)target.X * radius, ty = cy - (float)target.Y * radius;
                using var marker = new Pen(flick.Phase is FlickPhase.Release or FlickPhase.Recording ? Heat[5] : Color.FromArgb(255, 177, 74), 3);
                g.DrawEllipse(marker, tx - 12, ty - 12, 24, 24);
            }
        }
        if (!ShowResults)
        {
        var saved = g.Save(); g.SetClip(new RectangleF(12, 84, Math.Max(1, Width - 24), Math.Max(1, plotBottom - 84)));
        using var glow = new SolidBrush(Color.FromArgb(35, 96, 222, 251));
        using var dot = new SolidBrush(Color.FromArgb(96, 222, 251));
        float dx = cx + (float)Data.X * radius, dy = cy - (float)Data.Y * radius;
        g.FillEllipse(glow, dx - 11, dy - 11, 22, 22); g.FillEllipse(dot, dx - 5, dy - 5, 10, 10);
        g.Restore(saved);
        Label(g, $"X {Data.X,8:F4}   Y {Data.Y,8:F4}", numbers, Color.FromArgb(96, 222, 251), 22, plotBottom + 8);
        }
        if (PollingData != null)
        {
            Label(g, "Spin quickly around the perimeter.", small, ForeColor, 22, plotBottom + 45);
            Label(g, PollingData.Started ? "Measuring controller update timing…" : "Waiting for both sticks to move beyond 0.8…", small, ForeColor, 22, plotBottom + 80);
        }
        else if (flick != null && ShowResults)
        {
            var score = AnalyzerScore.From(Data, flick);
            Label(g, score.Grade, grade, GradeColor(score.Overall), 22, plotBottom + 35);
            Label(g, $"Overall {score.Overall:F1} / 100", small, ForeColor, 106, plotBottom + 54);
            ScoreLine(g, score.Perimeter, $"Perimeter: {score.Perimeter:F1}/100 · error {Data.Error:P2}", small, ForeColor, 22, plotBottom + 102);
            ScoreLine(g, score.Snapback, $"Snapback: {score.Snapback:F1}/100 · peak {flick.WorstSnapback:F5}", small, ForeColor, 22, plotBottom + 128);
            ScoreLine(g, score.Jitter, $"Jitter: {score.Jitter:F1}/100 · X {flick.AverageJitterX:F5} Y {flick.AverageJitterY:F5}", small, ForeColor, 22, plotBottom + 154);
            ScoreLine(g, score.Centering, $"Centering: {score.Centering:F1}/100 · worst {flick.WorstResting:F5}", small, ForeColor, 22, plotBottom + 184);
        }
        else if (flick?.Phase == FlickPhase.Complete)
        {
            Label(g, "Waiting for the other stick…", small, ForeColor, 22, plotBottom + 48);
        }
        else if (flick != null)
        {
            Label(g, $"{Flicks.Directions[flick.Target]} · flick {flick.Completed + 1} of 16", small, ForeColor, 22, plotBottom + 48);
            Label(g, flick.Notice.Length > 0 ? flick.Notice : flick.Phase == FlickPhase.Recording
                ? "Leave the stick untouched while recording." : "Move to the target, hold, then let go.", small, Color.FromArgb(155, 170, 193), 22, plotBottom + 80);
        }
        else if (ShowResults)
        {
            Label(g, $"Perimeter error: {Data.Error:P2}", small, ForeColor, 22, plotBottom + 54);
            Label(g, $"Radial: {Data.RadialError:P2} + missed: {Data.MissingPenalty:P2}", small, Color.FromArgb(155, 170, 193), 22, plotBottom + 107);
        }
        else
        {
            Label(g, Data.Ready ? "Complete · no longer recording this stick" : "Rotate around the full perimeter.", small, Data.Ready ? Heat[5] : ForeColor, 22, plotBottom + 45);
            Label(g, "Keep sweeping to find the maximum reach.", small, Color.FromArgb(155, 170, 193), 22, plotBottom + 100);
        }
    }
    static void Label(Graphics g, string value, Font font, Color color, float x, float y)
    { using var brush = new SolidBrush(color); g.DrawString(value, font, brush, x, y); }
    static void ScoreLine(Graphics g, double score, string value, Font font, Color color, float x, float y)
    {
        using var bullet = new SolidBrush(ScoreColor(score));
        g.FillEllipse(bullet, x, y + 4, 10, 10);
        Label(g, value, font, color, x + 19, y);
    }
    public static Color ScoreColor(double score)
    {
        double hue = Math.Clamp(score / 100, 0, 1) * 2;
        return hue <= 1 ? Color.FromArgb(255, (int)Math.Round(hue * 255), 0)
            : Color.FromArgb((int)Math.Round((2 - hue) * 255), 255, 0);
    }
    public static Color GradeColor(double score) => score >= 80 ? Heat[5] : score < 60 ? Heat[0] : Heat[1];
    void DrawSnapbacks(Graphics g, FlickStickSnapshot flick, float cx, float cy, float width, float height, float top)
    {
        var detected = flick.Results.Where(r => r.HasDetectedSnapback).ToArray();
        double reach = Math.Max(1.13, Math.Max(flick.WorstResting, detected.Select(r => r.Snapback).DefaultIfEmpty(0).Max()) * 1.08);
        float radius = Math.Max(10, Math.Min(width, height - 35) / 2 / (float)reach);
        Label(g, "Snapback", small, ForeColor, Width / 2f + 12, top);
        using var reference = new Pen(Color.FromArgb(110, 125, 148), 1.5f);
        g.DrawEllipse(reference, cx - radius, cy - radius, radius * 2, radius * 2);
        using var threshold = new Pen(Color.FromArgb(100, 125, 148)) { DashStyle = DashStyle.Dot };
        g.DrawEllipse(threshold, cx - radius * .1f, cy - radius * .1f, radius * .2f, radius * .2f);
        // Render all lines before endpoints so crossing lines cannot obscure the dots.
        foreach (var result in detected)
        {
            var origin = Flicks.TargetVector(result.Target);
            var peak = result.SnapbackPoint!.Value;
            using var line = new Pen(ScoreColor((.5 - result.Snapback) / .4 * 100), 1.5f);
            g.DrawLine(line, cx + (float)origin.X * radius, cy - (float)origin.Y * radius,
                cx + (float)peak.X * radius, cy - (float)peak.Y * radius);
        }
        foreach (var result in detected)
        {
            var peak = result.SnapbackPoint!.Value;
            float x = cx + (float)peak.X * radius, y = cy - (float)peak.Y * radius;
            using var dot = new SolidBrush(ScoreColor((.5 - result.Snapback) / .4 * 100));
            g.FillEllipse(Brushes.Black, x - 5, y - 5, 10, 10);
            g.FillEllipse(dot, x - 3.5f, y - 3.5f, 7, 7);
        }
        if (flick.WorstCentering is { } worst)
        {
            float x = cx + (float)worst.SettledCenter.X * radius, y = cy - (float)worst.SettledCenter.Y * radius;
            using var centerDot = new SolidBrush(Color.FromArgb(191, 0, 255));
            g.FillEllipse(Brushes.Black, x - 6, y - 6, 12, 12);
            g.FillEllipse(centerDot, x - 4, y - 4, 8, 8);
        }
    }
    static Color ReachColor(double radius)
    {
        if (radius < .9) return Color.FromArgb(255, 0, 0);
        if (radius < .99)
            return Color.FromArgb(255, (int)Math.Round(128 + 127 * (radius - .9) / .09), 0);
        if (radius <= 1.01) return Color.FromArgb(0, 255, 0);
        if (radius <= 1.1)
            return Color.FromArgb(0, (int)Math.Round(255 * (1 - (radius - 1.01) / .09)), 255);
        return Color.FromArgb(191, 0, 255);
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { title.Dispose(); numbers.Dispose(); small.Dispose(); grade.Dispose(); } base.Dispose(disposing); }
}

