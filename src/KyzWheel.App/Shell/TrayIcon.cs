using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KyzWheel.App.Shell;

public sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly MenuFlyoutItem _pause;

    public TrayIcon(Action openWheel, Action openConverter, Action openSettings, Action togglePause, Action exit)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Item("Abrir rueda", "\uE7C8", openWheel));
        menu.Items.Add(Item("Convertidor", "\uE8AB", openConverter));
        menu.Items.Add(Item("Ajustes", "\uE713", openSettings));
        menu.Items.Add(new MenuFlyoutSeparator());
        _pause = Item("Pausar 1 h", "\uE769", togglePause);
        menu.Items.Add(_pause);
        menu.Items.Add(Item("Salir", "\uE7E8", exit));
        _icon = new TaskbarIcon
        {
            ToolTipText = "kyzWheel",
            IconSource = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "kyzWheel.ico"))),
            ContextFlyout = menu,
            ContextMenuMode = ContextMenuMode.SecondWindow,
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(openWheel),
        };
        _icon.ForceCreate(false);
    }

    public void SetPaused(bool paused, string? until)
    {
        _pause.Text = paused ? "Reanudar" : "Pausar 1 h";
        _icon.ToolTipText = paused ? $"kyzWheel · en pausa hasta {until}" : "kyzWheel";
    }

    public void SetTooltip(string text) => _icon.ToolTipText = text;

    private static MenuFlyoutItem Item(string text, string glyph, Action a)
    {
        var i = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        i.Click += (_, _) => a();
        return i;
    }

    public void Dispose() => _icon.Dispose();
}
