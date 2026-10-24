using System.Diagnostics;
using KyzWheel.App.Converter;
using KyzWheel.App.Settings;
using KyzWheel.App.Shell;
using KyzWheel.App.Wheel;
using KyzWheel.Convert;
using KyzWheel.Convert.Images;
using KyzWheel.Core.Config;
using KyzWheel.Core.Files;
using KyzWheel.Core.Input;
using KyzWheel.Platform.Input;
using KyzWheel.Platform.Lifecycle;
using KyzWheel.Platform.Native;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace KyzWheel.App;

public partial class App : Application
{
    private readonly ConfigStore _store = new(ConfigStore.DefaultPath);
    private SingleInstance? _single;
    private DispatcherQueue _ui = null!;
    private AppConfig _config = AppConfig.Default;
    private InputThread? _input;
    private GlobalHotkeyService? _hotkeys;
    private DragShiftDetector? _drag;
    private WheelWindow _wheel = null!;
    private SidePanelWindow? _panel;
    private ConverterController _converter = null!;
    private ConverterWindow? _converterWindow;
    private SettingsWindow? _settings;
    private readonly Toasts _toasts = new();
    private TrayIcon? _tray;
    private DispatcherQueueTimer? _pauseTimer;
    private DispatcherQueueTimer? _trimTimer;
    private bool _paused;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log.Error(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error((Exception)e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _single = new SingleInstance();
        if (!_single.IsFirst)
        {
            SingleInstance.Signal(Environment.GetCommandLineArgs().Contains("--settings") ? "settings" : "show");
            Environment.Exit(0);
            return;
        }
        _ui = DispatcherQueue.GetForCurrentThread();
        _config = _store.Load();
        _converter = new ConverterController(new ConversionCatalog(ImageConversions.All()), _toasts);
        _toasts.Init();

        _wheel = new WheelWindow { PagesForFiles = DragPages };
        _wheel.ItemInvoked += OnItemInvoked;
        _wheel.FilesDropped += OnFilesDropped;
        _wheel.FirstFrame += ms => Log.Write("perf.log", $"open {ms:0.0} ms");
        _wheel.Hidden += ScheduleTrim;

        _input = new InputThread();
        _hotkeys = new GlobalHotkeyService(_input);
        _hotkeys.Triggered += id => _ui.TryEnqueue(() => OnHotkey(id));
        _drag = new DragShiftDetector(_input);
        _drag.Gesture += (g, x, y) => _ui.TryEnqueue(() => OnGesture(g, x, y));

        ApplyConfig(_config);
        _wheel.Prime(RootPage());
        _single.Command += c => _ui.TryEnqueue(() =>
        {
            if (c == "settings") OpenSettings();
            else OpenWheelAtCursor();
        });

        _tray = new TrayIcon(OpenWheelAtCursor, OpenConverter, OpenSettings, TogglePause, ExitApp);
        if (Environment.GetCommandLineArgs().Contains("--settings")) OpenSettings();
        if (Environment.GetCommandLineArgs().SkipWhile(a => a != "--at").Skip(1).FirstOrDefault() is { } at)
        {
            var p = at.Split(',');
            _wheel.ShowRoot(int.Parse(p[0]), int.Parse(p[1]), RootPage(), Stopwatch.StartNew());
        }
        ScheduleTrim();
    }

    private IReadOnlyList<string> ApplyConfig(AppConfig cfg)
    {
        var errors = new List<string>();
        _hotkeys!.UnregisterAll();
        if (Hotkey.TryParse(cfg.GlobalHotkey, out var g) && !_hotkeys.Register("global", g))
            errors.Add($"{cfg.GlobalHotkey} ya lo usa otra app");
        foreach (var s in cfg.Slots.Where(s => s.Hotkey is not null))
            if (Hotkey.TryParse(s.Hotkey, out var h) && !_hotkeys.Register("slot:" + s.Id, h))
                errors.Add($"{s.Hotkey} ya lo usa otra app");
        if (errors.Count > 0 && cfg != _config)
        {
            ApplyConfig(_config);
            return errors;
        }
        _config = cfg;
        _store.Save(cfg);
        UpdateAutostart(cfg.StartWithWindows);
        return errors;
    }

    private static void UpdateAutostart(bool on)
    {
        try
        {
            var guard = Path.Combine(AppContext.BaseDirectory, "KyzWheel.Guard.exe");
            if (on && File.Exists(guard)) AutostartService.Enable(guard);
            else if (!on) AutostartService.Disable();
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    private WheelPage RootPage() => new("kyzWheel", _config.Slots.Select(SlotItem).ToList());

    private static WheelItem SlotItem(Slot s) => new("slot:" + s.Id, s.Name, s.Glyph, s.ColorHex, 0, s);

    private IReadOnlyList<WheelPage> DragPages(IReadOnlyList<string> files)
    {
        var pages = new List<WheelPage>();
        if (files.Count == 0) return pages;
        var cat = _converter.Catalog;
        foreach (var page in cat.PagesOrder(files))
        {
            var items = cat.ActionsFor(page, files)
                .Select(c => new WheelItem("conv:" + c.Id, c.Label, c.Glyph, ConverterController.PageColor(page), 0, c))
                .ToList();
            pages.Add(new WheelPage(ConverterController.PageTitle(page), items, page));
        }
        var targets = _config.Slots.Where(s => s.Type is SlotType.WhatsApp or SlotType.Mail or SlotType.SendToFolder or SlotType.LaunchApp).Select(SlotItem).ToList();
        if (targets.Count > 0) pages.Add(new WheelPage("Enviar a", targets, "slots"));
        return pages;
    }

    private void OnHotkey(string id)
    {
        if (_paused) return;
        var sw = Stopwatch.StartNew();
        if (id == "global")
        {
            if (_wheel.IsShown && _wheel.Mode == WheelMode.Root) _wheel.HideAnimated();
            else OpenWheelAtCursor(sw);
            return;
        }
        var slot = _config.Slots.FirstOrDefault(s => "slot:" + s.Id == id);
        if (slot is not null) InvokeSlot(slot);
    }

    private void OpenWheelAtCursor() => OpenWheelAtCursor(Stopwatch.StartNew());

    private void OpenWheelAtCursor(Stopwatch sw)
    {
        _trimTimer?.Stop();
        var (x, y) = User32.CursorPosition();
        _wheel.ShowRoot(x, y, RootPage(), sw);
    }

    private void OnGesture(GestureOutput g, int x, int y)
    {
        if (_paused) return;
        switch (g)
        {
            case GestureOutput.OpenConverter:
            case GestureOutput.OpenTools:
                if (_wheel.IsShown && _wheel.Mode == WheelMode.Drag)
                {
                    if (g == GestureOutput.OpenTools) _wheel.GoToPage(p => p.Tag is ConverterPage.Tools);
                    return;
                }
                _trimTimer?.Stop();
                _wheel.ShowDrag(x, y, g == GestureOutput.OpenTools, Stopwatch.StartNew());
                break;
            case GestureOutput.NextPage:
                _wheel.NextPage();
                break;
            case GestureOutput.PrevPage:
                _wheel.PrevPage();
                break;
            case GestureOutput.Cancel:
                _wheel.HideAnimated();
                break;
            case GestureOutput.Close:
                _wheel.EndDrag();
                break;
        }
    }

    private void OnItemInvoked(WheelItem item)
    {
        if (item.Tag is Slot s) InvokeSlot(s);
    }

    private void OnFilesDropped(WheelItem item, IReadOnlyList<string> files)
    {
        if (files.Count == 0) return;
        switch (item.Tag)
        {
            case IConversion c:
                _ = _converter.RunAsync(c, files);
                break;
            case Slot s:
                DropOnSlot(s, files);
                break;
        }
    }

    private void InvokeSlot(Slot s)
    {
        try
        {
            switch (s.Type)
            {
                case SlotType.Converter:
                    _wheel.HideAnimated();
                    OpenConverter();
                    break;
                case SlotType.Mail:
                    ShowPanel(s.Name, "Aquí verás tus correos recientes de todas tus cuentas. Llega en la versión 2.");
                    break;
                case SlotType.WhatsApp:
                    ShowPanel(s.Name, "Aquí verás tus chats y archivos recientes de WhatsApp. Llega en la versión 3.");
                    break;
                case SlotType.LaunchApp:
                case SlotType.OpenUrl:
                case SlotType.SendToFolder:
                    _wheel.HideAnimated();
                    if (!string.IsNullOrWhiteSpace(s.Target)) Process.Start(new ProcessStartInfo(s.Target) { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
            _toasts.Info(s.Name, "No se pudo abrir: " + s.Target);
        }
    }

    private void ShowPanel(string title, string text)
    {
        _panel ??= new SidePanelWindow();
        var pos = _wheel.AppWindow.Position;
        var size = _wheel.AppWindow.Size;
        _wheel.HideAnimated();
        _panel.Show(title, SidePanelWindow.Message(text), pos.X + size.Width, pos.Y + size.Height / 8);
    }

    private void DropOnSlot(Slot s, IReadOnlyList<string> files)
    {
        try
        {
            switch (s.Type)
            {
                case SlotType.SendToFolder when s.Target is not null:
                    Directory.CreateDirectory(s.Target);
                    var copied = new List<string>();
                    foreach (var f in files)
                    {
                        var dest = Path.Combine(s.Target, Path.GetFileName(f));
                        if (File.Exists(dest) || Directory.Exists(dest)) dest = OutputNaming.NextFreePath(dest, Path.GetExtension(dest));
                        File.Copy(f, dest);
                        copied.Add(dest);
                    }
                    _toasts.Result($"Copiado a {s.Name}", $"{copied.Count} {(copied.Count == 1 ? "archivo" : "archivos")}", copied);
                    break;
                case SlotType.LaunchApp when s.Target is not null:
                    Process.Start(new ProcessStartInfo(s.Target, string.Join(' ', files.Select(f => $"\"{f}\""))) { UseShellExecute = true });
                    break;
                case SlotType.Mail:
                    _toasts.Info(s.Name, "Enviar por correo llega en la versión 2");
                    break;
                case SlotType.WhatsApp:
                    _toasts.Info(s.Name, "Enviar por WhatsApp llega en la versión 3");
                    break;
                case SlotType.Converter:
                    OpenConverter();
                    _converterWindow!.AddFiles(files);
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
            _toasts.Info(s.Name, e.Message);
        }
    }

    private void OpenConverter()
    {
        _converterWindow ??= new ConverterWindow(_converter);
        _converterWindow.CenterOnCursorMonitor();
        _converterWindow.BringToFront();
    }

    private void OpenSettings()
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow(_config, ApplyConfig);
            _settings.RecordingChanged += rec =>
            {
                if (rec) _hotkeys!.UnregisterAll();
                else ApplyConfig(_config);
            };
            _settings.CenterOnCursorMonitor();
        }
        _settings.BringToFront();
    }

    private void TogglePause()
    {
        _paused = !_paused;
        _drag!.Enabled = !_paused;
        _pauseTimer?.Stop();
        if (_paused)
        {
            _pauseTimer = _ui.CreateTimer();
            _pauseTimer.Interval = TimeSpan.FromHours(1);
            _pauseTimer.IsRepeating = false;
            _pauseTimer.Tick += (_, _) => TogglePause();
            _pauseTimer.Start();
        }
        _tray?.SetPaused(_paused, DateTime.Now.AddHours(1).ToString("HH:mm"));
    }

    private void ScheduleTrim()
    {
        _trimTimer ??= CreateTrimTimer();
        _trimTimer.Stop();
        _trimTimer.Start();
    }

    private DispatcherQueueTimer CreateTrimTimer()
    {
        var t = _ui.CreateTimer();
        t.Interval = TimeSpan.FromSeconds(3);
        t.IsRepeating = false;
        t.Tick += (_, _) =>
        {
            if (_wheel.IsShown) return;
            GC.Collect(2, GCCollectionMode.Optimized, false);
            Shcore.TrimWorkingSet();
        };
        return t;
    }

    private void ExitApp()
    {
        try
        {
            _tray?.Dispose();
            _toasts.Dispose();
            _hotkeys?.UnregisterAll();
            _input?.Dispose();
            _single?.Dispose();
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
        Environment.Exit(0);
    }
}
