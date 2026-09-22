using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

sealed class StickView : Control
{
    readonly Font title = new("Segoe UI", 27, FontStyle.Bold);
    readonly Font label = new("Segoe UI", 11, FontStyle.Bold);
    readonly Font numbers = new("Consolas", 16);
    readonly Font small = new("Consolas", 10);
    readonly Color muted = Color.FromArgb(148, 160, 181);
    readonly Color border = Color.FromArgb(49, 58, 75);
    ControllerFrame? frame;
    public ControllerSession Session { get; }
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
    readonly RivalsProcessor processor = new();
    RivalsFrame? processed;
    internal RivalsFrame CurrentFrame => processed!;
    public int LeftDeadzonePercent = 10;
    public int LeftSensitivityPercent = 120;
    public int HardPressPercent = 80;
    public int RightThresholdPercent = 35;
    public bool RoundDisplay = true;
    public bool OctagonalGate;
    public bool RawMechanics = true, ShowDeadzone = true;
    public HashSet<string> VisibleMechanics { get; } = Mechanics.All.Where(m => m.DefaultVisible).Select(m => m.Id).ToHashSet();
    public ModMode Mod = ModMode.Triggers;
    bool focused;
    public StickView(ControllerSession? session = null)
    {
        Session = session ?? new();
        DoubleBuffered = true;
        ResizeRedraw = true;
        processed = processor.Process(null, Session, new(10, 120, 80, 35, ModMode.Triggers));
        BackColor = Color.FromArgb(10, 13, 19);
        ForeColor = Color.FromArgb(242, 245, 251);
    }

    public void Sample()
    {
        // Discovery and opening can touch the same native backend; don't poll concurrently.
        frame = Session.Sample();
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        focused = pid == Environment.ProcessId;
        processed = processor.Process(frame, Session, Session.Source is KeyboardSource
            ? new(10, 100, 80, 35, Mod) : new(LeftDeadzonePercent, LeftSensitivityPercent, HardPressPercent, RightThresholdPercent, Mod));

    }

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintCount++;
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        DrawText(g, "Rivals of Aether 2", title, ForeColor, 30, 24);
        bool loading = Session.IsScanning || Session.IsConnecting;
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
        DrawText(g, frame?.Detail ?? Session.Source?.Status ?? "Connect a controller and click Refresh.", small, muted, 30, Height - 27);
    }

    void DrawStick(Graphics g, RectangleF card, string heading, int axisX, int axisY)
    {
        using var background = new SolidBrush(Color.FromArgb(18, 23, 33));
        using var outline = new Pen(border, 1);
        g.FillRectangle(background, card);
        g.DrawRectangle(outline, card.X, card.Y, card.Width, card.Height);
        DrawText(g, heading, label, ForeColor, card.X + 25, card.Y + 22);
        var result = axisX == 0 ? processed!.Left : processed!.Right;

        float helperTop = 50;
        if (VisibleMechanics.Contains(axisX == 0 ? "hard" : "right") && (axisX == 0 || Session.Source is not KeyboardSource))
        {
            string detectedLabel = axisX == 0 ? "Hard press detected: " : "Threshold detected: ";
            DrawText(g, detectedLabel, small, muted, card.X + 25, card.Y + helperTop);
            float detectedX = card.X + 29 + g.MeasureString(detectedLabel, small).Width;
            DrawText(g, result.PressText, small, result.PressText == "None" ? muted
                : result.Ambiguous ? Color.FromArgb(255, 210, 90) : Color.Aquamarine, detectedX, card.Y + helperTop);
            helperTop += 22;
        }
        if (axisX == 0)
        {
            int count = 0;
            void Indicator(bool show, string name, bool active, Color color)
            {
                if (!show) return;
                float px = card.X + 25 + (count % 2) * (card.Width - 40) / 2;
                float py = card.Y + helperTop + (count / 2) * 22;
                DrawText(g, name + ":", small, muted, px, py);
                DrawText(g, active ? "True" : "False", small, active ? color : muted,
                    px + g.MeasureString(name + ":", small).Width + 3, py);
                count++;
            }
            foreach (var mechanic in result.Mechanics.Where(m => m.Definition.Id != "hard"))
                Indicator(VisibleMechanics.Contains(mechanic.Definition.Id), mechanic.Definition.Helper, mechanic.Active,
                    mechanic.Definition.Id == "ledge" ? Color.Red : Color.Lime);
            helperTop += ((count + 1) / 2) * 22;
        }
        float plotTop = helperTop + 10;
        var plot = new RectangleF(card.X + 14, card.Y + plotTop, card.Width - 28, Math.Max(24, card.Height - plotTop - 150));
        float cx = plot.X + plot.Width / 2, cy = plot.Y + plot.Height / 2;
        float radius = Math.Min(plot.Width, plot.Height) * 0.32f;
        var plotState = g.Save();
        g.SetClip(plot, CombineMode.Intersect);
        if (RawMechanics && ShowDeadzone && Session.Source is not KeyboardSource)
        {
            float extent = radius * (float)result.Deadzone;
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
        var transform = new DiagramTransform(cx, cy, radius);
        foreach (var mechanic in result.Mechanics.Where(m => VisibleMechanics.Contains(m.Definition.Id) && !(m.Definition.Right && Session.Source is KeyboardSource)))
            MechanicRenderer.Draw(g, plot, transform, mechanic, RawMechanics, small);
        bool validX = result.ValidX, validY = result.ValidY;
        double x = result.Raw.X, y = result.Raw.Y;
        var (finalX, finalY) = result.Final;
        string rawX = result.DeviceX?.ToString() ?? "—", rawY = result.DeviceY?.ToString() ?? "—";
        if (validX && validY)
        {
            var rawPoint = transform.Raw(result.Raw);
            var finalPoint = transform.Game(result.Final);
            DrawDot(g, rawPoint.X, rawPoint.Y, Color.FromArgb(255, 83, 90), 7);
            DrawDot(g, finalPoint.X, finalPoint.Y, Color.FromArgb(65, 150, 255), 5);
        }
        g.Restore(plotState);
        float valuesY = card.Bottom - 138;
        DrawText(g, $"Final  X {Format(finalX, validX),8}  Y {Format(finalY, validY),8}", small, Color.FromArgb(65, 150, 255), card.X + 25, valuesY);
        DrawText(g, $"Raw    X {Format(x, validX),8}  Y {Format(y, validY),8}", small, Color.FromArgb(255, 83, 90), card.X + 25, valuesY + 22);
        DrawText(g, $"Device X: {rawX,6}  Y: {rawY,6}", small, muted, card.X + 25, valuesY + 44);
        if (axisX == 0)
        {
            var mods = processed!.Mods;
            DrawText(g, "Mod X", small, mods.X ? Color.Lime : muted, card.Right - 75, valuesY);
            DrawText(g, "Mod Y", small, mods.Y ? Color.Lime : muted, card.Right - 75, valuesY + 22);
        }
        if (Session.Source is SwitchSource)
            DrawText(g, "Stored calibration · direct USB", small, muted, card.X + 25, valuesY + 64);
        if (axisX == 0 && processed!.FastfallWarning)
        {
            var yellow = Color.FromArgb(255, 210, 90);
            DrawText(g, "Cannot fastfall without disabling", small, yellow, card.X + 25, valuesY + 92);
            DrawText(g, "the ledge grab box.", small, yellow, card.X + 25, valuesY + 110);
        }
    }

    string Format(double value, bool valid = true) => DisplayNumbers.Format(value, RoundDisplay, valid);

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




