using KyzWheel.Core.Theming;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace KyzWheel.App.Shell;

public static class Theme
{
    public static ThemePalette Current { get; private set; } = Themes.Find(null);

    public static event Action? Changed;

    public static double Opacity { get; private set; } = 1;

    public static double FactorFor(int transparency) => Math.Clamp(1.7 - 1.55 * Math.Clamp(transparency, 0, 100) / 100.0, 0.15, 1.7);

    public static void SetTransparency(int transparency)
    {
        var f = FactorFor(transparency);
        if (Math.Abs(f - Opacity) < 0.001) return;
        Opacity = f;
        Changed?.Invoke();
    }

    public static void Apply(string? id)
    {
        var t = Themes.Find(id);
        if (t == Current && id is not null) return;
        Current = t;
        Ui.ApplyAccent(t.Accent);
        Changed?.Invoke();
    }

    public static Windows.UI.Color Argb(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 6) h = "FF" + h;
        var v = System.Convert.ToUInt32(h, 16);
        var a = (byte)(v >> 24);
        if (a < 0xFF) a = (byte)Math.Clamp(Math.Round(a * Opacity), 0, 254);
        return ColorHelper.FromArgb(a, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static LinearGradientBrush Vertical(string top, string bottom, double split = 1)
    {
        var b = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0.5, 0), EndPoint = new Windows.Foundation.Point(0.5, 1) };
        b.GradientStops.Add(new GradientStop { Color = Argb(top), Offset = 0 });
        b.GradientStops.Add(new GradientStop { Color = Argb(bottom), Offset = split });
        return b;
    }

    public static LinearGradientBrush Sheen()
    {
        var c = Argb(Current.Sheen);
        var b = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0.2, 0), EndPoint = new Windows.Foundation.Point(0.6, 0.55) };
        b.GradientStops.Add(new GradientStop { Color = c, Offset = 0 });
        b.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb((byte)(c.A / 3), c.R, c.G, c.B), Offset = 0.35 });
        b.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0, c.R, c.G, c.B), Offset = 1 });
        return b;
    }

    public static LinearGradientBrush Edge(string hex)
    {
        var c = Argb(hex);
        var b = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0.5, 0), EndPoint = new Windows.Foundation.Point(0.5, 1) };
        b.GradientStops.Add(new GradientStop { Color = c, Offset = 0 });
        b.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb((byte)(c.A / 6), c.R, c.G, c.B), Offset = 0.55 });
        b.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb((byte)(c.A / 3), c.R, c.G, c.B), Offset = 1 });
        return b;
    }
}
