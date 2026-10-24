using KyzWheel.App.Shell;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KyzWheel.App.Wheel;

public sealed class ItemListPanel : Grid
{
    public ItemListPanel(IReadOnlyList<WheelItem> items, Action<WheelItem> pick)
    {
        RowSpacing = 10;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var rows = new StackPanel { Spacing = 2 };
        var search = new TextBox { PlaceholderText = "Buscar", CornerRadius = new CornerRadius(10) };
        void Render()
        {
            rows.Children.Clear();
            var q = search.Text.Trim();
            foreach (var it in items.Where(i => q.Length == 0 || i.Label.Contains(q, StringComparison.OrdinalIgnoreCase)))
                rows.Children.Add(Row(it, pick));
        }
        search.TextChanged += (_, _) => Render();
        Children.Add(search);
        var scroll = new ScrollViewer { Content = rows, Margin = new Thickness(-8, 0, -8, 0), Padding = new Thickness(8, 0, 8, 0) };
        SetRow(scroll, 1);
        Children.Add(scroll);
        Render();
        Loaded += (_, _) => search.Focus(FocusState.Programmatic);
    }

    private static UIElement Row(WheelItem it, Action<WheelItem> pick)
    {
        var color = Theme.Argb(it.ColorHex);
        var g = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 8, 10, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chip = new Grid { Width = 36, Height = 36 };
        chip.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = Ui.Brush(ColorHelper.FromArgb(0x24, color.R, color.G, color.B)) });
        var icon = Ui.Icon(it.Glyph, 15, color);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        chip.Children.Add(icon);
        g.Children.Add(chip);
        var label = Ui.Label(it.Label, 13, Ui.Text);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 1);
        g.Children.Add(label);
        if (it.Badge > 0)
        {
            var b = Ui.Label(it.Badge > 99 ? "99+" : it.Badge.ToString(), 10.5, Colors.Black, bold: true);
            var badge = new Border { CornerRadius = new CornerRadius(9), MinWidth = 18, Padding = new Thickness(5, 1, 5, 1), Background = Ui.Brush(color), Child = b, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(badge, 2);
            g.Children.Add(badge);
        }
        var surface = new Border { CornerRadius = new CornerRadius(10), Child = g, Background = Ui.Brush(Colors.Transparent) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(surface, it.Label);
        Ui.Pressable(surface, surface);
        surface.Tapped += (_, _) => pick(it);
        return surface;
    }
}
