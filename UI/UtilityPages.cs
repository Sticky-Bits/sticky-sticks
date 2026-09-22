// Device selection and its settings remain outside these pages, sharing one connection.
sealed class UtilityPages : Panel
{
    public UtilityPages(StickView view, TuningControls tuning)
    {
        Name = "UtilityPages";
        var profileLoader = new ProfileLoader(tuning);
        view.Session.SourceChanged += UpdateInputMode;
        void UpdateInputMode()
        {
            bool keyboard = view.Session.Source is KeyboardSource;
            tuning.SetKeyboard(keyboard);
            profileLoader.Enabled = !keyboard;
        }
        Disposed += (_, _) => view.Session.SourceChanged -= UpdateInputMode;
        UpdateInputMode();
        BackColor = DarkTheme.Background;
        var rivals = new Panel
        {
            Name = "RivalsPage", Dock = DockStyle.Fill, BackColor = BackColor
        };
        // Keep diagrams and statistics legible when settings reduce the available height.
        var plots = new StickScrollPanel(view) { Dock = DockStyle.Fill };
        view.Dock = DockStyle.Top;
        view.Height = 640;
        plots.Controls.Add(view);
        rivals.Controls.Add(plots);
        var sidebar = new Panel
        {
            Dock = DockStyle.Right, Width = 320, AutoScroll = true,
            BackColor = DarkTheme.Surface, Padding = new Padding(8)
        };
        tuning.Dock = DockStyle.Top;
        sidebar.Controls.Add(new CollapsibleSection("Stick Settings", tuning));
        sidebar.Controls.Add(new CollapsibleSection("Mechanics", new MechanicsControls(view)));
        sidebar.Controls.Add(new CollapsibleSection("Profiles", profileLoader));
        rivals.Controls.Add(sidebar);
        var analyzer = new ThumbstickAnalyzer(view.Session)
        {
            Name = "AnalyzerPage", Dock = DockStyle.Fill, BackColor = BackColor, Visible = false
        };
        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 42, BackColor = DarkTheme.Surface,
            Padding = new Padding(20, 0, 0, 0), WrapContents = false
        };
        var rivalsButton = new Button { Text = "Rivals of Aether 2", Width = 210, Height = 40, Margin = Padding.Empty };
        var analyzerButton = new Button { Text = "Thumbstick Analyzer", Width = 210, Height = 40, Margin = new Padding(4, 0, 0, 0) };
        foreach (var button in new[] { rivalsButton, analyzerButton })
        {
            DarkTheme.StyleButton(button);
            button.FlatAppearance.BorderSize = 0;
            navigation.Controls.Add(button);
        }
        Controls.Add(rivals); Controls.Add(analyzer); Controls.Add(navigation);
        void SelectPage(bool showRivals)
        {
            if (showRivals && view.Session.AnalyzerBusy) _ = analyzer.StopAsync();
            rivals.Visible = showRivals; analyzer.Visible = !showRivals;
            rivalsButton.BackColor = showRivals ? Color.FromArgb(39, 49, 67) : DarkTheme.Surface;
            analyzerButton.BackColor = !showRivals ? Color.FromArgb(39, 49, 67) : DarkTheme.Surface;
            rivalsButton.ForeColor = showRivals ? Color.White : Color.FromArgb(148, 160, 181);
            analyzerButton.ForeColor = !showRivals ? Color.White : Color.FromArgb(148, 160, 181);
        }
        rivalsButton.Click += (_, _) => SelectPage(true);
        analyzerButton.Click += (_, _) => SelectPage(false);
        SelectPage(true);
    }
}

// Size the content before ScrollableControl decides whether scrollbars are needed.
// A Resize event runs after that decision and can alternate stale scroll extents.
sealed class StickScrollPanel(Control content) : Panel
{
    bool layingOut;
    protected override void OnLayout(LayoutEventArgs e)
    {
        if (layingOut) return;
        layingOut = true;
        try
        {
            AutoScroll = true;
            int height = Math.Max(640, ClientSize.Height);
            if (content.Height != height) content.Height = height;
            base.OnLayout(e);
        }
        finally { layingOut = false; }
    }
    public static void VerifyResize()
    {
        using var content = new Panel { Dock = DockStyle.Top, Height = 640 };
        using var panel = new StickScrollPanel(content) { Size = new Size(800, 720) };
        using var form = new Form();
        panel.Controls.Add(content); form.Controls.Add(panel);
        form.Show();
        foreach (int height in Enumerable.Range(600, 121).Reverse().Concat(Enumerable.Range(600, 121)))
        {
            panel.Height = height; panel.PerformLayout();
            if (panel.VerticalScroll.Visible != (height < 640) || panel.HorizontalScroll.Visible || content.Height != Math.Max(640, height))
                throw new Exception($"Stick plot scroll layout failed at height {height}.");
        }
        form.Close();
    }
}
