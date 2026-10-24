using KyzWheel.App.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KyzWheel.App.Wheel;

public sealed class SidePanelWindow : GlassWindow
{
    private readonly StackPanel _host = new() { Spacing = 10, Padding = new Thickness(18) };
    private bool _shown;

    public SidePanelWindow() : base("kyzWheel", 340, 460, chrome: false)
    {
        Surface.Children.Add(_host);
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && _shown) HidePanel();
        };
        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            HidePanel();
        };
    }

    public void Show(string title, UIElement content, int wheelRight, int wheelTop)
    {
        _host.Children.Clear();
        _host.Children.Add(Heading(title, 17));
        _host.Children.Add(content);
        var wa = KyzWheel.Platform.Native.User32.WorkAreaAt(wheelRight, wheelTop);
        var size = AppWindow.Size;
        var x = wheelRight + 8;
        if (x + size.Width > wa.X + wa.W) x = wheelRight - size.Width * 2;
        var y = Math.Clamp(wheelTop, wa.Y, wa.Y + wa.H - size.Height);
        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        _shown = true;
        BringToFront();
        WheelAnimations.PanelIn(_host, _host.Children.Skip(1).ToList());
    }

    public static UIElement Message(string text) => Muted(text);

    public void HidePanel()
    {
        _shown = false;
        AppWindow.Hide();
    }
}
