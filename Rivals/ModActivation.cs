enum ModMode { Disabled, X, Y, Triggers }
static class ModActivation
{
    public static (double X, double Y) Apply(double x, double y, ModMode mode, double? leftTrigger, double? rightTrigger)
    {
        var active = Resolve(mode, leftTrigger, rightTrigger);
        if (!active.X && !active.Y) return (x, y);
        // Choose limits from the original game-space pair, before either axis is clamped.
        double maxX = active.X ? (y == 0 ? .663 : .738) : (y == 0 ? .338 : .312);
        double maxY = active.X ? (x == 0 ? .538 : .312) : .738;
        return (Math.Clamp(x, -maxX, maxX), Math.Clamp(y, -maxY, maxY));
    }
    public static (bool X, bool Y) Resolve(ModMode mode, double? leftTrigger, double? rightTrigger) => mode switch
    {
        ModMode.X => (true, false),
        ModMode.Y => (false, true),
        ModMode.Triggers => (leftTrigger > 25 && !(rightTrigger > 25), rightTrigger > 25 && !(leftTrigger > 25)),
        _ => (false, false)
    };
    public static void SelfTest()
    {
        static void Check((double X, double Y) actual, double x, double y)
        {
            if (Math.Abs(actual.X - x) > 1e-12 || Math.Abs(actual.Y - y) > 1e-12)
                throw new Exception($"Mod clamp mismatch: {actual} expected ({x}, {y}).");
        }
        foreach (double sign in new[] { -1.0, 1.0 })
        {
            Check(Apply(sign * 2, 0, ModMode.X, null, null), sign * .663, 0);
            Check(Apply(0, sign * 2, ModMode.X, null, null), 0, sign * .538);
            Check(Apply(sign * 2, -sign * 2, ModMode.X, null, null), sign * .738, -sign * .312);
            Check(Apply(sign * 2, 0, ModMode.Y, null, null), sign * .338, 0);
            Check(Apply(0, sign * 2, ModMode.Y, null, null), 0, sign * .738);
            Check(Apply(sign * 2, -sign * 2, ModMode.Y, null, null), sign * .312, -sign * .738);
        }
        Check(Apply(.2, -.1, ModMode.X, null, null), .2, -.1);
        Check(Apply(0, 0, ModMode.Y, null, null), 0, 0);
        Check(Apply(1, .00001, ModMode.X, null, null), .738, .00001);
        Check(Apply(2, -2, ModMode.Disabled, 255, 0), 2, -2);
        Check(Apply(2, -2, ModMode.Triggers, 255, 255), 2, -2);
        Check(Apply(2, -2, ModMode.Triggers, 255, 0), .738, -.312);
        Check(Apply(2, -2, ModMode.Triggers, 0, 255), .312, -.738);
        foreach (var mode in Enum.GetValues<ModMode>())
        foreach (double? left in new double?[] { null, 0, 25, 26, 255 })
        foreach (double? right in new double?[] { null, 0, 25, 26, 255 })
        {
            var result = Resolve(mode, left, right);
            if (result.X && result.Y || mode == ModMode.Disabled && result != (false, false) ||
                mode == ModMode.X && result != (true, false) || mode == ModMode.Y && result != (false, true) ||
                mode == ModMode.Triggers && result != (left > 25 && !(right > 25), right > 25 && !(left > 25)))
                throw new Exception("Exclusive mod activation failed.");
        }
    }
}
