using KyzWheel.App.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KyzWheel.App.Wheel;

public sealed class SidePanelWindow : GlassWindow
{
    private readonly Grid _host = new() { Padding = new Thickness(16, 14, 16, 16), RowSpacing = 10 };
    private readonly Grid _header = new() { ColumnSpacing = 10 };
    private readonly ContentControl _body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private bool _shown;
    private long _shownAt;

    public event Action? PanelHidden;

    public bool IsOpen => _shown;

    public SidePanelWindow() : base("kyzWheel", 380, 540, chrome: false)
    {
        _host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_actions, 2);
        _host.Children.Add(_header);
        Grid.SetRow(_body, 1);
        _host.Children.Add(_body);
        Surface.Children.Add(_host);
        Surface.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape) HidePanel();
        };
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && _shown && Environment.TickCount64 - _shownAt > 400 && !Pinned) HidePanel();
            if (e.WindowActivationState == WindowActivationState.Deactivated && _shown) LostFocus?.Invoke();
        };
        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            HidePanel();
        };
    }

    public bool Pinned { get; set; }

    public event Action? LostFocus;

    public void Show(string glyph, string colorHex, string title, string? subtitle, UIElement content, IReadOnlyList<Button> actions, int anchorX, int anchorY, int dock = 0)
    {
        _header.Children.Clear();
        var chip = new Grid { Width = 34, Height = 34 };
        var c = Ui.Parse(colorHex);
        chip.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = Ui.Brush(Microsoft.UI.ColorHelper.FromArgb(0x26, c.R, c.G, c.B)), Stroke = Ui.Brush(Microsoft.UI.ColorHelper.FromArgb(0x48, c.R, c.G, c.B)), StrokeThickness = 1 });
        chip.Children.Add(Ui.Icon(glyph, 16, c));
        chip.Children[1].SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        _header.Children.Add(chip);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Ui.Label(title, 16, bold: true));
        if (!string.IsNullOrEmpty(subtitle)) titles.Children.Add(Ui.Label(subtitle, 11.5, Ui.Tertiary));
        Grid.SetColumn(titles, 1);
        _header.Children.Add(titles);
        _actions.Children.Clear();
        foreach (var a in actions) _actions.Children.Add(a);
        _header.Children.Add(_actions);
        _body.Content = content;
        var wa = KyzWheel.Platform.Native.User32.WorkAreaAt(anchorX, anchorY);
        var size = AppWindow.Size;
        var x = Math.Clamp(anchorX - size.Width / 2, wa.X + 8, wa.X + wa.W - size.Width - 8);
        if (dock > 0)
        {
            var right = anchorX + dock + 14;
            var left = anchorX - dock - 14 - size.Width;
            x = right + size.Width <= wa.X + wa.W - 8 ? right : Math.Max(wa.X + 8, left);
        }
        var y = Math.Clamp(anchorY - size.Height / 2, wa.Y + 8, wa.Y + wa.H - size.Height - 8);
        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        _shown = true;
        _shownAt = Environment.TickCount64;
        BringToFront();
        WheelAnimations.PanelIn(_host, content is Panel p ? p.Children.ToList() : []);
    }

    public void Replace(UIElement content) => _body.Content = content;

    public void HidePanel()
    {
        if (!_shown) return;
        _shown = false;
        AppWindow.Hide();
        PanelHidden?.Invoke();
    }
}
