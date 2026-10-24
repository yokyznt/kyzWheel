using ImageMagick;
using KyzWheel.Convert.Images;

namespace KyzWheel.Convert.Tests;

public sealed class BackgroundTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    private string Subject()
    {
        var p = Path.Combine(_t.Path, "objeto.png");
        using var img = new MagickImage(MagickColors.White, 400, 400);
        img.Draw(new ImageMagick.Drawing.Drawables().FillColor(MagickColors.Red).Ellipse(200, 200, 110, 110, 0, 360));
        img.Write(p, MagickFormat.Png);
        return p;
    }

    [Fact]
    public async Task Remove_background_makes_corners_transparent_and_center_opaque()
    {
        BackgroundRemover.ModelDir = Path.Combine(_t.Path, "models");
        var scratch = Environment.GetEnvironmentVariable("TMPDIR");
        var pre = scratch is null ? null : Path.Combine(scratch, "u2netp.onnx");
        if (pre is not null && File.Exists(pre))
        {
            Directory.CreateDirectory(BackgroundRemover.ModelDir);
            File.Copy(pre, Path.Combine(BackgroundRemover.ModelDir, "u2netp.onnx"));
        }
        ConversionResultHolder r;
        try
        {
            r = new(await ImageConversions.All().Single(c => c.Id == "tool.removebg").RunAsync(Subject(), null, CancellationToken.None));
        }
        catch (ConversionException e) when (e.Message.Contains("internet"))
        {
            return;
        }
        using var img = new MagickImage(r.Value.OutputPath);
        var px = img.GetPixels();
        Assert.True(px.GetPixel(5, 5)!.GetChannel(3) <= 10);
        Assert.True(px.GetPixel(200, 200)!.GetChannel(3) >= 245);
    }

    private sealed record ConversionResultHolder(KyzWheel.Core.Files.ConversionResult Value);

    [Fact]
    public async Task Add_black_background_removes_transparency()
    {
        var p = Path.Combine(_t.Path, "t.png");
        using (var img = new MagickImage(MagickColors.Transparent, 50, 50)) img.Write(p, MagickFormat.Png);
        var r = await ImageConversions.All().Single(c => c.Id == "tool.addbgblack").RunAsync(p, null, CancellationToken.None);
        using var o = new MagickImage(r.OutputPath);
        Assert.True(o.IsOpaque);
        Assert.EndsWith(".png", r.OutputPath);
    }
}
