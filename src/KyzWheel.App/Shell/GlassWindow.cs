using KyzWheel.Platform.Native;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinUIEx;

namespace KyzWheel.App.Shell;

public class GlassWindow : Window
{
    protected Grid Surface { get; }

    public GlassWindow(string title, int width, int height, bool chrome = true)
    {
        Title = title;
        SystemBackdrop = new DesktopAcrylicBackdrop();
        Surface = new Grid { RequestedTheme = ElementTheme.Dark, BorderThickness = new Thickness(1) };
        Paint();
        Theme.Changed += Paint;
        Closed += (_, _) => Theme.Changed -= Paint;
        Content = Surface;
        if (chrome)
        {
            ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
        else
        {
            var p = (OverlappedPresenter)AppWindow.Presenter;
            p.SetBorderAndTitleBar(true, false);
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsAlwaysOnTop = true;
            AppWindow.IsShownInSwitchers = false;
        }
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "kyzWheel.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        var (x, y) = User32.CursorPosition();
        var s = Shcore.ScaleAt(x, y);
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * s), (int)(height * s)));
    }

    private void Paint()
    {
        var t = Theme.Current;
        var tint = Theme.Argb(t.PanelTint);
        var bg = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0.4, 1) };
        bg.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb((byte)Math.Max(0, tint.A - 0x18), tint.R, tint.G, tint.B), Offset = 0 });
        bg.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb((byte)Math.Min(255, tint.A + 0x30), tint.R, tint.G, tint.B), Offset = 1 });
        Surface.Background = bg;
        Surface.BorderBrush = Theme.Edge(t.PanelEdge);
    }

    public void CenterOnCursorMonitor()
    {
        var (x, y) = User32.CursorPosition();
        var wa = User32.WorkAreaAt(x, y);
        var size = AppWindow.Size;
        AppWindow.Move(new Windows.Graphics.PointInt32(wa.X + (wa.W - size.Width) / 2, wa.Y + (wa.H - size.Height) / 2));
    }

    public void BringToFront()
    {
        AppWindow.Show(true);
        Activate();
        Foreground.Force(this.GetWindowHandle());
    }

    protected static TextBlock Heading(string text, double size = 20) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
    };

    protected static TextBlock Muted(string text) => new()
    {
        Text = text,
        FontSize = 12.5,
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(ColorHelper.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
    };

    protected static Border Card(UIElement child) => new()
    {
        Background = new SolidColorBrush(ColorHelper.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
        BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(16),
        Child = child,
    };
}
