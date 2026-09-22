static class StickReachability
{
    public static (double X, double Y) Maximum(int sensitivityPercent, ModMode mode, double? lt, double? rt)
    {
        double reach = sensitivityPercent / 100.0;
        var diagonal = ModActivation.Apply(reach, reach, mode, lt, rt);
        var horizontal = ModActivation.Apply(reach, 0, mode, lt, rt);
        var vertical = ModActivation.Apply(0, reach, mode, lt, rt);
        return (Math.Max(horizontal.X, diagonal.X), Math.Max(vertical.Y, diagonal.Y));
    }
}
