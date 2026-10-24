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

    public WhatsAppPanel(WhatsAppBridge wa, Toasts toasts, IReadOnlyList<string> pendingFiles)
    {
        _wa = wa;
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
        g.Children.Add(Ui.Avatar(c.Name, c.IsGroup ? "#38BDF8" : Green, 38));
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
        var av = Ui.Avatar(c.Name, c.IsGroup ? "#38BDF8" : Green, 30);
        SetColumn(av, 1);
        head.Children.Add(av);
        var name = Ui.Label(c.Name, 14, bold: true);
        name.VerticalAlignment = VerticalAlignment.Center;
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

    private static UIElement Bubble(WaMessage m, bool group)
    {
        var s = new StackPanel { Spacing = 2 };
        if (group && !m.FromMe && !string.IsNullOrEmpty(m.Sender)) s.Children.Add(Ui.Label(m.Sender, 11, Ui.Parse("#38BDF8"), bold: true));
        var text = new TextBlock { Text = m.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13, IsTextSelectionEnabled = true, Foreground = Ui.Brush(Ui.Text) };
        s.Children.Add(text);
        var time = Ui.Label(m.Ts.ToLocalTime().ToString("HH:mm"), 10.5, Ui.Tertiary);
        time.HorizontalAlignment = HorizontalAlignment.Right;
        s.Children.Add(time);
        return new Border
        {
            Child = s,
            MaxWidth = 270,
            Padding = new Thickness(11, 7, 11, 6),
            CornerRadius = m.FromMe ? new CornerRadius(14, 14, 4, 14) : new CornerRadius(14, 14, 14, 4),
            Background = Ui.Brush(m.FromMe ? Ui.Parse("#1F6B45") : ColorHelper.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = m.FromMe ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        };
    }
}
