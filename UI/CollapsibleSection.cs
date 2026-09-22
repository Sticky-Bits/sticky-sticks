// Collapsing hides controls without recreating them or changing their values.
sealed class CollapsibleSection : Panel
{
    readonly Button heading;
    readonly Control body;
    readonly string title;
    bool expanded = true;
    [System.ComponentModel.DefaultValue(true)]
    public bool Expanded
    {
        get => expanded;
        set
        {
            expanded = value;
            body.Visible = value;
            heading.Text = $"{(value ? "▴" : "▾")}  {title}";
            heading.AccessibleDescription = value ? "Expanded. Activate to collapse." : "Collapsed. Activate to expand.";
            ResizeSection();
        }
    }
    public CollapsibleSection(string title, Control body)
    {
        this.title = title; this.body = body;
        Dock = DockStyle.Top; BackColor = DarkTheme.Surface;
        Padding = new Padding(0, 0, 0, 6);
        heading = new Button { Height = 34, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft, AccessibleName = title };
        DarkTheme.StyleButton(heading);
        heading.FlatAppearance.BorderSize = 0;
        body.Dock = DockStyle.Top;
        Controls.Add(body); Controls.Add(heading);
        heading.Click += (_, _) => Expanded = !Expanded;
        body.SizeChanged += (_, _) => ResizeSection();
        Expanded = true;
    }
    void ResizeSection()
    {
        int height = heading.Height + Padding.Vertical + (expanded ? body.Height : 0);
        if (Height != height) Height = height;
    }
}

sealed class ModControls : FlowLayoutPanel
{
    public ModControls(StickView view)
    {
        Dock = DockStyle.Top; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = false; Padding = new Padding(8, 6, 0, 10);
        BackColor = DarkTheme.Surface; ForeColor = Color.White;
        foreach (var mode in Enum.GetValues<ModMode>())
        {
            var option = new RadioButton { Text = mode.ToString(), AutoSize = true, Checked = mode == view.Mod };
            option.CheckedChanged += (_, _) => { if (option.Checked) { view.Mod = mode; view.Invalidate(); } };
            Controls.Add(option);
            if (mode == ModMode.Triggers)
            {
                void UpdateLabel() => option.Text = view.Session.Source is KeyboardSource ? "R / F" : "Triggers";
                view.Session.SourceChanged += UpdateLabel;
                Disposed += (_, _) => view.Session.SourceChanged -= UpdateLabel;
                UpdateLabel();
            }
        }
    }
}
