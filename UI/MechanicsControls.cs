sealed class MechanicsControls : FlowLayoutPanel
{
    public MechanicsControls(StickView view)
    {
        Dock = DockStyle.Top; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlowDirection = FlowDirection.TopDown; WrapContents = false; Padding = new Padding(8, 6, 4, 12);
        BackColor = DarkTheme.Surface; ForeColor = Color.White;
        CheckBox Toggle(string text, bool initial, Action<bool> apply)
        {
            var check = new CheckBox { Text = text, Checked = initial, AutoSize = true, Margin = new Padding(0, 4, 20, 4) };
            check.CheckedChanged += (_, _) => { apply(check.Checked); view.Invalidate(); };
            Controls.Add(check);
            return check;
        }
        CheckBox? rightThreshold = null;
        foreach (var mechanic in Mechanics.All)
        {
            var toggle = Toggle(mechanic.Name, view.VisibleMechanics.Contains(mechanic.Id), value =>
            {
                if (value) view.VisibleMechanics.Add(mechanic.Id);
                else view.VisibleMechanics.Remove(mechanic.Id);
            });
            if (mechanic.Right) rightThreshold = toggle;
        }
        Controls.Add(new Label { Text = "View mechanics in:", AutoSize = true, Margin = new Padding(0, 5, 12, 0) });
        var raw = new RadioButton { Text = "Raw coordinates", Checked = true, AutoSize = true };
        var game = new RadioButton { Text = "Game coordinates", AutoSize = true };
        var deadzone = new CheckBox { Text = "Show deadzone", Checked = true, AutoSize = true, Margin = new Padding(0, 3, 0, 8) };
        raw.CheckedChanged += (_, _) => { view.RawMechanics = raw.Checked; deadzone.Enabled = raw.Checked && view.Session.Source is not KeyboardSource; view.Invalidate(); };
        deadzone.CheckedChanged += (_, _) => { view.ShowDeadzone = deadzone.Checked; view.Invalidate(); };
        Controls.Add(raw); Controls.Add(game); Controls.Add(deadzone);
        SetFlowBreak(deadzone, true);
        Controls.Add(new Label { Text = "Mod:", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        Controls.Add(new ModControls(view));
        void UpdateMode()
        {
            deadzone.Enabled = raw.Checked && view.Session.Source is not KeyboardSource;
            if (rightThreshold != null) rightThreshold.Enabled = view.Session.Source is not KeyboardSource;
        }
        view.Session.SourceChanged += UpdateMode;
        Disposed += (_, _) => view.Session.SourceChanged -= UpdateMode;
        UpdateMode();

    }
    sealed class PreviewSource : ControllerSource
    {
        public override string[] Axes => ["LX", "LY", "RX", "RY"];
        public override ControllerFrame Read() => new([.1, .8, -.4, .6], [3277, 26214, -13107, 19660], "Synthetic controller");
    }
    public static void Preview(string path)
    {
        Application.EnableVisualStyles();
        using var view = new StickView { Dock = DockStyle.Fill };
        using var form = new Form { ClientSize = new Size(1440, 1080) };
        var pages = new UtilityPages(view, new TuningControls()) { Dock = DockStyle.Fill };
        form.Controls.Add(pages);
        form.Controls.Add(new ControllerToolbar(view) { Dock = DockStyle.Top });
        DarkTheme.StyleWindow(form);
        MechanicsControls? FindMechanics(Control parent) => parent as MechanicsControls ?? parent.Controls.Cast<Control>().Select(FindMechanics).FirstOrDefault(c => c != null);
        ModControls? FindMods(Control parent) => parent as ModControls ?? parent.Controls.Cast<Control>().Select(FindMods).FirstOrDefault(c => c != null);
        var controls = FindMechanics(pages)!;
        form.Shown += (_, _) => form.BeginInvoke((Action)(() =>
        {
            view.Session.Source = new PreviewSource();
            view.Sample();
            var snapshot = view.CurrentFrame;
            using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(bitmap, form.ClientRectangle); bitmap.Save(path);
            var game = controls.Controls.OfType<RadioButton>().Single(r => r.Text == "Game coordinates");
            game.Checked = true;
            if (view.RawMechanics || !view.ShowDeadzone) throw new Exception("Game space must preserve the deadzone preference.");
            form.DrawToBitmap(bitmap, form.ClientRectangle); bitmap.Save(path + ".game.png");
            view.RoundDisplay = false;
            form.DrawToBitmap(bitmap, form.ClientRectangle);
            if (!ReferenceEquals(snapshot, view.CurrentFrame)) throw new Exception("Painting or formatting advanced the game frame.");
            view.RoundDisplay = true;
            controls.Controls.OfType<RadioButton>().Single(r => r.Text == "Raw coordinates").Checked = true;
            FindMods(pages)!.Controls.OfType<RadioButton>().Single(r => r.Text == "X").Checked = true;
            foreach (var check in controls.Controls.OfType<CheckBox>().Where(c => c.Text != "Show deadzone"))
                check.Checked = check.Text == "Up/Down specials (0.42)";
            view.Sample();
            form.DrawToBitmap(bitmap, form.ClientRectangle); bitmap.Save(path + ".mod.png");
            form.ClientSize = new Size(1200, 930);
            form.PerformLayout();
            using var smaller = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(smaller, form.ClientRectangle); smaller.Save(path + ".small.png");
            IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
            var sections = Descendants(pages).OfType<CollapsibleSection>().ToArray();
            var sidebar = (Panel)sections[0].Parent!;
            foreach (var section in sections) section.Expanded = false;
            form.PerformLayout(); sidebar.PerformLayout();
            if (sections.Length != 3 || sections.Any(s => s.Height != 40) || sidebar.VerticalScroll.Visible)
                throw new Exception("Collapsed sections did not release sidebar space.");
            form.DrawToBitmap(smaller, form.ClientRectangle); smaller.Save(path + ".collapsed.png");
            foreach (var section in sections) section.Expanded = true;
            form.PerformLayout(); sidebar.PerformLayout();
            if (!sidebar.VerticalScroll.Visible || view.Mod != ModMode.X || !view.VisibleMechanics.Contains("special"))
                throw new Exception("Expanding sections lost settings or failed to restore scrolling.");
            var tuning = Descendants(pages).OfType<TuningControls>().Single();
            int savedSensitivity = tuning.LeftSensitivityPercent;
            view.Session.Source = new KeyboardSource();
            view.Sample();
            if (tuning.LeftSensitivityPercent != 100 || tuning.LeftDeadzonePercent != 10 || tuning.HardPressPercent != 80 ||
                tuning.Controls.OfType<PercentControl>().Any(c => c.Enabled) ||
                controls.Controls.OfType<CheckBox>().Single(c => c.Text == "Show deadzone").Enabled)
                throw new Exception("Keyboard fixed settings failed.");
            form.DrawToBitmap(smaller, form.ClientRectangle); smaller.Save(path + ".keyboard.png");
            view.Session.Source = new PreviewSource();
            if (tuning.LeftSensitivityPercent != savedSensitivity || tuning.Controls.OfType<PercentControl>().Any(c => !c.Enabled))
                throw new Exception("Controller settings were not restored after keyboard input.");
            form.Close();
        }));
        Application.Run(form);
    }
}



