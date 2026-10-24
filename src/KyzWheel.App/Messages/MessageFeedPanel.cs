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

public sealed record FeedEntry(string Key, string Name, string? Context, string Body, DateTimeOffset When, string ColorHex, bool Unread, string? AvatarKey = null, string? Thumb = null, Action? Open = null, string? Account = null);

public sealed class MessageFeedPanel : Grid
{
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly TextBlock _note;
    private readonly Toasts _toasts;
    private readonly string _empty;
    private readonly Func<FeedEntry, Task<string?>>? _avatar;
    private readonly TextBox _search = new() { PlaceholderText = "Buscar", CornerRadius = new CornerRadius(10) };
    private readonly HashSet<string> _seen = new();
    private IReadOnlyList<FeedEntry> _entries = [];

    public MessageFeedPanel(IReadOnlyList<FeedEntry> entries, Toasts toasts, string empty, Func<FeedEntry, Task<string?>>? avatar = null)
    {
        _toasts = toasts;
        _empty = empty;
        _avatar = avatar;
        RowSpacing = 8;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _search.TextChanged += (_, _) => Render(false);
        Children.Add(_search);
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
        Render(animate);
    }

    private void Render(bool animate)
    {
        var q = _search.Text.Trim();
        var list = _entries.Where(e => q.Length == 0 || $"{e.Name} {e.Context} {e.Body} {e.Account}".Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        _rows.Children.Clear();
        if (list.Count == 0) _rows.Children.Add(KyzWheel.App.Mail.MailPanel.Empty("\uE8F2", q.Length > 0 ? "Sin resultados" : "Sin mensajes", q.Length > 0 ? "Prueba con otra palabra." : _empty, null, null));
        var first = _seen.Count == 0;
        foreach (var e in list)
        {
            var card = Card(e);
            _rows.Children.Add(card);
            if (_seen.Add(e.Key) && animate && !first) KyzWheel.App.Wheel.WheelAnimations.RowEnter(card);
        }
    }

    private static TextBlock Text(string text, double size, Windows.UI.Color color, bool bold = false, int maxLines = 1) => new()
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
    };

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
        if (!string.IsNullOrEmpty(e.Thumb) && Thumb(e.Thumb) is { } img) t.Children.Add(img);
        t.Children.Add(Text(e.Body, 12.5, Ui.Text, maxLines: 8));
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
        wrap.Children.Add(card);
        wrap.Children.Add(actions);
        return wrap;
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
