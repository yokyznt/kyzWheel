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
    private readonly KyzWheel.App.Phone.PhoneService _phone = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _rehook;
    private IReadOnlyList<KyzWheel.Core.WhatsApp.WaChat> _waChats = [];
    private IReadOnlyList<KyzWheel.Core.WhatsApp.WaContact> _waContacts = [];
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
        if (!_config.MessagesMerged)
        {
            _config = _config with { Slots = SlotMigrations.MergeMessages(_config.Slots).ToList(), MessagesMerged = true };
            _store.Save(_config);
        }
        if (!_config.PhoneSlotOffered)
        {
            var slots = _config.Slots.ToList();
            if (slots.All(x => x.Type != SlotType.Phone) && slots.Count < AppConfig.MaxSlots)
                slots.Add(new Slot("phone", "Celular", "\uE8EA", "#60A5FA", SlotType.Phone, null, null, false));
            _config = _config with { Slots = slots, PhoneSlotOffered = true };
            _store.Save(_config);
        }
        if (!_config.ToolsOffered)
        {
            var slots = _config.Slots.ToList();
            (string Id, string Name, string Glyph, string Color, SlotType Type)[] tools =
            [
                ("capture", "Captura", "\uE722", "#F472B6", SlotType.Capture),
                ("projects", "Proyectos", "\uE943", "#A78BFA", SlotType.Projects),
                ("hermes", "Hermes", "\uE99A", "#FBBF24", SlotType.Hermes),
                ("music", "Música", "\uE8D6", "#1DB954", SlotType.Music),
            ];
            foreach (var t in tools)
                if (slots.All(x => x.Type != t.Type) && slots.Count < AppConfig.MaxSlots
                    && (t.Type != SlotType.Hermes || KyzWheel.App.Tools.HermesPanel.Exe() is not null)
                    && (t.Type != SlotType.Projects || Directory.Exists(KyzWheel.Core.Projects.ProjectScanner.DefaultRoot)))
                    slots.Add(new Slot(t.Id, t.Name, t.Glyph, t.Color, t.Type, null, null, false));
            _config = _config with { Slots = slots, ToolsOffered = true };
            _store.Save(_config);
        }
        Theme.SetTransparency(_config.TransparencyOrDefault);
        Theme.Apply(_config.ThemeId);
        _mail = new MailService(new CredentialManagerStore(), _ui);
        _mail.Changed += UpdateBadges;
        _mail.Changed += () => QueueFeedRefresh("mail");
        _mail.Arrived += OnMailArrived;
        _clips.Start();
        _wa = new WhatsAppBridge(_ui);
        _wa.ChatsChanged += () => _ = RefreshWaCacheAsync();
        _wa.ChatsChanged += () => QueueFeedRefresh("chats");
        _wa.ImChanged += () => QueueFeedRefresh("chats");
        _wa.ImStatusChanged += _ => QueueFeedRefresh("chats");
        _wa.ImMessageArrived += item =>
        {
            if (!_config.Slots.Any(s => s.Type is SlotType.Messages && s.Notify)) return;
            var (svc, chatId) = KyzWheel.Core.Messaging.Im.Split(item.Jid);
            var sv = KyzWheel.Core.Messaging.Im.Find(svc);
            var title = item.IsGroup && !string.IsNullOrEmpty(item.Message.Sender) ? $"{item.Message.Sender} · {item.ChatName}" : item.ChatName;
            Notify("im", item.Jid, title, item.Message.Text, sv.ColorHex, sv.Glyph, null, reply: text => _wa.ImSendAsync(svc, chatId, text));
        };
        Ui.LineCopied += line => Notify("copy", "", "Línea copiada", line, "#7DD3FC", "\uE8C8", null, true);
        _wa.StatusChanged += () => { if (_wa.Status.State == "open") _ = RefreshWaCacheAsync(); };
        _wa.StatusChanged += UpdateBadges;
        _wa.ChatsChanged += () => _ = RefreshWaUnreadAsync();
        _wa.MessageArrived += (chat, msg) =>
        {
            if (_config.Slots.Any(s => s.Type is SlotType.WhatsApp or SlotType.Messages && s.Notify))
                Notify("wa", chat.Jid, chat.IsGroup && !string.IsNullOrEmpty(msg.Sender) ? $"{msg.Sender} · {chat.Name}" : chat.Name, msg.Text, "#25D366", "\uE8BD", () => _wa.AvatarAsync(chat.Jid), reply: text => _wa.SendTextAsync(chat.Jid, text));
        };
        Toasts.Override = (title, body) => Notify("info", "", title, body, "#7DD3FC", "\uE946", null, true);
        _toasts.Clicked += (kind, id) => _ui.TryEnqueue(() => OpenFromToast(kind, id));
        _toasts.Replied += (kind, id, text) => _ui.TryEnqueue(() => _ = ReplyFromToastAsync(kind, id, text));
        _converter = new ConverterController(new ConversionCatalog([.. ImageConversions.All(), .. KyzWheel.Convert.Media.MediaConversions.All(), .. KyzWheel.Convert.Documents.PdfConversions.All()]), _toasts);
        _toasts.Init();
        KyzWheel.Convert.Documents.PdfConversions.EnsureFonts();
        KyzWheel.Convert.Media.FfmpegLocator.LocalDir = Path.Combine(Log.Dir, "ffmpeg");

        _wheel = new WheelWindow { PagesForFiles = DragPages, Steps = _config.WheelSteps };
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
        _wheel.Reordered += (items, parent) =>
        {
            if (parent is Slot ps)
            {
                if (!CustomizableSubs.Contains(ps.Type)) return;
                var sub = new Dictionary<string, IReadOnlyList<string>>(_config.SubOrder) { [ps.Id] = items.Select(i => i.Id).ToList() };
                _config = _config with { SubOrder = sub };
                _store.Save(_config);
                return;
            }
            var order = items.Select(i => i.Tag).OfType<Slot>().Select(x => x.Id).ToList();
            if (order.Count != _config.Slots.Count) return;
            var slots = order.Select(id => _config.Slots.First(x => x.Id == id)).ToList();
            _config = _config with { Slots = slots };
            _store.Save(_config);
        };
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
        if (Environment.GetCommandLineArgs().SkipWhile(a => a != "--chats").Skip(1).FirstOrDefault() is { } chatsArg)
        {
            var ct = _ui.CreateTimer();
            ct.Interval = TimeSpan.FromSeconds(9);
            ct.IsRepeating = false;
            ct.Tick += (_, _) => OpenChats(chatsArg == "all" ? null : chatsArg);
            ct.Start();
        }
        if (Environment.GetCommandLineArgs().Contains("--test-copy"))
        {
            var tw = new Microsoft.UI.Xaml.Window { Title = "copytest" };
            var tbx = Ui.CopyLine(new Microsoft.UI.Xaml.Controls.TextBlock { Text = "kyzWheel 0.7.0\nsegunda línea larga aquí", FontSize = 30, Margin = new Microsoft.UI.Xaml.Thickness(40) });
            Ui.LineCopied += l => Log.Write("app.log", "copytest:" + l + "|sel:" + tbx.SelectedText);
            tw.Content = tbx;
            tw.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(500, -900, 700, 300));
            tw.AppWindow.Show(false);
        }
        if (Environment.GetCommandLineArgs().Contains("--ig-login"))
        {
            var igw = new KyzWheel.App.Messages.InstagramLoginWindow();
            igw.AppWindow.Move(new Windows.Graphics.PointInt32(700, -1000));
            igw.AppWindow.Show(false);
        }
        if (Environment.GetCommandLineArgs().Contains("--phone-guide")) ShowPhoneSetup(new Slot("phone", "Celular", "\uE8EA", "#60A5FA", SlotType.Phone, null, null, false));
        if (Environment.GetCommandLineArgs().Contains("--test-banner"))
        {
            BannerWindow.Anchor = (960, -540);
            var bt = _ui.CreateTimer();
            bt.Interval = TimeSpan.FromMilliseconds(2500);
            bt.IsRepeating = false;
            bt.Tick += (_, _) => Notify("wa", "x@s.whatsapp.net", "Ana López", "¿Ya viste lo que te mandé? Contéstame cuando puedas 🙌", "#25D366", "\uE8BD", null, true, _ => Task.CompletedTask);
            bt.Start();
        }
        if (Environment.GetCommandLineArgs().SkipWhile(a => a != "--feed").Skip(1).FirstOrDefault() is { } feed)
        {
            var ft = _ui.CreateTimer();
            ft.Interval = TimeSpan.FromMilliseconds(9000);
            ft.IsRepeating = false;
            ft.Tick += (_, _) =>
            {
                if (feed == "wa") OpenChats(null);
                else ShowMailFeed();
            };
            ft.Start();
        }
        if (!Environment.GetCommandLineArgs().Contains("--passive")) Welcome();
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
        _ = CheckUpdatesAsync();
        var upd = _ui.CreateTimer();
        upd.Interval = TimeSpan.FromHours(3);
        upd.Tick += (_, _) => _ = CheckUpdatesAsync();
        upd.Start();
    }

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static (string? Id, string? Secret) _bundled;
    private static bool _bundledRead;

    private static (string? Id, string? Secret) BundledGoogleClient()
    {
        if (_bundledRead) return _bundled;
        _bundledRead = true;
        try
        {
            var p = Path.Combine(AppContext.BaseDirectory, "Assets", "google.json");
            if (!File.Exists(p)) return _bundled;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(p));
            var root = doc.RootElement;
            var c = root.TryGetProperty("installed", out var i) ? i : root;
            _bundled = (c.GetProperty("client_id").GetString(), c.GetProperty("client_secret").GetString());
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
        }
        return _bundled;
    }

    private IReadOnlyList<string> ApplyConfig(AppConfig cfg)
    {
        BannerWindow.Monitor = cfg.NotifyMonitor;
        WhatsAppBridge.MuteFilter = (archived, muted, locked) => (archived && cfg.WaHideArchived) || (muted && cfg.WaHideMuted) || (locked && cfg.HideLocked);
        WhatsAppBridge.TelegramApp = BundledTelegram();
        BannerWindow.Position = cfg.NotifyPosition;
        var bundled = BundledGoogleClient();
        var stored = _mail.Secrets;
        KyzWheel.Mail.GoogleOAuth.ClientId = NonEmpty(cfg.GoogleClientId) ?? NonEmpty(stored.Get(KyzWheel.Mail.GoogleOAuth.SecretKey + ":id")) ?? Environment.GetEnvironmentVariable("KYZ_GOOGLE_CLIENT_ID") ?? bundled.Id;
        KyzWheel.Mail.GoogleOAuth.ClientSecret = NonEmpty(cfg.GoogleClientSecret) ?? NonEmpty(stored.Get(KyzWheel.Mail.GoogleOAuth.SecretKey)) ?? Environment.GetEnvironmentVariable("KYZ_GOOGLE_CLIENT_SECRET") ?? bundled.Secret;
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
        Theme.SetTransparency(cfg.TransparencyOrDefault);
        _wheel.Steps = cfg.WheelSteps;
        Theme.Apply(cfg.ThemeId);
        _mail.Sync(cfg.MailAccounts ?? []);
        if (cfg.Slots.Any(s => s.Type is SlotType.WhatsApp or SlotType.Messages)) _wa.Start();
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
        SlotType.Messages => _wa.Status.Unread + _mail.Inbox.UnreadCount,
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
        _panel ??= CreatePanel();
        if (!_wa.Running && _wa.Status.State != "missing") _wa.Start();
        var sub = _wa.Status.State == "open" ? (_wa.Status.MeName is { Length: > 0 } n ? n : "Conectado") : "Sin vincular";
        _panel.Show(s.Glyph, s.ColorHex, s.Name, sub, new WhatsAppPanel(_wa, _toasts, files, chat), [], x, y);
    }

    private void UpdateBadges()
    {
        var unread = _mail.Inbox.UnreadCount;
        foreach (var s in _config.Slots.Where(s => s.Type == SlotType.Mail)) _wheel.SetBadge("slot:" + s.Id, unread);
        foreach (var s in _config.Slots.Where(s => s.Type == SlotType.WhatsApp)) _wheel.SetBadge("slot:" + s.Id, _wa.Status.Unread);
        foreach (var s in _config.Slots.Where(s => s.Type == SlotType.Messages)) _wheel.SetBadge("slot:" + s.Id, _wa.Status.Unread + _mail.Inbox.UnreadCount);
        _wheel.SetBadge("msg:wa", _wa.Status.Unread);
        _wheel.SetBadge("msg:mail", _mail.Inbox.UnreadCount);
        unread += _wa.Status.Unread;
        _tray?.SetTooltip(unread > 0 ? $"kyzWheel · {unread} sin leer" : "kyzWheel");
    }

    private BannerWindow? _banner;
    private readonly KyzWheel.Core.Notifications.QuietQueue _quiet = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _quietTimer;

    private void Notify(string kind, string id, string title, string body, string color, string glyph, Func<Task<string?>>? avatar, bool force = false, Func<string, Task>? reply = null)
    {
        _ui.TryEnqueue(() =>
        {
            try
            {
                if (!force && Foreground.IsExclusiveFullscreen())
                {
                    _quiet.Add(kind, title, body);
                    if (_quietTimer is null)
                    {
                        _quietTimer = _ui.CreateTimer();
                        _quietTimer.Interval = TimeSpan.FromSeconds(4);
                        _quietTimer.Tick += (_, _) =>
                        {
                            if (Foreground.IsExclusiveFullscreen() || _quiet.Count == 0) return;
                            var s = _quiet.Drain();
                            _quietTimer!.Stop();
                            (_banner ??= new BannerWindow()).Show(new Banner(s.Title, s.Body, "#34D399", "\uE8F2", () => ShowMailOrWaFeed()));
                        };
                    }
                    _quietTimer.Start();
                    return;
                }
                (_banner ??= new BannerWindow()).Show(new Banner(title, body, color, glyph, () => OpenFromToast(kind, id), avatar, reply));
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        });
    }

    private void ShowMailOrWaFeed()
    {
        if (_wa.Status.State == "open") OpenChats(null);
        else ShowMailFeed();
    }

    private readonly KyzWheel.App.Tools.MediaService _media = new();

    private sealed record ProjectAction(KyzWheel.Core.Projects.ProjectInfo Project, string Kind);

    private static IReadOnlyList<WheelItem> ProjectActions(KyzWheel.Core.Projects.ProjectInfo p, string color)
    {
        var list = new List<WheelItem>
        {
            new("pa:code:" + p.Name, "VS Code", "\uE943", color, 0, new ProjectAction(p, "code")),
            new("pa:dir:" + p.Name, "Carpeta", "\uE838", color, 0, new ProjectAction(p, "dir")),
            new("pa:term:" + p.Name, "Terminal", "\uE756", color, 0, new ProjectAction(p, "term")),
        };
        if (p.GitHubUrl is not null) list.Add(new("pa:gh:" + p.Name, "GitHub", "\uE774", color, 0, new ProjectAction(p, "gh")));
        if (p.HasApkScript) list.Add(new("pa:apk:" + p.Name, "Compilar APK", "\uE8EA", color, 0, new ProjectAction(p, "apk")));
        return list;
    }

    private void RunProjectAction(ProjectAction a)
    {
        try
        {
            var dir = a.Project.Path;
            System.Diagnostics.ProcessStartInfo psi = a.Kind switch
            {
                "code" => new("cmd.exe", $"/c code \"{dir}\"") { CreateNoWindow = true, UseShellExecute = false },
                "dir" => new("explorer.exe", $"\"{dir}\"") { UseShellExecute = true },
                "term" => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe"))
                    ? new("wt.exe", $"-d \"{dir}\"") { UseShellExecute = true }
                    : new("powershell.exe", "-NoExit") { WorkingDirectory = dir, UseShellExecute = true },
                "gh" => new(a.Project.GitHubUrl!) { UseShellExecute = true },
                _ => new("powershell.exe", "-NoExit -Command npm run android:apk") { WorkingDirectory = dir, UseShellExecute = true },
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception e)
        {
            Log.Error(e);
            _toasts.Info("Proyectos", "No se pudo abrir: " + e.Message);
        }
    }

    private async Task MusicAsync(Func<Task<bool>> op)
    {
        if (!await op()) Notify("music", "", "Música", "No hay nada reproduciéndose", "#1DB954", "\uE8D6", null, true);
        else if (_media.NowPlaying is { } np) Notify("music", "", _media.Playing ? "Sonando" : "En pausa", np, "#1DB954", "\uE8D6", null, true);
    }

    private string ClipText()
    {
        try
        {
            var view = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (view.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) return view.GetTextAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
        }
        return _clips.History.Items.FirstOrDefault(c => c.Kind == KyzWheel.Core.Clipboard.ClipKind.Text)?.Text ?? "";
    }

    private void ShowTranslate(string text)
    {
        Func<string, Task>? toPhone = _phone.Installed ? t => PhoneAsync("Traducción enviada", () => _phone.SendTextAsync(t)) : null;
        DockPanel("\uE8C1", "#38BDF8", "Traducir", "Español ↔ inglés", new KyzWheel.App.Tools.TranslatePanel(text, toPhone), []);
    }

    private async Task SnipAsync(bool ocr)
    {
        try
        {
            var path = await KyzWheel.App.Tools.ScreenTools.SnipAsync(TimeSpan.FromSeconds(60));
            if (path is null) return;
            if (ocr)
            {
                var text = await KyzWheel.App.Tools.ScreenTools.OcrAsync(path);
                if (text.Trim().Length == 0)
                {
                    Notify("cap", "", "Copiar texto", "No encontré texto en el recorte", "#F472B6", "\uE8E9", null, true);
                    return;
                }
                KyzWheel.App.Tools.ScreenTools.CopyText(text);
                Notify("cap", "", "Texto copiado", text, "#F472B6", "\uE8E9", null, true);
                return;
            }
            var (x, y) = User32.CursorPosition();
            _wheel.ShowFiles(x, y, [path], [FileRootPage([path])], Stopwatch.StartNew());
        }
        catch (Exception e)
        {
            Log.Error(e);
            Notify("cap", "", "Captura", "No se pudo: " + e.Message, "#F472B6", "\uE722", null, true);
        }
    }

    private async Task FullShotAsync()
    {
        try
        {
            await Task.Delay(350);
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            var before = DateTime.UtcNow;
            KyzWheel.Platform.Native.Keys.Chord(0x5B, 0x2C);
            string? path = null;
            for (var i = 0; i < 30 && path is null; i++)
            {
                await Task.Delay(200);
                if (Directory.Exists(dir)) path = new DirectoryInfo(dir).EnumerateFiles("*.png").Where(f => f.CreationTimeUtc >= before.AddSeconds(-1)).OrderByDescending(f => f.CreationTimeUtc).FirstOrDefault()?.FullName;
            }
            if (path is null) return;
            var (x, y) = User32.CursorPosition();
            _wheel.ShowFiles(x, y, [path], [FileRootPage([path])], Stopwatch.StartNew());
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    private void OpenFromToast(string kind, string id)
    {
        try
        {
            if (kind == "im")
            {
                OpenChats(KyzWheel.Core.Messaging.Im.Split(id).Svc);
                return;
            }
            if (kind == "wa")
            {
                var chat = _waChats.FirstOrDefault(c => c.Jid == id) ?? new KyzWheel.Core.WhatsApp.WaChat(id, id.Split('@')[0], "", DateTimeOffset.Now, 0, id.EndsWith("@g.us"));
                ShowWhatsAppPanel(WaSlot, [], chat);
            }
            else if (kind == "mail")
            {
                var parts = id.Split('|');
                var m = _mail.Inbox.Top(500).FirstOrDefault(x => x.Account == parts[0] && x.Uid.ToString() == parts.ElementAtOrDefault(1));
                if (m is not null) OpenMail(m);
                else ShowMailFeed();
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    private async Task ReplyFromToastAsync(string kind, string id, string text)
    {
        if (kind != "wa") return;
        try
        {
            await _wa.SendTextAsync(id, text);
        }
        catch (Exception e) when (e is WaException or TimeoutException or InvalidOperationException)
        {
            _toasts.Info("No se envió", e.Message);
        }
    }

    private void OnMailArrived(MailSummary m)
    {
        if (_config.Slots.Any(s => s.Type is SlotType.Mail or SlotType.Messages && s.Notify) && m.Unread)
            Notify("mail", $"{m.Account}|{m.Uid}", m.FromName.Length > 0 ? m.FromName : m.FromAddress, $"{m.Subject} — {m.Preview}", _mail.AccountOf(m.Account)?.ColorHex ?? "#EA4335", "\uE715", null);
    }

    private void ShowMailPanel(Slot s)
    {
        var (x, y) = WheelCenter();
        _wheel.HideAnimated();
        _panel ??= CreatePanel();
        var compose = Ui.IconButton("\uE70F", "Redactar", () => OpenCompose([], null));
        var refresh = Ui.IconButton("\uE72C", "Actualizar", () => { foreach (var a in _mail.Accounts) _mail.Restart(a.Email); });
        var settings = Ui.IconButton("\uE713", "Cuentas", OpenSettings);
        var sub = _mail.Accounts.Count switch
        {
            0 => "Sin cuentas",
            1 => _mail.Accounts[0].Email,
            var n => $"{n} cuentas · {_mail.Inbox.UnreadCount} sin leer",
        };
        _panel.Show(s.Glyph, s.ColorHex, s.Name, sub, new MailPanel(_mail, OpenMail, ConnectGmail), [compose, refresh, settings], x, y);
    }

    private void ShowClipboardPanel(Slot s)
    {
        var (x, y) = WheelCenter();
        _wheel.HideAnimated();
        _panel ??= CreatePanel();
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
        if (!_dockedPanel) _panel?.HidePanel();
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

    private bool _connecting;

    private void ConnectGmail()
    {
        if (!GoogleOAuth.Configured)
        {
            OpenAddAccount();
            return;
        }
        if (_connecting) return;
        _connecting = true;
        _panel?.HidePanel();
        _ = ConnectGmailAsync();
    }

    private async Task ConnectGmailAsync()
    {
        Notify("gmail", "", "Elige tu cuenta de Google", "Se abrió tu navegador. Si dice «app no verificada»: Configuración avanzada › Ir a kyzWheel.", "#EA4335", "\uE715", null, true);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var r = await GoogleOAuth.SignInAsync(cts.Token);
            _mail.Secrets.Set(ProviderPresets.SecretKey(r.Email), r.RefreshToken);
            var existing = _config.MailAccounts ?? [];
            if (existing.Any(a => a.Email.Equals(r.Email, StringComparison.OrdinalIgnoreCase)))
            {
                _mail.Restart(r.Email);
                Notify("gmail", "", "Gmail ya estaba conectado", r.Email + " · acceso renovado", "#EA4335", "\uE715", null, true);
                return;
            }
            var account = ProviderPresets.For(r.Email) with { DisplayName = r.Name, Auth = MailAuth.Google };
            ApplyConfig(_config with { MailAccounts = [.. existing, account] });
            Notify("gmail", "", "Gmail conectado", r.Email + " · toca para ver tus correos", "#EA4335", "\uE715", null, true);
        }
        catch (Exception e) when (e is MailKit.Security.AuthenticationException or OperationCanceledException or HttpRequestException or InvalidOperationException or System.Net.HttpListenerException)
        {
            Notify("gmail", "", "No se conectó Gmail", e is OperationCanceledException ? "Se acabó el tiempo. Inténtalo otra vez." : e.Message, "#EA4335", "\uE715", null, true);
        }
        finally
        {
            _connecting = false;
        }
    }

    private async Task CheckUpdatesAsync()
    {
        var v = await Task.Run(Updater.CheckAsync);
        if (v is null) return;
        _ui.TryEnqueue(() => (_banner ??= new BannerWindow()).Show(new Banner("Nueva versión " + v, "Toca para instalarla ahora. Si no, se instala sola la próxima vez que inicies.", "#34D399", "\uE896", Updater.InstallNow)));
    }

    private void Welcome()
    {
        if (_config.Welcomed) return;
        _config = _config with { Welcomed = true };
        _store.Save(_config);
        var hk = _config.GlobalHotkey.Replace("+", " + ");
        var t = _ui.CreateTimer();
        t.Interval = TimeSpan.FromSeconds(3);
        t.IsRepeating = false;
        t.Tick += (_, _) =>
        {
            (_banner ??= new BannerWindow()).Show(new Banner("kyzWheel está listo", $"Atajo: {hk}. Aprieta y suelta para abrir la rueda.", "#34D399", "\uE8F2", OpenWheelAtCursor));
            if ((_config.MailAccounts ?? []).Count > 0) return;
            var t2 = _ui.CreateTimer();
            t2.Interval = TimeSpan.FromSeconds(8);
            t2.IsRepeating = false;
            t2.Tick += (_, _) => _banner!.Show(new Banner("Conecta tu Gmail", "Toca aquí y elige tu cuenta de Google.", "#EA4335", "\uE715", ConnectGmail));
            t2.Start();
        };
        t.Start();
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
        var items = _config.Slots.Where(s => s.Type is not (SlotType.Clipboard or SlotType.OpenUrl or SlotType.Capture or SlotType.Projects or SlotType.Music)).Select(SlotItem).ToList();
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
        var targets = _config.Slots.Where(s => s.Type is SlotType.WhatsApp or SlotType.Mail or SlotType.SendToFolder or SlotType.LaunchApp or SlotType.Phone).Select(SlotItem).ToList();
        if (_config.Slots.Any(s => s.Type == SlotType.Messages))
        {
            targets.Insert(0, SlotItem(WaSlot));
            targets.Insert(1, SlotItem(MailSlot));
        }
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
            _waContacts = await _wa.SearchAsync("", 5000);
        }
        catch (Exception e) when (e is WaException or TimeoutException or InvalidOperationException)
        {
        }
    }

    private IReadOnlyList<WheelItem> PhoneItems(Slot s) =>
    [
                    new("phone:recv", "Recibidos", "\uE896", s.ColorHex, 0, "phone:recv"),
                    new("phone:guide", "Vincular", "\uE71B", s.ColorHex, 0, "phone:guide"),
                    new("phone:clip", "Portapapeles", "\uE77F", s.ColorHex, 0, "phone:clip"),
                    new("phone:file", "Archivo", "\uE8E5", s.ColorHex, 0, "phone:file"),
                    new("phone:shot", "Última captura", "\uE722", s.ColorHex, 0, "phone:shot"),
                    new("phone:ring", "Sonar", "\uEA8F", s.ColorHex, 0, "phone:ring"),
                    new("phone:ping", "Avisar", "\uE7E7", s.ColorHex, 0, "phone:ping"),
                ];

    public static readonly SlotType[] CustomizableSubs = [SlotType.Phone, SlotType.Capture, SlotType.Music, SlotType.Messages];

    private WheelPage? Customize(WheelPage? page, Slot s)
    {
        if (page is null || !CustomizableSubs.Contains(s.Type)) return page;
        var hidden = _config.SubHidden.TryGetValue(s.Id, out var h) ? h.ToHashSet() : [];
        var order = _config.SubOrder.TryGetValue(s.Id, out var o) ? o.ToList() : [];
        var items = page.Items.Where(i => !hidden.Contains(i.Id)).Select((i, n) => (i, n)).OrderBy(x => order.IndexOf(x.i.Id) is var k && k >= 0 ? k : 1000 + x.n).Select(x => x.i).ToList();
        return page with { Items = items };
    }

    public IReadOnlyList<(string Id, string Label, string Glyph)> SubCatalog(Slot s)
    {
        if (s.Id == ChatsPseudoSlot.Id)
            return [("chats:all", "Todos", "\uE8F2"), .. KyzWheel.Core.Messaging.Im.All.Select(sv => ("chats:" + sv.Id, sv.Name, sv.Glyph))];
        if (!CustomizableSubs.Contains(s.Type)) return [];
        var raw = s.Type == SlotType.Phone ? PhoneItems(s) : SubPageRaw(new WheelItem("slot:" + s.Id, s.Name, s.Glyph, s.ColorHex, 0, s), WheelMode.Root)?.Items ?? [];
        return raw.Select(i => (i.Id, i.Label, i.Glyph)).ToList();
    }

    private WheelPage? ExpandItem(WheelItem item, WheelMode mode)
    {
        var page = SubPageRaw(item, mode);
        return item.Tag is Slot s && mode == WheelMode.Root ? Customize(page, s) : page;
    }

    private WheelPage? SubPageRaw(WheelItem item, WheelMode mode)
    {
        if (item.Tag is "phone:clip")
        {
            var clips = _clips.History.Items;
            if (clips.Count == 0) return null;
            var phoneColor = FirstSlot(SlotType.Phone)?.ColorHex ?? "#60A5FA";
            var list = new List<WheelItem> { new("phone:clipnow", "Lo copiado ahora", "\uE77F", phoneColor, 0, "phone:clipnow") };
            list.AddRange(clips.Select(c => new WheelItem("pclip:" + c.Id, Short(c.Preview), c.Kind switch
            {
                KyzWheel.Core.Clipboard.ClipKind.Image => "\uEB9F",
                KyzWheel.Core.Clipboard.ClipKind.Files => "\uE8B7",
                _ => "\uE8C8",
            }, phoneColor, 0, new PhoneClip(c))));
            return new WheelPage("Al celular", list);
        }
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
                {
                    var waItems = _waChats.Select(c => new WheelItem("wa:" + c.Jid, Short(c.Name), c.IsGroup ? "\uE716" : "\uE77B", s.ColorHex, c.Unread, c)).ToList();
                    if (_wheel.Files.Count > 0)
                    {
                        var known = waItems.Select(i => i.Id).ToHashSet();
                        waItems.AddRange(_waContacts.Where(c => known.Add("wa:" + c.Jid)).Select(c => new WheelItem("wa:" + c.Jid, Short(c.Name), c.Jid.EndsWith("@g.us") ? "\uE716" : "\uE77B", s.ColorHex, 0, new KyzWheel.Core.WhatsApp.WaChat(c.Jid, c.Name, "", DateTimeOffset.MinValue, 0, c.Jid.EndsWith("@g.us")))));
                    }
                    return new WheelPage(s.Name, waItems, s);
                }
            case SlotType.Mail when mode == WheelMode.Root && _mail.Accounts.Count > 0:
                var mails = _mail.Inbox.Top(24);
                if (mails.Count == 0) return null;
                var items = new List<WheelItem> { new("mail:compose", "Redactar", "\uE70F", s.ColorHex, 0, "compose") };
                items.AddRange(mails.Select(m => new WheelItem("mail:" + m.MessageId, Short(m.FromName.Length > 0 ? m.FromName : m.FromAddress), m.HasAttachments ? "\uE723" : "\uE715", s.ColorHex, m.Unread ? 1 : 0, m)));
                return new WheelPage(s.Name, items, s);
            case SlotType.Messages when mode == WheelMode.Root:
                return new WheelPage(s.Name,
                [
                    new("msg:mail", _mail.Accounts.Count == 0 ? "Conectar Gmail" : "Gmail", "\uE715", "#EA4335", _mail.Inbox.UnreadCount, "msg:mail"),
                    new("msg:chats", "Mensajes", "\uE8F2", "#34D399", _wa.Status.Unread, "msg:chats", ChatItems()),
                ], s);
            case SlotType.Messages:
                return new WheelPage(s.Name, [SlotItem(WaSlot), SlotItem(MailSlot)], s);
            case SlotType.Capture:
                return new WheelPage(s.Name,
                [
                    new("cap:snip", "Recortar", "\uE7A8", s.ColorHex, 0, "cap:snip"),
                    new("cap:ocr", "Copiar texto", "\uE8E9", s.ColorHex, 0, "cap:ocr"),
                    new("cap:full", "Pantalla", "\uE7F4", s.ColorHex, 0, "cap:full"),
                    new("cap:clip", "Grabar clip", "\uE714", s.ColorHex, 0, "cap:clip"),
                ], s);
            case SlotType.Projects:
                var projects = KyzWheel.Core.Projects.ProjectScanner.Scan();
                if (projects.Count == 0) return null;
                return new WheelPage(s.Name, projects.Select(pr => new WheelItem("proj:" + pr.Name, Short(pr.Name), "\uE8B7", s.ColorHex, 0, pr, ProjectActions(pr, s.ColorHex))).ToList(), s);
            case SlotType.Music:
                _ = _media.RefreshAsync();
                return new WheelPage(_media.NowPlaying is { } np ? Short(np, 28) : s.Name,
                [
                    new("music:prev", "Anterior", "\uE892", s.ColorHex, 0, "music:prev"),
                    new("music:toggle", _media.Playing ? "Pausa" : "Reproducir", _media.Playing ? "\uE769" : "\uE768", s.ColorHex, 0, "music:toggle"),
                    new("music:next", "Siguiente", "\uE893", s.ColorHex, 0, "music:next"),
                    new("music:volup", "Subir volumen", "\uE995", s.ColorHex, 0, "music:volup"),
                    new("music:voldown", "Bajar volumen", "\uE993", s.ColorHex, 0, "music:voldown"),
                    new("music:mute", "Silenciar", "\uE74F", s.ColorHex, 0, "music:mute"),
                ], s);
            case SlotType.Phone when mode == WheelMode.Root && _phone.Installed:
                return new WheelPage(s.Name, PhoneItems(s), s);
            case SlotType.Clipboard when mode == WheelMode.Root:
                var clips = _clips.History.Items;
                if (clips.Count == 0) return null;
                var clipItems = new List<WheelItem> { new("clip:translate", "Traducir", "\uE8C1", s.ColorHex, 0, "clip:translate") };
                clipItems.AddRange(clips.Select(c => new WheelItem("clip:" + c.Id, Short(c.Preview), c.Kind switch
                {
                    KyzWheel.Core.Clipboard.ClipKind.Image => "\uEB9F",
                    KyzWheel.Core.Clipboard.ClipKind.Files => "\uE8B7",
                    _ => c.Pinned ? "\uE718" : "\uE8C8",
                }, s.ColorHex, 0, c)));
                return new WheelPage(s.Name, clipItems, s);
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
        _panel ??= CreatePanel();
        var title = parent?.Label ?? "Más opciones";
        _panel.Show(parent?.Glyph ?? "\uE712", parent?.ColorHex ?? "#FFFFFF", title, $"{rest.Count} opciones", new ItemListPanel(rest, it =>
        {
            _panel.HidePanel();
            if (mode == WheelMode.Files && files.Count > 0) OnFilesDropped(it, files);
            else OnItemInvoked(it);
        }), [], x, y);
    }

    private Slot? FirstSlot(SlotType t) => _config.Slots.FirstOrDefault(x => x.Type == t);

    private Slot WaSlot => FirstSlot(SlotType.WhatsApp) ?? new Slot("whatsapp", "WhatsApp", "\uE8BD", "#25D366", SlotType.WhatsApp, null, null, true);

    private Slot MailSlot => FirstSlot(SlotType.Mail) ?? new Slot("mail", "Gmail", "\uE715", "#EA4335", SlotType.Mail, null, null, true);

    private sealed record PhoneClip(KyzWheel.Core.Clipboard.ClipItem Item);

    private void SendClipToPhone(KyzWheel.Core.Clipboard.ClipItem c)
    {
        switch (c.Kind)
        {
            case KyzWheel.Core.Clipboard.ClipKind.Text:
                _ = PhoneAsync("Texto en el portapapeles del celular", () => _phone.SendTextAsync(c.Text!));
                break;
            case KyzWheel.Core.Clipboard.ClipKind.Image when c.ImagePath is { } img && File.Exists(img):
                _ = PhoneAsync("Imagen enviada a Descargas", () => _phone.SendFilesAsync([img]));
                break;
            case KyzWheel.Core.Clipboard.ClipKind.Files:
                var files = c.Files!.Where(File.Exists).ToList();
                if (files.Count > 0) _ = PhoneAsync(files.Count == 1 ? "Archivo enviado" : $"{files.Count} archivos enviados", () => _phone.SendFilesAsync(files));
                break;
        }
    }

    private bool _dockedPanel;

    private void DockPanel(string glyph, string color, string title, string sub, UIElement content, IReadOnlyList<Microsoft.UI.Xaml.Controls.Button> actions)
    {
        var (x, y) = WheelCenter();
        _panel ??= CreatePanel();
        _wheel.ReleaseCursor();
        _wheel.Docked = true;
        _panel.Pinned = true;
        _dockedPanel = true;
        _panel.Show(glyph, color, title, sub, content, actions, x, y, _wheel.RadiusPx);
    }

    private SidePanelWindow CreatePanel()
    {
        var p = new SidePanelWindow();
        p.PanelHidden += () =>
        {
            if (!_dockedPanel) return;
            _dockedPanel = false;
            _feed = null;
            _feedKind = null;
            p.Pinned = false;
            if (_wheel.IsShown) _wheel.HideAnimated();
        };
        p.LostFocus += CheckDockFocus;
        return p;
    }

    private void CheckDockFocus()
    {
        if (!_dockedPanel) return;
        var t = _ui.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(160);
        t.IsRepeating = false;
        t.Tick += (_, _) =>
        {
            var fg = KyzWheel.Platform.Shell.ShellSelection.Foreground();
            if (!_dockedPanel || KyzWheel.Platform.Native.User32.ProcessOf(fg) == Environment.ProcessId) return;
            _panel?.HidePanel();
        };
        t.Start();
    }

    private KyzWheel.App.Messages.MessageFeedPanel? _feed;
    private string? _feedKind;
    private bool _feedRefreshQueued;

    private void QueueFeedRefresh(string kind)
    {
        if (!_dockedPanel || _feed is null || _feedKind != kind || _feedRefreshQueued) return;
        _feedRefreshQueued = true;
        var t = _ui.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(250);
        t.IsRepeating = false;
        t.Tick += async (_, _) =>
        {
            _feedRefreshQueued = false;
            if (!_dockedPanel || _feed is null || _feedKind != kind) return;
            _feed.Update(kind == "chats" ? await ChatEntriesAsync() : MailEntries());
        };
        t.Start();
    }

    public static readonly Slot ChatsPseudoSlot = new("messages-chats", "Mensajes › chats", "\uE8F2", "#34D399", SlotType.Messages, null, null, false);

    private IReadOnlyList<WheelItem> ChatItems()
    {
        var items = new List<WheelItem> { new("chats:all", "Todos", "\uE8F2", "#34D399", _wa.Status.Unread, "chats:all") };
        items.AddRange(KyzWheel.Core.Messaging.Im.All.Select(sv => new WheelItem("chats:" + sv.Id, sv.Name, sv.Glyph, sv.ColorHex, sv.Id == "whatsapp" ? _wa.Status.Unread : 0, "chats:" + sv.Id)));
        return Customize(new WheelPage("Mensajes", items), ChatsPseudoSlot)!.Items;
    }

    private bool ServiceOpen(string svc) => svc == "whatsapp" ? _wa.Status.State == "open" : _wa.Im.TryGetValue(svc, out var st) && st.Open;

    private void OpenChats(string? svc)
    {
        if (svc is not null && !ServiceOpen(svc))
        {
            if (svc == "whatsapp") ShowWhatsAppPanel(WaSlot, []);
            else ShowImLogin(svc);
            return;
        }
        _ = ShowChatsFeedAsync(svc);
    }

    private void ShowImLogin(string svc)
    {
        var sv = KyzWheel.Core.Messaging.Im.Find(svc);
        DockPanel(sv.Glyph, sv.ColorHex, sv.Name, "Sin conectar", new KyzWheel.App.Messages.ImLoginPanel(_wa, sv, () => _ = ShowChatsFeedAsync(svc)), []);
    }

    private async Task ShowChatsFeedAsync(string? filter)
    {
        try { await _wa.RefreshImAsync(); } catch (Exception) { }
        var entries = await ChatEntriesAsync();
        var waNote = Ui.Label("", 11, Ui.Tertiary);
        _feed = new KyzWheel.App.Messages.MessageFeedPanel(entries, _toasts, "Cuando lleguen mensajes aparecen aquí.", e => e.AvatarKey is { } jid ? _wa.AvatarAsync(jid) : Task.FromResult<string?>(null), f => KyzWheel.App.WhatsApp.WhatsAppPanel.MediaView(_wa, waNote, f));
        var services = KyzWheel.Core.Messaging.Im.All.Where(sv => ServiceOpen(sv.Id)).ToList();
        _feed.SetFilters(services, filter);
        _feedKind = "chats";
        var sv0 = filter is null ? null : KyzWheel.Core.Messaging.Im.Find(filter);
        var connect = new List<Microsoft.UI.Xaml.Controls.Button>();
        foreach (var off in KyzWheel.Core.Messaging.Im.All.Where(x => !ServiceOpen(x.Id)))
        {
            var id = off.Id;
            connect.Add(Ui.IconButton(off.Glyph, "Conectar " + off.Name, () => OpenChats(id)));
        }
        DockPanel(sv0?.Glyph ?? "\uE8F2", sv0?.ColorHex ?? "#34D399", sv0?.Name ?? "Mensajes", "En vivo · responde, manda stickers o llama", _feed, connect);
    }

    private async Task<IReadOnlyList<KyzWheel.App.Messages.FeedEntry>> ChatEntriesAsync()
    {
        var all = new List<KyzWheel.App.Messages.FeedEntry>();
        if (_wa.Status.State == "open") all.AddRange(await WaEntriesAsync());
        IReadOnlyList<KyzWheel.Core.WhatsApp.WaFeedItem> im = [];
        try
        {
            if (_wa.Running) im = await _wa.ImRecentAsync(200);
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
        foreach (var g in KyzWheel.Core.WhatsApp.WaFeedGrouper.Group(im, 25))
        {
            var (svc, chatId) = KyzWheel.Core.Messaging.Im.Split(g.Jid);
            var sv = KyzWheel.Core.Messaging.Im.Find(svc);
            var lines = g.Lines.Select(l =>
            {
                var m = l.Item.Message;
                var who = m.FromMe ? "Tú" : g.IsGroup ? m.Sender : g.ChatName;
                return new KyzWheel.App.Messages.FeedLine(who, m.Text, m.Ts, l.Depth, l.ParentInGroup, null, null, null);
            }).ToList();
            var last = g.Lines.MaxBy(l => l.Item.Message.Ts)!.Item.Message;
            all.Add(new KyzWheel.App.Messages.FeedEntry(g.Jid, g.ChatName, sv.Name + (g.Lines.Count > 1 ? $" · {g.Lines.Count} mensajes" : ""), string.Join("\n", g.Lines.Select(l => l.Item.Message.Text)), g.Last, sv.ColorHex, false, null, null, null, null, lines, last.Id + g.Lines.Count)
            {
                Service = svc,
                Reply = text => _wa.ImSendAsync(svc, chatId, text),
                Stickers = svc == "instagram" ? null : () => _wa.StickersAsync(svc),
                SendSticker = svc == "instagram" ? null : st => _wa.SendStickerAsync(svc, chatId, st),
                Call = async () => OpenUrl(await _wa.CallUrlAsync(svc, chatId)),
            });
        }
        return all.OrderByDescending(e => e.When).ToList();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("No encontré la app para abrir " + url.Split(':')[0] + ": " + e.Message);
        }
    }

    private static string WaCallUrl(KyzWheel.Core.WhatsApp.WaChat c)
    {
        var digits = new string((c.Phone ?? (c.Jid.EndsWith("@s.whatsapp.net") ? c.Jid.Split('@')[0] : "")).Where(char.IsDigit).ToArray());
        return digits.Length > 6 ? "whatsapp://send?phone=" + digits : "whatsapp://";
    }

    private static (string? Id, string? Hash) BundledTelegram()
    {
        try
        {
            var f = Path.Combine(AppContext.BaseDirectory, "Assets", "telegram.json");
            if (!File.Exists(f)) return (Environment.GetEnvironmentVariable("KYZ_TG_API_ID"), Environment.GetEnvironmentVariable("KYZ_TG_API_HASH"));
            using var d = System.Text.Json.JsonDocument.Parse(File.ReadAllText(f));
            return (d.RootElement.GetProperty("apiId").ToString(), d.RootElement.GetProperty("apiHash").GetString());
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    private async Task<IReadOnlyList<KyzWheel.App.Messages.FeedEntry>> WaEntriesAsync()
    {
        IReadOnlyList<KyzWheel.Core.WhatsApp.WaFeedItem> items;
        try
        {
            items = await _wa.RecentMessagesAsync(120);
        }
        catch (Exception e)
        {
            Log.Error(e);
            return [];
        }
        var chats = _waChats.ToDictionary(c => c.Jid);
        return KyzWheel.Core.WhatsApp.WaFeedGrouper.Group(items).Select(g =>
        {
            var lines = g.Lines.Select(l =>
            {
                var m = l.Item.Message;
                var who = m.FromMe ? "Tú" : g.IsGroup ? (string.IsNullOrEmpty(m.Sender) ? null : m.Sender) : g.ChatName;
                var text = m.File is null ? m.Text : m.File.Kind is "sticker" or "audio" ? "" : m.Caption;
                return new KyzWheel.App.Messages.FeedLine(who, text, m.Ts, l.Depth, l.ParentInGroup, m.QuoteSender, m.QuoteText, m.File);
            }).ToList();
            var last = g.Lines.MaxBy(l => l.Item.Message.Ts)!.Item.Message;
            var chat = chats.TryGetValue(g.Jid, out var c) ? c : new KyzWheel.Core.WhatsApp.WaChat(g.Jid, g.ChatName, "", g.Last, 0, g.IsGroup);
            return new KyzWheel.App.Messages.FeedEntry(
                g.Jid,
                g.ChatName,
                "WhatsApp" + (g.Lines.Count > 1 ? $" · {g.Lines.Count} mensajes" : ""),
                string.Join("\n", g.Lines.Select(l => l.Item.Message.Text)),
                g.Last,
                "#25D366",
                chat.Unread > 0,
                g.Jid,
                null,
                () => ShowWhatsAppPanel(WaSlot, [], chat),
                null,
                lines,
                last.Id + g.Lines.Count)
            {
                Service = "whatsapp",
                Reply = text => _wa.SendTextAsync(chat.Jid, text),
                Stickers = () => _wa.StickersAsync("whatsapp"),
                SendSticker = st => _wa.SendStickerAsync("whatsapp", chat.Jid, st),
                Call = () => { OpenUrl(WaCallUrl(chat)); return Task.CompletedTask; },
            };
        }).ToList();
    }

    private IReadOnlyList<KyzWheel.App.Messages.FeedEntry> MailEntries()
    {
        var multi = _mail.Accounts.Count > 1;
        return _mail.Inbox.Top(40).DistinctBy(m => (m.Account, string.IsNullOrEmpty(m.MessageId) ? m.Uid.ToString() : m.MessageId)).Select(m => new KyzWheel.App.Messages.FeedEntry(
            m.Account + "|" + m.Uid,
            m.FromName.Length > 0 ? m.FromName : m.FromAddress,
            m.Subject,
            m.Preview,
            m.Date,
            _mail.AccountOf(m.Account)?.ColorHex ?? "#EA4335",
            m.Unread,
            null,
            null,
            () => OpenMail(m),
            multi ? m.Account : null)).ToList();
    }

    private void ShowMailFeed()
    {
        if (_mail.Accounts.Count == 0)
        {
            _wheel.HideAnimated();
            ConnectGmail();
            return;
        }
        _feed = new KyzWheel.App.Messages.MessageFeedPanel(MailEntries(), _toasts, "No hay correos recientes.");
        _feedKind = "mail";
        var compose = Ui.IconButton("\uE70F", "Redactar", () => OpenCompose([], null));
        var refresh = Ui.IconButton("\uE72C", "Actualizar", () => { foreach (var a in _mail.Accounts) _mail.Restart(a.Email); });
        var add = Ui.IconButton("\uE710", "Agregar otra cuenta de Google", ConnectGmail);
        DockPanel("\uE715", "#EA4335", "Gmail", $"{_mail.Inbox.UnreadCount} sin leer · en vivo", _feed, [add, refresh, compose]);
    }

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
            case "phone:clip":
            case "phone:clipnow":
                _wheel.HideAnimated();
                _ = PhoneAsync("Portapapeles enviado", () => _phone.SendClipboardAsync());
                break;
            case "phone:file":
                PickFilesForPhone();
                break;
            case "phone:shot":
                _wheel.HideAnimated();
                var shot = LatestScreenshot();
                if (shot is null) _toasts.Info("Celular", "No encontré capturas en Imágenes › Capturas de pantalla");
                else _ = PhoneAsync("Captura enviada", () => _phone.SendFilesAsync([shot]));
                break;
            case "phone:ring":
                _wheel.HideAnimated();
                _ = PhoneAsync("Sonando", () => _phone.RingAsync());
                break;
            case "phone:guide":
                ShowPhoneSetup(_config.Slots.FirstOrDefault(x => x.Type == SlotType.Phone) ?? new Slot("phone", "Celular", "\uE8EA", "#60A5FA", SlotType.Phone, null, null, false));
                break;
            case "phone:recv":
                {
                    var recv = RecentFiles(1);
                    var dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    _wheel.HideAnimated();
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", recv.Count > 0 && recv[0].StartsWith(dl, StringComparison.OrdinalIgnoreCase) ? $"/select,\"{recv[0]}\"" : $"\"{dl}\"") { UseShellExecute = true });
                    break;
                }
            case "cap:snip":
                _wheel.HideAnimated();
                _ = SnipAsync(false);
                break;
            case "cap:ocr":
                _wheel.HideAnimated();
                _ = SnipAsync(true);
                break;
            case "cap:full":
                _wheel.HideAnimated();
                _ = FullShotAsync();
                break;
            case "cap:clip":
                _wheel.HideAnimated();
                KyzWheel.Platform.Native.Keys.Chord(0x5B, 0x12, 0x47);
                break;
            case "music:prev":
                _ = MusicAsync(() => _media.PreviousAsync());
                break;
            case "music:toggle":
                _ = MusicAsync(() => _media.ToggleAsync());
                break;
            case "music:volup":
                KyzWheel.Platform.Native.Keys.Chord(0xAF);
                KyzWheel.Platform.Native.Keys.Chord(0xAF);
                break;
            case "music:voldown":
                KyzWheel.Platform.Native.Keys.Chord(0xAE);
                KyzWheel.Platform.Native.Keys.Chord(0xAE);
                break;
            case "music:mute":
                KyzWheel.Platform.Native.Keys.Chord(0xAD);
                break;
            case "music:next":
                _ = MusicAsync(() => _media.NextAsync());
                break;
            case "clip:translate":
                ShowTranslate(ClipText());
                break;
            case ProjectAction pa:
                _wheel.HideAnimated();
                RunProjectAction(pa);
                break;
            case "phone:ping":
                _wheel.HideAnimated();
                _ = PhoneAsync("Aviso enviado", () => _phone.PingAsync("Hola desde kyzWheel"));
                break;
            case RecentFile rf:
                var (rx, ry) = WheelCenter();
                ShowConverterWheel(rx, ry, [rf.Path]);
                break;
            case "compose":
                _wheel.HideAnimated();
                OpenCompose([], null);
                break;
            case KyzWheel.Core.WhatsApp.WaChat chat:
                ShowWhatsAppPanel(WaSlot, [], chat);
                break;
            case "msg:wa":
                OpenChats("whatsapp");
                break;
            case "chats:all":
                OpenChats(null);
                break;
            case string chatsTag when chatsTag.StartsWith("chats:"):
                OpenChats(chatsTag[6..]);
                break;
            case "msg:mail":
                ShowMailFeed();
                break;
            case "msg:compose":
                _wheel.HideAnimated();
                OpenCompose([], null);
                break;
            case PhoneClip pc:
                _wheel.HideAnimated();
                SendClipToPhone(pc.Item);
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
                case SlotType.Hermes:
                    DockPanel("\uE99A", s.ColorHex, "Hermes", _wheel.Files.Count > 0 ? "Pregunta sobre el archivo" : "Pregunta lo que sea", new KyzWheel.App.Tools.HermesPanel(_wheel.Files.ToList()), []);
                    break;
                case SlotType.Mail:
                    ShowMailPanel(s);
                    break;
                case SlotType.Clipboard:
                    ShowClipboardPanel(s);
                    break;
                case SlotType.Phone:
                    ShowPhoneSetup(s);
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

    private async Task PhoneAsync(string done, Func<Task<string>> action)
    {
        try
        {
            var name = await action();
            _toasts.Info(done, name);
        }
        catch (KyzWheel.App.Phone.PhoneException e)
        {
            _toasts.Info("Celular", e.Message);
        }
    }

    private static string? LatestScreenshot()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        if (!Directory.Exists(dir)) return null;
        return new DirectoryInfo(dir).EnumerateFiles().Where(f => f.Extension is ".png" or ".jpg" or ".jpeg").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
    }

    private async void PickFilesForPhone()
    {
        _wheel.HideAnimated();
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _wheel.GetWindowHandleSafe());
        var picked = await picker.PickMultipleFilesAsync();
        var files = picked?.Select(p => p.Path).Where(p => !string.IsNullOrEmpty(p)).ToList() ?? [];
        if (files.Count > 0) await PhoneAsync(files.Count == 1 ? "Archivo enviado" : $"{files.Count} archivos enviados", () => _phone.SendFilesAsync(files));
    }

    private void ShowPhoneSetup(Slot s)
    {
        var (x, y) = WheelCenter();
        _wheel.HideAnimated();
        _panel ??= CreatePanel();
        _panel.Show(s.Glyph, s.ColorHex, "Vincular celular", "Guía en 4 pasos", new KyzWheel.App.Phone.PhoneGuide(_phone, () => _ = PhoneAsync("Celular conectado", () => _phone.PingAsync("kyzWheel conectado"))), [], x, y);
    }

    private void ShowPanel(Slot s, string text)
    {
        var (x, y) = WheelCenter();
        _wheel.HideAnimated();
        _panel ??= CreatePanel();
        _panel.Show(s.Glyph, s.ColorHex, s.Name, "Próximamente", MailPanel.Empty(s.Glyph, "En camino", text, null, null), [], x, y);
    }

    private async Task SendToChatAsync(KyzWheel.Core.WhatsApp.WaChat chat, IReadOnlyList<string> files)
    {
        var total = files.Sum(f => File.Exists(f) ? new FileInfo(f).Length : 0);
        if (total > 8 * 1024 * 1024) Notify("send", "", "Enviando a " + chat.Name, (files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} archivos") + $" · {total / 1048576.0:0} MB, puede tardar un poco", "#25D366", "\uE724", null, true);
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
        if (ok > 0) Notify("send", "", "Enviado a " + chat.Name, ok == 1 ? Path.GetFileName(files[0]) : $"{ok} archivos", "#25D366", "\uE73E", () => _wa.AvatarAsync(chat.Jid), true);
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
                case SlotType.Phone:
                    _ = PhoneAsync(files.Count == 1 ? $"Enviado: {Path.GetFileName(files[0])}" : $"{files.Count} archivos enviados", () => _phone.SendFilesAsync(files));
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
            _settings.SetSubCatalog(SubCatalog, [ChatsPseudoSlot]);
            _settings.TestNotification += (mon, pos) =>
            {
                var (om, op) = (BannerWindow.Monitor, BannerWindow.Position);
                BannerWindow.Monitor = mon;
                BannerWindow.Position = pos;
                _banner?.Hide();
                (_banner ??= new BannerWindow()).Show(new Banner("Aviso de prueba", "Aquí saldrán tus avisos de WhatsApp y correo", "#25D366", "\uE8BD", null, null, _ => Task.CompletedTask));
                BannerWindow.Monitor = om;
                BannerWindow.Position = op;
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
