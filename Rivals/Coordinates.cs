static class Coordinates
{
    // Reference guides describe the deadzone/sensitivity mapping. Mod clamps change
    // the final position, not the location or visibility of these reference lines.
    public static IReadOnlyList<AxisRegion> ProjectGuides(MechanicDefinition mechanic, RivalsSettings settings, bool raw) =>
        Project(mechanic, settings with { Mod = ModMode.Disabled }, raw, null, null);
    public static GamePosition ToGame(RawPosition raw, RivalsSettings settings, bool right, double? lt, double? rt)
    {
        if (right) return new(StickProcessing.RightOutput(raw.X), StickProcessing.RightOutput(raw.Y));
        var p = ModActivation.Apply(StickProcessing.LeftOutput(raw.X, settings.Deadzone, settings.Sensitivity),
            StickProcessing.LeftOutput(raw.Y, settings.Deadzone, settings.Sensitivity), settings.Mod, lt, rt);
        return new(p.X, p.Y);
    }
    // A clamp has no unique inverse. Project each axis into regions where its cap
    // is constant: the other axis is either inside or outside its deadzone.
    public static IReadOnlyList<AxisRegion> Project(MechanicDefinition mechanic, RivalsSettings settings, bool raw, double? lt, double? rt)
    {
        var regions = new List<AxisRegion>();
        double threshold = mechanic.Threshold(settings);
        double dz = mechanic.Right ? .2 : settings.Deadzone / 100.0;
        double boundary = raw ? StickProcessing.MechanicRawBoundary(mechanic.Right ? 20 : settings.Deadzone, mechanic.Right ? 100 : settings.Sensitivity, threshold) : threshold;
        foreach (bool xAxis in new[] { true, false })
        foreach (int sign in new[] { -1, 1 })
        {
            if (xAxis ? !mechanic.XAxis : sign == 1 ? !mechanic.PositiveY : !mechanic.NegativeY) continue;
            if (!raw) { regions.Add(new(xAxis, sign, boundary, double.NegativeInfinity, double.PositiveInfinity)); continue; }
            foreach (var band in new[] { (Min: double.NegativeInfinity, Max: -dz, Other: -1.0), (Min: -dz, Max: dz, Other: 0.0), (Min: dz, Max: double.PositiveInfinity, Other: 1.0) })
            {
                var maximum = ToGame(xAxis ? new(1, band.Other) : new(band.Other, 1), settings, mechanic.Right, lt, rt);
                if (mechanic.Reached(xAxis ? maximum.X : maximum.Y, threshold))
                    regions.Add(new(xAxis, sign, boundary, band.Min, band.Max, band.Other != 1, band.Other != -1));
            }
        }
        return regions.AsReadOnly();
    }
}
