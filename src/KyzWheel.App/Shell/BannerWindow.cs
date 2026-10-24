using KyzWheel.Platform.Native;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KyzWheel.App.Shell;

public sealed record Banner(string Title, string Body, string ColorHex, string Glyph, Action? Open, Func<Task<string?>>? Avatar = null);

public sealed class BannerWindow : GlassWindow
{
    private readonly Grid _row = new() { ColumnSpacing = 12, Padding = new Thickness(14, 10, 8, 10) };
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private Banner? _current;
    private int _stack;
    private bool _shown;

    public static (int X, int Y)? Anchor { get; set; }

    public BannerWindow() : base("kyzWheel · aviso", 400, 84, chrome: false)
    {
        _row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hit = new Border { Background = Ui.Brush(Colors.Transparent), Child = _row };
        hit.Tapped += (_, _) =>
        {
            var open = _current?.Open;
            Hide();
            open?.Invoke();
        };
        hit.PointerEntered += (_, _) => _timer?.Stop();
        hit.PointerExited += (_, _) => _timer?.Start();
        Surface.Children.Add(hit);
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(6);
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Hide();
        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    public void Show(Banner b)
    {
        _stack = _shown ? _stack + 1 : 0;
        _current = b;
        _row.Children.Clear();
        var av = Ui.Avatar(b.Title, b.ColorHex, 44);
        var badge = new Grid { Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -3, -3) };
        badge.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = Ui.Brush(Ui.Parse(b.ColorHex)), Stroke = Ui.Brush(ColorHelper.FromArgb(0xFF, 0x12, 0x12, 0x18)), StrokeThickness = 2 });
        var g = Ui.Icon(b.Glyph, 9, Colors.White);
        g.HorizontalAlignment = HorizontalAlignment.Center;
        badge.Children.Add(g);
        var avHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
        avHost.Children.Add(av);
        avHost.Children.Add(badge);
        _row.Children.Add(avHost);
        if (b.Avatar is not null) _ = LoadAvatarAsync(av, b.Avatar);
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        head.Children.Add(Ui.Label(b.Title, 13.5, Ui.Text, bold: true));
        if (_stack > 0) head.Children.Add(Ui.Label($"+{_stack}", 11, Ui.Parse(b.ColorHex), bold: true));
        t.Children.Add(head);
        t.Children.Add(Ui.Label(b.Body.ReplaceLineEndings(" "), 12.5, Ui.Secondary, maxLines: 2));
        Grid.SetColumn(t, 1);
        _row.Children.Add(t);
        var close = Ui.IconButton("\uE711", "Cerrar", Hide);
        close.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(close, 2);
        _row.Children.Add(close);
        Place();
        if (!_shown)
        {
            _shown = true;
            AppWindow.Show(false);
            KyzWheel.App.Wheel.WheelAnimations.RowEnter(Surface);
        }
        _timer.Stop();
        _timer.Start();
    }

    private void Place()
    {
        var (x, y) = Anchor ?? User32.CursorPosition();
        var wa = User32.WorkAreaAt(x, y);
        var size = AppWindow.Size;
        AppWindow.Move(new Windows.Graphics.PointInt32(wa.X + (wa.W - size.Width) / 2, wa.Y + 14));
    }

    private static async Task LoadAvatarAsync(Grid av, Func<Task<string?>> load)
    {
        try
        {
            if (await load() is { } p && File.Exists(p))
                av.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = new Microsoft.UI.Xaml.Media.ImageBrush { ImageSource = new BitmapImage(new Uri(p)) { DecodePixelWidth = 88 }, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill } });
        }
        catch (Exception)
        {
        }
    }

    public void Hide()
    {
        _timer.Stop();
        if (!_shown) return;
        _shown = false;
        _stack = 0;
        AppWindow.Hide();
    }
}
