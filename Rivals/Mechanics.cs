sealed record MechanicDefinition(string Id, string Name, string Helper, double FixedThreshold, bool XAxis, bool PositiveY, bool NegativeY,
    Color Color, bool DefaultVisible = false, bool Right = false, bool Strict = false, int LabelOffset = 0)
{
    public double Threshold(RivalsSettings settings) => Id switch { "hard" => settings.HardPress / 100.0, "right" => settings.RightThreshold / 100.0, _ => FixedThreshold };
    public bool Reached(double value, double threshold) => Strict ? value > threshold : value >= threshold;
    public bool Active(GamePosition p, RivalsSettings settings) =>
        (XAxis && Reached(Math.Abs(p.X), Threshold(settings))) || (PositiveY && Reached(p.Y, Threshold(settings))) || (NegativeY && Reached(-p.Y, Threshold(settings)));
}
static class Mechanics
{
    public static readonly IReadOnlyList<MechanicDefinition> All = Array.AsReadOnly(new MechanicDefinition[]
    {
        new("hard", "Hard press threshold", "Hard press detected", 0, true, true, true, Color.FromArgb(255,177,74), true),
        new("right", "Right stick threshold", "Threshold detected", 0, true, true, true, Color.FromArgb(255,177,74), true, true),
        new("ledge", "Ledge grab box disable (game 0.8)", "Ledge box disabled", .8, false, false, true, Color.FromArgb(220,110,255), LabelOffset: 3),
        new("universal", "Universal minimum input (0.2875)", "Directional input", .2875, true, true, true, Color.FromArgb(70,230,230), Strict: true),
        new("crouch", "Crouch (0.626 down)", "Crouch", .626, false, false, true, Color.FromArgb(255,225,70), LabelOffset: -18),
        new("strong", "Tap Strongs (0.655)", "Tap Strongs", .655, true, true, true, Color.FromArgb(255,110,150)),
        new("special", "Directional Specials (0.42)", "Directional Specials", .42, true, true, true, Color.FromArgb(130,220,90)),
        new("reverse", "B Reverse (0.6 left/right)", "B Reverse", .6, true, false, false, Color.FromArgb(100,160,255))
    });
}
