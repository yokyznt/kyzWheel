using System.Diagnostics;
using KyzWheel.Core.Wheel;
using KyzWheel.Platform.Native;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using WinUIEx;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace KyzWheel.App.Wheel;

public sealed partial class WheelWindow : Window
{
    private const double Size = 440;
    private const double C = Size / 2;
    private const double Ro = 196;
    private const double Ri = 76;
    private const double GapDeg = 0.9;

    private sealed record SliceVisual(Canvas Group, Path Body, Path Halo, Border Badge, WheelItem Item);

    private readonly List<SliceVisual> _slices = [];
    private IReadOnlyList<WheelPage> _pages = [];
    private int _page;
    private int _hover = -1;
    private WheelMode _mode;
    private bool _wantTools;
    private bool _shown;
    private bool _dropped;
    private bool _activeSinceShow;
    private long _shownAt;
    private IReadOnlyList<string> _dragFiles = [];
    private string? _hubDefault;
    private bool _locked;
    private (int L, int T, int R, int B) _lockRect;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _lockTimer;
    private Stopwatch? _perf;

    public event Action<WheelItem>? ItemInvoked;
    public event Action<WheelItem, IReadOnlyList<string>>? FilesDropped;
    public event Action<double>? FirstFrame;
    public event Action? Hidden;

    public Func<IReadOnlyList<string>, IReadOnlyList<WheelPage>>? PagesForFiles { get; set; }

    public bool IsShown => _shown;
    public WheelMode Mode => _mode;

    public WheelWindow()
    {
        InitializeComponent();
        ApplyTheme();
        KyzWheel.App.Shell.Theme.Changed += ApplyTheme;
        SystemBackdrop = new KyzWheel.App.Shell.GlassBackdrop(1f);
        var p = (OverlappedPresenter)AppWindow.Presenter;
        p.SetBorderAndTitleBar(false, false);
        p.IsResizable = false;
        p.IsMaximizable = false;
        p.IsMinimizable = false;
        p.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;
        Dwm.MakePopup(this.GetWindowHandle());
        Title = "kyzWheel";
        WheelAnimations.SetCenter(Stage, (float)C, (float)C);
        WheelAnimations.SetCenter(Layer, (float)C, (float)C);
        Root.PointerMoved += OnPointerMoved;
        Root.PointerReleased += OnPointerReleased;
        Root.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(Root).Properties.IsRightButtonPressed)
            {
                e.Handled = true;
                HideAnimated();
            }
        };
        _lockTimer = DispatcherQueue.CreateTimer();
        _lockTimer.Interval = TimeSpan.FromMilliseconds(200);
        _lockTimer.Tick += (_, _) => { if (_locked && _shown) ApplyLock(); };
        Root.PointerExited += (_, _) => SetHover(-1);
        Root.KeyDown += OnKeyDown;
        Root.DragEnter += OnDragEnter;
        Root.DragOver += OnDragOver;
        Root.DragLeave += (_, _) => SetHover(-1);
        Root.Drop += OnDrop;
        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                _activeSinceShow = true;
                return;
            }
            if (_mode == WheelMode.Root && _shown && _activeSinceShow && Environment.TickCount64 - _shownAt > 400) HideAnimated();
        };
        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            HideAnimated();
        };
    }

    public void Prime(WheelPage page)
    {
        _pages = [page];
        _page = 0;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(-32000, -32000, 440, 440));
        Dwm.ClipCircle(this.GetWindowHandle(), 440, Ro + 1);
        BuildPage(0);
        AppWindow.Show(false);
        WheelAnimations.Open(Stage, _slices.Select(s => (UIElement)s.Group).ToList());
        var t = DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(400);
        t.IsRepeating = false;
        t.Tick += (_, _) =>
        {
            if (!_shown) AppWindow.Hide();
        };
        t.Start();
    }

    public void ShowRoot(int x, int y, WheelPage page, Stopwatch? perf = null)
    {
        ClearHubFile();
        _dragFiles = [];
        _mode = WheelMode.Root;
        _pages = [page];
        _page = 0;
        Present(x, y, true, perf);
    }

    public void ShowFiles(int x, int y, IReadOnlyList<string> files, IReadOnlyList<WheelPage> pages, Stopwatch? perf = null)
    {
        _mode = WheelMode.Files;
        _dragFiles = files;
        _pages = pages.Count > 0 ? pages : [new WheelPage("Sin acciones", [])];
        _page = 0;
        _hubDefault = files.Count == 1 ? System.IO.Path.GetFileName(files[0]) : $"{files.Count} archivos";
        SetHubFile(files);
        Present(x, y, true, perf);
    }

    private async void SetHubFile(IReadOnlyList<string> files)
    {
        HubThumbHost.Visibility = Visibility.Visible;
        HubTitle.Margin = new Thickness(0, 0, 0, 0);
        HubCount.Visibility = files.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        HubCountText.Text = files.Count > 99 ? "99+" : files.Count.ToString();
        HubThumb.Source = null;
        try
        {
            var path = files[0];
            Windows.Storage.FileProperties.StorageItemThumbnail thumb = Directory.Exists(path)
                ? await (await StorageFolder.GetFolderFromPathAsync(path)).GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 96)
                : await (await StorageFile.GetFileFromPathAsync(path)).GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 96);
            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            await bmp.SetSourceAsync(thumb);
            if (_mode == WheelMode.Files) HubThumb.Source = bmp;
        }
        catch (Exception)
        {
        }
    }

    private void ClearHubFile()
    {
        HubThumbHost.Visibility = Visibility.Collapsed;
        HubTitle.Margin = new Thickness(0, 50, 0, 0);
        _hubDefault = null;
    }

    public void ShowDrag(int x, int y, bool tools, Stopwatch? perf = null)
    {
        ClearHubFile();
        _mode = WheelMode.Drag;
        _wantTools = tools;
        _dropped = false;
        _dragFiles = [];
        _pages = [new WheelPage("Suelta aquí", [])];
        _page = 0;
        Present(x, y, false, perf);
    }

    private void Present(int x, int y, bool activate, Stopwatch? perf)
    {
        _perf = perf;
        var scale = Shcore.ScaleAt(x, y);
        var px = (int)Math.Round(Size * scale);
        var wa = User32.WorkAreaAt(x, y);
        var (cx, cy) = WheelGeometry.ClampToWorkArea(x, y, px / 2.0, new KyzWheel.Core.Wheel.Rect(wa.X, wa.Y, wa.W, wa.H));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32((int)cx - px / 2, (int)cy - px / 2, px, px));
        Dwm.ClipCircle(this.GetWindowHandle(), px, (Ro + 1) * scale);
        _locked = _mode == WheelMode.Files;
        if (_locked)
        {
            _lockRect = WheelGeometry.LockRect((int)cx, (int)cy, (Ro - 10) * scale);
            CursorLock.MoveTo((int)cx, (int)cy);
            ApplyLock();
            _lockTimer.Start();
        }
        BuildPage(0);
        _shown = true;
        _activeSinceShow = false;
        _shownAt = Environment.TickCount64;
        if (_perf is not null) CompositionTarget.Rendering += OnFirstRender;
        AppWindow.Show(activate);
        if (activate)
        {
            Activate();
            Foreground.Force(this.GetWindowHandle());
            Root.Focus(FocusState.Programmatic);
        }
        WheelAnimations.Open(Stage, _slices.Select(s => (UIElement)s.Group).ToList());
    }

    private void OnFirstRender(object? sender, object e)
    {
        CompositionTarget.Rendering -= OnFirstRender;
        if (_perf is null) return;
        _perf.Stop();
        FirstFrame?.Invoke(_perf.Elapsed.TotalMilliseconds);
        _perf = null;
    }

    private void ApplyLock() => CursorLock.Lock(_lockRect.L, _lockRect.T, _lockRect.R, _lockRect.B);

    private void Unlock()
    {
        if (!_locked) return;
        _locked = false;
        _lockTimer.Stop();
        CursorLock.Release();
    }

    public void HideAnimated()
    {
        Unlock();
        if (!_shown) return;
        _shown = false;
        SetHover(-1);
        WheelAnimations.Close(Stage, () =>
        {
            if (_shown) return;
            AppWindow.Hide();
            Hidden?.Invoke();
        });
    }

    public void EndDrag()
    {
        if (_mode != WheelMode.Drag) return;
        var t = DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(350);
        t.IsRepeating = false;
        t.Tick += (_, _) =>
        {
            if (!_dropped) HideAnimated();
        };
        t.Start();
    }

    public void NextPage() => SwitchPage(_page + 1, 1);
    public void PrevPage() => SwitchPage(_page - 1, -1);

    public void GoToPage(Func<WheelPage, bool> match)
    {
        for (var i = 0; i < _pages.Count; i++)
            if (match(_pages[i]))
            {
                SwitchPage(i, i >= _page ? 1 : -1);
                return;
            }
    }

    public void SetBadge(string itemId, int count)
    {
        foreach (var s in _slices.Where(s => s.Item.Id == itemId))
        {
            var old = s.Badge.Visibility == Visibility.Visible ? (s.Badge.Child as TextBlock)?.Text : null;
            ApplyBadge(s.Badge, count);
            if (count > 0 && old != BadgeText(count)) WheelAnimations.BadgePop(s.Badge);
        }
    }

    public void PulseSuccess(string itemId)
    {
        foreach (var s in _slices.Where(s => s.Item.Id == itemId)) WheelAnimations.Success(s.Group);
    }

    private void SwitchPage(int index, int dir)
    {
        if (_pages.Count <= 1) return;
        _page = ((index % _pages.Count) + _pages.Count) % _pages.Count;
        BuildPage(_page);
        WheelAnimations.PageSwitch(Layer, dir);
    }

    private void BuildPage(int index)
    {
        Layer.Children.Clear();
        _slices.Clear();
        _hover = -1;
        var page = _pages[index];
        var n = page.Items.Count;
        for (var i = 0; i < n; i++)
        {
            var item = page.Items[i];
            var color = ParseColor(item.ColorHex);
            var geometry = SliceGeometry(i, n);
            var group = new Canvas { Width = Size, Height = Size };
            var t = KyzWheel.App.Shell.Theme.Current;
            var body = new Path { Data = geometry, Fill = KyzWheel.App.Shell.Theme.Vertical(t.SliceTop, t.SliceBottom), Stroke = KyzWheel.App.Shell.Theme.Edge(t.SliceStroke), StrokeThickness = 1 };
            var sheen = new Path { Data = SliceGeometry(i, n), Fill = KyzWheel.App.Shell.Theme.Sheen(), IsHitTestVisible = false };
            var halo = new Path { Data = SliceGeometry(i, n), Fill = Tint(color, 0x2E, 0x0A), Stroke = new SolidColorBrush(ColorHelper.FromArgb(0x99, color.R, color.G, color.B)), StrokeThickness = 1.2, Opacity = 0, IsHitTestVisible = false };
            group.Children.Add(body);
            group.Children.Add(sheen);
            group.Children.Add(halo);
            var mid = n == 1 ? 180 : WheelGeometry.SliceArc(i, n).startDeg + 180.0 / n;
            var (mx, my) = Polar(mid, (Ro + Ri) / 2);
            var content = new StackPanel { Width = 104, Spacing = 7, IsHitTestVisible = false };
            var chip = new Grid { Width = 42, Height = 42, HorizontalAlignment = HorizontalAlignment.Center };
            chip.Children.Add(new Ellipse { Fill = new SolidColorBrush(ColorHelper.FromArgb(0x24, color.R, color.G, color.B)), Stroke = new SolidColorBrush(ColorHelper.FromArgb(0x40, color.R, color.G, color.B)), StrokeThickness = 1 });
            chip.Children.Add(new FontIcon { Glyph = item.Glyph, FontSize = 19, Foreground = new SolidColorBrush(color), FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets") });
            content.Children.Add(chip);
            content.Children.Add(new TextBlock { Text = item.Label, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = 10, Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 100 });
            Canvas.SetLeft(content, mx - 52);
            Canvas.SetTop(content, my - 34);
            group.Children.Add(content);
            var badge = new Border { MinWidth = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(color), Padding = new Thickness(5, 0, 5, 0), IsHitTestVisible = false, Child = new TextBlock { FontSize = 10.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.Black), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            Canvas.SetLeft(badge, mx + 8);
            Canvas.SetTop(badge, my - 40);
            ApplyBadge(badge, item.Badge);
            WheelAnimations.SetCenter(badge, 9, 9);
            group.Children.Add(badge);
            WheelAnimations.SetCenter(group, (float)C, (float)C);
            Layer.Children.Add(group);
            _slices.Add(new SliceVisual(group, body, halo, badge, item));
        }
        HubTitle.Text = _hubDefault ?? page.Title;
        HubSub.Text = _mode switch
        {
            WheelMode.Files => (_pages.Count > 1 ? page.Title + " · TAB" : page.Title).ToUpperInvariant(),
            WheelMode.Drag => n == 0 ? "" : _pages.Count > 1 ? "SHIFT · TAB CAMBIA" : "SUELTA EN UNA ACCIÓN",
            _ => "ESC PARA CERRAR",
        };
        Dots.Children.Clear();
        if (_pages.Count > 1)
            for (var i = 0; i < _pages.Count; i++)
                Dots.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = new SolidColorBrush(i == index ? Colors.White : ColorHelper.FromArgb(0x55, 0xFF, 0xFF, 0xFF)) });
    }

    private void ApplyTheme()
    {
        var t = KyzWheel.App.Shell.Theme.Current;
        Hub.Fill = KyzWheel.App.Shell.Theme.Vertical(t.HubTop, t.HubBottom);
        Hub.Stroke = KyzWheel.App.Shell.Theme.Edge(t.Rim);
        Rim.Stroke = KyzWheel.App.Shell.Theme.Edge(t.Rim);
        HubSheen.Fill = KyzWheel.App.Shell.Theme.Sheen();
        var g = KyzWheel.App.Shell.Theme.Argb(t.Glow);
        var glow = new RadialGradientBrush();
        glow.GradientStops.Add(new GradientStop { Color = g, Offset = 0.84 });
        glow.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb((byte)(g.A / 3), g.R, g.G, g.B), Offset = 0.92 });
        glow.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0, g.R, g.G, g.B), Offset = 1 });
        Glow.Fill = glow;
        if (_pages.Count > 0) BuildPage(_page);
    }

    private static Brush SliceFill()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        b.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0xFF, 0x1B, 0x1B, 0x22), Offset = 0 });
        b.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0xFF, 0x0D, 0x0D, 0x12), Offset = 1 });
        return b;
    }

    private static Brush Tint(Windows.UI.Color c, byte top, byte bottom)
    {
        var b = new RadialGradientBrush { Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5, GradientOrigin = new Point(0.5, 0.5) };
        b.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(bottom, c.R, c.G, c.B), Offset = 0.35 });
        b.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(top, c.R, c.G, c.B), Offset = 1 });
        return b;
    }

    private static string BadgeText(int count) => count > 99 ? "99+" : count.ToString();

    private static void ApplyBadge(Border b, int count)
    {
        b.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (b.Child is TextBlock t) t.Text = BadgeText(count);
    }

    private static (double x, double y) Polar(double deg, double r)
    {
        var a = deg * Math.PI / 180;
        return (C + r * Math.Sin(a), C - r * Math.Cos(a));
    }

    private static Geometry SliceGeometry(int i, int n)
    {
        if (n == 1)
        {
            var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
            g.Children.Add(new EllipseGeometry { Center = new Point(C, C), RadiusX = Ro, RadiusY = Ro });
            g.Children.Add(new EllipseGeometry { Center = new Point(C, C), RadiusX = Ri, RadiusY = Ri });
            return g;
        }
        var (start, sweep) = WheelGeometry.SliceArc(i, n);
        var s = start + GapDeg / 2;
        var e = start + sweep - GapDeg / 2;
        var large = e - s > 180;
        var (ox1, oy1) = Polar(s, Ro);
        var (ox2, oy2) = Polar(e, Ro);
        var (ix2, iy2) = Polar(e, Ri);
        var (ix1, iy1) = Polar(s, Ri);
        var fig = new PathFigure { StartPoint = new Point(ox1, oy1), IsClosed = true };
        fig.Segments.Add(new ArcSegment { Point = new Point(ox2, oy2), Size = new Size(Ro, Ro), SweepDirection = SweepDirection.Clockwise, IsLargeArc = large });
        fig.Segments.Add(new LineSegment { Point = new Point(ix2, iy2) });
        fig.Segments.Add(new ArcSegment { Point = new Point(ix1, iy1), Size = new Size(Ri, Ri), SweepDirection = SweepDirection.Counterclockwise, IsLargeArc = large });
        var pg = new PathGeometry();
        pg.Figures.Add(fig);
        return pg;
    }

    private static Windows.UI.Color ParseColor(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 6) h = "FF" + h;
        var v = System.Convert.ToUInt32(h, 16);
        return ColorHelper.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    private int HitTest(Point p) => _locked
        ? (_slices.Count == 0 ? -1 : WheelGeometry.SliceAtAngle(p.X - C, p.Y - C, _slices.Count, Ri * 0.55))
        : HitTestRing(p);

    private int HitTestRing(Point p) =>
        _slices.Count == 0 ? -1 : WheelGeometry.SliceAt(p.X - C, p.Y - C, _slices.Count, Ri, Ro + 6);

    private void SetHover(int index)
    {
        if (index == _hover) return;
        if (_hover >= 0 && _hover < _slices.Count) WheelAnimations.Hover(_slices[_hover].Group, _slices[_hover].Halo, false);
        _hover = index;
        if (_hover >= 0)
        {
            WheelAnimations.Hover(_slices[_hover].Group, _slices[_hover].Halo, true);
            HubTitle.Text = _slices[_hover].Item.Label;
        }
        else if (_pages.Count > 0)
        {
            HubTitle.Text = _hubDefault ?? _pages[_page].Title;
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e) => SetHover(HitTest(e.GetCurrentPoint(Stage).Position));

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(Stage).Position;
        var idx = HitTest(p);
        if (idx >= 0)
        {
            var item = _slices[idx].Item;
            WheelAnimations.Success(_slices[idx].Group);
            if (_mode == WheelMode.Files && _dragFiles.Count > 0)
            {
                FilesDropped?.Invoke(item, _dragFiles);
                var t = DispatcherQueue.CreateTimer();
                t.Interval = TimeSpan.FromMilliseconds(200);
                t.IsRepeating = false;
                t.Tick += (_, _) => HideAnimated();
                t.Start();
            }
            else ItemInvoked?.Invoke(item);
            return;
        }

    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Escape:
                HideAnimated();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Tab:
            case Windows.System.VirtualKey.Right:
                NextPage();
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Left:
                PrevPage();
                e.Handled = true;
                break;
        }
    }

    private async void OnDragEnter(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        if (_mode != WheelMode.Drag || _dragFiles.Count > 0 || PagesForFiles is null) return;
        var def = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            _dragFiles = items.OfType<StorageFile>().Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            var pages = PagesForFiles(_dragFiles);
            if (pages.Count == 0) pages = [new WheelPage("Sin acciones", [])];
            _pages = pages;
            var start = 0;
            if (_wantTools)
                for (var i = 0; i < pages.Count; i++)
                    if (pages[i].Tag is KyzWheel.Convert.ConverterPage.Tools) start = i;
            _page = start;
            BuildPage(start);
            WheelAnimations.PageSwitch(Layer, 1);
        }
        finally
        {
            def.Complete();
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var idx = HitTest(e.GetPosition(Stage));
        SetHover(idx);
        e.AcceptedOperation = idx >= 0 ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.DragUIOverride.IsGlyphVisible = idx >= 0;
        e.DragUIOverride.IsCaptionVisible = idx >= 0;
        if (idx >= 0) e.DragUIOverride.Caption = _slices[idx].Item.Label;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var idx = HitTest(e.GetPosition(Stage));
        if (idx < 0) return;
        _dropped = true;
        var item = _slices[idx].Item;
        var files = _dragFiles;
        if (files.Count == 0 && e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var def = e.GetDeferral();
            files = (await e.DataView.GetStorageItemsAsync()).OfType<StorageFile>().Select(f => f.Path).ToList();
            def.Complete();
        }
        WheelAnimations.Success(_slices[idx].Group);
        FilesDropped?.Invoke(item, files);
        var t = DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(240);
        t.IsRepeating = false;
        t.Tick += (_, _) => HideAnimated();
        t.Start();
    }
}
