using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace KyzWheel.Convert.Documents;

public static class PdfRender
{
    public static async Task<int> RenderPagesAsync(string pdf, string folder, double dpi, IProgress<double>? progress, CancellationToken ct)
    {
        var doc = await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(pdf));
        Directory.CreateDirectory(folder);
        var stem = Path.GetFileNameWithoutExtension(pdf);
        for (uint i = 0; i < doc.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var page = doc.GetPage(i);
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = (uint)Math.Round(page.Size.Width * dpi / 96.0) });
            var bytes = new byte[stream.Size];
            stream.Seek(0);
            await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
            await File.WriteAllBytesAsync(Path.Combine(folder, $"{stem}_{i + 1:000}.png"), bytes, ct);
            progress?.Report((i + 1.0) / doc.PageCount);
        }
        return (int)doc.PageCount;
    }

    public static async Task<IReadOnlyList<string>> OcrPagesAsync(string pdf, IProgress<double>? progress, CancellationToken ct)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
            ?? throw new ConversionException("Instala un paquete de reconocimiento de texto en Configuración › Idioma");
        var doc = await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(pdf));
        var pages = new List<string>();
        for (uint i = 0; i < doc.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var page = doc.GetPage(i);
            using var stream = new InMemoryRandomAccessStream();
            var width = Math.Min(OcrEngine.MaxImageDimension, (uint)Math.Round(page.Size.Width * 2));
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = width });
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bmp = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bmp);
            pages.Add(string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)));
            progress?.Report((i + 1.0) / doc.PageCount);
        }
        return pages;
    }
}
