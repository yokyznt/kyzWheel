using KyzWheel.App.Shell;
using KyzWheel.Convert;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace KyzWheel.App.Converter;

public sealed class ConverterWindow : GlassWindow
{
    private readonly ConverterController _controller;
    private readonly List<string> _files = [];
    private readonly ListView _list = new() { Height = 170, SelectionMode = ListViewSelectionMode.None };
    private readonly ComboBox _actions = new() { PlaceholderText = "Elige una acción", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _run = new() { Content = "Convertir", Style = (Style)Application.Current.Resources["AccentButtonStyle"], IsEnabled = false };
    private readonly TextBlock _status;

    public ConverterWindow(ConverterController controller) : base("kyzWheel · Convertidor", 520, 600)
    {
        _controller = controller;
        _status = Muted("");
        var drop = new Border
        {
            Height = 120,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            Background = new SolidColorBrush(ColorHelper.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
            AllowDrop = true,
            Child = new TextBlock { Text = "Suelta archivos aquí", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 },
        };
        drop.DragOver += (_, e) => e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None;
        drop.Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            AddFiles((await e.DataView.GetStorageItemsAsync()).OfType<StorageFile>().Select(f => f.Path));
        };
        var pick = new Button { Content = "Elegir archivos" };
        pick.Click += async (_, _) => await PickAsync();
        var paste = new Button { Content = "Pegar imagen del portapapeles" };
        paste.Click += async (_, _) => await PasteAsync();
        var clear = new Button { Content = "Limpiar" };
        clear.Click += (_, _) =>
        {
            _files.Clear();
            Refresh();
        };
        _run.Click += async (_, _) => await RunAsync();
        _actions.SelectionChanged += (_, _) => _run.IsEnabled = _actions.SelectedItem is ComboBoxItem;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(pick);
        buttons.Children.Add(paste);
        buttons.Children.Add(clear);
        var stack = new StackPanel { Spacing = 14, Padding = new Thickness(24, 44, 24, 24) };
        stack.Children.Add(Heading("Convertidor"));
        stack.Children.Add(Muted("Convierte varios archivos a la vez. Todo pasa en tu PC, nada se sube."));
        stack.Children.Add(drop);
        stack.Children.Add(buttons);
        stack.Children.Add(_list);
        stack.Children.Add(_actions);
        stack.Children.Add(_run);
        stack.Children.Add(_status);
        Surface.Children.Add(new ScrollViewer { Content = stack });
        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            AppWindow.Hide();
        };
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
            if (File.Exists(p) && !_files.Contains(p, StringComparer.OrdinalIgnoreCase)) _files.Add(p);
        Refresh();
    }

    private void Refresh()
    {
        _list.Items.Clear();
        foreach (var f in _files) _list.Items.Add(new TextBlock { Text = Path.GetFileName(f), TextTrimming = TextTrimming.CharacterEllipsis });
        _actions.Items.Clear();
        if (_files.Count > 0)
        {
            foreach (var page in _controller.Catalog.PagesOrder(_files))
                foreach (var c in _controller.Catalog.ActionsFor(page, _files))
                    _actions.Items.Add(new ComboBoxItem { Content = $"{ConverterController.PageTitle(page)} · {c.Label}", Tag = c });
        }
        _run.IsEnabled = false;
        _status.Text = _files.Count == 0 ? "" : _actions.Items.Count == 0 ? "No hay acciones para estos archivos" : $"{_files.Count} archivo(s)";
    }

    private async Task RunAsync()
    {
        if (_actions.SelectedItem is not ComboBoxItem { Tag: IConversion c }) return;
        _run.IsEnabled = false;
        _status.Text = "Convirtiendo…";
        _status.Text = await _controller.RunAsync(c, _files.ToList());
        _run.IsEnabled = true;
    }

    private async Task PickAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");
        var files = await picker.PickMultipleFilesAsync();
        AddFiles(files.Select(f => f.Path));
    }

    private async Task PasteAsync()
    {
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Bitmap))
        {
            _status.Text = "No hay una imagen en el portapapeles";
            return;
        }
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "kyzWheel");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"portapapeles_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        var reference = await content.GetBitmapAsync();
        using var stream = await reference.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var folder = await StorageFolder.GetFolderFromPathAsync(dir);
        var file = await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.GenerateUniqueName);
        using (var output = await file.OpenAsync(FileAccessMode.ReadWrite))
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
        }
        AddFiles([file.Path]);
    }
}
