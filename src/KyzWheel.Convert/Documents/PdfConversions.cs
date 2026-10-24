using System.Diagnostics;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ImageMagick;
using KyzWheel.Core.Files;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace KyzWheel.Convert.Documents;

public static class PdfConversions
{
    private static int _fonts;

    public static void EnsureFonts()
    {
        if (Interlocked.Exchange(ref _fonts, 1) == 0) GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    }

    public static IReadOnlyList<IConversion> All() =>
    [
        new DocAction("doc.topdf", "A PDF", "\uEA90", ["txt", "md", "csv", "log"], TextToPdfAsync),
        new DocAction("doc.compress", "Comprimir", "\uE73F", ["pdf"], CompressAsync),
        new DocAction("doc.split", "Dividir", "\uE8C6", ["pdf"], SplitAsync),
        new MergeAction(),
        new DocAction("doc.ocr", "OCR texto", "\uE8D2", ["pdf"], OcrAsync),
        new DocAction("doc.toimages", "A imágenes", "\uEB9F", ["pdf"], ToImagesAsync),
        new DocAction("doc.toword", "A Word", "\uE8A5", ["pdf"], ToWordAsync),
        new DocAction("doc.print", "Imprimir", "\uE749", ["pdf", "docx", "doc", "txt", "png", "jpg", "jpeg"], PrintAsync),
        new DocAction("doc.preview", "Vista previa", "\uE890", ["pdf", "docx", "doc", "txt", "md", "rtf", "odt"], PreviewAsync),
        new ImagesToPdfAction(),
    ];

    private static ConversionResult Result(string src, string output)
        => new(src, output, new FileInfo(src).Length, Directory.Exists(output) ? Directory.GetFiles(output).Sum(f => new FileInfo(f).Length) : new FileInfo(output).Length);

    private static string Fresh(string candidate)
        => File.Exists(candidate) || Directory.Exists(candidate) ? OutputNaming.NextFreePath(candidate, Path.GetExtension(candidate)) : candidate;

    private static string FreeDir(string src, string suffix)
    {
        var dir = Path.GetDirectoryName(src)!;
        var stem = Path.GetFileNameWithoutExtension(src) + suffix;
        for (var i = 0; ; i++)
        {
            var p = Path.Combine(dir, i == 0 ? stem : $"{stem}_{i}");
            if (!Directory.Exists(p) && !File.Exists(p)) return p;
        }
    }

    private static PdfDocument Open(string path, PdfDocumentOpenMode mode)
    {
        FileGuards.EnsureLocal(path);
        try
        {
            return PdfReader.Open(path, mode);
        }
        catch (Exception e) when (e is PdfReaderException or InvalidOperationException)
        {
            throw new ConversionException(e.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ? "El PDF tiene contraseña" : "No se pudo leer el PDF", e);
        }
    }

    public static Task<ConversionResult> SplitAsync(string path, IProgress<double>? progress, CancellationToken ct) => Task.Run(() =>
    {
        using var src = Open(path, PdfDocumentOpenMode.Import);
        var dir = FreeDir(path, "_paginas");
        Directory.CreateDirectory(dir);
        var stem = Path.GetFileNameWithoutExtension(path);
        for (var i = 0; i < src.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var one = new PdfDocument();
            one.AddPage(src.Pages[i]);
            one.Save(Path.Combine(dir, $"{stem}_{i + 1:000}.pdf"));
            progress?.Report((i + 1.0) / src.PageCount);
        }
        return Result(path, dir);
    }, ct);

    public static Task<ConversionResult> MergeAsync(IReadOnlyList<string> paths, CancellationToken ct) => Task.Run(() =>
    {
        using var output = new PdfDocument();
        foreach (var p in paths)
        {
            ct.ThrowIfCancellationRequested();
            using var src = Open(p, PdfDocumentOpenMode.Import);
            foreach (var page in src.Pages) output.AddPage(page);
        }
        var first = paths[0];
        var target = Fresh(Path.Combine(Path.GetDirectoryName(first)!, Path.GetFileNameWithoutExtension(first) + "_unido.pdf"));
        output.Save(target);
        return new ConversionResult(first, target, paths.Sum(p => new FileInfo(p).Length), new FileInfo(target).Length);
    }, ct);

    public static Task<ConversionResult> ImagesToPdfAsync(IReadOnlyList<string> images, CancellationToken ct) => Task.Run(() =>
    {
        EnsureFonts();
        using var doc = new PdfDocument();
        foreach (var img in images)
        {
            ct.ThrowIfCancellationRequested();
            FileGuards.EnsureLocal(img);
            using var magick = new MagickImage(img);
            magick.AutoOrient();
            magick.ColorAlpha(MagickColors.White);
            var jpg = magick.ToByteArray(MagickFormat.Jpeg);
            using var ms = new MemoryStream(jpg);
            using var x = XImage.FromStream(ms);
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(x.PointWidth);
            page.Height = XUnit.FromPoint(x.PointHeight);
            using var g = XGraphics.FromPdfPage(page);
            g.DrawImage(x, 0, 0, x.PointWidth, x.PointHeight);
        }
        var first = images[0];
        var target = images.Count == 1 ? OutputNaming.NextFreePath(first, ".pdf") : Fresh(Path.Combine(Path.GetDirectoryName(first)!, Path.GetFileNameWithoutExtension(first) + "_unido.pdf"));
        doc.Save(target);
        return new ConversionResult(first, target, images.Sum(p => new FileInfo(p).Length), new FileInfo(target).Length);
    }, ct);

    public static Task<ConversionResult> TextToPdfAsync(string path, IProgress<double>? progress, CancellationToken ct) => Task.Run(() =>
    {
        EnsureFonts();
        FileGuards.EnsureLocal(path);
        var lines = File.ReadAllLines(path);
        using var doc = new PdfDocument();
        var font = new XFont("Arial", 10.5);
        PdfPage? page = null;
        XGraphics? g = null;
        double y = 0;
        const double margin = 56;
        foreach (var raw in lines.DefaultIfEmpty(""))
        {
            foreach (var line in Wrap(raw, 95))
            {
                if (page is null || y > page.Height.Point - margin)
                {
                    g?.Dispose();
                    page = doc.AddPage();
                    g = XGraphics.FromPdfPage(page);
                    y = margin;
                }
                g!.DrawString(line, font, XBrushes.Black, new XPoint(margin, y));
                y += 15;
            }
        }
        g?.Dispose();
        var target = OutputNaming.NextFreePath(path, ".pdf");
        doc.Save(target);
        return Result(path, target);
    }, ct);

    private static IEnumerable<string> Wrap(string line, int width)
    {
        if (line.Length <= width)
        {
            yield return line;
            yield break;
        }
        var rest = line;
        while (rest.Length > width)
        {
            var cut = rest.LastIndexOf(' ', width);
            if (cut <= 0) cut = width;
            yield return rest[..cut];
            rest = rest[cut..].TrimStart();
        }
        if (rest.Length > 0) yield return rest;
    }

    public static Task<ConversionResult> CompressAsync(string path, IProgress<double>? progress, CancellationToken ct) => Task.Run(() =>
    {
        using var doc = Open(path, PdfDocumentOpenMode.Modify);
        doc.Options.CompressContentStreams = true;
        doc.Options.NoCompression = false;
        doc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
        doc.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Automatic;
        var target = OutputNaming.NextFreePath(path, ".pdf");
        var tmp = target + ".kyztmp";
        doc.Save(tmp);
        var before = new FileInfo(path).Length;
        var after = new FileInfo(tmp).Length;
        if (after >= before)
        {
            File.Delete(tmp);
            return new ConversionResult(path, path, before, before);
        }
        File.Move(tmp, target);
        return new ConversionResult(path, target, before, after);
    }, ct);

    public static async Task<ConversionResult> ToImagesAsync(string path, IProgress<double>? progress, CancellationToken ct)
    {
        FileGuards.EnsureLocal(path);
        var dir = FreeDir(path, "_imagenes");
        try
        {
            await PdfRender.RenderPagesAsync(path, dir, 150, progress, ct);
        }
        catch (Exception e) when (e is not (OperationCanceledException or ConversionException))
        {
            throw new ConversionException("No se pudo leer el PDF", e);
        }
        return Result(path, dir);
    }

    public static async Task<ConversionResult> OcrAsync(string path, IProgress<double>? progress, CancellationToken ct)
    {
        FileGuards.EnsureLocal(path);
        var pages = await PdfRender.OcrPagesAsync(path, progress, ct);
        var target = OutputNaming.NextFreePath(path, ".txt");
        await File.WriteAllTextAsync(target, string.Join(Environment.NewLine + Environment.NewLine, pages), ct);
        return Result(path, target);
    }

    public static async Task<ConversionResult> ToWordAsync(string path, IProgress<double>? progress, CancellationToken ct)
    {
        FileGuards.EnsureLocal(path);
        var pages = await PdfRender.OcrPagesAsync(path, progress, ct);
        var target = OutputNaming.NextFreePath(path, ".docx");
        using (var doc = WordprocessingDocument.Create(target, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            for (var i = 0; i < pages.Count; i++)
            {
                foreach (var para in pages[i].Split(Environment.NewLine))
                    body.Append(new Paragraph(new Run(new Text(para) { Space = SpaceProcessingModeValues.Preserve })));
                if (i < pages.Count - 1) body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
            }
            main.Document = new Document(body);
            main.Document.Save();
        }
        return Result(path, target);
    }

    public static Task<ConversionResult> PrintAsync(string path, IProgress<double>? progress, CancellationToken ct)
    {
        FileGuards.EnsureLocal(path);
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "print" });
        }
        catch (Exception e)
        {
            throw new ConversionException("No hay una app predeterminada para imprimir este archivo", e);
        }
        return Task.FromResult(new ConversionResult(path, path, new FileInfo(path).Length, new FileInfo(path).Length));
    }

    public static Task<ConversionResult> PreviewAsync(string path, IProgress<double>? progress, CancellationToken ct)
    {
        FileGuards.EnsureLocal(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return Task.FromResult(new ConversionResult(path, path, new FileInfo(path).Length, new FileInfo(path).Length));
    }
}

internal sealed class DocAction(string id, string label, string glyph, string[] exts, Func<string, IProgress<double>?, CancellationToken, Task<ConversionResult>> run) : IConversion
{
    public string Id => id;
    public string Label => label;
    public string Glyph => glyph;
    public ConverterPage Page => ConverterPage.Documents;
    public bool CanHandle(string path) => exts.Contains(FileGuards.Ext(path));
    public Task<ConversionResult> RunAsync(string path, IProgress<double>? progress, CancellationToken ct) => run(path, progress, ct);
}

internal sealed class MergeAction : IMultiConversion
{
    public string Id => "doc.merge";
    public string Label => "Unir";
    public string Glyph => "\uE71D";
    public ConverterPage Page => ConverterPage.Documents;
    public bool CanHandle(string path) => FileGuards.Ext(path) == "pdf";
    public Task<ConversionResult> RunAsync(string path, IProgress<double>? progress, CancellationToken ct) => PdfConversions.MergeAsync([path], ct);
    public Task<ConversionResult> RunManyAsync(IReadOnlyList<string> paths, IProgress<double>? progress, CancellationToken ct) => PdfConversions.MergeAsync(paths, ct);
}

internal sealed class ImagesToPdfAction : IMultiConversion
{
    public string Id => "doc.imagespdf";
    public string Label => "Unir en PDF";
    public string Glyph => "\uEA90";
    public ConverterPage Page => ConverterPage.Images;
    public bool CanHandle(string path) => KyzWheel.Convert.Images.ImageConversions.IsImage(path);
    public Task<ConversionResult> RunAsync(string path, IProgress<double>? progress, CancellationToken ct) => PdfConversions.ImagesToPdfAsync([path], ct);
    public Task<ConversionResult> RunManyAsync(IReadOnlyList<string> paths, IProgress<double>? progress, CancellationToken ct) => PdfConversions.ImagesToPdfAsync(paths, ct);
}
