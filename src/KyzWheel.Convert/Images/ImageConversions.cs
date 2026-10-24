using ImageMagick;
using KyzWheel.Core.Files;

namespace KyzWheel.Convert.Images;

public static class ImageConversions
{
    internal static readonly HashSet<string> Readable = ["png", "jpg", "jpeg", "jfif", "webp", "heic", "heif", "gif", "avif", "tif", "tiff", "bmp", "ico"];

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
        new ImageCompressor(),
        new MetadataStripper(),
    ];

    internal static bool IsImage(string path) => Readable.Contains(FileGuards.Ext(path));

    internal static ConversionResult Result(string src, string output)
        => new(src, output, new FileInfo(src).Length, new FileInfo(output).Length);

    internal static MagickImageCollection Load(string path)
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
