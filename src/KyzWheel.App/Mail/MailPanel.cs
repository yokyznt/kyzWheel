using KyzWheel.App.Shell;
using KyzWheel.Mail;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KyzWheel.App.Mail;

public sealed class MailPanel : Grid
{
    private readonly MailService _service;
    private readonly Action<MailSummary> _open;
    private readonly Action _addAccount;
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly StackPanel _status = new() { Spacing = 6 };
    private string? _lastTop;

    public MailPanel(MailService service, Action<MailSummary> open, Action addAccount)
    {
        _service = service;
        _open = open;
        _addAccount = addAccount;
        RowSpacing = 8;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Children.Add(_status);
        var scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(-8, 0, -8, 0), Padding = new Thickness(8, 0, 8, 0) };
        SetRow(scroll, 1);
        Children.Add(scroll);
        Render();
        service.Changed += Render;
        Unloaded += (_, _) => service.Changed -= Render;
    }

    private void Render()
    {
        _status.Children.Clear();
        foreach (var s in _service.Inbox.Statuses.Where(s => s.State is AccountState.AuthFailed or AccountState.Offline))
            _status.Children.Add(StatusRow(s));
        _rows.Children.Clear();
        if (_service.Accounts.Count == 0)
        {
            _rows.Children.Add(Empty("\uE715", "Vincula tu primera cuenta", "Gmail, Outlook, Yahoo, iCloud o cualquier correo con IMAP. Usa una contraseña de aplicación.", "Agregar cuenta", _addAccount));
            return;
        }
        var top = _service.Inbox.Top();
        if (top.Count == 0)
        {
            var loading = _service.Inbox.Statuses.Any(s => s.State == AccountState.Connecting) || _service.Inbox.Statuses.Count < _service.Accounts.Count;
            _rows.Children.Add(Empty(loading ? "\uE895" : "\uE73E", loading ? "Conectando…" : "Todo al día", loading ? "Trayendo tus correos recientes" : "No hay correos recientes", null, null));
            return;
        }
        var newTop = _lastTop is not null && top[0].MessageId != _lastTop;
        _lastTop = top[0].MessageId;
        foreach (var m in top) _rows.Children.Add(Row(m));
        if (newTop) KyzWheel.App.Wheel.WheelAnimations.RowEnter(_rows.Children[0]);
    }

    private UIElement Row(MailSummary m)
    {
        var color = _service.AccountOf(m.Account)?.ColorHex ?? "#FF8A3D";
        var g = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 9, 10, 9) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var avatar = Ui.Avatar(m.FromName, color, 38);
        avatar.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(avatar);
        var text = new StackPanel { Spacing = 1 };
        var top = new Grid { ColumnSpacing = 8 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Ui.Label(m.FromName, 13.5, bold: m.Unread, color: m.Unread ? Ui.Text : Ui.Secondary));
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (m.HasAttachments) right.Children.Add(Ui.Icon("\uE723", 11, Ui.Tertiary));
        right.Children.Add(Ui.Label(Ui.When(m.Date), 11.5, m.Unread ? Ui.Parse(color) : Ui.Tertiary));
        if (m.Unread) right.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 7, Height = 7, Fill = Ui.Brush(Ui.Parse(color)), VerticalAlignment = VerticalAlignment.Center });
        SetColumn(right, 1);
        top.Children.Add(right);
        text.Children.Add(top);
        text.Children.Add(Ui.Label(m.Subject, 12.5, m.Unread ? Ui.Text : Ui.Secondary, bold: false));
        text.Children.Add(Ui.Label(m.Preview, 12, Ui.Tertiary));
        SetColumn(text, 1);
        g.Children.Add(text);
        var surface = new Border { CornerRadius = new CornerRadius(10), Child = g, Background = Ui.Brush(Colors.Transparent) };
        ToolTipService.SetToolTip(surface, m.Account);
        Ui.Pressable(surface, surface);
        surface.Tapped += (_, _) => _open(m);
        return surface;
    }

    private UIElement StatusRow(AccountStatus s)
    {
        var warn = s.State == AccountState.AuthFailed ? Ui.Parse("#F87171") : Ui.Parse("#FACC15");
        var g = new Grid { ColumnSpacing = 10, Padding = new Thickness(10, 8, 10, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(Ui.Icon(s.State == AccountState.AuthFailed ? "\uE72E" : "\uEB55", 14, warn));
        var t = new StackPanel();
        t.Children.Add(Ui.Label(s.Account, 12, Ui.Text, bold: true));
        t.Children.Add(Ui.Label(s.Error ?? "", 11.5, Ui.Secondary));
        SetColumn(t, 1);
        g.Children.Add(t);
        var retry = Ui.IconButton("\uE72C", "Reintentar", () => _service.Restart(s.Account));
        SetColumn(retry, 2);
        g.Children.Add(retry);
        return new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = Ui.Brush(ColorHelper.FromArgb(0x18, warn.R, warn.G, warn.B)),
            BorderBrush = Ui.Brush(ColorHelper.FromArgb(0x30, warn.R, warn.G, warn.B)),
            BorderThickness = new Thickness(1),
            Child = g,
        };
    }

    public static UIElement Empty(string glyph, string title, string body, string? action, Action? click)
    {
        var s = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12, 56, 12, 0), MaxWidth = 280 };
        var chip = new Grid { Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Center };
        chip.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = Ui.Brush(ColorHelper.FromArgb(0x14, 0xFF, 0xFF, 0xFF)) });
        var icon = Ui.Icon(glyph, 22, Ui.Secondary);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        chip.Children.Add(icon);
        s.Children.Add(chip);
        var t = Ui.Label(title, 15, bold: true);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        s.Children.Add(t);
        var b = Ui.Label(body, 12.5, Ui.Secondary, maxLines: 3);
        b.TextAlignment = TextAlignment.Center;
        b.HorizontalAlignment = HorizontalAlignment.Center;
        s.Children.Add(b);
        if (action is not null && click is not null)
        {
            var btn = Ui.Primary(action, click, "\uE710");
            btn.HorizontalAlignment = HorizontalAlignment.Center;
            btn.Margin = new Thickness(0, 8, 0, 0);
            s.Children.Add(btn);
        }
        return s;
    }
}
