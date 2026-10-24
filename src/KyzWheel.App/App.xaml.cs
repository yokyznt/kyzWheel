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
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _rehook;
    private IReadOnlyList<KyzWheel.Core.WhatsApp.WaChat> _waChats = [];
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
        _wa.ChatsChanged += () => _ = RefreshWaCacheAsync();
        _wa.StatusChanged += () => { if (_wa.Status.State == "open") _ = RefreshWaCacheAsync(); };
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
        _wheel.Expand = ExpandItem;
        _wheel.Overflow += OnOverflow;
        _rehook = _ui.CreateTimer();
        _rehook.Interval = TimeSpan.FromSeconds(45);
        _rehook.Tick += (_, _) => _input?.Rehook();
        _rehook.Start();
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume) _ui.TryEnqueue(() => { _input?.Rehook(); ApplyConfig(_config); });
        };
        Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.ConsoleConnect) _ui.TryEnqueue(() => { _input?.Rehook(); ApplyConfig(_config); });
        };
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
        KyzWheel.Mail.GoogleOAuth.ClientId = cfg.GoogleClientId ?? Environment.GetEnvironmentVariable("KYZ_GOOGLE_CLIENT_ID");
        KyzWheel.Mail.GoogleOAuth.ClientSecret = cfg.GoogleClientSecret ?? Environment.GetEnvironmentVariable("KYZ_GOOGLE_CLIENT_SECRET");
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

    private void ShowWhatsAppPanel(Slot s, IReadOnlyList<string> files, KyzWheel.Core.WhatsApp.WaChat? chat = null)
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
        _panel.Show(s.Glyph, s.ColorHex, s.Name, sub, new WhatsAppPanel(_wa, _toasts, files, chat), _wa.Status.State == "open" ? [logout] : [], x, y);
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
        _addAccount.GoogleClientSaved += (id, secret) => ApplyConfig(_config with { GoogleClientId = id, GoogleClientSecret = secret });
        _addAccount.Closed += (_, _) => _addAccount = null;
        _addAccount.CenterOnCursorMonitor();
        _addAccount.BringToFront();
    }

    private IReadOnlyList<WheelPage> ConverterPages(IReadOnlyList<string> files)
    {
        var cat = _converter.Catalog;
        return cat.PagesOrder(files)
            .Select(page => new WheelPage(ConverterController.PageTitle(page),
                cat.ActionsFor(page, files).Select(c => new WheelItem("conv:" + c.Id, c.Label, c.Glyph, ConverterController.PageColor(page), 0, c)).ToList(),
                page, PageGlyph(page), ConverterController.PageColor(page)))
            .Where(p => p.Items.Count > 0)
            .ToList();
    }

    private static string PageGlyph(KyzWheel.Convert.ConverterPage p) => p.ToString() switch
    {
        "Images" => "\uEB9F",
        "Media" => "\uE714",
        "Documents" => "\uE8A5",
        "Tools" => "\uE90F",
        _ => "\uE8AB",
    };

    private WheelPage FileRootPage(IReadOnlyList<string> files)
    {
        var items = _config.Slots.Where(s => s.Type != SlotType.Clipboard && s.Type != SlotType.OpenUrl).Select(SlotItem).ToList();
        if (!_config.Slots.Any(s => s.Type == SlotType.Converter))
            items.Insert(0, new WheelItem("slot:converter", "Convertir", "\uE8AB", "#FF8A3D", 0, new Slot("converter", "Convertir", "\uE8AB", "#FF8A3D", SlotType.Converter, null, null, false)));
        return new WheelPage(files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} archivos", items);
    }

    private sealed record RecentFile(string Path);

    private static IReadOnlyList<string> RecentFiles(int n)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] dirs = [Path.Combine(home, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots")];
        var files = new List<FileInfo>();
        foreach (var d in dirs)
        {
            try
            {
                if (Directory.Exists(d)) files.AddRange(new DirectoryInfo(d).EnumerateFiles().Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !f.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && !f.Name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return files.OrderByDescending(f => f.LastWriteTimeUtc).Take(n).Select(f => f.FullName).ToList();
    }

    private static string FileGlyph(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".heic" or ".gif" or ".bmp" or ".tiff" or ".avif" => "\uEB9F",
        ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi" => "\uE714",
        ".mp3" or ".wav" or ".m4a" or ".flac" or ".ogg" => "\uE8D6",
        ".pdf" or ".docx" or ".txt" or ".md" => "\uE8A5",
        _ => "\uE8B7",
    };

    private async void PickFileForConverter()
    {
        var (x, y) = WheelCenter();
        _wheel.HideAnimated();
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _wheel.GetWindowHandleSafe());
        var picked = await picker.PickMultipleFilesAsync();
        var files = picked?.Select(p => p.Path).Where(p => !string.IsNullOrEmpty(p)).ToList() ?? [];
        if (files.Count == 0) return;
        ShowConverterWheel(x, y, files);
    }

    private void ShowConverterWheel(int x, int y, IReadOnlyList<string> files)
    {
        var pages = ConverterPages(files);
        if (pages.Count == 0)
        {
            _toasts.Info("Convertir", "No hay acciones para este tipo de archivo");
            return;
        }
        _trimTimer?.Stop();
        _wheel.ShowFiles(x, y, files, pages);
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
        if (_wa.Status.State == "open") _ = RefreshWaCacheAsync();
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
                _wheel.ShowFiles(x, y, selected, [FileRootPage(selected)], sw);
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

    private static string Short(string s, int n = 16)
    {
        s = s.ReplaceLineEndings(" ").Trim();
        return s.Length <= n ? s : s[..(n - 1)] + "…";
    }

    private async Task RefreshWaCacheAsync()
    {
        if (_wa.Status.State != "open") return;
        try
        {
            _waChats = await _wa.RecentChatsAsync(24);
        }
        catch (Exception e) when (e is WaException or TimeoutException or InvalidOperationException)
        {
        }
    }

    private WheelPage? ExpandItem(WheelItem item, WheelMode mode)
    {
        if (item.Tag is RecentFile rf)
        {
            _wheel.AttachFiles([rf.Path]);
            var pages = ConverterPages([rf.Path]);
            return pages.Count == 0 ? null : WheelFit.Group(pages, "Convertir");
        }
        if (item.Tag is not Slot s) return null;
        switch (s.Type)
        {
            case SlotType.Converter when _wheel.Files.Count > 0:
                var conv = ConverterPages(_wheel.Files);
                return conv.Count == 0 ? null : WheelFit.Group(conv, "Convertir") with { Tag = s };
            case SlotType.Converter:
                var recent = new List<WheelItem> { new("conv:pick", "Elegir archivo", "\uE8E5", s.ColorHex, 0, "pick") };
                recent.AddRange(RecentFiles(23).Select(p => new WheelItem("recent:" + p, Short(Path.GetFileNameWithoutExtension(p), 14), FileGlyph(p), s.ColorHex, 0, new RecentFile(p))));
                return new WheelPage("Convertir", recent, s);
            case SlotType.WhatsApp when _wa.Status.State == "open" && _waChats.Count > 0:
                return new WheelPage(s.Name, _waChats.Select(c => new WheelItem("wa:" + c.Jid, Short(c.Name), c.IsGroup ? "\uE716" : "\uE77B", s.ColorHex, c.Unread, c)).ToList(), s);
            case SlotType.Mail when mode == WheelMode.Root && _mail.Accounts.Count > 0:
                var mails = _mail.Inbox.Top(24);
                if (mails.Count == 0) return null;
                var items = new List<WheelItem> { new("mail:compose", "Redactar", "\uE70F", s.ColorHex, 0, "compose") };
                items.AddRange(mails.Select(m => new WheelItem("mail:" + m.MessageId, Short(m.FromName.Length > 0 ? m.FromName : m.FromAddress), m.HasAttachments ? "\uE723" : "\uE715", s.ColorHex, m.Unread ? 1 : 0, m)));
                return new WheelPage(s.Name, items, s);
            case SlotType.Clipboard when mode == WheelMode.Root:
                var clips = _clips.History.Items;
                if (clips.Count == 0) return null;
                return new WheelPage(s.Name, clips.Select(c => new WheelItem("clip:" + c.Id, Short(c.Preview), c.Kind switch
                {
                    KyzWheel.Core.Clipboard.ClipKind.Image => "\uEB9F",
                    KyzWheel.Core.Clipboard.ClipKind.Files => "\uE8B7",
                    _ => c.Pinned ? "\uE718" : "\uE8C8",
                }, s.ColorHex, 0, c)).ToList(), s);
            default:
                return null;
        }
    }

    private void OnOverflow(WheelItem? parent, IReadOnlyList<WheelItem> rest)
    {
        if (parent?.Tag is Slot s && _wheel.Mode == WheelMode.Root && s.Type != SlotType.Converter)
        {
            InvokeSlot(s);
            return;
        }
        var files = _wheel.Files.ToList();
        var mode = _wheel.Mode;
        var (x, y) = WheelCenter();
        _wheel.HideAnimated();
        _panel ??= new SidePanelWindow();
        var title = parent?.Label ?? "Más opciones";
        _panel.Show(parent?.Glyph ?? "\uE712", parent?.ColorHex ?? "#FFFFFF", title, $"{rest.Count} opciones", new ItemListPanel(rest, it =>
        {
            _panel.HidePanel();
            if (mode == WheelMode.Files && files.Count > 0) OnFilesDropped(it, files);
            else OnItemInvoked(it);
        }), [], x, y);
    }

    private Slot? FirstSlot(SlotType t) => _config.Slots.FirstOrDefault(x => x.Type == t);

    private void OnItemInvoked(WheelItem item)
    {
        switch (item.Tag)
        {
            case Slot s:
                InvokeSlot(s);
                break;
            case KyzWheel.Core.Clipboard.ClipItem c:
                _wheel.HideAnimated();
                PasteClip(c);
                break;
            case MailSummary m:
                _wheel.HideAnimated();
                OpenMail(m);
                break;
            case "pick":
                PickFileForConverter();
                break;
            case RecentFile rf:
                var (rx, ry) = WheelCenter();
                ShowConverterWheel(rx, ry, [rf.Path]);
                break;
            case "compose":
                _wheel.HideAnimated();
                OpenCompose([], null);
                break;
            case KyzWheel.Core.WhatsApp.WaChat chat when FirstSlot(SlotType.WhatsApp) is { } ws:
                ShowWhatsAppPanel(ws, [], chat);
                break;
        }
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
            case KyzWheel.Core.WhatsApp.WaChat chat:
                _ = SendToChatAsync(chat, files);
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

    private async Task SendToChatAsync(KyzWheel.Core.WhatsApp.WaChat chat, IReadOnlyList<string> files)
    {
        var ok = 0;
        foreach (var f in files)
        {
            try
            {
                await _wa.SendFileAsync(chat.Jid, f);
                ok++;
            }
            catch (Exception e) when (e is WaException or TimeoutException or InvalidOperationException)
            {
                _toasts.Info(chat.Name, $"No se pudo enviar {Path.GetFileName(f)}: {e.Message}");
            }
        }
        if (ok > 0) _toasts.Info(chat.Name, ok == 1 ? $"Enviado {Path.GetFileName(files[0])}" : $"Enviados {ok} archivos");
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
                    var (cx, cy) = User32.CursorPosition();
                    ShowConverterWheel(cx, cy, files);
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
