using System.Runtime.InteropServices.WindowsRuntime;
using KyzWheel.App.Shell;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;
using WinClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace KyzWheel.App.Tools;

public static class ScreenTools
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "kyzWheel");

    public static async Task<string?> SnipAsync(TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? s, object e) => tcs.TrySetResult(true);
        var before = DateTimeOffset.Now;
        WinClipboard.ContentChanged += Changed;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
            using var cts = new CancellationTokenSource(timeout);
            while (!cts.IsCancellationRequested)
            {
                var done = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => false, TaskScheduler.Default));
                if (done != tcs.Task) break;
                var view = WinClipboard.GetContent();
                if (view.Contains(StandardDataFormats.Bitmap)) return await SaveBitmapAsync(view);
                tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            return null;
        }
        finally
        {
            WinClipboard.ContentChanged -= Changed;
        }
    }

    private static async Task<string> SaveBitmapAsync(DataPackageView view)
    {
        var reference = await view.GetBitmapAsync();
        using var stream = await reference.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bmp = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        Directory.CreateDirectory(Folder);
        var folder = await StorageFolder.GetFolderFromPathAsync(Folder);
        var file = await folder.CreateFileAsync($"recorte_{DateTime.Now:yyyyMMdd_HHmmss}.png", CreationCollisionOption.GenerateUniqueName);
        using (var output = await file.OpenAsync(FileAccessMode.ReadWrite))
        {
            var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            enc.SetSoftwareBitmap(bmp);
            await enc.FlushAsync();
        }
        return file.Path;
    }

    public static async Task<string> OcrAsync(string imagePath)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
            ?? throw new InvalidOperationException("Instala un paquete de reconocimiento de texto en Configuración › Idioma");
        var file = await StorageFile.GetFileFromPathAsync(imagePath);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        SoftwareBitmap bmp;
        var max = OcrEngine.MaxImageDimension;
        if (decoder.PixelWidth > max || decoder.PixelHeight > max)
        {
            var scale = Math.Min((double)max / decoder.PixelWidth, (double)max / decoder.PixelHeight);
            bmp = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform { ScaledWidth = (uint)(decoder.PixelWidth * scale), ScaledHeight = (uint)(decoder.PixelHeight * scale) }, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        }
        else bmp = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        using (bmp)
        {
            var r = await engine.RecognizeAsync(bmp);
            return string.Join(Environment.NewLine, r.Lines.Select(l => l.Text));
        }
    }

    public static void CopyText(string text)
    {
        var dp = new DataPackage();
        dp.SetText(text);
        WinClipboard.SetContent(dp);
        WinClipboard.Flush();
    }
}
