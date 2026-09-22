static class MechanicStateTests
{
    static bool Active(string id, double x, double y) => Mechanics.All.Single(m => m.Id == id).Active(new(x, y), default);
    public static bool Crouch(double y) => Active("crouch", 0, y);
    public static bool TapStrongs(double x, double y) => Active("strong", x, y);
    public static bool DirectionalSpecials(double x, double y) => Active("special", x, y);
    public static bool BReverse(double x) => Active("reverse", x, 0);
    public static bool DirectionalInput(double x, double y) => Active("universal", x, y);
    public static bool LedgeDisabled(double y) => Active("ledge", 0, y);
    public static void SelfTest()
    {
        if (!Crouch(-.626) || Crouch(.626) || Crouch(-.6259) ||
            !TapStrongs(-.655, 0) || !TapStrongs(0, .655) || TapStrongs(.6549, -.6549) ||
            !DirectionalSpecials(0, -.42) || DirectionalSpecials(.4199, 0) || !BReverse(-.6) || BReverse(.5999))
            throw new Exception("Fixed directional mechanic thresholds failed.");
        if (!DirectionalInput(-.2876, 0) || !DirectionalInput(0, .2876) ||
            DirectionalInput(.2875, -.2875) || DirectionalInput(.2, -.2) ||
            !LedgeDisabled(-.8) || LedgeDisabled(-.79999) || LedgeDisabled(.9) ||
            StickReachability.Maximum(120, ModMode.X, null, null) != (.738, .538) ||
            StickReachability.Maximum(120, ModMode.Y, null, null) != (.338, .738) ||
            StickReachability.Maximum(20, ModMode.Disabled, null, null) != (.2, .2))
            throw new Exception("Mechanic thresholds or maximum reach failed.");
    }
}
