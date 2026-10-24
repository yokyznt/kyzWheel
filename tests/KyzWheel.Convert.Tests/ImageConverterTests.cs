using DocumentFormat.OpenXml.Packaging;
using ImageMagick;
using KyzWheel.Convert.Images;

namespace KyzWheel.Convert.Tests;

public sealed class ImageConverterTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    private static IConversion Get(string id) => ImageConversions.All().Single(c => c.Id == id);

    [Theory]
    [InlineData("png", "webp", MagickFormat.WebP)]
    [InlineData("png", "avif", MagickFormat.Avif)]
    [InlineData("jpg", "heic", MagickFormat.Heic)]
    [InlineData("heic", "jpeg", MagickFormat.Jpeg)]
    [InlineData("png", "pdf", MagickFormat.Pdf)]
    [InlineData("png", "tiff", MagickFormat.Tiff)]
    [InlineData("png", "bmp", MagickFormat.Bmp)]
    [InlineData("webp", "png", MagickFormat.Png)]
    [InlineData("png", "gif", MagickFormat.Gif)]
    public async Task Converts_and_writes_next_to_source(string from, string to, MagickFormat expected)
    {
        var src = from switch
        {
            "png" => _t.Png(),
            "jpg" => _t.Jpeg(),
            "heic" => _t.Convert(_t.Jpeg("h.jpg"), MagickFormat.Heic, ".heic"),
            "webp" => _t.Convert(_t.Png("w.png"), MagickFormat.WebP, ".webp"),
            _ => throw new ArgumentException(from),
        };
        var r = await Get("img." + to).RunAsync(src, null, CancellationToken.None);
        Assert.Equal(Path.GetDirectoryName(src), Path.GetDirectoryName(r.OutputPath));
        Assert.EndsWith(to == "jpeg" ? ".jpg" : "." + to, r.OutputPath);
        Assert.True(File.Exists(r.OutputPath));
        if (expected != MagickFormat.Pdf)
            Assert.Equal(expected, Normalize(new MagickImageInfo(r.OutputPath).Format));
        else
            Assert.StartsWith("%PDF", File.ReadAllText(r.OutputPath)[..4]);
        Assert.Equal(new FileInfo(src).Length, r.SizeBefore);
        Assert.Equal(new FileInfo(r.OutputPath).Length, r.SizeAfter);
    }

    private static MagickFormat Normalize(MagickFormat f) => f switch
    {
        MagickFormat.Jpg => MagickFormat.Jpeg,
        MagickFormat.Heif => MagickFormat.Heic,
        MagickFormat.Tif => MagickFormat.Tiff,
        _ => f,
    };

    [Fact]
    public void Same_format_is_not_offered()
    {
        var src = _t.Png();
        Assert.False(Get("img.png").CanHandle(src));
        Assert.False(Get("img.jpeg").CanHandle(_t.Jpeg()));
        Assert.False(Get("img.webp").CanHandle(Path.Combine(_t.Path, "a.txt")));
    }

    [Fact]
    public async Task Docx_contains_one_image_part()
    {
        var r = await Get("img.docx").RunAsync(_t.Png(), null, CancellationToken.None);
        using var doc = WordprocessingDocument.Open(r.OutputPath, false);
        Assert.Single(doc.MainDocumentPart!.ImageParts);
        Assert.Contains("pic:pic", doc.MainDocumentPart.Document!.OuterXml);
    }

    [Fact]
    public async Task Strip_removes_gps_and_exif()
    {
        var src = _t.Jpeg();
        Assert.NotNull(new MagickImage(src).GetExifProfile());
        var r = await Get("tool.strip").RunAsync(src, null, CancellationToken.None);
        using var img = new MagickImage(r.OutputPath);
        Assert.Null(img.GetExifProfile());
        Assert.EndsWith("foto_1.jpg", r.OutputPath);
    }

    [Fact]
    public async Task Compress_never_bigger_than_source()
    {
        var big = _t.Jpeg("big.jpg", 1024);
        var r = await Get("tool.compress").RunAsync(big, null, CancellationToken.None);
        Assert.True(r.SizeAfter < r.SizeBefore);
        Assert.True(File.Exists(r.OutputPath));

        var tiny = _t.Png("tiny.png", false);
        var r2 = await Get("tool.compress").RunAsync(tiny, null, CancellationToken.None);
        Assert.True(r2.SizeAfter <= r2.SizeBefore);
        if (r2.OutputPath == tiny) Assert.Single(Directory.GetFiles(_t.Path, "tiny*"));
    }

    [Fact]
    public async Task Source_is_never_modified()
    {
        var src = _t.Png();
        var before = await File.ReadAllBytesAsync(src);
        foreach (var c in ImageConversions.All().Where(c => c.CanHandle(src)))
            await c.RunAsync(src, null, CancellationToken.None);
        Assert.Equal(before, await File.ReadAllBytesAsync(src));
    }

    [Fact]
    public async Task Cloud_only_file_returns_clear_error()
    {
        var src = _t.Png();
        File.SetAttributes(src, FileAttributes.Offline);
        var ex = await Assert.ThrowsAsync<ConversionException>(() => Get("img.webp").RunAsync(src, null, CancellationToken.None));
        Assert.Equal("Archivo solo en la nube: ábrelo una vez para descargarlo", ex.Message);
        File.SetAttributes(src, FileAttributes.Normal);
    }

    [Fact]
    public async Task Missing_file_returns_clear_error()
    {
        var ex = await Assert.ThrowsAsync<ConversionException>(() => Get("img.webp").RunAsync(Path.Combine(_t.Path, "nope.png"), null, CancellationToken.None));
        Assert.Equal("El archivo ya no existe", ex.Message);
    }
}
