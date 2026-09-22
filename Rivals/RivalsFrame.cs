readonly record struct RawPosition(double X, double Y);
readonly record struct GamePosition(double X, double Y);
readonly record struct RivalsSettings(int Deadzone = 10, int Sensitivity = 120, int HardPress = 80, int RightThreshold = 35, ModMode Mod = ModMode.Triggers);
readonly record struct AxisRegion(bool XAxis, int Sign, double Boundary, double OtherMin, double OtherMax, bool IncludeMin = true, bool IncludeMax = true)
{
    public bool Contains(RawPosition p, bool strict)
    {
        double axis = Sign * (XAxis ? p.X : p.Y), other = XAxis ? p.Y : p.X;
        return (strict ? axis > Boundary : axis >= Boundary) &&
            (IncludeMin ? other >= OtherMin : other > OtherMin) && (IncludeMax ? other <= OtherMax : other < OtherMax);
    }
}
sealed record MechanicResult(MechanicDefinition Definition, bool Active, double Threshold, IReadOnlyList<AxisRegion> RawRegions, IReadOnlyList<AxisRegion> GameRegions);
sealed record StickResult(RawPosition Raw, GamePosition Final, long? DeviceX, long? DeviceY, bool ValidX, bool ValidY,
    string PressText, bool Ambiguous, IReadOnlyList<MechanicResult> Mechanics, double Deadzone, double RawThreshold, GamePosition Maximum);
sealed record RivalsFrame(StickResult Left, StickResult Right, (bool X, bool Y) Mods, bool FastfallWarning);

