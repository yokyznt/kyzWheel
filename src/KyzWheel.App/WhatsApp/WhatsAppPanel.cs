using System.Diagnostics;
using KyzWheel.App.Mail;
using KyzWheel.App.Shell;
using KyzWheel.Core.Files;
using KyzWheel.Core.WhatsApp;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace KyzWheel.App.WhatsApp;

public sealed class WhatsAppPanel : Grid
{
    private const string Green = "#25D366";
    private readonly WhatsAppBridge _wa;
    private readonly Toasts _toasts;
    private readonly IReadOnlyList<string> _pendingFiles;
    private readonly ContentControl _view = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock _note;
    private string _tab = "chats";
    private string _query = "";
    private WaChat? _open;
    private int _version;

    public WhatsAppPanel(WhatsAppBridge wa, Toasts toasts, IReadOnlyList<string> pendingFiles, WaChat? openChat = null)
    {
        _wa = wa;
        _open = openChat;
        if (openChat is not null) _ = wa.MarkReadAsync(openChat.Jid).ContinueWith(_ => { }, TaskScheduler.Default);
        _toasts = toasts;
        _pendingFiles = pendingFiles;
        _note = Ui.Label("", 11.5, Ui.Tertiary);
        RowSpacing = 6;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(_view);
        SetRow(_note, 1);
        Children.Add(_note);
        wa.StatusChanged += OnChanged;
        wa.ChatsChanged += OnChanged;
        Unloaded += (_, _) =>
        {
            wa.StatusChanged -= OnChanged;
            wa.ChatsChanged -= OnChanged;
        };
        if (pendingFiles.Count > 0) _note.Text = pendingFiles.Count == 1 ? $"Elige a quién enviar {Path.GetFileName(pendingFiles[0])}" : $"Elige a quién enviar {pendingFiles.Count} archivos";
        _ = RenderAsync();
    }

    private void OnChanged() => _ = RenderAsync();

    private async Task RenderAsync()
    {
        var v = ++_version;
        var st = _wa.Status.State;
        if (st == "missing")
        {
            _view.Content = MailPanel.Empty("\uE8BD", "WhatsApp no está incluido", "Esta instalación no trae el conector de WhatsApp.", null, null);
            return;
        }
        if (st is "qr" or "logged_out" || (st == "connecting" && _wa.QrDataUrl is not null))
        {
            _view.Content = Pairing();
            return;
        }
        if (st != "open")
        {
            _view.Content = MailPanel.Empty("\uE895", st == "reconnecting" ? "Reconectando…" : "Conectando…", "Abriendo tu sesión de WhatsApp", null, null);
            return;
        }
        try
        {
            if (_open is not null)
            {
                var msgs = await _wa.MessagesAsync(_open.Jid, 30);
                if (v == _version) _view.Content = ChatView(_open, msgs);
                return;
            }
            UIElement list;
            if (_query.Length > 0)
            {
                var found = await _wa.SearchAsync(_query);
                list = Rows(found.Select(c => ChatRow(new WaChat(c.Jid, c.Name, c.Jid.Split('@')[0], DateTimeOffset.MinValue, 0, c.Jid.EndsWith("@g.us")))).ToList(), "Sin resultados");
            }
            else if (_tab == "files")
            {
                var files = await _wa.RecentFilesAsync(8);
                list = Rows(files.Select(FileRow).ToList(), "Aún no hay archivos recientes");
            }
            else
            {
                var chats = await _wa.RecentChatsAsync(8);
                list = Rows(chats.Select(ChatRow).ToList(), "Sincronizando chats…");
            }
            if (v == _version) _view.Content = ListView(list);
        }
        catch (WaException e)
        {
            if (v == _version) _view.Content = MailPanel.Empty("\uEB55", "No se pudo cargar", e.Message, "Reintentar", () => _ = RenderAsync());
        }
    }

    private UIElement Pairing()
    {
        var s = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        var frame = new Border { Width = 240, Height = 240, CornerRadius = new CornerRadius(16), Background = Ui.Brush(Colors.White), Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Center };
        if (_wa.QrDataUrl is { } url && url.Contains(','))
        {
            var bmp = new BitmapImage();
            var bytes = System.Convert.FromBase64String(url[(url.IndexOf(',') + 1)..]);
            var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            _ = Task.Run(async () =>
            {
                await ms.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(bytes));
                ms.Seek(0);
                DispatcherQueue.TryEnqueue(() => _ = bmp.SetSourceAsync(ms));
            });
            frame.Child = new Image { Source = bmp, Stretch = Stretch.Uniform };
        }
        else
        {
            frame.Child = new ProgressRing { IsActive = true, Width = 32, Height = 32 };
        }
        s.Children.Add(frame);
        var t = Ui.Label("Vincula tu WhatsApp", 15, bold: true);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        s.Children.Add(t);
        var steps = Ui.Label("En tu teléfono: WhatsApp › Ajustes › Dispositivos vinculados › Vincular un dispositivo, y escanea este código.", 12.5, Ui.Secondary, maxLines: 4);
        steps.TextAlignment = TextAlignment.Center;
        steps.MaxWidth = 300;
        s.Children.Add(steps);
        var warn = Ui.Pill("Conexión no oficial · riesgo bajo de restricción", Ui.Parse("#FACC15"));
        warn.HorizontalAlignment = HorizontalAlignment.Center;
        s.Children.Add(warn);
        if (_wa.Status.State == "logged_out")
        {
            var b = Ui.Primary("Generar código", () => { _wa.Start(); _ = _wa.CallAsync("login"); }, "\uE72C");
            b.HorizontalAlignment = HorizontalAlignment.Center;
            s.Children.Add(b);
        }
        return s;
    }

    private UIElement ListView(UIElement list)
    {
        var g = new Grid { RowSpacing = 10 };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var search = new TextBox { PlaceholderText = "Buscar contacto", Text = _query, CornerRadius = new CornerRadius(10) };
        search.TextChanged += async (_, _) =>
        {
            var q = search.Text.Trim();
            await Task.Delay(220);
            if (q != search.Text.Trim()) return;
            _query = q;
            await RenderAsync();
            search.Focus(FocusState.Programmatic);
            search.SelectionStart = search.Text.Length;
        };
        g.Children.Add(search);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        tabs.Children.Add(Tab("Chats", "chats"));
        tabs.Children.Add(Tab("Archivos", "files"));
        tabs.Visibility = _query.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        SetRow(tabs, 1);
        g.Children.Add(tabs);
        var scroll = new ScrollViewer { Content = list, Margin = new Thickness(-8, 0, -8, 0), Padding = new Thickness(8, 0, 8, 0) };
        SetRow(scroll, 2);
        g.Children.Add(scroll);
        return g;
    }

    private Button Tab(string text, string key)
    {
        var active = _tab == key;
        var b = new Button
        {
            Content = text,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 5, 14, 5),
            BorderThickness = new Thickness(1),
            BorderBrush = Ui.Brush(active ? Ui.Parse(Green, 0x66) : Ui.Hairline),
            Background = Ui.Brush(active ? Ui.Parse(Green, 0x22) : Colors.Transparent),
            Foreground = Ui.Brush(active ? Ui.Parse(Green) : Ui.Secondary),
        };
        b.Click += (_, _) =>
        {
            _tab = key;
            _ = RenderAsync();
        };
        return b;
    }

    private static StackPanel Rows(IReadOnlyList<UIElement> rows, string empty)
    {
        var s = new StackPanel { Spacing = 2 };
        if (rows.Count == 0) s.Children.Add(MailPanel.Empty("\uE8BD", empty, "", null, null));
        foreach (var r in rows) s.Children.Add(r);
        return s;
    }

    private UIElement ChatRow(WaChat c)
    {
        var g = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 9, 10, 9) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(Avatar(c.Name, c.IsGroup ? "#38BDF8" : Green, 38, c.Jid));
        var t = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Ui.Label(c.Name, 13.5, bold: c.Unread > 0));
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (c.LastTs > DateTimeOffset.MinValue) right.Children.Add(Ui.Label(Ui.When(c.LastTs), 11.5, c.Unread > 0 ? Ui.Parse(Green) : Ui.Tertiary));
        if (c.Unread > 0)
            right.Children.Add(new Border { MinWidth = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Ui.Brush(Ui.Parse(Green)), Padding = new Thickness(5, 0, 5, 0), Child = new TextBlock { Text = c.Unread > 99 ? "99+" : c.Unread.ToString(), FontSize = 10.5, Foreground = Ui.Brush(Colors.Black), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        SetColumn(right, 1);
        top.Children.Add(right);
        t.Children.Add(top);
        t.Children.Add(Ui.Label(c.LastText, 12, Ui.Tertiary));
        SetColumn(t, 1);
        g.Children.Add(t);
        var surface = new Border { CornerRadius = new CornerRadius(10), Child = g, Background = Ui.Brush(Colors.Transparent), AllowDrop = true };
        Ui.Pressable(surface, surface);
        surface.Tapped += (_, _) =>
        {
            if (_pendingFiles.Count > 0) _ = SendFilesAsync(c, _pendingFiles, surface);
            else Open(c);
        };
        surface.DragOver += (_, e) =>
        {
            e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None;
            e.DragUIOverride.Caption = "Enviar a " + c.Name;
            surface.Background = Ui.Brush(Ui.Parse(Green, 0x22));
        };
        surface.DragLeave += (_, _) => surface.Background = Ui.Brush(Colors.Transparent);
        surface.Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var files = (await e.DataView.GetStorageItemsAsync()).OfType<StorageFile>().Select(f => f.Path).ToList();
            await SendFilesAsync(c, files, surface);
        };
        return surface;
    }

    private async Task SendFilesAsync(WaChat c, IReadOnlyList<string> files, Border row)
    {
        _note.Text = $"Enviando a {c.Name}…";
        var ok = 0;
        foreach (var f in files)
        {
            try
            {
                await _wa.SendFileAsync(c.Jid, f);
                ok++;
            }
            catch (WaException e)
            {
                _note.Text = e.Message;
            }
        }
        row.Background = Ui.Brush(Colors.Transparent);
        if (ok > 0)
        {
            KyzWheel.App.Wheel.WheelAnimations.Success(row);
            _note.Text = ok == 1 ? $"Enviado a {c.Name}" : $"{ok} archivos enviados a {c.Name}";
            _toasts.Info("WhatsApp", _note.Text);
        }
    }

    private UIElement FileRow(WaFile f)
    {
        var g = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 9, 10, 9) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var glyph = f.Mime.StartsWith("image/") ? "\uEB9F" : f.Mime.StartsWith("video/") ? "\uE714" : f.Mime.StartsWith("audio/") ? "\uE8D6" : f.Mime.Contains("pdf") ? "\uEA90" : "\uE8A5";
        var chip = new Grid { Width = 38, Height = 38 };
        chip.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Ui.Brush(ColorHelper.FromArgb(0x18, 0xFF, 0xFF, 0xFF)) });
        var icon = Ui.Icon(glyph, 16);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        chip.Children.Add(icon);
        g.Children.Add(chip);
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(Ui.Label(f.FileName, 13));
        var meta = string.Join(" · ", new[] { f.ChatName, f.Size > 0 ? BatchSummary.Size(f.Size) : null, f.Ts is { } ts ? Ui.When(ts) : null }.Where(x => !string.IsNullOrEmpty(x)));
        t.Children.Add(Ui.Label(meta, 11.5, Ui.Tertiary));
        SetColumn(t, 1);
        g.Children.Add(t);
        var surface = new Border { CornerRadius = new CornerRadius(10), Child = g, Background = Ui.Brush(Colors.Transparent) };
        Ui.Pressable(surface, surface);
        surface.Tapped += async (_, _) =>
        {
            try
            {
                _note.Text = "Descargando…";
                var path = await _wa.DownloadAsync(f);
                _note.Text = "Guardado en Descargas\\kyzWheel";
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (WaException e)
            {
                _note.Text = e.Message;
            }
        };
        return surface;
    }

    private void Open(WaChat c)
    {
        _open = c;
        _ = _wa.MarkReadAsync(c.Jid).ContinueWith(_ => { }, TaskScheduler.Default);
        _ = RenderAsync();
    }

    private UIElement ChatView(WaChat c, IReadOnlyList<WaMessage> msgs)
    {
        var g = new Grid { RowSpacing = 10 };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = new Grid { ColumnSpacing = 10 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.Children.Add(Ui.IconButton("\uE72B", "Volver", () =>
        {
            _open = null;
            _ = RenderAsync();
        }));
        var av = Avatar(c.Name, c.IsGroup ? "#38BDF8" : Green, 34, c.Jid);
        SetColumn(av, 1);
        head.Children.Add(av);
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 0 };
        name.Children.Add(Ui.Label(c.Name, 14, bold: true));
        if (c.Phone is { } phone && phone != c.Name) name.Children.Add(Ui.Label(phone, 11, Ui.Tertiary));
        SetColumn(name, 2);
        head.Children.Add(name);
        g.Children.Add(head);
        var bubbles = new StackPanel { Spacing = 4, Padding = new Thickness(0, 4, 0, 4) };
        foreach (var m in msgs) bubbles.Children.Add(Bubble(m, c.IsGroup));
        var scroll = new ScrollViewer { Content = bubbles };
        scroll.Loaded += (_, _) => scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
        SetRow(scroll, 1);
        g.Children.Add(scroll);
        var box = new Grid { ColumnSpacing = 8 };
        box.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        box.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var input = new TextBox { PlaceholderText = "Escribe un mensaje", CornerRadius = new CornerRadius(16), AcceptsReturn = false };
        box.Children.Add(input);
        async Task Send()
        {
            var text = input.Text.Trim();
            if (text.Length == 0) return;
            input.IsEnabled = false;
            try
            {
                await _wa.SendTextAsync(c.Jid, text);
                input.Text = "";
                await RenderAsync();
            }
            catch (WaException e)
            {
                _note.Text = e.Message;
            }
            input.IsEnabled = true;
        }
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                _ = Send();
            }
        };
        var send = Ui.IconButton("\uE724", "Enviar", () => _ = Send());
        send.Background = Ui.Brush(Ui.Parse(Green, 0x33));
        send.CornerRadius = new CornerRadius(16);
        SetColumn(send, 1);
        box.Children.Add(send);
        SetRow(box, 2);
        g.Children.Add(box);
        g.Loaded += (_, _) => input.Focus(FocusState.Programmatic);
        return g;
    }

    private static readonly Dictionary<string, string?> AvatarCache = new();

    private Grid Avatar(string name, string color, double size, string jid)
    {
        var g = Ui.Avatar(name, color, size);
        _ = LoadAvatarAsync(g, jid, size);
        return g;
    }

    private async Task LoadAvatarAsync(Grid g, string jid, double size)
    {
        if (!AvatarCache.TryGetValue(jid, out var path))
        {
            try
            {
                path = await _wa.AvatarAsync(jid);
            }
            catch (Exception e) when (e is WaException or TimeoutException or InvalidOperationException)
            {
                return;
            }
            AvatarCache[jid] = path;
        }
        if (path is null || !File.Exists(path)) return;
        var img = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = (int)(size * 2) };
        img.UriSource = new Uri(path);
        g.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = new Microsoft.UI.Xaml.Media.ImageBrush { ImageSource = img, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill } });
    }

    private static async Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> FromBase64Async(string? b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        try
        {
            var bytes = System.Convert.FromBase64String(b64);
            using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var w = new Windows.Storage.Streams.DataWriter(ms.GetOutputStreamAt(0)))
            {
                w.WriteBytes(bytes);
                await w.StoreAsync();
            }
            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            await bmp.SetSourceAsync(ms);
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string FormatSize(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:0} KB",
        _ => $"{b / 1048576.0:0.0} MB",
    };

    private static string Duration(int s) => $"{s / 60}:{s % 60:00}";

    private static void OpenPath(string path) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });

    internal static UIElement MediaView(WhatsAppBridge _wa, TextBlock _note, WaFile f)
    {
        var host = new Grid();
        switch (f.Kind)
        {
            case "image":
            case "sticker":
            {
                var sticker = f.Kind == "sticker";
                double w = sticker ? 130 : 236;
                double h = sticker ? 130 : f.Width > 0 && f.Height > 0 ? Math.Clamp(w * f.Height / f.Width, 90, 300) : 170;
                var img = new Image { Width = w, Height = h, Stretch = sticker ? Microsoft.UI.Xaml.Media.Stretch.Uniform : Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
                var frame = new Border { CornerRadius = new CornerRadius(sticker ? 0 : 10), Child = img, Background = Ui.Brush(sticker ? Colors.Transparent : ColorHelper.FromArgb(0x22, 0xFF, 0xFF, 0xFF)) };
                host.Children.Add(frame);
                async Task Load()
                {
                    if (await FromBase64Async(f.Thumb) is { } t && img.Source is null) img.Source = t;
                    try
                    {
                        var p = await _wa.MediaPathAsync(f);
                        img.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(p)) { DecodePixelWidth = (int)(w * 2) };
                        frame.Tag = p;
                    }
                    catch (Exception e) when (e is WaException or TimeoutException) { }
                }
                _ = Load();
                frame.Tapped += (_, _) => { if (frame.Tag is string p) OpenPath(p); };
                Ui.Pressable(frame, frame);
                break;
            }
            case "video":
            {
                var w = 236.0;
                var h = f.Width > 0 && f.Height > 0 ? Math.Clamp(w * f.Height / f.Width, 120, 320) : 150;
                var img = new Image { Width = w, Height = h, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
                var overlay = new Grid();
                overlay.Children.Add(img);
                var play = new Grid { Width = 46, Height = 46, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                play.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = Ui.Brush(ColorHelper.FromArgb(0x99, 0, 0, 0)) });
                var icon = Ui.Icon("\uE768", 18, Colors.White);
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                play.Children.Add(icon);
                overlay.Children.Add(play);
                if (f.Seconds > 0)
                {
                    var d = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(6), Background = Ui.Brush(ColorHelper.FromArgb(0x99, 0, 0, 0)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Child = Ui.Label(Duration(f.Seconds), 10.5, Colors.White) };
                    overlay.Children.Add(d);
                }
                var frame = new Border { CornerRadius = new CornerRadius(10), Child = overlay, Width = w, Height = h, Background = Ui.Brush(ColorHelper.FromArgb(0x33, 0, 0, 0)) };
                host.Children.Add(frame);
                _ = LoadThumbAsync(img, f.Thumb);
                Ui.Pressable(frame, frame);
                frame.Tapped += async (_, _) =>
                {
                    icon.Glyph = "\uE895";
                    try
                    {
                        var p = await _wa.MediaPathAsync(f);
                        var player = new MediaPlayerElement { Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(p)), AreTransportControlsEnabled = true, AutoPlay = true, Width = w, Height = h, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
                        frame.Child = player;
                        frame.Tag = p;
                    }
                    catch (WaException e)
                    {
                        icon.Glyph = "\uE783";
                        _note.Text = e.Message;
                    }
                };
                break;
            }
            case "audio":
            {
                var row = new Grid { ColumnSpacing = 10, Width = 230 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var btn = new Grid { Width = 36, Height = 36 };
                btn.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = Ui.Brush(Ui.Parse(Green, 0x40)) });
                var icon = Ui.Icon("\uE768", 14, Colors.White);
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                btn.Children.Add(icon);
                row.Children.Add(btn);
                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 3 };
                var wave = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Height = 18 };
                var rnd = new Random(f.MessageId.GetHashCode());
                for (var i = 0; i < 26; i++) wave.Children.Add(new Border { Width = 3, Height = 4 + rnd.Next(13), CornerRadius = new CornerRadius(1.5), VerticalAlignment = VerticalAlignment.Center, Background = Ui.Brush(ColorHelper.FromArgb(0x88, 0xFF, 0xFF, 0xFF)) });
                info.Children.Add(wave);
                info.Children.Add(Ui.Label((f.Ptt ? "Nota de voz" : "Audio") + (f.Seconds > 0 ? " · " + Duration(f.Seconds) : ""), 11, Ui.Tertiary));
                SetColumn(info, 1);
                row.Children.Add(info);
                var surface = new Border { Child = row, Background = Ui.Brush(Colors.Transparent) };
                host.Children.Add(surface);
                Ui.Pressable(surface, surface);
                surface.Tapped += async (_, _) =>
                {
                    if (surface.Tag is MediaPlayerElement) return;
                    icon.Glyph = "\uE895";
                    try
                    {
                        var p = await _wa.MediaPathAsync(f);
                        var player = new MediaPlayerElement { Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(p)), AreTransportControlsEnabled = true, AutoPlay = true, Width = 240, Height = 56 };
                        player.TransportControls.IsCompact = true;
                        player.TransportControls.IsZoomButtonVisible = false;
                        surface.Tag = player;
                        surface.Child = player;
                    }
                    catch (WaException e)
                    {
                        icon.Glyph = "\uE783";
                        _note.Text = e.Message;
                    }
                };
                break;
            }
            default:
            {
                var row = new Grid { ColumnSpacing = 10, Width = 230, Padding = new Thickness(8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var ext = Path.GetExtension(f.FileName).TrimStart('.').ToUpperInvariant();
                var badge = new Border { Width = 36, Height = 42, CornerRadius = new CornerRadius(6), Background = Ui.Brush(Ui.Parse(ext == "PDF" ? "#EF4444" : ext is "DOC" or "DOCX" ? "#3B82F6" : ext is "XLS" or "XLSX" ? "#22C55E" : "#64748B", 0xCC)), Child = Ui.Label(ext.Length > 4 ? ext[..4] : ext, 9.5, Colors.White, bold: true) };
                ((TextBlock)badge.Child).HorizontalAlignment = HorizontalAlignment.Center;
                ((TextBlock)badge.Child).VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(badge);
                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
                info.Children.Add(Ui.Label(f.FileName, 12.5, Ui.Text, maxLines: 2));
                info.Children.Add(Ui.Label(f.Size > 0 ? FormatSize(f.Size) : ext, 11, Ui.Tertiary));
                SetColumn(info, 1);
                row.Children.Add(info);
                var dl = Ui.Icon("\uE896", 14, Ui.Secondary);
                dl.VerticalAlignment = VerticalAlignment.Center;
                SetColumn(dl, 2);
                row.Children.Add(dl);
                var surface = new Border { Child = row, CornerRadius = new CornerRadius(10), Background = Ui.Brush(ColorHelper.FromArgb(0x1A, 0, 0, 0)) };
                host.Children.Add(surface);
                Ui.Pressable(surface, surface);
                surface.Tapped += async (_, _) =>
                {
                    dl.Glyph = "\uE895";
                    try
                    {
                        var p = surface.Tag as string ?? await _wa.DownloadAsync(f);
                        surface.Tag = p;
                        dl.Glyph = "\uE8E5";
                        OpenPath(p);
                    }
                    catch (WaException e)
                    {
                        dl.Glyph = "\uE783";
                        _note.Text = e.Message;
                    }
                };
                break;
            }
        }
        return host;
    }

    internal static string NameColor(string name)
    {
        string[] palette = ["#38BDF8", "#F472B6", "#FBBF24", "#A78BFA", "#34D399", "#FB923C", "#F87171", "#60A5FA"];
        return palette[(int)((uint)name.GetHashCode() % palette.Length)];
    }

    private static async Task LoadThumbAsync(Image img, string? b64)
    {
        if (await FromBase64Async(b64) is { } t) img.Source = t;
    }

    private UIElement Bubble(WaMessage m, bool group)
    {
        var s = new StackPanel { Spacing = 4 };
        if (group && !m.FromMe && !string.IsNullOrEmpty(m.Sender)) s.Children.Add(Ui.Label(m.Sender, 11, Ui.Parse(NameColor(m.Sender)), bold: true));
        if (Quote(m.QuoteSender, m.QuoteText) is { } quote) s.Children.Add(quote);
        if (m.File is { } file) s.Children.Add(MediaView(_wa, _note, file));
        var body = m.File is null ? m.Text : m.Caption;
        if (!string.IsNullOrEmpty(body))
        {
            var text = Ui.CopyLine(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Ui.Brush(Ui.Text), MaxWidth = 240 });
            s.Children.Add(text);
        }
        var sticker = m.File?.Kind == "sticker";
        var time = Ui.Label(m.Ts.ToLocalTime().ToString("HH:mm"), 10.5, Ui.Tertiary);
        time.HorizontalAlignment = HorizontalAlignment.Right;
        s.Children.Add(time);
        return new Border
        {
            Child = s,
            MaxWidth = 270,
            Padding = sticker ? new Thickness(2) : m.File is { Kind: "image" or "video" } ? new Thickness(4, 4, 4, 5) : new Thickness(11, 7, 11, 6),
            CornerRadius = m.FromMe ? new CornerRadius(14, 14, 4, 14) : new CornerRadius(14, 14, 14, 4),
            Background = Ui.Brush(sticker ? Colors.Transparent : m.FromMe ? Ui.Parse("#1F6B45") : ColorHelper.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = m.FromMe ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        };
    }

    internal static UIElement? Quote(string? who, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var q = new StackPanel { Spacing = 1 };
        if (!string.IsNullOrEmpty(who)) q.Children.Add(Ui.Label(who, 10.5, Ui.Parse(NameColor(who)), bold: true));
        q.Children.Add(Ui.CopyLine(new TextBlock { Text = text, FontSize = 11.5, Foreground = Ui.Brush(Ui.Secondary), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, IsTextSelectionEnabled = true }));
        return new Border
        {
            Child = q,
            Padding = new Thickness(8, 4, 8, 5),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = Ui.Brush(Ui.Parse(string.IsNullOrEmpty(who) ? "#25D366" : NameColor(who))),
            Background = Ui.Brush(ColorHelper.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
        };
    }
}
