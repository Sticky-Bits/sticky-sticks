using System.Drawing.Drawing2D;

// Raw and game dots share the same unit scale; only mechanic overlays change space.
readonly record struct DiagramTransform(float CenterX, float CenterY, float Radius)
{
    public PointF Raw(RawPosition p) => Point(p.X, p.Y);
    public PointF Game(GamePosition p) => Point(p.X, p.Y);
    public PointF Point(double x, double y) => new(CenterX + (float)x * Radius, CenterY - (float)y * Radius);
}
static class MechanicRenderer
{
    public static void Draw(Graphics g, RectangleF plot, DiagramTransform transform, MechanicResult mechanic, bool raw, Font font)
    {
        var regions = raw ? mechanic.RawRegions : mechanic.GameRegions;
        using var union = new Region(); union.MakeEmpty();
        using var pen = new Pen(mechanic.Definition.Color, 1.5f) { DashStyle = DashStyle.Dash };
        double left = (plot.Left - transform.CenterX) / transform.Radius;
        double right = (plot.Right - transform.CenterX) / transform.Radius;
        double bottom = (transform.CenterY - plot.Bottom) / transform.Radius;
        double top = (transform.CenterY - plot.Top) / transform.Radius;
        foreach (var region in regions)
        {
            double edge = region.Sign * region.Boundary;
            double lo = Math.Max(region.OtherMin, region.XAxis ? bottom : left);
            double hi = Math.Min(region.OtherMax, region.XAxis ? top : right);
            if (lo >= hi) continue;
            double axisMin = Math.Max(region.Sign > 0 ? edge : double.NegativeInfinity, region.XAxis ? left : bottom);
            double axisMax = Math.Min(region.Sign < 0 ? edge : double.PositiveInfinity, region.XAxis ? right : top);
            if (axisMax > axisMin)
            {
                var a = region.XAxis ? transform.Point(axisMin, hi) : transform.Point(lo, axisMax);
                var b = region.XAxis ? transform.Point(axisMax, lo) : transform.Point(hi, axisMin);
                union.Union(RectangleF.FromLTRB(a.X, a.Y, b.X, b.Y));
            }
        }
        using var shade = new SolidBrush(Color.FromArgb(24, mechanic.Definition.Color));
        g.FillRegion(shade, union);
        foreach (var region in regions)
        {
            double lo = Math.Max(region.OtherMin, region.XAxis ? bottom : left);
            double hi = Math.Min(region.OtherMax, region.XAxis ? top : right);
            if (lo >= hi) continue;
            double edge = region.Sign * region.Boundary;
            var a = region.XAxis ? transform.Point(edge, lo) : transform.Point(lo, edge);
            var b = region.XAxis ? transform.Point(edge, hi) : transform.Point(hi, edge);
            g.DrawLine(pen, a, b);
        }
        if (mechanic.Definition.LabelOffset != 0 && regions.Count > 0)
        {
            var point = transform.Point(0, -regions[0].Boundary);
            using var brush = new SolidBrush(mechanic.Definition.Color);
            g.DrawString(mechanic.Definition.Helper, font, brush, plot.Left + 5, point.Y + mechanic.Definition.LabelOffset);
        }
    }
}
