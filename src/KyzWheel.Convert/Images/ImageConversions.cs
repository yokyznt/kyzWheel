using ImageMagick;
using KyzWheel.Core.Files;

namespace KyzWheel.Convert.Images;

public static class ImageConversions
{
    internal static readonly HashSet<string> Readable = ["png", "jpg", "jpeg", "jfif", "webp", "heic", "heif", "gif", "avif", "tif", "tiff", "bmp", "ico", "psd", "tga", "svg", "jxl", "dds", "jp2"];

    public static IEnumerable<string> Extensions => Readable;

    public static IReadOnlyList<IConversion> All() =>
    [
        new ImageFormatConversion("img.png", "PNG", "png", MagickFormat.Png, ["png"]),
        new ImageFormatConversion("img.webp", "WEBP", "webp", MagickFormat.WebP, ["webp"]),
        new HeicConversion(),
        new ImageFormatConversion("img.gif", "GIF", "gif", MagickFormat.Gif, ["gif"]),
        new ImageFormatConversion("img.avif", "AVIF", "avif", MagickFormat.Avif, ["avif"]),
        new ImageFormatConversion("img.jpeg", "JPEG", "jpg", MagickFormat.Jpeg, ["jpg", "jpeg", "jfif"]),
        new ImageFormatConversion("img.pdf", "PDF", "pdf", MagickFormat.Pdf, []),
        new ImageToDocx(),
        new ImageFormatConversion("img.tiff", "TIFF", "tiff", MagickFormat.Tiff, ["tif", "tiff"]),
        new ImageFormatConversion("img.bmp", "BMP", "bmp", MagickFormat.Bmp, ["bmp"]),
        new ImageFormatConversion("img.jxl", "JPEG XL", "jxl", MagickFormat.Jxl, ["jxl"]),
        new IconConversion(),
        new ImageCompressor(),
        new MetadataStripper(),
        new RemoveBackground(),
        new AddBackground("tool.addbg", "Fondo blanco", "#FFFFFF"),
        new AddBackground("tool.addbgblack", "Fondo negro", "#000000"),
        new ImageTransform("tool.rotate", "Girar 90°", "\uE7AD", i => i.Rotate(90)),
        new ImageTransform("tool.flip", "Espejo", "\uE8AB", i => i.Flop()),
        new ImageTransform("tool.gray", "Blanco y negro", "\uE790", i => i.Grayscale()),
        new ImageTransform("tool.half", "Mitad de tamaño", "\uE73F", i => i.Resize(new Percentage(50))),
    ];

    public static bool IsImage(string path) => Readable.Contains(FileGuards.Ext(path));

    public static ConversionResult Result(string src, string output)
        => new(src, output, new FileInfo(src).Length, new FileInfo(output).Length);

    public static MagickImageCollection Load(string path)
    {
        FileGuards.EnsureLocal(path);
        try
        {
            var c = new MagickImageCollection(path);
            foreach (var f in c) f.AutoOrient();
            return c;
        }
        catch (MagickException e)
        {
            throw new ConversionException("No se pudo leer la imagen", e);
        }
    }

    internal static void WriteAtomic(string output, Action<string> write)
    {
        var tmp = output + ".kyztmp";
        try
        {
            write(tmp);
            File.Move(tmp, output);
        }
        catch (MagickException e)
        {
            File.Delete(tmp);
            throw new ConversionException("No se pudo convertir la imagen", e);
        }
    }
}

internal sealed class ImageFormatConversion(string id, string label, string ext, MagickFormat format, string[] sameExts) : IConversion
{
    private static readonly HashSet<MagickFormat> Animated = [MagickFormat.Gif, MagickFormat.WebP, MagickFormat.Tiff, MagickFormat.Pdf];

    public string Id => id;
    public string Label => label;
    public string Glyph => "\uEB9F";
    public ConverterPage Page => ConverterPage.Images;

    public bool CanHandle(string path) => ImageConversions.IsImage(path) && !sameExts.Contains(FileGuards.Ext(path));

    public Task<ConversionResult> RunAsync(string path, IProgress<double>? progress, CancellationToken ct) => Task.Run(() =>
    {
        using var images = ImageConversions.Load(path);
        ct.ThrowIfCancellationRequested();
        var output = OutputNaming.NextFreePath(path, "." + ext);
        ImageConversions.WriteAtomic(output, tmp =>
        {
            if (Animated.Contains(format) && images.Count > 1)
            {
                if (format == MagickFormat.Gif) images.Optimize();
                images.Write(tmp, format);
            }
            else
            {
                var first = images[0];
                if (format is MagickFormat.Jpeg or MagickFormat.Bmp) first.ColorAlpha(MagickColors.White);
                first.Quality = 85;
                first.Write(tmp, format);
            }
        });
        progress?.Report(1);
        return ImageConversions.Result(path, output);
    }, ct);
}

internal sealed class HeicConversion : IConversion
{
    public string Id => "img.heic";
    public string Label => "HEIC";
    public string Glyph => "\uEB9F";
    public ConverterPage Page => ConverterPage.Images;

    public bool CanHandle(string path)
        => ImageConversions.IsImage(path) && FileGuards.Ext(path) is not ("heic" or "heif") && HeicEncoder.IsAvailable;

    public async Task<ConversionResult> RunAsync(string path, IProgress<double>? progress, CancellationToken ct)
    {
        using var images = await Task.Run(() => ImageConversions.Load(path), ct);
        var output = OutputNaming.NextFreePath(path, ".heic");
        var tmp = output + ".kyztmp";
        try
        {
            await HeicEncoder.WriteAsync(images[0], tmp);
            File.Move(tmp, output);
        }
        catch (Exception e) when (e is not ConversionException)
        {
            File.Delete(tmp);
            throw new ConversionException("No se pudo convertir a HEIC", e);
        }
        progress?.Report(1);
        return ImageConversions.Result(path, output);
    }
}

internal sealed class IconConversion : IConversion
{
    public string Id => "img.ico";
    public string Label => "ICO";
    public string Glyph => "\uEB9F";
    public ConverterPage Page => ConverterPage.Images;

    public bool CanHandle(string path) => ImageConversions.IsImage(path) && FileGuards.Ext(path) != "ico";

    public Task<ConversionResult> RunAsync(string path, IProgress<double>? progress, CancellationToken ct) => Task.Run(() =>
    {
        using var images = ImageConversions.Load(path);
        var src = images[0];
        var side = Math.Max(src.Width, src.Height);
        src.BackgroundColor = MagickColors.Transparent;
        src.Extent(side, side, Gravity.Center);
        using var icon = new MagickImageCollection();
        foreach (var s in new uint[] { 256, 128, 64, 48, 32, 16 })
        {
            var f = src.Clone();
            f.Resize(s, s);
            icon.Add(f);
        }
        var output = OutputNaming.NextFreePath(path, ".ico");
        ImageConversions.WriteAtomic(output, tmp => icon.Write(tmp, MagickFormat.Ico));
        progress?.Report(1);
        return ImageConversions.Result(path, output);
    }, ct);
}

internal sealed class ImageTransform(string id, string label, string glyph, Action<IMagickImage<byte>> apply) : IConversion
{
    public string Id => id;
    public string Label => label;
    public string Glyph => glyph;
    public ConverterPage Page => ConverterPage.Tools;

    public bool CanHandle(string path) => ImageConversions.IsImage(path) && FileGuards.Ext(path) is not ("svg" or "psd" or "dds");

    public Task<ConversionResult> RunAsync(string path, IProgress<double>? progress, CancellationToken ct) => Task.Run(() =>
    {
        using var images = ImageConversions.Load(path);
        foreach (var f in images) apply(f);
        var output = OutputNaming.NextFreePath(path, Path.GetExtension(path));
        ImageConversions.WriteAtomic(output, tmp =>
        {
            if (images.Count > 1) images.Write(tmp, images[0].Format);
            else
            {
                images[0].Quality = 92;
                images[0].Write(tmp, images[0].Format);
            }
        });
        progress?.Report(1);
        return ImageConversions.Result(path, output);
    }, ct);
}
