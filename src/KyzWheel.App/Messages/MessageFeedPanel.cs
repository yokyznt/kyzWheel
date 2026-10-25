using KyzWheel.App.Shell;
using KyzWheel.Core.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace KyzWheel.App.Messages;

public sealed record FeedLine(string? Who, string Text, DateTimeOffset When, int Depth, bool Nested, string? QuoteWho, string? QuoteText, KyzWheel.Core.WhatsApp.WaFile? File);

public sealed record FeedEntry(string Key, string Name, string? Context, string Body, DateTimeOffset When, string ColorHex, bool Unread, string? AvatarKey = null, string? Thumb = null, Action? Open = null, string? Account = null, IReadOnlyList<FeedLine>? Lines = null, string Signature = "")
{
    public string? Service { get; init; }
    public Func<string, Task>? Reply { get; init; }
    public Func<Task<IReadOnlyList<KyzWheel.Core.Messaging.ImSticker>>>? Stickers { get; init; }
    public Func<KyzWheel.Core.Messaging.ImSticker, Task>? SendSticker { get; init; }
    public Func<Task>? Call { get; init; }
}

public sealed class MessageFeedPanel : Grid
{
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly TextBlock _note;
    private readonly Toasts _toasts;
    private readonly string _empty;
    private readonly Func<FeedEntry, Task<string?>>? _avatar;
    private readonly Func<KyzWheel.Core.WhatsApp.WaFile, UIElement>? _media;
    private readonly TextBox _search = new() { PlaceholderText = "Buscar", CornerRadius = new CornerRadius(10) };
    private readonly HashSet<string> _seen = new();
    private IReadOnlyList<FeedEntry> _entries = [];
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private string? _filter;
    private string? _replyOpen;
    private bool _pending;

    public event Action<string?>? FilterChanged;

    public void SetFilters(IReadOnlyList<KyzWheel.Core.Messaging.ImService> services, string? selected)
    {
        _filter = selected;
        _chips.Children.Clear();
        void Chip(string? id, string label, string color, string? glyph)
        {
            var on = _filter == id;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            if (glyph is not null) content.Children.Add(Ui.Icon(glyph, 11, on ? Colors.Black : Ui.Parse(color)));
            content.Children.Add(new TextBlock { Text = label, FontSize = 12, FontFamily = Ui.Font, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Ui.Brush(on ? Colors.Black : Ui.Text) });
            var b = new Button { Content = content, CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 4, 10, 4), Background = Ui.Brush(on ? Ui.Parse(color) : ColorHelper.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(0) };
            b.Click += (_, _) =>
            {
                SetFilters(services, id);
                FilterChanged?.Invoke(id);
                Render(false);
            };
            _chips.Children.Add(b);
        }
        Chip(null, "Todos", "#FFFFFF", null);
        foreach (var sv in services) Chip(sv.Id, sv.Name, sv.ColorHex, sv.Glyph);
    }

    public MessageFeedPanel(IReadOnlyList<FeedEntry> entries, Toasts toasts, string empty, Func<FeedEntry, Task<string?>>? avatar = null, Func<KyzWheel.Core.WhatsApp.WaFile, UIElement>? media = null)
    {
        _media = media;
        _toasts = toasts;
        _empty = empty;
        _avatar = avatar;
        RowSpacing = 8;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _search.TextChanged += (_, _) => Render(false);
        var top = new StackPanel { Spacing = 8 };
        top.Children.Add(new ScrollViewer { Content = _chips, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled });
        top.Children.Add(_search);
        Children.Add(top);
        var scroll = new ScrollViewer { Content = _rows, Margin = new Thickness(-8, 0, -8, 0), Padding = new Thickness(8, 0, 8, 0) };
        SetRow(scroll, 1);
        Children.Add(scroll);
        _note = Ui.Label("", 11.5, Ui.Tertiary);
        SetRow(_note, 2);
        Children.Add(_note);
        Update(entries, false);
    }

    public void Update(IReadOnlyList<FeedEntry> entries, bool animate = true)
    {
        _entries = entries;
        if (_replyOpen is not null)
        {
            _pending = true;
            return;
        }
        Render(animate);
    }

    private void Render(bool animate)
    {
        var q = _search.Text.Trim();
        var list = _entries.Where(e => _filter is null || e.Service == _filter).Where(e => q.Length == 0 || $"{e.Name} {e.Context} {e.Body} {e.Account} {string.Join(' ', e.Lines?.Select(l => l.Who + " " + l.Text) ?? [])}".Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        _rows.Children.Clear();
        if (list.Count == 0) _rows.Children.Add(KyzWheel.App.Mail.MailPanel.Empty("\uE8F2", q.Length > 0 ? "Sin resultados" : "Sin mensajes", q.Length > 0 ? "Prueba con otra palabra." : _empty, null, null));
        var first = _seen.Count == 0;
        foreach (var e in list)
        {
            var card = Card(e);
            _rows.Children.Add(card);
            if (_seen.Add(e.Key + "|" + e.Signature) && animate && !first) KyzWheel.App.Wheel.WheelAnimations.RowEnter(card);
        }
    }

    private static TextBlock Text(string text, double size, Windows.UI.Color color, bool bold = false, int maxLines = 1) => Ui.CopyLine(new TextBlock
    {
        Text = text,
        FontSize = size,
        FontFamily = Ui.Font,
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        Foreground = Ui.Brush(color),
        TextWrapping = maxLines == 1 ? TextWrapping.NoWrap : TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = maxLines,
        IsTextSelectionEnabled = true,
    });

    private UIElement Card(FeedEntry e)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 10, 12, 10),
            Background = Ui.Brush(ColorHelper.FromArgb(0xF2, 0x16, 0x17, 0x1E)),
            BorderBrush = Ui.Brush(e.Unread ? Ui.Parse(e.ColorHex, 0x88) : ColorHelper.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
        };
        var g = new Grid { ColumnSpacing = 10 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var av = Ui.Avatar(e.Name, e.ColorHex, 34);
        av.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(av);
        if (_avatar is not null && e.AvatarKey is not null) _ = LoadAvatarAsync(av, e, _avatar);
        var t = new StackPanel { Spacing = 3 };
        var top = new Grid { ColumnSpacing = 6 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Text(e.Name, 13, Ui.Text, bold: true));
        var when = Ui.Label(Ui.When(e.When), 11, e.Unread ? Ui.Parse(e.ColorHex) : Ui.Tertiary);
        Grid.SetColumn(when, 1);
        top.Children.Add(when);
        t.Children.Add(top);
        if (!string.IsNullOrEmpty(e.Context)) t.Children.Add(Text(e.Context, 11.5, Ui.Secondary, maxLines: 2));
        if (e.Lines is { Count: > 0 } lines) t.Children.Add(Thread(lines));
        else
        {
            if (!string.IsNullOrEmpty(e.Thumb) && Thumb(e.Thumb) is { } img) t.Children.Add(img);
            t.Children.Add(Text(e.Body, 12.5, Ui.Text, maxLines: 8));
        }
        if (!string.IsNullOrEmpty(e.Account)) t.Children.Add(Text(e.Account, 10.5, Ui.Tertiary));
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        card.Child = g;
        if (e.Open is { } open) card.DoubleTapped += (_, _) => open();

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 2, 0) };
        if (CodeFinder.Find(e.Context, e.Body) is { } code)
        {
            var chip = Ui.Primary("Copiar " + code, () => CopyText(code, $"Código {code} copiado"), "\uE8C8");
            chip.Height = 30;
            chip.Padding = new Thickness(10, 0, 12, 0);
            actions.Children.Add(chip);
        }
        actions.Children.Add(Ui.IconButton("\uE8C8", "Copiar todo", () => CopyText(string.IsNullOrEmpty(e.Context) ? $"{e.Name}: {e.Body}" : $"{e.Name} — {e.Context}\n{e.Body}", "Texto copiado")));
        actions.Children.Add(Ui.IconButton("\uE722", "Captura del mensaje", () => _ = CaptureAsync(card, e)));
        if (e.Open is { } o) actions.Children.Add(Ui.IconButton("\uE740", "Abrir completo", o));
        var wrap = new StackPanel { Spacing = 0 };
        var tray = new StackPanel { Spacing = 6, Margin = new Thickness(12, 4, 2, 0) };
        if (e.Call is { } call) actions.Children.Insert(0, Ui.IconButton("\uE717", "Llamar (abre la app)", () => _ = Run(call, "Abriendo la app para llamar…")));
        if (e.Stickers is not null && e.SendSticker is not null) actions.Children.Insert(0, Ui.IconButton("\uE76E", "Mandar sticker", () => _ = StickersAsync(e, tray)));
        if (e.Reply is not null)
        {
            actions.Children.Insert(0, Ui.IconButton("\uE97A", "Responder", () => ReplyBox(e, tray)));
            if (_replyOpen == e.Key) ReplyBox(e, tray, false);
        }
        wrap.Children.Add(card);
        wrap.Children.Add(actions);
        wrap.Children.Add(tray);
        return wrap;
    }

    private UIElement Thread(IReadOnlyList<FeedLine> lines)
    {
        var list = new StackPanel { Spacing = 6, Margin = new Thickness(0, 2, 0, 0) };
        string? prevWho = null;
        foreach (var l in lines.TakeLast(12))
        {
            var row = new StackPanel { Spacing = 3, Margin = new Thickness(l.Depth * 12, 0, 0, 0) };
            if (l.Who is { } who && (who != prevWho || l.Depth > 0))
            {
                var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                head.Children.Add(Ui.Label(who, 11, Ui.Parse(KyzWheel.App.WhatsApp.WhatsAppPanel.NameColor(who)), bold: true));
                head.Children.Add(Ui.Label(l.When.ToLocalTime().ToString("HH:mm"), 10, Ui.Tertiary));
                row.Children.Add(head);
            }
            prevWho = l.Who;
            if (!l.Nested && KyzWheel.App.WhatsApp.WhatsAppPanel.Quote(l.QuoteWho, l.QuoteText) is { } q) row.Children.Add(q);
            if (l.File is { } f && _media is not null && f.Kind is "sticker" or "audio" or "image" or "video") row.Children.Add(f.Kind == "sticker" ? new Viewbox { Child = _media(f), Height = 84, HorizontalAlignment = HorizontalAlignment.Left } : _media(f));
            if (l.Text.Length > 0) row.Children.Add(Text(l.Text, 12.5, Ui.Text, maxLines: 6));
            UIElement item = row;
            if (l.Depth > 0)
                item = new Border { Child = row, BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = Ui.Brush(ColorHelper.FromArgb(0x55, 0x25, 0xD3, 0x66)), Padding = new Thickness(8, 0, 0, 0), Margin = new Thickness(l.Depth * 10 - 10, 0, 0, 0) };
            if (l.Depth > 0) row.Margin = new Thickness(0);
            list.Children.Add(item);
        }
        if (lines.Count > 12) list.Children.Insert(0, Ui.Label($"+{lines.Count - 12} anteriores", 10.5, Ui.Tertiary));
        return list;
    }

    private async Task Run(Func<Task> act, string busy)
    {
        _note.Text = busy;
        try
        {
            await act();
            _note.Text = "";
        }
        catch (Exception ex)
        {
            _note.Text = "No se pudo: " + ex.Message;
        }
    }

    private void ReplyBox(FeedEntry e, StackPanel tray, bool focus = true)
    {
        if (tray.Children.OfType<Grid>().Any(g => (string?)g.Tag == "reply"))
        {
            tray.Children.Clear();
            _replyOpen = null;
            if (_pending)
            {
                _pending = false;
                Render(true);
            }
            return;
        }
        _replyOpen = e.Key;
        var g = new Grid { ColumnSpacing = 6, Tag = "reply" };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox { PlaceholderText = "Responder a " + e.Name + "…", CornerRadius = new CornerRadius(10), AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, MaxHeight = 120 };
        g.Children.Add(box);
        async Task Send()
        {
            var text = box.Text.Trim();
            if (text.Length == 0) return;
            box.IsEnabled = false;
            try
            {
                await e.Reply!(text);
                box.Text = "";
                _note.Text = "Enviado a " + e.Name;
            }
            catch (Exception ex)
            {
                _note.Text = "No se envió: " + ex.Message;
            }
            box.IsEnabled = true;
            box.Focus(FocusState.Programmatic);
        }
        var send = Ui.IconButton("\uE724", "Enviar", () => _ = Send());
        Grid.SetColumn(send, 1);
        g.Children.Add(send);
        box.KeyDown += (_, k) =>
        {
            if (k.Key == Windows.System.VirtualKey.Enter) { k.Handled = true; _ = Send(); }
        };
        tray.Children.Clear();
        tray.Children.Add(g);
        if (focus) box.Loaded += (_, _) => box.Focus(FocusState.Programmatic);
    }

    private async Task StickersAsync(FeedEntry e, StackPanel tray)
    {
        if (tray.Children.OfType<GridView>().Any())
        {
            tray.Children.Clear();
            return;
        }
        _note.Text = "Cargando stickers…";
        IReadOnlyList<KyzWheel.Core.Messaging.ImSticker> list;
        try
        {
            list = await e.Stickers!();
        }
        catch (Exception ex)
        {
            _note.Text = "No hay stickers: " + ex.Message;
            return;
        }
        _note.Text = list.Count == 0 ? "Aún no hay stickers recientes en esta app. Los que te manden aparecerán aquí." : "";
        if (list.Count == 0) return;
        var grid = new GridView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, MaxHeight = 220 };
        foreach (var st in list)
        {
            if (!File.Exists(st.Path)) continue;
            grid.Items.Add(new Image { Width = 64, Height = 64, Tag = st, Source = new BitmapImage(new Uri(st.Path)) { DecodePixelWidth = 128 } });
        }
        grid.ItemClick += async (_, a) =>
        {
            if (a.ClickedItem is not Image { Tag: KyzWheel.Core.Messaging.ImSticker st }) return;
            tray.Children.Clear();
            await Run(() => e.SendSticker!(st), "Enviando sticker…");
            if (_note.Text.Length == 0) _note.Text = "Sticker enviado a " + e.Name;
        };
        tray.Children.Clear();
        tray.Children.Add(grid);
    }

    private static Image? Thumb(string b64)
    {
        try
        {
            var bytes = System.Convert.FromBase64String(b64);
            var bmp = new BitmapImage();
            var ms = new InMemoryRandomAccessStream();
            using (var w = new DataWriter(ms.GetOutputStreamAt(0)))
            {
                w.WriteBytes(bytes);
                w.StoreAsync().AsTask().Wait(500);
            }
            ms.Seek(0);
            bmp.SetSource(ms);
            return new Image { Source = bmp, MaxHeight = 140, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task LoadAvatarAsync(Grid av, FeedEntry e, Func<FeedEntry, Task<string?>> avatar)
    {
        try
        {
            if (await avatar(e) is { } p && File.Exists(p))
                av.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = new Microsoft.UI.Xaml.Media.ImageBrush { ImageSource = new BitmapImage(new Uri(p)) { DecodePixelWidth = 68 }, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill } });
        }
        catch (Exception)
        {
        }
    }

    private void CopyText(string text, string note)
    {
        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
        Clipboard.Flush();
        _note.Text = note;
    }

    private async Task CaptureAsync(FrameworkElement card, FeedEntry e)
    {
        try
        {
            var rtb = new RenderTargetBitmap();
            var scale = XamlRoot?.RasterizationScale ?? 1;
            await rtb.RenderAsync(card, (int)(card.ActualWidth * scale * 2), (int)(card.ActualHeight * scale * 2));
            var buffer = await rtb.GetPixelsAsync();
            var pixels = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "kyzWheel");
            Directory.CreateDirectory(dir);
            var safe = string.Concat(e.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            var folder = await StorageFolder.GetFolderFromPathAsync(dir);
            var file = await folder.CreateFileAsync($"mensaje_{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.png", CreationCollisionOption.GenerateUniqueName);
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, pixels);
                await enc.FlushAsync();
            }
            var dp = new DataPackage();
            dp.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            dp.SetStorageItems([file]);
            Clipboard.SetContent(dp);
            Clipboard.Flush();
            _note.Text = "Captura copiada y guardada en Imágenes › kyzWheel";
            _toasts.Result("Captura del mensaje", "Copiada al portapapeles", [file.Path]);
        }
        catch (Exception ex)
        {
            _note.Text = "No se pudo capturar: " + ex.Message;
        }
    }
}
