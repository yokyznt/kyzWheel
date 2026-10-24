using KyzWheel.Core.Files;

namespace KyzWheel.Convert.Images;

internal sealed class MetadataStripper : IConversion
{
    public string Id => "tool.strip";
    public string Label => "Quitar GPS/EXIF";
    public string Glyph => "\uE72E";
    public ConverterPage Page => ConverterPage.Tools;

    public bool CanHandle(string path) => ImageConversions.IsImage(path);

    public Task<ConversionResult> RunAsync(string path, IProgress<double>? progress, CancellationToken ct) => Task.Run(() =>
    {
        using var images = ImageConversions.Load(path);
        var output = OutputNaming.NextFreePath(path, Path.GetExtension(path));
        ImageConversions.WriteAtomic(output, tmp =>
        {
            foreach (var img in images)
            {
                var q = img.Quality;
                img.Strip();
                if (q > 0) img.Quality = q;
            }
            if (images.Count > 1) images.Write(tmp, images[0].Format);
            else images[0].Write(tmp, images[0].Format);
        });
        progress?.Report(1);
        return ImageConversions.Result(path, output);
    }, ct);
}
