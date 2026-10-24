using KyzWheel.App.Mail;
using KyzWheel.App.Shell;
using KyzWheel.Core.Clipboard;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KyzWheel.App.Clips;

public sealed class ClipboardPanel : Grid
{
    private readonly ClipboardService _service;
    private readonly Action<ClipItem> _paste;
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private string _query = "";

    public ClipboardPanel(ClipboardService service, Action<ClipItem> paste)
    {
        _service = service;
        _paste = paste;
        RowSpacing = 10;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var search = new TextBox { PlaceholderText = "Buscar en lo copiado", CornerRadius = new CornerRadius(10) };
        search.TextChanged += (_, _) =>
        {
            _query = search.Text.Trim();
            Render();
        };
        Children.Add(search);
        var scroll = new ScrollViewer { Content = _rows, Margin = new Thickness(-8, 0, -8, 0), Padding = new Thickness(8, 0, 8, 0) };
        SetRow(scroll, 1);
        Children.Add(scroll);
        Render();
        service.History.Changed += OnChanged;
        Unloaded += (_, _) => service.History.Changed -= OnChanged;
        Loaded += (_, _) => search.Focus(FocusState.Programmatic);
    }

    private void OnChanged() => DispatcherQueue.TryEnqueue(Render);

    private void Render()
    {
        _rows.Children.Clear();
        var items = _service.History.Items.Where(i => _query.Length == 0 || (i.Text ?? i.Preview).Contains(_query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (items.Count == 0)
        {
            _rows.Children.Add(MailPanel.Empty("\uE77F", _query.Length > 0 ? "Sin resultados" : "Nada copiado aún", "Lo que copies (texto, imágenes o archivos) aparece aquí para pegarlo con un clic.", null, null));
            return;
        }
        var i = 0;
        foreach (var item in items)
        {
            var row = Row(item, i < 9 ? i + 1 : null);
            _rows.Children.Add(row);
            i++;
        }
    }

    private UIElement Row(ClipItem item, int? number)
    {
        var g = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 8, 6, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lead = new Grid { Width = 40, Height = 40 };
        lead.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Ui.Brush(ColorHelper.FromArgb(0x16, 0xFF, 0xFF, 0xFF)) });
        if (item.Kind == ClipKind.Image && item.ImagePath is { } img && File.Exists(img))
            lead.Children.Add(new Border { CornerRadius = new CornerRadius(10), Child = new Image { Source = new BitmapImage(new Uri(img)) { DecodePixelWidth = 80 }, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill } });
        else
        {
            var icon = Ui.Icon(item.Kind == ClipKind.Files ? "\uE8B7" : "\uE8C8", 15);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            lead.Children.Add(icon);
        }
        g.Children.Add(lead);
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        t.Children.Add(Ui.Label(item.Preview.ReplaceLineEndings(" "), 12.5, Ui.Text, maxLines: 2));
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (number is { } n) meta.Children.Add(Ui.Label(n.ToString(), 10.5, Theme.Argb(Theme.Current.Accent), bold: true));
        meta.Children.Add(Ui.Label(Ui.When(item.At), 11, Ui.Tertiary));
        if (item.Pinned) meta.Children.Add(Ui.Icon("\uE718", 10, Theme.Argb(Theme.Current.Accent)));
        t.Children.Add(meta);
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Opacity = 0.7 };
        actions.Children.Add(Ui.IconButton(item.Pinned ? "\uE77A" : "\uE718", item.Pinned ? "Desfijar" : "Fijar", () => _service.History.TogglePin(item.Id)));
        actions.Children.Add(Ui.IconButton("\uE74D", "Borrar", () => _service.History.Remove(item.Id)));
        Grid.SetColumn(actions, 2);
        g.Children.Add(actions);
        var surface = new Border { CornerRadius = new CornerRadius(10), Child = g, Background = Ui.Brush(Colors.Transparent) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(surface, "Pegar " + item.Preview);
        Ui.Pressable(surface, surface);
        surface.Tapped += (_, e) =>
        {
            if (e.OriginalSource is FrameworkElement fe && IsInside(fe, actions)) return;
            _paste(item);
        };
        return surface;
    }

    private static bool IsInside(FrameworkElement e, FrameworkElement container)
    {
        DependencyObject? d = e;
        while (d is not null)
        {
            if (d == container) return true;
            d = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d);
        }
        return false;
    }
}
