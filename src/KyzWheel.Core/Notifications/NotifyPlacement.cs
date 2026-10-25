namespace KyzWheel.Core.Notifications;

public readonly record struct ScreenRect(int X, int Y, int W, int H)
{
    public bool Contains(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;
}

public static class NotifyPlacement
{
    public const int Margin = 16;

    public static readonly string[] Positions = ["top-center", "top-right", "top-left", "bottom-right", "bottom-center", "bottom-left"];

    public static ScreenRect Screen(IReadOnlyList<ScreenRect> works, int primary, (int X, int Y) cursor, string? monitor)
    {
        if (works.Count == 0) return new ScreenRect(0, 0, 1920, 1080);
        primary = Math.Clamp(primary, 0, works.Count - 1);
        if (monitor == "cursor") return works.FirstOrDefault(w => w.Contains(cursor.X, cursor.Y), works[primary]);
        if (int.TryParse(monitor, out var n) && n >= 1 && n <= works.Count) return works[n - 1];
        return works[primary];
    }

    public static (int X, int Y) Place(IReadOnlyList<ScreenRect> works, int primary, (int X, int Y) cursor, string? monitor, string? position, int w, int h)
    {
        var s = Screen(works, primary, cursor, monitor);
        var pos = Positions.Contains(position) ? position! : "top-center";
        var x = pos.EndsWith("left") ? s.X + Margin : pos.EndsWith("right") ? s.X + s.W - w - Margin : s.X + (s.W - w) / 2;
        var y = pos.StartsWith("bottom") ? s.Y + s.H - h - Margin : s.Y + Margin;
        return (x, y);
    }
}
