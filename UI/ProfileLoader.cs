sealed class ProfileLoader : FlowLayoutPanel
{
    readonly ToolTip help = new();
    readonly Button attempt = new() { Text = "Attempt to load profiles", Width = 276, Height = 32 };
    readonly Button browse = new() { Text = "Browse for save file…", Width = 276, Height = 30 };
    readonly ComboBox profiles = new DarkComboBox { Width = 276, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Saved profile" };
    readonly ComboBox controllers = new DarkComboBox { Width = 276, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Saved controller preset" };
    readonly Label status = new() { AutoSize = true, MaximumSize = new Size(276, 0), ForeColor = Color.FromArgb(255, 210, 90) };
    readonly Label values = new() { AutoSize = true, MaximumSize = new Size(276, 0) };
    readonly Button apply = new() { Text = "Load selected profile", Width = 276, Height = 30 };
    readonly FlowLayoutPanel selection = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false, Margin = Padding.Empty };
    IReadOnlyList<SavedControllerProfile> loaded = [];
    public ProfileLoader(TuningControls tuning)
    {
        Dock = DockStyle.Top; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlowDirection = FlowDirection.TopDown; WrapContents = false; Padding = new Padding(8, 4, 0, 10);
        BackColor = DarkTheme.Surface; ForeColor = Color.White;
        foreach (var button in new[] { attempt, browse, apply }) DarkTheme.StyleButton(button);
        DarkTheme.StylePicker(profiles); DarkTheme.StylePicker(controllers);
        help.SetToolTip(browse, @"Usual location: %LocalAppData%\Rivals2\Saved\SaveGames\Rivals2_PlayerTagSaveSlot.sav");
        Controls.Add(attempt); Controls.Add(browse); Controls.Add(status); Controls.Add(selection);
        selection.Controls.Add(new Label { Text = "Profile", AutoSize = true }); selection.Controls.Add(profiles);
        selection.Controls.Add(new Label { Text = "Controller preset", AutoSize = true }); selection.Controls.Add(controllers);
        selection.Controls.Add(values); selection.Controls.Add(apply);
        attempt.Click += async (_, _) => await LoadFile(ProfileSaveReader.DefaultPath);
        browse.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "Choose Rivals 2 profile save", Filter = "Rivals save files (*.sav)|*.sav|All files (*.*)|*.*", FileName = ProfileSaveReader.FileName, CheckFileExists = true };
            var directory = Path.GetDirectoryName(ProfileSaveReader.DefaultPath);
            if (Directory.Exists(directory)) dialog.InitialDirectory = directory;
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK) await LoadFile(dialog.FileName);
        };
        profiles.SelectedIndexChanged += (_, _) =>
        {
            controllers.Items.Clear();
            foreach (var profile in loaded.Where(p => p.Name == profiles.SelectedItem as string)) controllers.Items.Add(profile.ControllerType);
            if (controllers.Items.Count > 0) controllers.SelectedIndex = 0;
        };
        controllers.SelectedIndexChanged += (_, _) =>
        {
            var profile = Selected();
            values.Text = profile == null ? "" : profile.Summary + "\n\nSaved floats are rounded to whole percentages for these sliders.";
            apply.Enabled = profile != null;
        };
        apply.Click += (_, _) =>
        {
            if (Selected() is not { } profile) return;
            tuning.LoadProfile(profile);
            status.ForeColor = Color.Aquamarine;
            status.Text = $"Loaded {profile.Name} · {profile.ControllerType}";
        };
    }
    SavedControllerProfile? Selected() => loaded.FirstOrDefault(p => p.Name == profiles.SelectedItem as string && p.ControllerType == controllers.SelectedItem as string);
    protected override void Dispose(bool disposing)
    {
        if (disposing) help.Dispose();
        base.Dispose(disposing);
    }
    async Task LoadFile(string path)
    {
        attempt.Enabled = browse.Enabled = false; selection.Visible = false;
        status.ForeColor = Color.FromArgb(255, 210, 90); status.Text = "Reading profiles…";
        try
        {
            var result = await Task.Run(() => ProfileSaveReader.Load(path));
            if (IsDisposed) return;
            loaded = result; profiles.Items.Clear();
            profiles.Items.AddRange(loaded.Select(p => p.Name).Distinct().Cast<object>().ToArray());
            profiles.SelectedIndex = 0; selection.Visible = true;
            status.ForeColor = Color.Aquamarine;
            status.Text = $"Found {profiles.Items.Count} profiles. Choose the controller preset used in game.";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            if (IsDisposed) return;
            status.Text = ex is FileNotFoundException or DirectoryNotFoundException
                ? "No profile save found in AppData. Use Browse to choose the file yourself."
                : $"Could not read profiles: {ex.Message}\nUse Browse to choose another save, or try again after the game finishes saving.";
        }
        finally { if (!IsDisposed) attempt.Enabled = browse.Enabled = true; }
    }
    public static void Preview(string output)
    {
        Application.EnableVisualStyles();
        using var form = new Form { ClientSize = new Size(640, 800) };
        using var tuning = new TuningControls { Dock = DockStyle.Right, Width = 320 };
        using var loader = new ProfileLoader(tuning) { Dock = DockStyle.Fill };
        form.Controls.Add(loader); form.Controls.Add(tuning);
        form.Shown += async (_, _) =>
        {
            try
            {
                await loader.LoadFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sav"));
                if (loader.selection.Visible || !loader.browse.Enabled || !loader.status.Text.Contains("No profile save"))
                    throw new Exception("Missing-file fallback failed.");
                await loader.LoadFile(ProfileSaveReader.DefaultPath);
                if (!loader.selection.Visible || loader.Selected() is not { } selected) throw new Exception("Profile selection failed.");
                loader.apply.PerformClick();
                if (tuning.LeftDeadzonePercent != SavedControllerProfile.Percent(selected.Deadzone) ||
                    tuning.LeftSensitivityPercent != SavedControllerProfile.Percent(selected.Sensitivity) ||
                    tuning.HardPressPercent != SavedControllerProfile.Percent(selected.HardPress) ||
                    tuning.RightThresholdPercent != SavedControllerProfile.Percent(selected.RightThreshold))
                    throw new Exception("Profile did not update all four sliders.");
                using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
                form.DrawToBitmap(bitmap, form.ClientRectangle); bitmap.Save(output);
                File.WriteAllText(output + ".txt", "PASS: missing-file fallback, profile selection and all four slider values.");
            }
            catch (Exception ex) { File.WriteAllText(output + ".txt", "FAIL: " + ex); }
            finally { form.Close(); }
        };
        Application.Run(form);
    }
}

