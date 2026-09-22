static class RefactorTests
{
    public static void Run()
    {
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        for (int keys = 0; keys < 64; keys++)
        {
            bool Held(int bit) => (keys & (1 << bit)) != 0;
            var keyboard = KeyboardSource.FromKeys(Held(0), Held(1), Held(2), Held(3), Held(4), Held(5));
            Require(keyboard.Values[0] == (Held(1) ? 1 : 0) - (Held(0) ? 1 : 0) &&
                keyboard.Values[1] == (Held(2) ? 1 : 0) - (Held(3) ? 1 : 0) && keyboard.Values[2] == 0 && keyboard.Values[3] == 0,
                "Keyboard cancellation/diagonal values failed.");
            var active = ModActivation.Resolve(ModMode.Triggers, keyboard.LeftTrigger, keyboard.RightTrigger);
            Require(active == (Held(4) && !Held(5), Held(5) && !Held(4)), "Keyboard R/F exclusivity failed.");
        }
        var guideSession = new ControllerSession();
        var guideProcessor = new RivalsProcessor();
        var guideSettings = new RivalsSettings(10, 120, 80, 35, ModMode.Triggers);
        var guideInput = new ControllerFrame([1, 1, 1, 1], [1, 1, 1, 1], "Overlay regression");
        var baseline = guideProcessor.Process(guideInput, guideSession, guideSettings);
        foreach (var triggers in new[] { (LT: 255.0, RT: 0.0), (LT: 0.0, RT: 255.0), (LT: 255.0, RT: 255.0) })
        {
            var held = guideProcessor.Process(guideInput with { LeftTrigger = triggers.LT, RightTrigger = triggers.RT }, guideSession, guideSettings);
            for (int i = 0; i < baseline.Left.Mechanics.Count; i++)
                Require(baseline.Left.Mechanics[i].RawRegions.SequenceEqual(held.Left.Mechanics[i].RawRegions), "Mod trigger changed reference overlays.");
            var expected = ModActivation.Apply(1.2, 1.2, ModMode.Triggers, triggers.LT, triggers.RT);
            Require(held.Left.Final == new GamePosition(expected.X, expected.Y), "Stable guides disabled actual mod processing.");
        }
        var session = new ControllerSession();
        var processor = new RivalsProcessor();
        var settings = new RivalsSettings(50, 200, 40, 50, ModMode.Disabled);
        var input = new ControllerFrame([.75, -.6, .6, -.6], [750, -600, 600, -600], "Test");
        var result = processor.Process(input, session, settings);
        Require(Math.Abs(result.Left.Final.X - 1) < 1e-12 && Math.Abs(result.Left.Final.Y + .4) < 1e-12, "Processing order changed.");
        Require(Math.Abs(result.Right.Final.X - .5) < 1e-12, "Right stick fixed processing changed.");
        var saved = result.Left;
        session.Invert[0] = true; session.Mapping[1] = 2;
        var remapped = processor.Process(input, session, settings);
        Require(remapped.Left.Raw == new RawPosition(-.75, .6) && saved.Raw == new RawPosition(.75, -.6), "Mapping or frame isolation failed.");
        Require(remapped.Left.DeviceY == 600, "Device value does not follow mapping.");
        session.Mapping[0] = -1;
        var invalid = processor.Process(input, session, settings);
        Require(!invalid.Left.ValidX && invalid.Left.PressText == "None", "Invalid mapping did not reset detection.");
        Require(DisplayNumbers.Format(.12351, true) == "0.124" && DisplayNumbers.Format(.12349, true) == "0.123", "Display rounding changed.");
        Require(DisplayNumbers.Format(-.00001, true) == "0.000", "Negative zero formatting changed.");
        var unrounded = Coordinates.ToGame(new(.75012345, 0), settings, false, null, null);
        _ = DisplayNumbers.Format(unrounded.X, true);
        Require(unrounded.X == Coordinates.ToGame(new(.75012345, 0), settings, false, null, null).X, "Formatting altered processing.");
        var random = new Random(73);
        foreach (int deadzone in new[] { 5, 10, 50, 85 })
        foreach (int sensitivity in new[] { 5, 50, 100, 120, 300 })
        foreach (ModMode mode in Enum.GetValues<ModMode>())
        foreach (var triggers in new[] { (0.0, 0.0), (26.0, 0.0), (0.0, 26.0), (255.0, 255.0) })
        {
            var s = new RivalsSettings(deadzone, sensitivity, 80, 35, mode);
            foreach (var mechanic in Mechanics.All)
            {
                var regions = Coordinates.Project(mechanic, s, true, triggers.Item1, triggers.Item2);
                for (int i = 0; i < 100; i++)
                {
                    double dz = mechanic.Right ? .2 : deadzone / 100.0;
                    // Include pure axes and exact deadzone edges as well as arbitrary diagonals.
                    var raw = new RawPosition(i % 10 == 0 ? dz : i % 10 == 1 ? 0 : random.NextDouble() * 2 - 1,
                        i % 10 == 2 ? -dz : i % 10 == 3 ? 0 : random.NextDouble() * 2 - 1);
                    var game = Coordinates.ToGame(raw, s, mechanic.Right, triggers.Item1, triggers.Item2);
                    bool expected = mechanic.Active(game, s);
                    bool projected = regions.Any(r => r.Contains(raw, mechanic.Strict));
                    Require(expected == projected, $"Raw projection mismatch: {mechanic.Id} {s}, raw {raw}, game {game}");
                }
            }
        }
        var modX = settings with { Deadzone = 10, Sensitivity = 120, Mod = ModMode.X };
        var special = Mechanics.All.Single(m => m.Id == "special");
        var specialRegions = Coordinates.Project(special, modX, true, 0, 0);
        Require(specialRegions.Any(r => r.Contains(new(0, 1), false)) && !specialRegions.Any(r => r.Contains(new(.11, 1), false)), "Mod X axis-dependent vertical cap lost.");
        var hard = Mechanics.All.Single(m => m.Id == "hard");
        Require(Coordinates.Project(hard, modX with { HardPress = 80 }, true, 0, 0).Count == 0, "Unreachable mod threshold was projected.");
        // Game-space guides always stay at the game threshold, irrespective of mods.
        Require(Coordinates.Project(hard, modX, false, 0, 0).All(r => r.Boundary == .4), "Game-space boundary moved.");
        var screen = new DiagramTransform(100, 100, 50);
        Require(screen.Raw(new(1, 1)) == new System.Drawing.PointF(150, 50) && screen.Game(new(2, -2)) == new System.Drawing.PointF(200, 200), "Screen projection clamps or flips coordinates.");
    }

    public static async Task SessionLifecycle()
    {
        var source = new SessionSource();
        var session = new ControllerSession { Source = source };
        session.Sample();
        if (source.Reads != 1) throw new Exception("Session did not read source.");
        session.IsScanning = true; session.Sample(); session.IsScanning = false;
        if (source.Reads != 1) throw new Exception("Session polled during discovery.");
        session.StartCapture(_ => { });
        session.Sample();
        if (source.Reads != 1) throw new Exception("UI polled alongside analyzer capture.");
        await session.CloseSource();
        await session.CloseSource();
        if (source.Disposals != 1 || session.Source != null || session.AnalyzerRecording != null || session.AnalyzerBusy)
            throw new Exception("Session shutdown did not release capture and source exactly once.");
    }
    sealed class SessionSource : ControllerSource
    {
        public int Reads, Disposals;
        public override bool HasReportStream => true;
        public override string[] Axes => ["LX", "LY", "RX", "RY"];
        public override ControllerFrame Read() { Reads++; return new([0, 0, 0, 0], [0, 0, 0, 0], "Session test"); }
        public override ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
