using KyzWheel.Core.Notifications;
using KyzWheel.Platform.Native;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KyzWheel.App.Shell;

public sealed record Banner(string Title, string Body, string ColorHex, string Glyph, Action? Open, Func<Task<string?>>? Avatar = null, Func<string, Task>? Reply = null);

public sealed class BannerWindow : GlassWindow
{
    private const int W = 460;
    private const int H = 112;
    private const int HReply = 168;
    private readonly Grid _row = new() { ColumnSpacing = 14, Padding = new Thickness(16, 14, 10, 14) };
    private readonly StackPanel _root = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private Banner? _current;
    private int _stack;
    private bool _shown;
    private bool _replying;

    public static (int X, int Y)? Anchor { get; set; }
    public static string Monitor { get; set; } = "cursor";
    public static string Position { get; set; } = "top-center";

    public BannerWindow() : base("kyzWheel · aviso", W, H, chrome: false)
    {
        _row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hit = new Border { Background = Ui.Brush(Colors.Transparent), Child = _row };
        hit.Tapped += (_, _) =>
        {
            if (_replying) return;
            var open = _current?.Open;
            Hide();
            open?.Invoke();
        };
        _root.Children.Add(hit);
        _root.PointerEntered += (_, _) => _timer?.Stop();
        _root.PointerExited += (_, _) => { if (!_replying) _timer?.Start(); };
        Surface.Children.Add(_root);
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(7);
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
        if (_replying && _shown) return;
        _stack = _shown ? _stack + 1 : 0;
        _current = b;
        _row.Children.Clear();
        while (_root.Children.Count > 1) _root.Children.RemoveAt(1);
        var av = Ui.Avatar(b.Title, b.ColorHex, 52);
        var badge = new Grid { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -3, -3) };
        badge.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = Ui.Brush(Ui.Parse(b.ColorHex)), Stroke = Ui.Brush(ColorHelper.FromArgb(0xFF, 0x12, 0x12, 0x18)), StrokeThickness = 2 });
        var g = Ui.Icon(b.Glyph, 10, Colors.White);
        g.HorizontalAlignment = HorizontalAlignment.Center;
        badge.Children.Add(g);
        var avHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
        avHost.Children.Add(av);
        avHost.Children.Add(badge);
        _row.Children.Add(avHost);
        if (b.Avatar is not null) _ = LoadAvatarAsync(av, b.Avatar);
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 3 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(new TextBlock { Text = b.Title, FontFamily = Ui.Font, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = Ui.Brush(Colors.White), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300 });
        if (_stack > 0) head.Children.Add(Ui.Label($"+{_stack}", 12, Ui.Parse(b.ColorHex), bold: true));
        t.Children.Add(head);
        t.Children.Add(new TextBlock { Text = b.Body.ReplaceLineEndings(" "), FontFamily = Ui.Font, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = Ui.Brush(ColorHelper.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(t, 1);
        _row.Children.Add(t);
        var side = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Top };
        side.Children.Add(Ui.IconButton("\uE711", "Cerrar", Hide));
        if (b.Reply is not null) side.Children.Add(Ui.IconButton("\uE97A", "Responder", StartReply));
        Grid.SetColumn(side, 2);
        _row.Children.Add(side);
        Size(H);
        if (!_shown)
        {
            _shown = true;
            AppWindow.Show(false);
            KyzWheel.App.Wheel.WheelAnimations.RowEnter(Surface);
        }
        _timer.Stop();
        _timer.Start();
    }

    private void StartReply()
    {
        if (_current?.Reply is not { } reply || _replying) return;
        _replying = true;
        _timer.Stop();
        var box = new TextBox { PlaceholderText = "Responder…", FontSize = 14, CornerRadius = new CornerRadius(10), Margin = new Thickness(16, 0, 8, 0) };
        var bar = new Grid { ColumnSpacing = 6, Padding = new Thickness(0, 0, 10, 14) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(box);
        async Task Send()
        {
            var text = box.Text.Trim();
            if (text.Length == 0) return;
            box.IsEnabled = false;
            try
            {
                await reply(text);
                _replying = false;
                Hide();
            }
            catch (Exception e)
            {
                box.IsEnabled = true;
                box.Header = "No se envió: " + e.Message;
            }
        }
        var send = Ui.Primary("Enviar", () => _ = Send(), "\uE724");
        Grid.SetColumn(send, 1);
        bar.Children.Add(send);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; _ = Send(); }
            else if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; _replying = false; Hide(); }
        };
        _root.Children.Add(bar);
        Size(HReply);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Foreground.Force(hwnd);
        Activate();
        box.Focus(FocusState.Programmatic);
    }

    private void Size(int h)
    {
        var (cx, cy) = Anchor ?? User32.CursorPosition();
        var screens = Screens.All();
        var primary = Math.Max(0, screens.ToList().FindIndex(s => s.Primary));
        var work = NotifyPlacement.Screen(screens.Select(s => s.Work).ToList(), primary, (cx, cy), Anchor is null ? Monitor : "cursor");
        var scale = Shcore.ScaleAt(work.X + work.W / 2, work.Y + work.H / 2);
        var w = (int)(W * scale);
        var hh = (int)(h * scale);
        var (x, y) = NotifyPlacement.Place([work], 0, (work.X, work.Y), "1", Position, w, hh);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, w, hh));
    }

    private static async Task LoadAvatarAsync(Grid av, Func<Task<string?>> load)
    {
        try
        {
            if (await load() is { } p && File.Exists(p))
                av.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = new Microsoft.UI.Xaml.Media.ImageBrush { ImageSource = new BitmapImage(new Uri(p)) { DecodePixelWidth = 104 }, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill } });
        }
        catch (Exception)
        {
        }
    }

    public void Hide()
    {
        _timer.Stop();
        _replying = false;
        if (!_shown) return;
        _shown = false;
        _stack = 0;
        AppWindow.Hide();
    }
}
