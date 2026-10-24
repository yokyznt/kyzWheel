using System.Runtime.InteropServices.WindowsRuntime;
using KyzWheel.App.Shell;
using KyzWheel.Core.Clipboard;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using WinClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace KyzWheel.App.Clips;

public sealed class ClipboardService
{
    private readonly string _imageDir = Path.Combine(Log.Dir, "clips");
    private bool _suppress;

    public ClipHistory History { get; } = new(Path.Combine(Log.Dir, "clips.json"));

    public void Start() => WinClipboard.ContentChanged += (_, _) => _ = CaptureAsync();

    private async Task CaptureAsync()
    {
        if (_suppress)
        {
            _suppress = false;
            return;
        }
        try
        {
            var view = WinClipboard.GetContent();
            if (view.Contains("ExcludeClipboardContentFromMonitorProcessing") || view.Contains("CanIncludeInClipboardHistory")) return;
            if (view.Contains(StandardDataFormats.StorageItems))
            {
                var items = await view.GetStorageItemsAsync();
                History.AddFiles(items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList());
            }
            else if (view.Contains(StandardDataFormats.Bitmap))
            {
                var reference = await view.GetBitmapAsync();
                using var stream = await reference.OpenReadAsync();
                var bytes = new byte[stream.Size];
                await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
                if (bytes.Length > 25_000_000) return;
                var hash = ClipHistory.HashOf(bytes);
                Directory.CreateDirectory(_imageDir);
                var path = Path.Combine(_imageDir, hash + ".png");
                if (!File.Exists(path))
                {
                    stream.Seek(0);
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    using var bmp = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                    var file = await (await StorageFolder.GetFolderFromPathAsync(_imageDir)).CreateFileAsync(hash + ".png", CreationCollisionOption.ReplaceExisting);
                    using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
                    var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
                    enc.SetSoftwareBitmap(bmp);
                    await enc.FlushAsync();
                }
                History.AddImage(hash, path);
            }
            else if (view.Contains(StandardDataFormats.Text))
            {
                History.AddText(await view.GetTextAsync());
            }
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException)
        {
        }
    }

    public async Task CopyAsync(ClipItem item)
    {
        var dp = new DataPackage();
        switch (item.Kind)
        {
            case ClipKind.Text:
                dp.SetText(item.Text!);
                break;
            case ClipKind.Image when item.ImagePath is { } p && File.Exists(p):
                dp.SetBitmap(RandomAccessStreamReference.CreateFromFile(await StorageFile.GetFileFromPathAsync(p)));
                break;
            case ClipKind.Files:
                var files = new List<IStorageItem>();
                foreach (var f in item.Files!)
                {
                    if (File.Exists(f)) files.Add(await StorageFile.GetFileFromPathAsync(f));
                    else if (Directory.Exists(f)) files.Add(await StorageFolder.GetFolderFromPathAsync(f));
                }
                if (files.Count == 0) return;
                dp.SetStorageItems(files);
                break;
            default:
                return;
        }
        _suppress = true;
        WinClipboard.SetContent(dp);
        WinClipboard.Flush();
    }
}
