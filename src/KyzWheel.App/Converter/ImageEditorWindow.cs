using ImageMagick;
using KyzWheel.Convert.Images;
using KyzWheel.App.Shell;
using KyzWheel.Core.Files;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;

namespace KyzWheel.App.Converter;

public sealed class ImageEditorWindow : GlassWindow
{
    public enum Tool { Crop, Arrow, Box, Pen, Text, Blur }

    private static readonly string[] Palette = ["#EF4444", "#FACC15", "#FFFFFF", "#111111"];
    private static readonly HashSet<string> Writable = ["png", "jpg", "jpeg", "jfif", "webp", "bmp", "gif", "tif", "tiff", "avif", "heic", "heif", "jxl"];

    private readonly string _source;
    private MagickImage _image;
    private readonly Stack<MagickImage> _history = new();
    private readonly List<Mark> _marks = [];
    private readonly Grid _stage = new();
    private readonly Image _view = new() { Stretch = Stretch.Fill };
    private readonly Canvas _overlay = new() { Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(1, 0, 0, 0)) };
    private readonly Viewbox _box = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
    private readonly ScrollViewer _scroll = new() { ZoomMode = ZoomMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinZoomFactor = 0.5f, MaxZoomFactor = 8f };
    private readonly Dictionary<Tool, Button> _toolButtons = [];
    private readonly List<Button> _colorButtons = [];
    private readonly TextBox _text = new() { PlaceholderText = "Texto a poner", Width = 160 };
    private readonly ComboBox _ratio = new() { ItemsSource = new[] { "Libre", "1:1", "16:9", "4:3" }, SelectedIndex = 0, Width = 92 };
    private readonly TextBlock _status = Ui.Label("", 12, Ui.Tertiary);
    private Tool _tool = Tool.Arrow;
    private string _color = Palette[0];
    private Point? _start;
    private List<Point>? _pen;
    private Shape? _ghost;

    public event Action<string>? Saved;

    public ImageEditorWindow(string path) : base("Editar · " + System.IO.Path.GetFileName(path), 980, 760)
    {
        _source = path;
        using var all = new MagickImageCollection(path);
        _image = (MagickImage)all[0].Clone();
        _image.AutoOrient();

        var root = new Grid { Padding = new Thickness(16, 40, 16, 16), RowSpacing = 10 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 4 };
        AddTool(bar, Tool.Arrow, "\uE72A", "Flecha");
        AddTool(bar, Tool.Box, "\uE739", "Rectángulo");
        AddTool(bar, Tool.Pen, "\uED63", "Lápiz");
        AddTool(bar, Tool.Text, "\uE8D2", "Texto (clic donde va)");
        AddTool(bar, Tool.Blur, "\uE7B3", "Censurar (pixelar una zona)");
        AddTool(bar, Tool.Crop, "\uE7A8", "Recortar");
        bar.Children.Add(_ratio);
        bar.Children.Add(_text);
        foreach (var c in Palette)
        {
            var b = new Button { Width = 26, Height = 26, Padding = new Thickness(0), CornerRadius = new CornerRadius(13), Background = Ui.Brush(Ui.Parse(c)), BorderThickness = new Thickness(2) };
            ToolTipService.SetToolTip(b, "Color");
            b.Click += (_, _) => { _color = c; Highlight(); };
            _colorButtons.Add(b);
            bar.Children.Add(b);
        }
        bar.Children.Add(Ui.IconButton("\uE7AD", "Girar 90°", () => Commit(i => i.Rotate(90))));
        bar.Children.Add(Ui.IconButton("\uE8AB", "Espejo", () => Commit(i => i.Flop())));
        bar.Children.Add(Ui.IconButton("\uE7A7", "Deshacer (Ctrl+Z)", Undo));
        root.Children.Add(new ScrollViewer { Content = bar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled });

        _stage.Children.Add(_view);
        _stage.Children.Add(_overlay);
        _box.Child = _stage;
        _scroll.Content = _box;
        _scroll.SizeChanged += (_, e) =>
        {
            _box.Width = Math.Max(1, e.NewSize.Width);
            _box.Height = Math.Max(1, e.NewSize.Height);
        };
        Grid.SetRow(_scroll, 1);
        root.Children.Add(_scroll);
        _overlay.PointerPressed += OnDown;
        _overlay.PointerMoved += OnMove;
        _overlay.PointerReleased += OnUp;

        var foot = new Grid { ColumnSpacing = 10 };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foot.Children.Add(_status);
        var save = Ui.Primary("Guardar copia", Save, "\uE74E");
        Grid.SetColumn(save, 1);
        foot.Children.Add(save);
        Grid.SetRow(foot, 2);
        root.Children.Add(foot);
        Surface.Children.Add(root);

        Surface.KeyDown += (_, e) =>
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (ctrl && e.Key == Windows.System.VirtualKey.Z) { Undo(); e.Handled = true; }
            if (ctrl && e.Key == Windows.System.VirtualKey.S) { Save(); e.Handled = true; }
            if (e.Key == Windows.System.VirtualKey.Escape && _start is not null) { CancelGhost(); e.Handled = true; }
        };
        Closed += (_, _) =>
        {
            _image.Dispose();
            foreach (var h in _history) h.Dispose();
        };
        Highlight();
        _ = RefreshAsync();
    }

    private void AddTool(StackPanel bar, Tool t, string glyph, string tip)
    {
        var b = Ui.IconButton(glyph, tip, () => { _tool = t; Highlight(); });
        _toolButtons[t] = b;
        bar.Children.Add(b);
    }

    private void Highlight()
    {
        foreach (var (t, b) in _toolButtons)
            b.Background = Ui.Brush(t == _tool ? Microsoft.UI.ColorHelper.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Microsoft.UI.Colors.Transparent);
        for (var i = 0; i < Palette.Length; i++)
            _colorButtons[i].BorderBrush = Ui.Brush(Palette[i] == _color ? Microsoft.UI.Colors.White : Microsoft.UI.ColorHelper.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        _ratio.Visibility = _tool == Tool.Crop ? Visibility.Visible : Visibility.Collapsed;
        _text.Visibility = _tool == Tool.Text ? Visibility.Visible : Visibility.Collapsed;
        _status.Text = _tool switch
        {
            Tool.Crop => "Arrastra para recortar · Ctrl+rueda para zoom",
            Tool.Blur => "Arrastra sobre lo que quieras tapar",
            Tool.Text => "Escribe el texto arriba y haz clic donde va",
            _ => "Arrastra sobre la imagen · Ctrl+Z deshace · Ctrl+rueda para zoom",
        };
    }

    private double Stroke => Annotations.StrokeFor(_image.Width, _image.Height);

    private async Task RefreshAsync()
    {
        var bytes = _image.ToByteArray(MagickFormat.Png);
        using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await ms.WriteAsync(bytes.AsBuffer());
        ms.Seek(0);
        var bmp = new BitmapImage();
        await bmp.SetSourceAsync(ms);
        _view.Source = bmp;
        _stage.Width = _image.Width;
        _stage.Height = _image.Height;
        _overlay.Width = _image.Width;
        _overlay.Height = _image.Height;
        Redraw();
    }

    private void Redraw(Mark? extra = null)
    {
        _overlay.Children.Clear();
        foreach (var m in _marks) foreach (var s in Shapes(m, Stroke)) _overlay.Children.Add(s);
        if (extra is not null) foreach (var s in Shapes(extra, Stroke)) _overlay.Children.Add(s);
        if (_ghost is not null) _overlay.Children.Add(_ghost);
    }

    private static PointD D(Point p) => new(p.X, p.Y);

    private static Point P(PointD p) => new(p.X, p.Y);

    private static IEnumerable<UIElement> Shapes(Mark m, double stroke)
    {
        var brush = Ui.Brush(Ui.Parse(m.Color));
        switch (m.Kind)
        {
            case MarkKind.Arrow when m.Points.Count == 2:
                foreach (var (a, b) in Annotations.ArrowSegments(m.Points[0], m.Points[1], stroke))
                    yield return new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = brush, StrokeThickness = stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                break;
            case MarkKind.Box when m.Points.Count == 2:
                var r = Annotations.Norm(m.Points[0], m.Points[1]);
                var rect = new Rectangle { Width = r.W, Height = r.H, Stroke = brush, StrokeThickness = stroke };
                Canvas.SetLeft(rect, r.X);
                Canvas.SetTop(rect, r.Y);
                yield return rect;
                break;
            case MarkKind.Pen:
                var pl = new Polyline { Stroke = brush, StrokeThickness = stroke, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                foreach (var p in m.Points) pl.Points.Add(P(p));
                yield return pl;
                break;
            case MarkKind.Text when m.Points.Count == 1:
                var tb = new TextBlock { Text = m.Text, Foreground = brush, FontSize = Annotations.TextSize(stroke), FontWeight = Microsoft.UI.Text.FontWeights.Bold, FontFamily = new FontFamily("Segoe UI") };
                Canvas.SetLeft(tb, m.Points[0].X);
                Canvas.SetTop(tb, m.Points[0].Y);
                yield return tb;
                break;
        }
    }

    private double Ratio => (_ratio.SelectedItem as string) switch { "1:1" => 1, "16:9" => 16.0 / 9, "4:3" => 4.0 / 3, _ => 0 };

    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, _image.Width), Math.Clamp(p.Y, 0, _image.Height));

    private Point End(Point a, Point b) => _tool == Tool.Crop ? Clamp(P(Annotations.Constrain(D(a), D(b), Ratio))) : b;

    private void OnDown(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(_overlay).Properties.IsLeftButtonPressed) return;
        var p = Clamp(e.GetCurrentPoint(_overlay).Position);
        if (_tool == Tool.Text)
        {
            if (string.IsNullOrWhiteSpace(_text.Text)) { _status.Text = "Primero escribe el texto arriba"; return; }
            _marks.Add(new Mark(MarkKind.Text, [D(p)], _color, _text.Text.Trim()));
            Redraw();
            return;
        }
        _start = p;
        _pen = _tool == Tool.Pen ? [p] : null;
        _overlay.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnMove(object sender, PointerRoutedEventArgs e)
    {
        if (_start is not { } a) return;
        var b = End(a, Clamp(e.GetCurrentPoint(_overlay).Position));
        switch (_tool)
        {
            case Tool.Pen:
                _pen!.Add(b);
                _ghost = null;
                Redraw(new Mark(MarkKind.Pen, _pen.Select(D).ToList(), _color));
                return;
            case Tool.Arrow:
                _ghost = null;
                Redraw(new Mark(MarkKind.Arrow, [D(a), D(b)], _color));
                return;
            case Tool.Box:
                _ghost = null;
                Redraw(new Mark(MarkKind.Box, [D(a), D(b)], _color));
                return;
        }
        var r = Annotations.Norm(D(a), D(b));
        var rect = new Rectangle
        {
            Width = r.W,
            Height = r.H,
            Stroke = Ui.Brush(Microsoft.UI.Colors.White),
            StrokeThickness = Math.Max(2, Stroke / 2),
            StrokeDashArray = [4, 3],
            Fill = Ui.Brush(Microsoft.UI.ColorHelper.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
        };
        Canvas.SetLeft(rect, r.X);
        Canvas.SetTop(rect, r.Y);
        _ghost = rect;
        Redraw();
    }

    private void OnUp(object sender, PointerRoutedEventArgs e)
    {
        if (_start is not { } a) return;
        _overlay.ReleasePointerCapture(e.Pointer);
        var b = End(a, Clamp(e.GetCurrentPoint(_overlay).Position));
        _start = null;
        _ghost = null;
        var r = Annotations.Norm(D(a), D(b));
        switch (_tool)
        {
            case Tool.Pen when _pen is { Count: > 1 }:
                _marks.Add(new Mark(MarkKind.Pen, _pen.Select(D).ToList(), _color));
                break;
            case Tool.Arrow when r.W + r.H > 6:
                _marks.Add(new Mark(MarkKind.Arrow, [D(a), D(b)], _color));
                break;
            case Tool.Box when r.W > 3 && r.H > 3:
                _marks.Add(new Mark(MarkKind.Box, [D(a), D(b)], _color));
                break;
            case Tool.Crop when r.W > 8 && r.H > 8:
                Commit(i => i.Crop(new MagickGeometry((int)r.X, (int)r.Y, (uint)r.W, (uint)r.H)));
                break;
            case Tool.Blur when r.W > 3 && r.H > 3:
                Commit(i => Annotations.Pixelate(i, r.X, r.Y, r.W, r.H));
                break;
        }
        _pen = null;
        Redraw();
    }

    private void CancelGhost()
    {
        _start = null;
        _pen = null;
        _ghost = null;
        Redraw();
    }

    private void Commit(Action<MagickImage> op)
    {
        _history.Push((MagickImage)_image.Clone());
        if (_marks.Count > 0)
        {
            Annotations.Burn(_image, _marks);
            _marks.Clear();
        }
        op(_image);
        _image.ResetPage();
        _ = RefreshAsync();
    }

    private void Undo()
    {
        if (_marks.Count > 0)
        {
            _marks.RemoveAt(_marks.Count - 1);
            Redraw();
            return;
        }
        if (_history.Count == 0) return;
        _image.Dispose();
        _image = _history.Pop();
        _ = RefreshAsync();
    }

    private void Save()
    {
        try
        {
            using var output = (MagickImage)_image.Clone();
            Annotations.Burn(output, _marks);
            var ext = KyzWheel.Convert.FileGuards.Ext(_source);
            var target = OutputNaming.NextFreePath(_source, Writable.Contains(ext) ? System.IO.Path.GetExtension(_source) : ".png");
            if (!Writable.Contains(ext)) output.Format = MagickFormat.Png;
            output.Quality = 92;
            output.Write(target);
            Saved?.Invoke(target);
            Close();
        }
        catch (Exception e)
        {
            Log.Error(e);
            _status.Text = "No se pudo guardar: " + e.Message;
        }
    }
}
