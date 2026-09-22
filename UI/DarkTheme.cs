using System.Runtime.InteropServices;

static class DarkTheme
{
    public static readonly Color Background = Color.FromArgb(10, 13, 19);
    public static readonly Color Surface = Color.FromArgb(24, 30, 42);
    public static readonly Color Border = Color.FromArgb(49, 58, 75);

    public static void StyleButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = Color.FromArgb(32, 40, 55);
        button.ForeColor = Color.FromArgb(235, 240, 248);
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, 57, 76);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(55, 71, 95);
    }

    public static void StylePicker(ComboBox picker)
    {
        picker.FlatStyle = FlatStyle.Flat;
        picker.BackColor = Color.FromArgb(18, 23, 33);
        picker.ForeColor = Color.FromArgb(235, 240, 248);
        picker.DrawMode = DrawMode.OwnerDrawFixed;
        picker.ItemHeight = 22;
        picker.DrawItem += (_, e) =>
        {
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(selected ? Color.FromArgb(45, 57, 76) : picker.BackColor);
            e.Graphics.FillRectangle(background, e.Bounds);
            string text = (e.Index >= 0 ? picker.GetItemText(picker.Items[e.Index]) : picker.Text) ?? "";
            TextRenderer.DrawText(e.Graphics, text, picker.Font,
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 12), e.Bounds.Height),
                picker.Enabled ? picker.ForeColor : Color.FromArgb(148, 160, 181),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        };
    }

    public static void StyleWindow(Form window)
    {
        window.BackColor = Background;
        window.HandleCreated += (_, _) =>
        {
            // Unsupported DWM attributes are ignored, preserving native window controls.
            int dark = 1;
            DwmSetWindowAttribute(window.Handle, 20, ref dark, sizeof(int));
            int caption = ColorTranslator.ToWin32(Surface);
            DwmSetWindowAttribute(window.Handle, 35, ref caption, sizeof(int));
            int border = ColorTranslator.ToWin32(Border);
            DwmSetWindowAttribute(window.Handle, 34, ref border, sizeof(int));
            int text = ColorTranslator.ToWin32(Color.FromArgb(235, 240, 248));
            DwmSetWindowAttribute(window.Handle, 36, ref text, sizeof(int));
        };
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

// Retain native dropdown and keyboard behaviour, but paint its border and arrow dark.
sealed class DarkComboBox : ComboBox
{
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x000F && m.Msg != 0x0317 && m.Msg != 0x0318) return;
        using var graphics = m.Msg == 0x000F ? Graphics.FromHwnd(Handle) : Graphics.FromHdc(m.WParam);
        int arrowWidth = SystemInformation.VerticalScrollBarWidth;
        using var background = new SolidBrush(DarkTheme.Surface);
        graphics.FillRectangle(background, Width - arrowWidth - 1, 1, arrowWidth, Height - 2);
        using var border = new Pen(DarkTheme.Border);
        graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        using var arrow = new Pen(Enabled ? ForeColor : Color.Gray, 1.5f);
        float cx = Width - arrowWidth / 2f - 1, cy = Height / 2f;
        graphics.DrawLines(arrow, [new PointF(cx - 3, cy - 2), new PointF(cx, cy + 1), new PointF(cx + 3, cy - 2)]);
    }
}
