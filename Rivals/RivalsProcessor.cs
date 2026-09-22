sealed class RivalsProcessor
{
    readonly PressDetector left = new(4), right = new(null);
    (ControllerSource? Source, RivalsSettings Settings, AxisConfiguration Mapping)? previous;
    public RivalsFrame Process(ControllerFrame? frame, ControllerSession session, RivalsSettings settings)
    {
        var key = (session.Source, settings, AxisConfiguration.Capture(session.Mapping, session.Invert));
        if (previous != key || frame == null) { left.Reset(); right.Reset(); previous = key; }
        var mods = ModActivation.Resolve(settings.Mod, frame?.LeftTrigger, frame?.RightTrigger);
        StickResult Build(bool isRight, PressDetector detector)
        {
            int axis = isRight ? 2 : 0;
            var raw = new RawPosition(AxisMapping.Read(frame, axis, session.Mapping, session.Invert), AxisMapping.Read(frame, axis + 1, session.Mapping, session.Invert));
            bool vx = double.IsFinite(raw.X), vy = double.IsFinite(raw.Y);
            var final = Coordinates.ToGame(raw, settings, isRight, frame?.LeftTrigger, frame?.RightTrigger);
            double threshold = (isRight ? settings.RightThreshold : settings.HardPress) / 100.0;
            if (vx && vy) detector.Sample(final.X, final.Y, threshold); else detector.Reset();
            long? Device(int a) => frame != null && session.Mapping[a] >= 0 && session.Mapping[a] < frame.Raw.Length ? frame.Raw[session.Mapping[a]] : null;
            var mechanics = Mechanics.All.Where(m => m.Right == isRight).Select(m => new MechanicResult(m, m.Active(final, settings), m.Threshold(settings),
                Coordinates.ProjectGuides(m, settings, true), Coordinates.ProjectGuides(m, settings, false))).ToArray();
            var max = isRight ? (X: 1.0, Y: 1.0) : StickReachability.Maximum(settings.Sensitivity, settings.Mod, frame?.LeftTrigger, frame?.RightTrigger);
            return new(raw, final, Device(axis), Device(axis + 1), vx, vy, detector.Text, detector.Ambiguous, Array.AsReadOnly(mechanics),
                (isRight ? 20 : settings.Deadzone) / 100.0, StickProcessing.MechanicRawBoundary(isRight ? 20 : settings.Deadzone, isRight ? 100 : settings.Sensitivity, threshold), new(max.X, max.Y));
        }
        var l = Build(false, left); var r = Build(true, right);
        double ledgeThreshold = Mechanics.All.Single(m => m.Id == "ledge").Threshold(settings);
        return new(l, r, mods, l.Maximum.Y >= settings.HardPress / 100.0 && settings.HardPress / 100.0 >= ledgeThreshold);
    }
}
