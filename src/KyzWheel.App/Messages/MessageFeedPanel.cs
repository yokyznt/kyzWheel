using KyzWheel.App.Shell;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace KyzWheel.App.Messages;

public sealed record FeedEntry(string Name, string? Context, string Body, DateTimeOffset When, string ColorHex, bool Unread, string? AvatarPath = null, string? Thumb = null, Action? Open = null);

public sealed class MessageFeedPanel : Grid
{
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly TextBlock _note;
    private readonly Toasts _toasts;

    public MessageFeedPanel(IReadOnlyList<FeedEntry> entries, Toasts toasts, string empty, Func<FeedEntry, Task<string?>>? avatar = null)
    {
        _toasts = toasts;
        RowSpacing = 8;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scroll = new ScrollViewer { Content = _rows, Margin = new Thickness(-8, 0, -8, 0), Padding = new Thickness(8, 0, 8, 0) };
        Children.Add(scroll);
        _note = Ui.Label("", 11.5, Ui.Tertiary);
        SetRow(_note, 1);
        Children.Add(_note);
        if (entries.Count == 0) _rows.Children.Add(KyzWheel.App.Mail.MailPanel.Empty("\uE8F2", "Sin mensajes", empty, null, null));
        foreach (var e in entries) _rows.Children.Add(Card(e, avatar));
    }

    private UIElement Card(FeedEntry e, Func<FeedEntry, Task<string?>>? avatar)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 10, 12, 10),
            Background = Ui.Brush(ColorHelper.FromArgb(0xF2, 0x16, 0x17, 0x1E)),
            BorderBrush = Ui.Brush(ColorHelper.FromArgb(e.Unread ? (byte)0x55 : (byte)0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
        };
        var g = new Grid { ColumnSpacing = 10 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var av = Ui.Avatar(e.Name, e.ColorHex, 34);
        av.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(av);
        if (avatar is not null) _ = LoadAvatarAsync(av, e, avatar);
        var t = new StackPanel { Spacing = 3 };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Ui.Label(e.Name, 13, bold: true));
        var when = Ui.Label(Ui.When(e.When), 11, e.Unread ? Ui.Parse(e.ColorHex) : Ui.Tertiary);
        Grid.SetColumn(when, 1);
        top.Children.Add(when);
        t.Children.Add(top);
        if (!string.IsNullOrEmpty(e.Context)) t.Children.Add(Ui.Label(e.Context, 11.5, Ui.Secondary));
        if (!string.IsNullOrEmpty(e.Thumb) && Thumb(e.Thumb) is { } img) t.Children.Add(img);
        t.Children.Add(new TextBlock { Text = e.Body, TextWrapping = TextWrapping.Wrap, MaxLines = 8, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5, FontFamily = Ui.Font, Foreground = Ui.Brush(Ui.Text), IsTextSelectionEnabled = true });
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        card.Child = g;

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 2, 0) };
        actions.Children.Add(Ui.IconButton("\uE8C8", "Copiar texto", () => Copy(e)));
        actions.Children.Add(Ui.IconButton("\uE722", "Captura del mensaje", () => _ = CaptureAsync(card, e)));
        if (e.Open is { } open) actions.Children.Add(Ui.IconButton("\uE8A7", "Abrir", open));
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

    private void Copy(FeedEntry e)
    {
        var dp = new DataPackage();
        dp.SetText(string.IsNullOrEmpty(e.Context) ? $"{e.Name}: {e.Body}" : $"{e.Name} — {e.Context}\n{e.Body}");
        Clipboard.SetContent(dp);
        Clipboard.Flush();
        _note.Text = "Texto copiado";
    }

    private async Task CaptureAsync(FrameworkElement card, FeedEntry e)
    {
        try
        {
            var rtb = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            var scale = XamlRoot?.RasterizationScale ?? 1;
            await rtb.RenderAsync(card, (int)(card.ActualWidth * scale * 2), (int)(card.ActualHeight * scale * 2));
            var buffer = await rtb.GetPixelsAsync();
            var pixels = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "kyzWheel");
            Directory.CreateDirectory(dir);
            var safe = string.Concat(e.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            var name = $"mensaje_{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var folder = await StorageFolder.GetFolderFromPathAsync(dir);
            var file = await folder.CreateFileAsync(name, CreationCollisionOption.GenerateUniqueName);
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
