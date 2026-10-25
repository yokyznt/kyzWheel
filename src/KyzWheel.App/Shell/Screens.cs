using KyzWheel.Core.Notifications;
using Microsoft.UI.Windowing;

namespace KyzWheel.App.Shell;

public static class Screens
{
    public sealed record Info(ScreenRect Work, ScreenRect Bounds, bool Primary);

    public static IReadOnlyList<Info> All()
    {
        var list = new List<Info>();
        try
        {
            var primary = DisplayArea.Primary?.DisplayId.Value;
            var all = DisplayArea.FindAll();
            for (var i = 0; i < all.Count; i++)
            {
                var d = all[i];
                list.Add(new Info(new ScreenRect(d.WorkArea.X, d.WorkArea.Y, d.WorkArea.Width, d.WorkArea.Height), new ScreenRect(d.OuterBounds.X, d.OuterBounds.Y, d.OuterBounds.Width, d.OuterBounds.Height), d.DisplayId.Value == primary));
            }
        }
        catch (Exception)
        {
        }
        list.Sort((a, b) => a.Primary != b.Primary ? (a.Primary ? -1 : 1) : a.Bounds.X != b.Bounds.X ? a.Bounds.X.CompareTo(b.Bounds.X) : a.Bounds.Y.CompareTo(b.Bounds.Y));
        return list;
    }

    public static string Describe(Info s, int n)
    {
        var where = s.Primary ? "principal" : s.Bounds.Y < 0 ? "arriba" : s.Bounds.X < 0 ? "izquierda" : s.Bounds.Y > 0 && s.Bounds.X == 0 ? "abajo" : "derecha";
        return $"Pantalla {n} · {where} · {s.Bounds.W}×{s.Bounds.H}";
    }
}
