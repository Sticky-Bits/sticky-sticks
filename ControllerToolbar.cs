sealed class ControllerToolbar : Panel
{
    readonly StickView view;
    readonly ComboBox picker = new DarkComboBox { Width = 620, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Button refresh = new() { Text = "Refresh", Width = 100 };
    readonly ComboBox[] axes = new ComboBox[4];
    readonly CheckBox[] inversions = new CheckBox[4];
    bool listing, changing, closing;
    public ControllerToolbar(StickView view)
    {
        this.view = view;
        Height = 54; Padding = new Padding(20, 8, 20, 8);
        BackColor = Color.FromArgb(24, 30, 42); ForeColor = Color.White;
        var row = new TableLayoutPanel { Dock = DockStyle.Top, Height = 36, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        picker.Dock = DockStyle.Fill;
        picker.AccessibleName = "Controller";
        DarkTheme.StylePicker(picker);
        refresh.Dock = DockStyle.Fill;
        var settingsButton = new Button { Text = "Settings", Dock = DockStyle.Fill, Name = "SettingsButton" };
        DarkTheme.StyleButton(refresh); DarkTheme.StyleButton(settingsButton);
        row.Controls.Add(new Label { Text = "Input Selection", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(180, 190, 205) }, 0, 0);
        row.Controls.Add(picker, 1, 0); row.Controls.Add(refresh, 2, 0); row.Controls.Add(settingsButton, 3, 0);
        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, Visible = false, Padding = new Padding(0, 8, 0, 0),
            BackColor = BackColor, ForeColor = ForeColor, Name = "SettingsPanel"
        };
        Controls.Add(options); Controls.Add(row);
        settingsButton.Click += (_, _) =>
        {
            bool expanded = !options.Visible;
            options.Visible = expanded;
            Height = expanded ? 164 : 54;
            settingsButton.Text = expanded ? "Close settings" : "Settings";
        };
        var rounding = new CheckBox { Text = "Round to 3 decimals", Checked = true, AutoSize = true, Margin = new Padding(10, 5, 0, 0) };
        rounding.CheckedChanged += (_, _) => { view.RoundDisplay = rounding.Checked; view.Invalidate(); };
        for (int i = 0; i < 4; i++)
        {
            int axis = i;
            options.Controls.Add(new Label { Text = new[] { "Left X", "Left Y", "Right X", "Right Y" }[i], AutoSize = true, Margin = new Padding(8, 7, 3, 0) });
            axes[i] = new DarkComboBox { Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
            DarkTheme.StylePicker(axes[i]);
            inversions[i] = new CheckBox { Text = "Invert", AutoSize = true };
            axes[i].SelectedIndexChanged += (_, _) => view.Mapping[axis] = axes[axis].SelectedIndex - 1;
            inversions[i].CheckedChanged += (_, _) => view.Invert[axis] = inversions[axis].Checked;
            options.Controls.Add(axes[i]); options.Controls.Add(inversions[i]);
        }
        options.SetFlowBreak(inversions[3], true);
        options.Controls.Add(rounding);
        var octagonal = new CheckBox { Text = "Octagonal gate (visual only)", AutoSize = true, Margin = new Padding(12, 5, 0, 0) };
        octagonal.CheckedChanged += (_, _) => { view.OctagonalGate = octagonal.Checked; view.Invalidate(); };
        options.Controls.Add(octagonal);
        picker.SelectedIndexChanged += async (_, _) => { if (!listing) await SelectController(); };
        refresh.Click += async (_, _) => await RefreshControllers();
    }
    async Task SelectController()
    {
        if (changing || listing || closing) return;
        changing = true; picker.Enabled = refresh.Enabled = false;
        view.IsConnecting = true; view.Invalidate();
        try
        {
            if (view.StopAnalyzer != null) await view.StopAnalyzer();
            var previous = view.Source; view.Source = null;
            if (previous != null) await previous.DisposeAsync();
            if (closing) return;
            if (picker.SelectedItem is ControllerChoice choice)
            {
                var source = await Task.Run(choice.Open);
                if (closing) { await source.DisposeAsync(); return; }
                view.Source = source;
                for (int i = 0; i < 4; i++)
                {
                    axes[i].Items.Clear(); axes[i].Items.Add("None"); axes[i].Items.AddRange(source.Axes);
                    axes[i].SelectedIndex = source.DefaultMapping[i] + 1;
                    inversions[i].Checked = source is JoystickSource or SdlSource && (i == 1 || i == 3);
                }
            }
        }
        catch (Exception ex) { if (!closing) MessageBox.Show(FindForm(), ex.Message, "Controller unavailable"); }
        finally
        {
            changing = false; view.IsConnecting = false; view.Invalidate();
            picker.Enabled = refresh.Enabled = !closing;
        }
    }
    public async Task RefreshControllers()
    {
        if (changing || listing || closing) return;
        string? selected = (picker.SelectedItem as ControllerChoice)?.Id;
        try
        {
            listing = true;
            picker.Enabled = refresh.Enabled = false;
            view.IsScanning = true; view.Invalidate();
            if (view.StopAnalyzer != null) await view.StopAnalyzer();
            var choices = await Task.Run(ControllerSource.Discover);
            if (closing) return;
            picker.Items.Clear(); picker.Items.AddRange(choices.ToArray());
            int index = choices.FindIndex(c => c.Id == selected);
            if (index < 0) index = choices.FindIndex(c => c.Id.StartsWith("sdl:", StringComparison.OrdinalIgnoreCase) && c.Id != "sdl:unavailable");
            picker.SelectedIndex = index >= 0 ? index : choices.Count > 0 ? 0 : -1;
            listing = false;
            if (view.Source == null || (picker.SelectedItem as ControllerChoice)?.Id != selected) await SelectController();
        }
        catch (Exception ex) { if (!closing) MessageBox.Show(FindForm(), ex.Message, "Controller discovery"); }
        finally
        {
            listing = false; view.IsScanning = false; view.Invalidate();
            picker.Enabled = refresh.Enabled = !closing && !changing;
        }
    }
    public async Task CloseSource()
    {
        closing = true;
        // Native discovery cannot be cancelled safely; wait asynchronously before SDL shutdown.
        while (changing || listing) await Task.Delay(10);
        if (view.StopAnalyzer != null) await view.StopAnalyzer();
        if (view.Source != null) { await view.Source.DisposeAsync(); view.Source = null; }
    }
}

