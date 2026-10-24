using ImageMagick;
using KyzWheel.Core.Files;

namespace KyzWheel.Convert.Images;

internal sealed class ImageCompressor : IConversion
{
    public string Id => "tool.compress";
    public string Label => "Comprimir";
    public string Glyph => "\uE73F";
    public ConverterPage Page => ConverterPage.Tools;

    public bool CanHandle(string path) => ImageConversions.IsImage(path);

    public Task<ConversionResult> RunAsync(string path, IProgress<double>? progress, CancellationToken ct) => Task.Run(() =>
    {
        using var images = ImageConversions.Load(path);
        var ext = Path.GetExtension(path);
        var output = OutputNaming.NextFreePath(path, ext);
        var tmp = output + ".kyztmp";
        try
        {
            foreach (var img in images)
            {
                img.Strip();
                img.Quality = 75;
                img.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
                img.Settings.SetDefine(MagickFormat.WebP, "method", "6");
            }
            if (images.Count > 1) images.Write(tmp, images[0].Format);
            else images[0].Write(tmp, images[0].Format);
        }
        catch (MagickException e)
        {
            File.Delete(tmp);
            throw new ConversionException("No se pudo comprimir la imagen", e);
        }
        var before = new FileInfo(path).Length;
        var after = new FileInfo(tmp).Length;
        progress?.Report(1);
        if (after >= before)
        {
            File.Delete(tmp);
            return new ConversionResult(path, path, before, before);
        }
        File.Move(tmp, output);
        return new ConversionResult(path, output, before, after);
    }, ct);
}
