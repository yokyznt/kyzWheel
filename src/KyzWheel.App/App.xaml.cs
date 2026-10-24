using System.Diagnostics;
using KyzWheel.App.Converter;
using KyzWheel.App.Mail;
using KyzWheel.App.Clips;
using KyzWheel.App.WhatsApp;
using KyzWheel.Mail;
using KyzWheel.Platform.Secrets;
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
    private MailService _mail = null!;
    private WhatsAppBridge _wa = null!;
    private readonly ClipboardService _clips = new();
    private nint _lastForeground;
    private AddAccountWindow? _addAccount;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log.Error(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            KyzWheel.Platform.Native.CursorLock.Release();
            Log.Error((Exception)e.ExceptionObject);
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KyzWheel.Platform.Native.CursorLock.Release();
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
        Theme.Apply(_config.ThemeId);
        _mail = new MailService(new CredentialManagerStore(), _ui);
        _mail.Changed += UpdateBadges;
        _mail.Arrived += OnMailArrived;
        _clips.Start();
        _wa = new WhatsAppBridge(_ui);
        _wa.StatusChanged += UpdateBadges;
        _wa.ChatsChanged += () => _ = RefreshWaUnreadAsync();
        _wa.MessageArrived += (chat, msg) =>
        {
            if (_config.Slots.Any(s => s.Type == SlotType.WhatsApp && s.Notify)) _toasts.Info(chat.Name, msg.Text);
        };
        _converter = new ConverterController(new ConversionCatalog([.. ImageConversions.All(), .. KyzWheel.Convert.Media.MediaConversions.All(), .. KyzWheel.Convert.Documents.PdfConversions.All()]), _toasts);
        _toasts.Init();
        KyzWheel.Convert.Documents.PdfConversions.EnsureFonts();
        KyzWheel.Convert.Media.FfmpegLocator.LocalDir = Path.Combine(Log.Dir, "ffmpeg");

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
        if (Environment.GetCommandLineArgs().SkipWhile(a => a != "--open").Skip(1).FirstOrDefault() is { } open
            && Enum.TryParse<SlotType>(open, true, out var type)
            && _config.Slots.FirstOrDefault(s => s.Type == type) is { } slot)
            InvokeSlot(slot);
        if (Environment.GetCommandLineArgs().SkipWhile(a => a != "--simulate-hotkey").Skip(1).FirstOrDefault() is { } ms && int.TryParse(ms, out var delay))
        {
            var t = _ui.CreateTimer();
            t.Interval = TimeSpan.FromMilliseconds(delay);
            t.IsRepeating = false;
            t.Tick += (_, _) => OnHotkey("global");
            t.Start();
        }
        ScheduleTrim();
        _ = Task.Run(Updater.CheckAsync);
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
        Theme.Apply(cfg.ThemeId);
        _mail.Sync(cfg.MailAccounts ?? []);
        if (cfg.Slots.Any(s => s.Type == SlotType.WhatsApp)) _wa.Start();
        else _ = _wa.DisposeAsync().AsTask();
        UpdateBadges();
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

    private WheelItem SlotItem(Slot s) => new("slot:" + s.Id, s.Name, s.Glyph, s.ColorHex, BadgeFor(s), s);

    private int BadgeFor(Slot s) => s.Type switch
    {
        SlotType.Mail => _mail.Inbox.UnreadCount,
        SlotType.WhatsApp => _wa.Status.Unread,
        _ => 0,
    };

    private async Task RefreshWaUnreadAsync()
    {
        await _wa.RefreshStatusAsync();
        UpdateBadges();
    }

    private void ShowWhatsAppPanel(Slot s, IReadOnlyList<string> files)
    {
        var (x, y) = WheelCenter();
        _wheel.HideAnimated();
        _panel ??= new SidePanelWindow();
        if (!_wa.Running && _wa.Status.State != "missing") _wa.Start();
        var sub = _wa.Status.State == "open" ? (_wa.Status.MeName is { Length: > 0 } n ? n : "Conectado") : "Sin vincular";
        var logout = Ui.IconButton("\uE8BD", "Desvincular WhatsApp", async () =>
        {
            try { await _wa.LogoutAsync(); } catch (WaException) { }
        });
        _panel.Show(s.Glyph, s.ColorHex, s.Name, sub, new WhatsAppPanel(_wa, _toasts, files), _wa.Status.State == "open" ? [logout] : [], x, y);
    }

    private void UpdateBadges()
    {
        var unread = _mail.Inbox.UnreadCount;
        foreach (var s in _config.Slots.Where(s => s.Type == SlotType.Mail)) _wheel.SetBadge("slot:" + s.Id, unread);
        foreach (var s in _config.Slots.Where(s => s.Type == SlotType.WhatsApp)) _wheel.SetBadge("slot:" + s.Id, _wa.Status.Unread);
        unread += _wa.Status.Unread;
        _tray?.SetTooltip(unread > 0 ? $"kyzWheel · {unread} sin leer" : "kyzWheel");
    }

    private void OnMailArrived(MailSummary m)
    {
        if (_config.Slots.Any(s => s.Type == SlotType.Mail && s.Notify) && m.Unread)
            _toasts.Info(m.FromName, $"{m.Subject}\n{m.Preview}");
    }

    private void ShowMailPanel(Slot s)
    {
        var (x, y) = WheelCenter();
        _wheel.HideAnimated();
        _panel ??= new SidePanelWindow();
        var compose = Ui.IconButton("\uE70F", "Redactar", () => OpenCompose([], null));
        var refresh = Ui.IconButton("\uE72C", "Actualizar", () => { foreach (var a in _mail.Accounts) _mail.Restart(a.Email); });
        var settings = Ui.IconButton("\uE713", "Cuentas", OpenSettings);
        var sub = _mail.Accounts.Count switch
        {
            0 => "Sin cuentas",
            1 => _mail.Accounts[0].Email,
            var n => $"{n} cuentas · {_mail.Inbox.UnreadCount} sin leer",
        };
        _panel.Show(s.Glyph, s.ColorHex, s.Name, sub, new MailPanel(_mail, OpenMail, OpenAddAccount), [compose, refresh, settings], x, y);
    }

    private void ShowClipboardPanel(Slot s)
    {
        var (x, y) = WheelCenter();
        _wheel.HideAnimated();
        _panel ??= new SidePanelWindow();
        var clear = Ui.IconButton("\uE894", "Borrar todo (menos fijados)", () => _clips.History.ClearUnpinned());
        _panel.Show(s.Glyph, s.ColorHex, s.Name, $"{_clips.History.Items.Count} elementos", new ClipboardPanel(_clips, PasteClip), [clear], x, y);
    }

    private async void PasteClip(KyzWheel.Core.Clipboard.ClipItem item)
    {
        await _clips.CopyAsync(item);
        _panel?.HidePanel();
        var target = _lastForeground;
        await Task.Run(() => KyzWheel.Platform.Native.Keys.PasteInto(target));
    }

    private (int X, int Y) WheelCenter()
    {
        if (_wheel.IsShown)
        {
            var p = _wheel.AppWindow.Position;
            var z = _wheel.AppWindow.Size;
            return (p.X + z.Width / 2, p.Y + z.Height / 2);
        }
        return User32.CursorPosition();
    }

    private void OpenMail(MailSummary m)
    {
        _panel?.HidePanel();
        var w = new MailReaderWindow(_mail, m, r => OpenCompose([], r));
        w.CenterOnCursorMonitor();
        w.BringToFront();
    }

    private void OpenCompose(IReadOnlyList<string> files, MailSummary? reply)
    {
        _panel?.HidePanel();
        if (_mail.Accounts.Count == 0)
        {
            OpenAddAccount();
            return;
        }
        var w = new ComposeWindow(_mail, _toasts, files, reply);
        w.CenterOnCursorMonitor();
        w.BringToFront();
    }

    private void OpenAddAccount()
    {
        _panel?.HidePanel();
        if (_addAccount is not null)
        {
            _addAccount.BringToFront();
            return;
        }
        _addAccount = new AddAccountWindow(_mail.Secrets, _config.MailAccounts ?? []);
        _addAccount.Added += a => ApplyConfig(_config with { MailAccounts = [.. _config.MailAccounts ?? [], a] });
        _addAccount.Closed += (_, _) => _addAccount = null;
        _addAccount.CenterOnCursorMonitor();
        _addAccount.BringToFront();
    }

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
        if (Foreground.IsFullscreenAppActive() && !_wheel.IsShown) return;
        var sw = Stopwatch.StartNew();
        if (id == "global")
        {
            if (_wheel.IsShown && _wheel.Mode != WheelMode.Drag)
            {
                _wheel.HideAnimated();
                return;
            }
            var fg = KyzWheel.Platform.Shell.ShellSelection.Foreground();
            _lastForeground = fg;
            var selected = KyzWheel.Platform.Shell.ShellSelection.Selected(fg);
            if (selected.Count > 0)
            {
                _trimTimer?.Stop();
                var (x, y) = User32.CursorPosition();
                _wheel.ShowFiles(x, y, selected, DragPages(selected), sw);
            }
            else OpenWheelAtCursor(sw);
            return;
        }
        var slot = _config.Slots.FirstOrDefault(s => "slot:" + s.Id == id);
        if (slot is not null) InvokeSlot(slot);
    }

    private void OpenWheelAtCursor() => OpenWheelAtCursor(Stopwatch.StartNew());

    private void OpenWheelAtCursor(Stopwatch sw)
    {
        var fgNow = KyzWheel.Platform.Shell.ShellSelection.Foreground();
        if (fgNow != 0 && fgNow != _wheel.GetWindowHandleSafe()) _lastForeground = fgNow;
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
                    ShowMailPanel(s);
                    break;
                case SlotType.Clipboard:
                    ShowClipboardPanel(s);
                    break;
                case SlotType.WhatsApp:
                    ShowWhatsAppPanel(s, []);
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

    private void ShowPanel(Slot s, string text)
    {
        var (x, y) = WheelCenter();
        _wheel.HideAnimated();
        _panel ??= new SidePanelWindow();
        _panel.Show(s.Glyph, s.ColorHex, s.Name, "Próximamente", MailPanel.Empty(s.Glyph, "En camino", text, null, null), [], x, y);
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
                    OpenCompose(files, null);
                    break;
                case SlotType.WhatsApp:
                    ShowWhatsAppPanel(s, files);
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
            _settings = new SettingsWindow(_config, ApplyConfig)
            {
                AddAccountRequested = (existing, done) =>
                {
                    var w = new AddAccountWindow(_mail.Secrets, existing);
                    w.Added += done;
                    w.CenterOnCursorMonitor();
                    w.BringToFront();
                },
                AccountRemoved = a => _mail.Secrets.Remove(ProviderPresets.SecretKey(a.Email)),
            };
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
            _mail.StopAll();
            _wa.DisposeAsync().AsTask().Wait(3000);
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
