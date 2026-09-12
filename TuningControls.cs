using System.Globalization;
using System.Numerics;

sealed class TuningControls : TableLayoutPanel
{
    readonly PercentControl leftDeadzone = new("Left stick deadzone", 10, 5, 85);
    readonly PercentControl leftSensitivity = new("Left stick sensitivity", 120, 5, 300);
    readonly PercentControl hardPress = new("Hard press threshold", 80, 50, 95);
    readonly PercentControl rightThreshold = new("Right stick threshold", 35, 5, 95);
    readonly PercentControl rightDeadzone = new("Right stick deadzone (fixed)", 20, 5, 85,
        "Right stick deadzone is set to 20% in game and this cannot be changed");
    readonly PercentControl rightSensitivity = new("Right stick sensitivity (fixed)", 100, 5, 300,
        "Right stick sensitivity is set to 100% in game and this cannot be changed");
    public int LeftDeadzonePercent => leftDeadzone.CurrentValue;
    public int LeftSensitivityPercent => leftSensitivity.CurrentValue;
    public int HardPressPercent => hardPress.CurrentValue;
    public int RightThresholdPercent => rightThreshold.CurrentValue;
    public TuningControls()
    {
        Height = 300; ColumnCount = 2; RowCount = 4; Padding = new Padding(20, 6, 20, 6);
        BackColor = Color.FromArgb(18, 23, 33); ForeColor = Color.White;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 3; i++) RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3));
        RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(leftDeadzone, 0, 0);
        Controls.Add(leftSensitivity, 0, 1);
        Controls.Add(hardPress, 0, 2);
        Controls.Add(rightDeadzone, 1, 0);
        Controls.Add(rightSensitivity, 1, 1);
        Controls.Add(rightThreshold, 1, 2);
        var menuNote = new Label
        {
            Text = "As of version 1.7.1, the character select settings menu and full settings menu can show different percentage values for the same underlying setting. For more precise settings, read and set the values from the full settings menu.",
            Dock = DockStyle.Fill, AutoSize = false,
            Margin = new Padding(8, 5, 8, 0), ForeColor = Color.FromArgb(180, 190, 205),
            TextAlign = ContentAlignment.MiddleLeft
        };
        Controls.Add(menuNote, 0, 3); SetColumnSpan(menuNote, 2);
    }
}

sealed class PercentControl : TableLayoutPanel
{
    readonly TrackBar slider;
    readonly TextBox entry;
    readonly int defaultValue, min, max;
    bool updating;
    readonly ToolTip? help;
    public int CurrentValue => slider.Value;
    public PercentControl(string name, int defaultValue, int min, int max,
        string? fixedHelp = null, string? fixedDisplay = null, string unit = "%")
    {
        this.defaultValue = defaultValue; this.min = min; this.max = max;
        Dock = DockStyle.Fill; ColumnCount = 3; RowCount = 2; Margin = new Padding(8, 3, 8, 3);
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 25));
        RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = new Label { Text = name, AutoSize = true, Dock = DockStyle.Fill };
        Controls.Add(label, 0, 0); SetColumnSpan(label, 3);
        slider = new TrackBar { Minimum = min, Maximum = max, Value = defaultValue, SmallChange = 1, LargeChange = 1, TickStyle = TickStyle.None, Dock = DockStyle.Fill, AccessibleName = name };
        entry = new TextBox { Text = defaultValue.ToString(CultureInfo.InvariantCulture), Dock = DockStyle.Top, TextAlign = HorizontalAlignment.Right, BackColor = Color.FromArgb(10, 13, 19), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, AccessibleName = name + " percentage" };
        Controls.Add(slider, 0, 1); Controls.Add(entry, 1, 1);
        Controls.Add(new Label { Text = unit, AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, 2, 1);
        if (fixedHelp != null)
        {
            slider.Enabled = entry.Enabled = false;
            entry.Text = fixedDisplay ?? defaultValue.ToString(CultureInfo.InvariantCulture);
            label.ForeColor = Color.FromArgb(148, 160, 181);
            help = new ToolTip { ShowAlways = true };
            // Keep the label enabled so tooltips work even though the inputs are disabled.
            help.SetToolTip(label, fixedHelp);
            help.SetToolTip(this, fixedHelp);
            return;
        }
        slider.ValueChanged += (_, _) =>
        {
            if (updating) return;
            updating = true; entry.Text = slider.Value.ToString(CultureInfo.InvariantCulture); updating = false;
        };
        entry.TextChanged += (_, _) =>
        {
            if (updating) return;
            // Update the slider immediately, but allow incomplete text while the user is typing.
            updating = true; slider.Value = Parse(entry.Text, defaultValue, min, max); updating = false;
        };
        entry.Leave += (_, _) => Commit();
        entry.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; } };
    }
    void Commit()
    {
        int value = Parse(entry.Text, defaultValue, min, max);
        updating = true; slider.Value = value; entry.Text = value.ToString(CultureInfo.InvariantCulture); updating = false;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) help?.Dispose();
        base.Dispose(disposing);
    }
    internal static int Parse(string text, int fallback, int min, int max) =>
        BigInteger.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? (int)BigInteger.Clamp(value, min, max) : fallback;

    internal static void SelfTest()
    {
        if (Parse("garbage", 10, 5, 85) != 10 || Parse("", 10, 5, 85) != 10 ||
            Parse("12.5", 10, 5, 85) != 10 || Parse("-100", 10, 5, 85) != 5 ||
            Parse("99999999999999999999999", 10, 5, 85) != 85 || Parse("42", 10, 5, 85) != 42)
            throw new Exception("Percentage validation failed.");
    }
}
