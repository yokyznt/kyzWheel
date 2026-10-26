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
    private const double Size = 360;
    private const double C = Size / 2;
    private const double DiscR = 178;
    private const double Ro = 166;
    private const double Ri = 78;
    private static double GapPx => KyzWheel.App.Shell.Theme.Current.Gap;
    private static double CornerOut => KyzWheel.App.Shell.Theme.Current.CornerOut;
    private static double CornerIn => KyzWheel.App.Shell.Theme.Current.CornerIn;
    private const double GrooveR = 70;
    private const double StickR = 60;
    private const double StickDead = 14;

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
    private (int X, int Y) _lockCenter;
    private (int X, int Y) _restore;
    private double _scale = 1;
    private double _sx;
    private double _sy;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _lockTimer;
    private Stopwatch? _perf;

    public event Action<WheelItem>? ItemInvoked;
    public event Action<IReadOnlyList<WheelItem>, object?>? Reordered;
    public event Action<WheelItem, IReadOnlyList<string>>? FilesDropped;
    public event Action<double>? FirstFrame;
    public event Action? Hidden;

    public Func<IReadOnlyList<string>, IReadOnlyList<WheelPage>>? PagesForFiles { get; set; }
    public Func<WheelItem, WheelMode, WheelPage?>? Expand { get; set; }
    public event Action<WheelItem?, IReadOnlyList<WheelItem>>? Overflow;
    public IReadOnlyList<string> Files => _dragFiles;
    private readonly Stack<(IReadOnlyList<WheelPage> Pages, int Page, WheelItem? Parent, IReadOnlyList<string> Files)> _stack = new();
    private WheelItem? _parent;
    public int Depth => _stack.Count;

    public bool IsShown => _shown;

    public nint GetWindowHandleSafe() => this.GetWindowHandle();
    public WheelMode Mode => _mode;
    private bool _docked;

    public bool Docked
    {
        get => _docked;
        set
        {
            if (_docked == value) return;
            _docked = value;
            if (value) SetHover(-1);
            Stage.Opacity = value ? 0.4 : 1;
        }
    }

    public event Action? DockedClick;

    public bool Steps { get; set; }

    private long _lastStep;
    public event Action? LostFocus;
    public int RadiusPx => (int)Math.Round(DiscR * (_scale <= 0 ? 1 : _scale));
    public void ReleaseCursor() => Unlock();
    public static bool Passive { get; } = Environment.GetCommandLineArgs().Contains("--passive");

    public WheelWindow()
    {
        InitializeComponent();
        ApplyTheme();
        KyzWheel.App.Shell.Theme.Changed += ApplyTheme;
        SystemBackdrop = new KyzWheel.App.Shell.GlassBackdrop(0.5f);
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
        WheelAnimations.SetCenter(Pointer, (float)C, (float)C);
        Root.PointerMoved += OnPointerMoved;
        Root.PointerReleased += OnPointerReleased;
        Root.PointerPressed += (_, e) =>
        {
            var props = e.GetCurrentPoint(Root).Properties;
            if (Docked)
            {
                e.Handled = true;
                DockedClick?.Invoke();
                return;
            }
            if (props.IsMiddleButtonPressed)
            {
                e.Handled = true;
                ToggleEdit();
                return;
            }
            if (props.IsRightButtonPressed)
            {
                e.Handled = true;
                if (_editing)
                {
                    ToggleEdit();
                    return;
                }
                if (!Back()) HideAnimated();
            }
        };
        _lockTimer = DispatcherQueue.CreateTimer();
        _lockTimer.Interval = TimeSpan.FromMilliseconds(200);
        _lockTimer.Tick += (_, _) => { if (_locked && _shown) ApplyLock(); };
        Root.PointerExited += (_, _) => { if (!_locked) SetHover(-1); };
        Root.PointerWheelChanged += (_, e) =>
        {
            if (_editing || Docked || _slices.Count == 0) return;
            e.Handled = true;
            var d = e.GetCurrentPoint(Root).Properties.MouseWheelDelta;
            if (d == 0) return;
            var dir = d < 0 ? 1 : -1;
            SetHover(_hover < 0 ? 0 : ((_hover + dir) % _slices.Count + _slices.Count) % _slices.Count);
            _lastStep = Environment.TickCount64;
        };
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
            if (e.WindowActivationState == WindowActivationState.Deactivated && Docked && _shown) LostFocus?.Invoke();
            if (!_locked && !Docked && _mode == WheelMode.Root && _shown && _activeSinceShow && Environment.TickCount64 - _shownAt > 400) HideAnimated();
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
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(-32000, -32000, (int)Size, (int)Size));
        Dwm.ClipCircle(this.GetWindowHandle(), (int)Size, DiscR + 1);
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
        _editing = false;
        _editPick = -1;
        HintTopIcon.Glyph = "\uE70F";
        HubTitle.FontFamily = KyzWheel.App.Shell.Ui.Font;
        ClearHubFile();
        _dragFiles = [];
        _mode = WheelMode.Root;
        ResetStack();
        _pages = [WheelFit.Fit(page, "#FFFFFF")];
        _page = 0;
        Present(x, y, true, perf);
    }

    public void ShowFiles(int x, int y, IReadOnlyList<string> files, IReadOnlyList<WheelPage> pages, Stopwatch? perf = null)
    {
        _mode = WheelMode.Files;
        _dragFiles = files;
        ResetStack();
        _pages = pages.Count > 0 ? [WheelFit.Fit(WheelFit.Group(pages, "Acciones"), "#FFFFFF")] : [new WheelPage("Sin acciones", [])];
        _page = 0;
        _hubDefault = files.Count == 1 ? System.IO.Path.GetFileName(files[0]) : $"{files.Count} archivos";
        SetHubFile(files);
        Present(x, y, true, perf);
    }

    private async void SetHubFile(IReadOnlyList<string> files)
    {
        var token = ++_previewToken;
        HubLabels.Visibility = Visibility.Collapsed;
        HubCount.Visibility = files.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        HubCountText.Text = files.Count > 99 ? "99+" : files.Count.ToString();
        HubPreview.Visibility = Visibility.Collapsed;
        HubPreviewBrush.ImageSource = null;
        HubThumb.Source = null;
        HubThumb.Visibility = Visibility.Visible;
        try
        {
            var path = files[0];
            if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && await PdfPreview(path) is { } pdf)
            {
                if (token != _previewToken || _dragFiles.Count == 0) return;
                HubPreviewBrush.AlignmentY = AlignmentY.Top;
                HubPreviewBrush.ImageSource = pdf;
                HubPreview.Visibility = Visibility.Visible;
                HubThumb.Visibility = Visibility.Collapsed;
                return;
            }
            Windows.Storage.FileProperties.StorageItemThumbnail thumb = Directory.Exists(path)
                ? await (await StorageFolder.GetFolderFromPathAsync(path)).GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 320, Windows.Storage.FileProperties.ThumbnailOptions.ResizeThumbnail)
                : await (await StorageFile.GetFileFromPathAsync(path)).GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 320, Windows.Storage.FileProperties.ThumbnailOptions.ResizeThumbnail);
            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            await bmp.SetSourceAsync(thumb);
            if (token != _previewToken || _dragFiles.Count == 0) return;
            if (thumb.Type == Windows.Storage.FileProperties.ThumbnailType.Image)
            {
                HubPreviewBrush.AlignmentY = AlignmentY.Center;
                HubPreviewBrush.ImageSource = bmp;
                HubPreview.Visibility = Visibility.Visible;
                HubThumb.Visibility = Visibility.Collapsed;
            }
            else HubThumb.Source = bmp;
        }
        catch (Exception)
        {
        }
    }

    private int _previewToken;

    private static async Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> PdfPreview(string path)
    {
        try
        {
            var doc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
            if (doc.PageCount == 0) return null;
            using var page = doc.GetPage(0);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = 320, BackgroundColor = Colors.White });
            stream.Seek(0);
            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            await bmp.SetSourceAsync(stream);
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ClearHubFile()
    {
        _previewToken++;
        HubLabels.Visibility = Visibility.Visible;
        HubThumb.Visibility = Visibility.Collapsed;
        HubThumb.Source = null;
        HubPreview.Visibility = Visibility.Collapsed;
        HubPreviewBrush.ImageSource = null;
        HubCount.Visibility = Visibility.Collapsed;
        _hubDefault = null;
    }

    public void ShowDrag(int x, int y, bool tools, Stopwatch? perf = null)
    {
        ClearHubFile();
        _mode = WheelMode.Drag;
        _wantTools = tools;
        _dropped = false;
        _dragFiles = [];
        ResetStack();
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
        Dwm.ClipCircle(this.GetWindowHandle(), px, (DiscR + 1) * scale);
        _locked = _mode != WheelMode.Drag && !Passive;
        _scale = scale;
        _sx = 0;
        _sy = 0;
        if (_locked)
        {
            if (!CursorLock.Active) _restore = User32.CursorPosition();
            _lockCenter = ((int)cx, (int)cy);
            _lockRect = WheelGeometry.LockRect((int)cx, (int)cy, Ri * scale);
            CursorLock.MoveTo((int)cx, (int)cy);
            ApplyLock();
            CursorLock.Hide();
            _lockTimer.Start();
        }
        BuildPage(0);
        _shown = true;
        _activeSinceShow = false;
        _shownAt = Environment.TickCount64;
        if (_perf is not null) CompositionTarget.Rendering += OnFirstRender;
        AppWindow.Show(activate);
        if (activate && !Passive)
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
        CursorLock.Show();
        CursorLock.MoveTo(_restore.X, _restore.Y);
    }

    public void HideAnimated()
    {
        Docked = false;
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

    private void ResetStack()
    {
        _stack.Clear();
        _parent = null;
    }

    public void Push(WheelPage page, WheelItem parent)
    {
        _stack.Push((_pages, _page, _parent, _dragFiles));
        _parent = parent;
        _pages = [WheelFit.Fit(page, parent.ColorHex)];
        _page = 0;
        _sx = 0;
        _sy = 0;
        SetHover(-1);
        BuildPage(0);
        WheelAnimations.Drill(Layer, true);
        WheelAnimations.Drill(HubTitle, true);
    }

    public void AttachFiles(IReadOnlyList<string> files)
    {
        _dragFiles = files;
        _hubDefault = files.Count == 1 ? System.IO.Path.GetFileName(files[0]) : $"{files.Count} archivos";
        SetHubFile(files);
    }

    private bool _editing;
    private int _editPick = -1;

    public bool Editing => _editing;

    private void ToggleEdit()
    {
        if (!_editing && (_stack.Count > 1 || (_stack.Count == 1 && _parent?.Tag is not KyzWheel.Core.Config.Slot) || _mode != WheelMode.Root || _pages.Count != 1 || _slices.Count < 2 || _slices.Any(x => x.Item.Tag is MoreItems))) return;
        _editing = !_editing;
        _editPick = -1;
        HintTopIcon.Glyph = _editing ? "\uE73E" : "\uE70F";
        BuildPage(_page);
        if (_editing)
        {
            HubTitle.Text = "\uE8CB";
            HubTitle.FontFamily = KyzWheel.App.Shell.Ui.Icons;
            HubSub.Text = "";
            foreach (var s in _slices) WheelAnimations.Wiggle(s.Group);
        }
        else HubTitle.FontFamily = KyzWheel.App.Shell.Ui.Font;
    }

    private void EditPick(int idx)
    {
        if (idx < 0 || idx >= _slices.Count) return;
        if (_editPick < 0)
        {
            _editPick = idx;
            WheelAnimations.Hover(_slices[idx].Group, _slices[idx].Halo, true, MidDeg(idx));
            return;
        }
        var a = _editPick;
        _editPick = -1;
        if (a == idx) return;
        var items = _pages[_page].Items.ToList();
        (items[a], items[idx]) = (items[idx], items[a]);
        _pages = [_pages[_page] with { Items = items }];
        _page = 0;
        BuildPage(0);
        HubTitle.Text = "\uE8CB";
        HubTitle.FontFamily = KyzWheel.App.Shell.Ui.Icons;
        HubSub.Text = "";
        WheelAnimations.Success(_slices[idx].Group);
        Reordered?.Invoke(items, _stack.Count == 0 ? null : _parent?.Tag);
    }

    public bool Back()
    {
        if (_stack.Count == 0) return false;
        var (pages, page, parent, files) = _stack.Pop();
        if (files.Count == 0 && _dragFiles.Count > 0 && _mode == WheelMode.Root)
        {
            _dragFiles = [];
            ClearHubFile();
        }
        _pages = pages;
        _page = page;
        _parent = parent;
        _sx = 0;
        _sy = 0;
        SetHover(-1);
        BuildPage(_page);
        WheelAnimations.Drill(Layer, false);
        WheelAnimations.Drill(HubTitle, false);
        return true;
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
            var body = new Path { Data = geometry, Fill = Glassy(t.SliceTop, t.SliceBottom), Stroke = KyzWheel.App.Shell.Theme.Edge(t.SliceStroke), StrokeThickness = 1 };
            var sheen = new Path { Data = SliceGeometry(i, n), Fill = KyzWheel.App.Shell.Theme.Sheen(), IsHitTestVisible = false };
            var halo = new Path { Data = SliceGeometry(i, n), Fill = Vivid(color), Stroke = new SolidColorBrush(ColorHelper.FromArgb(0x80, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1, IsHitTestVisible = false };
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(halo).Opacity = 0;
            group.Children.Add(body);
            group.Children.Add(sheen);
            group.Children.Add(halo);
            var mid = n == 1 ? 180 : WheelGeometry.SliceArc(i, n).startDeg + 180.0 / n;
            var (mx, my) = Polar(mid, (Ro + Ri) / 2);
            var content = new StackPanel { Width = 124, Spacing = 6, IsHitTestVisible = false };
            content.Children.Add(new FontIcon { Glyph = item.Glyph, FontSize = 20, Foreground = new SolidColorBrush(Colors.White), FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"), HorizontalAlignment = HorizontalAlignment.Center });
            content.Children.Add(new TextBlock { Text = t.Upper ? item.Label.ToUpperInvariant() : item.Label, FontFamily = KyzWheel.App.Shell.Ui.FontFor(t.Font), FontSize = (n > 6 ? 10.5 : 11.5) + (t.Upper ? 0 : 1), FontWeight = Microsoft.UI.Text.FontWeights.Bold, CharacterSpacing = t.Spacing, Foreground = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = n > 6 ? 92 : 120 });
            Canvas.SetLeft(content, mx - 62);
            Canvas.SetTop(content, my - 24);
            group.Children.Add(content);
            var badge = new Border { MinWidth = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(color), Padding = new Thickness(5, 0, 5, 0), IsHitTestVisible = false, Child = new TextBlock { FontFamily = KyzWheel.App.Shell.Ui.Font, FontSize = 10.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.Black), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            Canvas.SetLeft(badge, mx + 8);
            Canvas.SetTop(badge, my - 30);
            ApplyBadge(badge, item.Badge);
            WheelAnimations.SetCenter(badge, 9, 9);
            group.Children.Add(badge);
            WheelAnimations.SetCenter(group, (float)C, (float)C);
            Layer.Children.Add(group);
            _slices.Add(new SliceVisual(group, body, halo, badge, item));
        }
        PointerArc.Data = PointerGeometry(n);
        HubTitle.Text = _hubDefault ?? page.Title;
        HubSub.Text = _mode switch
        {
            WheelMode.Drag => n == 0 ? "" : _pages.Count > 1 ? "SHIFT · TAB CAMBIA" : "SUELTA EN UNA ACCIÓN",
            _ when _stack.Count > 0 => (_mode == WheelMode.Files ? page.Title + " · " : "") + "CLIC DER. ATRÁS",
            WheelMode.Files => "ELIGE · CLIC DER. SALE",
            _ => "CLIC DER. CIERRA",
        };
        HubSub.Text = HubSub.Text.ToUpperInvariant();
        if (_mode == WheelMode.Root && _stack.Count == 0 && _dragFiles.Count == 0) HubLabels.Visibility = Visibility.Collapsed;
        else if (HubThumb.Visibility != Visibility.Visible) HubLabels.Visibility = Visibility.Visible;
        Dots.Children.Clear();
        if (_pages.Count > 1)
            for (var i = 0; i < _pages.Count; i++)
                Dots.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = new SolidColorBrush(i == index ? Colors.White : ColorHelper.FromArgb(0x55, 0xFF, 0xFF, 0xFF)) });
    }

    private void ApplyTheme()
    {
        if (_pages.Count > 0 && IsShown) BuildPage(_page);
        var t = KyzWheel.App.Shell.Theme.Current;
        Hub.Stroke = KyzWheel.App.Shell.Theme.Edge(t.Rim);
        Rim.Stroke = KyzWheel.App.Shell.Theme.Edge(t.Rim);
        HubSheen.Fill = KyzWheel.App.Shell.Theme.Sheen();
        var top = KyzWheel.App.Shell.Theme.Argb(t.HubTop);
        var bottom = KyzWheel.App.Shell.Theme.Argb(t.HubBottom);
        var disc = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        disc.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0x38, top.R, top.G, top.B), Offset = 0 });
        disc.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0x58, bottom.R, bottom.G, bottom.B), Offset = 1 });
        var hub = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        hub.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0x70, top.R, top.G, top.B), Offset = 0 });
        hub.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0x90, bottom.R, bottom.G, bottom.B), Offset = 1 });
        Hub.Fill = hub;
        Disc.Fill = disc;
        var g = KyzWheel.App.Shell.Theme.Argb(t.Glow);
        var glow = new RadialGradientBrush();
        glow.GradientStops.Add(new GradientStop { Color = g, Offset = 0.84 });
        glow.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb((byte)(g.A / 3), g.R, g.G, g.B), Offset = 0.92 });
        glow.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0, g.R, g.G, g.B), Offset = 1 });
        Glow.Fill = glow;
        var accent = KyzWheel.App.Shell.Theme.Argb(t.Accent);
        PointerArc.Stroke = new SolidColorBrush(accent);
        PointerDot.Fill = new SolidColorBrush(accent);
        if (_shown) WheelAnimations.Crossfade(Stage);
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

    private static Brush Glassy(string topHex, string bottomHex)
    {
        var a = KyzWheel.App.Shell.Theme.Argb(topHex);
        var z = KyzWheel.App.Shell.Theme.Argb(bottomHex);
        var b = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        b.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb((byte)Math.Min((int)a.A, 0x5C), a.R, a.G, a.B), Offset = 0 });
        b.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb((byte)Math.Min((int)z.A, 0x70), z.R, z.G, z.B), Offset = 1 });
        return b;
    }

    private static Brush Vivid(Windows.UI.Color c)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        b.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0xF2, (byte)Math.Min(255, c.R + 30), (byte)Math.Min(255, c.G + 30), (byte)Math.Min(255, c.B + 30)), Offset = 0 });
        b.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0xF2, (byte)(c.R * 0.72), (byte)(c.G * 0.72), (byte)(c.B * 0.72)), Offset = 1 });
        return b;
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
        var gapOut = GapPx / 2 / Ro * 180 / Math.PI;
        var gapIn = GapPx / 2 / Ri * 180 / Math.PI;
        var so = start + gapOut;
        var eo = start + sweep - gapOut;
        var si = start + gapIn;
        var ei = start + sweep - gapIn;
        var dOut = CornerOut / Ro * 180 / Math.PI;
        var dIn = CornerIn / Ri * 180 / Math.PI;
        Point P(double deg, double r)
        {
            var (x, y) = Polar(deg, r);
            return new Point(x, y);
        }
        double Lerp(double a, double b, double t) => a + (b - a) * t;
        double EdgeAt(double sIn, double sOut, double r) => Lerp(sIn, sOut, (r - Ri) / (Ro - Ri));
        var rOutEdge = Ro - CornerOut;
        var rInEdge = Ri + CornerIn;
        var fig = new PathFigure { StartPoint = P(EdgeAt(si, so, rInEdge), rInEdge), IsClosed = true };
        fig.Segments.Add(new LineSegment { Point = P(EdgeAt(si, so, rOutEdge), rOutEdge) });
        fig.Segments.Add(new QuadraticBezierSegment { Point1 = P(so, Ro), Point2 = P(so + dOut, Ro) });
        fig.Segments.Add(new ArcSegment { Point = P(eo - dOut, Ro), Size = new Size(Ro, Ro), SweepDirection = SweepDirection.Clockwise, IsLargeArc = eo - so - 2 * dOut > 180 });
        fig.Segments.Add(new QuadraticBezierSegment { Point1 = P(eo, Ro), Point2 = P(EdgeAt(ei, eo, rOutEdge), rOutEdge) });
        fig.Segments.Add(new LineSegment { Point = P(EdgeAt(ei, eo, rInEdge), rInEdge) });
        fig.Segments.Add(new QuadraticBezierSegment { Point1 = P(ei, Ri), Point2 = P(ei - dIn, Ri) });
        fig.Segments.Add(new ArcSegment { Point = P(si + dIn, Ri), Size = new Size(Ri, Ri), SweepDirection = SweepDirection.Counterclockwise, IsLargeArc = ei - si - 2 * dIn > 180 });
        fig.Segments.Add(new QuadraticBezierSegment { Point1 = P(si, Ri), Point2 = P(EdgeAt(si, so, rInEdge), rInEdge) });
        var pg = new PathGeometry();
        pg.Figures.Add(fig);
        return pg;
    }

    private static Geometry PointerGeometry(int n)
    {
        if (n <= 1) return new EllipseGeometry { Center = new Point(C, C), RadiusX = GrooveR, RadiusY = GrooveR };
        var half = 180.0 / n - (GapPx / 2 + 1.5) / GrooveR * 180 / Math.PI;
        var (x0, y0) = Polar(-half, GrooveR);
        var (x1, y1) = Polar(half, GrooveR);
        var fig = new PathFigure { StartPoint = new Point(x0, y0), IsClosed = false, IsFilled = false };
        fig.Segments.Add(new ArcSegment { Point = new Point(x1, y1), Size = new Size(GrooveR, GrooveR), SweepDirection = SweepDirection.Clockwise, IsLargeArc = half > 90 });
        var g = new PathGeometry();
        g.Figures.Add(fig);
        return g;
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
        if (_hover >= 0 && _hover < _slices.Count) WheelAnimations.Hover(_slices[_hover].Group, _slices[_hover].Halo, false, MidDeg(_hover));
        _hover = index;
        WheelAnimations.PointTo(Pointer, _hover >= 0 ? MidDeg(_hover) : 0, _hover >= 0);
        if (_hover >= 0)
        {
            WheelAnimations.Hover(_slices[_hover].Group, _slices[_hover].Halo, true, MidDeg(_hover));
            HubTitle.Text = _slices[_hover].Item.Label;
        }
        else if (_pages.Count > 0)
        {
            HubTitle.Text = _hubDefault ?? _pages[_page].Title;
        }
    }

    private double MidDeg(int i) => _slices.Count <= 1 ? 180 : WheelGeometry.SliceArc(i, _slices.Count).startDeg + 180.0 / _slices.Count;

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (Docked) return;
        if (!_locked)
        {
            var t = HitTest(e.GetCurrentPoint(Stage).Position);
            if (t < 0 && Steps && _hover >= 0) return;
            StepTo(t);
            return;
        }
        var (x, y) = User32.CursorPosition();
        var dx = (x - _lockCenter.X) / _scale;
        var dy = (y - _lockCenter.Y) / _scale;
        if (dx == 0 && dy == 0) return;
        CursorLock.MoveTo(_lockCenter.X, _lockCenter.Y);
        _sx += dx;
        _sy += dy;
        var m = Math.Sqrt(_sx * _sx + _sy * _sy);
        if (m > StickR)
        {
            _sx *= StickR / m;
            _sy *= StickR / m;
            m = StickR;
        }
        if (m >= StickDead && _slices.Count > 0) StepTo(WheelGeometry.SliceAtAngle(_sx, _sy, _slices.Count, 0));
    }

    private void StepTo(int target)
    {
        if (!Steps || target < 0 || _hover < 0 || target == _hover || _slices.Count < 3)
        {
            SetHover(target);
            return;
        }
        var now = Environment.TickCount64;
        if (now - _lastStep < 110) return;
        _lastStep = now;
        var n = _slices.Count;
        var fwd = ((target - _hover) % n + n) % n;
        SetHover(((_hover + (fwd <= n / 2 ? 1 : -1)) % n + n) % n);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (Docked) return;
        var kind = e.GetCurrentPoint(Stage).Properties.PointerUpdateKind;
        if (kind is Microsoft.UI.Input.PointerUpdateKind.RightButtonReleased or Microsoft.UI.Input.PointerUpdateKind.MiddleButtonReleased) return;
        var idx = _locked ? _hover : HitTest(e.GetCurrentPoint(Stage).Position);
        if (_editing)
        {
            EditPick(idx);
            return;
        }
        Choose(idx);
    }

    private void Choose(int idx)
    {
        if (idx >= 0 && _slices[idx].Item.Tag is MoreItems more)
        {
            WheelAnimations.Success(_slices[idx].Group);
            Overflow?.Invoke(_parent, more.Items);
            return;
        }
        if (idx >= 0 && _mode != WheelMode.Drag)
        {
            var it = _slices[idx].Item;
            var sub = it.Children is { Count: > 0 } kids ? new WheelPage(it.Label, kids, it.Tag) : Expand?.Invoke(it, _mode);
            if (sub is { Items.Count: > 0 })
            {
                Push(sub, it);
                return;
            }
        }
        if (idx >= 0)
        {
            var item = _slices[idx].Item;
            WheelAnimations.Success(_slices[idx].Group);
            if (_mode != WheelMode.Drag && _dragFiles.Count > 0)
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
        if (Docked)
        {
            if (e.Key == Windows.System.VirtualKey.Escape) DockedClick?.Invoke();
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Escape when _editing:
                ToggleEdit();
                break;
            case Windows.System.VirtualKey.Escape:
            case Windows.System.VirtualKey.Back:
                if (!Back()) HideAnimated();
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
            case Windows.System.VirtualKey.Enter when _hover >= 0:
                Choose(_hover);
                e.Handled = true;
                break;
            case >= Windows.System.VirtualKey.Number1 and <= Windows.System.VirtualKey.Number8:
            case >= Windows.System.VirtualKey.NumberPad1 and <= Windows.System.VirtualKey.NumberPad8:
                var n = e.Key >= Windows.System.VirtualKey.NumberPad1 ? e.Key - Windows.System.VirtualKey.NumberPad1 : e.Key - Windows.System.VirtualKey.Number1;
                if (n < _slices.Count)
                {
                    SetHover(n);
                    Choose(n);
                }
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
