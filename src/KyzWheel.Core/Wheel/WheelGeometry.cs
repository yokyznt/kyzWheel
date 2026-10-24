namespace KyzWheel.Core.Wheel;

public readonly record struct Rect(double X, double Y, double W, double H);

public static class WheelGeometry
{
    public static int SliceAt(double dx, double dy, int count, double innerR, double outerR)
    {
        if (count <= 0) return -1;
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (r < innerR || r > outerR) return -1;
        var sweep = 360.0 / count;
        var angle = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        var shifted = ((angle + sweep / 2) % 360 + 360) % 360;
        return Math.Min(count - 1, (int)(shifted / sweep));
    }

    public static (double startDeg, double sweepDeg) SliceArc(int index, int count)
    {
        var sweep = 360.0 / count;
        return (index * sweep - sweep / 2, sweep);
    }

    public static (double x, double y) ClampToWorkArea(double cx, double cy, double radius, Rect workArea)
    {
        var x = Math.Clamp(cx, workArea.X + radius, Math.Max(workArea.X + radius, workArea.X + workArea.W - radius));
        var y = Math.Clamp(cy, workArea.Y + radius, Math.Max(workArea.Y + radius, workArea.Y + workArea.H - radius));
        return (x, y);
    }
}
