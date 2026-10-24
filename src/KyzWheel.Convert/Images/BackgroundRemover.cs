using System.Security.Cryptography;
using ImageMagick;
using KyzWheel.Core.Files;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KyzWheel.Convert.Images;

public static class BackgroundRemover
{
    public const string ModelUrl = "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx";
    public const string ModelSha256 = "309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8";
    private const int Side = 320;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static InferenceSession? _session;

    public static string ModelDir { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "kyzWheel.Data", "models");

    public static async Task<string> EnsureModelAsync(CancellationToken ct)
    {
        var path = Path.Combine(ModelDir, "u2netp.onnx");
        if (File.Exists(path)) return path;
        Directory.CreateDirectory(ModelDir);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var bytes = await http.GetByteArrayAsync(ModelUrl, ct);
            if (System.Convert.ToHexStringLower(SHA256.HashData(bytes)) != ModelSha256)
                throw new ConversionException("El modelo para quitar fondo no pasó la verificación");
            await File.WriteAllBytesAsync(path + ".tmp", bytes, ct);
            File.Move(path + ".tmp", path, true);
            return path;
        }
        catch (HttpRequestException e)
        {
            throw new ConversionException("Se necesita internet una vez para descargar el modelo de quitar fondo (5 MB)", e);
        }
    }

    private static async Task<InferenceSession> SessionAsync(CancellationToken ct)
    {
        if (_session is not null) return _session;
        await Gate.WaitAsync(ct);
        try
        {
            return _session ??= new InferenceSession(await EnsureModelAsync(ct), new SessionOptions { IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2) });
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<MagickImage> CutoutAsync(string path, CancellationToken ct)
    {
        var session = await SessionAsync(ct);
        return await Task.Run(() =>
        {
            var img = new MagickImage(path);
            img.AutoOrient();
            img.ColorSpace = ColorSpace.sRGB;
            using var small = (MagickImage)img.Clone();
            small.Alpha(AlphaAction.Off);
            small.Resize(new MagickGeometry(Side, Side) { IgnoreAspectRatio = true });
            var px = small.GetPixels().ToByteArray(PixelMapping.RGB)!;
            var input = new DenseTensor<float>([1, 3, Side, Side]);
            float[] mean = [0.485f, 0.456f, 0.406f], std = [0.229f, 0.224f, 0.225f];
            for (var y = 0; y < Side; y++)
                for (var x = 0; x < Side; x++)
                    for (var c = 0; c < 3; c++)
                        input[0, c, y, x] = (px[(y * Side + x) * 3 + c] / 255f - mean[c]) / std[c];
            using var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)]);
            var mask = results.First().AsEnumerable<float>().Take(Side * Side).ToArray();
            float min = mask.Min(), max = mask.Max(), range = Math.Max(1e-6f, max - min);
            var gray = new byte[Side * Side];
            for (var i = 0; i < gray.Length; i++) gray[i] = (byte)Math.Clamp((mask[i] - min) / range * 255f, 0, 255);
            using var alpha = new MagickImage(MagickColors.Black, Side, Side);
            alpha.GetPixels().SetPixels(gray.SelectMany(g => new[] { g, g, g }).ToArray());
            alpha.Resize(new MagickGeometry(img.Width, img.Height) { IgnoreAspectRatio = true });
            alpha.ColorType = ColorType.Grayscale;
            img.Alpha(AlphaAction.Set);
            img.Composite(alpha, CompositeOperator.CopyAlpha);
            return img;
        }, ct);
    }
}

internal sealed class RemoveBackground : IConversion
{
    public string Id => "tool.removebg";
    public string Label => "Quitar fondo";
    public string Glyph => "\uE8B3";
    public ConverterPage Page => ConverterPage.Tools;
    public bool CanHandle(string path) => ImageConversions.IsImage(path);

    public async Task<ConversionResult> RunAsync(string path, IProgress<double>? progress, CancellationToken ct)
    {
        FileGuards.EnsureLocal(path);
        using var img = await BackgroundRemover.CutoutAsync(path, ct);
        var output = OutputNaming.NextFreePath(Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_sinfondo.png"), ".png");
        img.Write(output, MagickFormat.Png);
        return ImageConversions.Result(path, output);
    }
}

internal sealed class AddBackground(string id, string label, string hex) : IConversion
{
    public string Id => id;
    public string Label => label;
    public string Glyph => "\uE790";
    public ConverterPage Page => ConverterPage.Tools;
    public bool CanHandle(string path) => ImageConversions.IsImage(path);

    public Task<ConversionResult> RunAsync(string path, IProgress<double>? progress, CancellationToken ct) => Task.Run(() =>
    {
        using var images = ImageConversions.Load(path);
        var img = images[0];
        img.BackgroundColor = new MagickColor(hex);
        img.Alpha(AlphaAction.Remove);
        var output = OutputNaming.NextFreePath(path, ".png");
        img.Write(output, MagickFormat.Png);
        return ImageConversions.Result(path, output);
    }, ct);
}
