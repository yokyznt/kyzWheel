using KyzWheel.Core.Files;

namespace KyzWheel.Convert.Tests;

public sealed class ConversionCatalogTests
{
    private sealed record Fake(string Id, ConverterPage Page, string[] Exts, string? Same = null) : IConversion
    {
        public string Label => Id;
        public string Glyph => "";
        public bool CanHandle(string path)
        {
            var e = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            return Exts.Contains(e) && e != Same;
        }
        public Task<ConversionResult> RunAsync(string path, IProgress<double>? p, CancellationToken ct) => throw new NotSupportedException();
    }

    private static readonly string[] Img = ["png", "jpg", "webp"];

    private readonly ConversionCatalog _c = new([
        new Fake("img.png", ConverterPage.Images, Img, "png"),
        new Fake("img.webp", ConverterPage.Images, Img, "webp"),
        new Fake("tool.compress", ConverterPage.Tools, Img),
        new Fake("tool.preview", ConverterPage.Tools, ["*"]) ,
        new Fake("media.mp3", ConverterPage.Media, ["mp4", "wav"]),
        new Fake("doc.split", ConverterPage.Documents, ["pdf"]),
    ]);

    [Fact] public void Png_opens_images_page() => Assert.Equal(ConverterPage.Images, _c.PageFor("a.PNG"));
    [Fact] public void Mp4_opens_media() => Assert.Equal(ConverterPage.Media, _c.PageFor("a.mp4"));
    [Fact] public void Pdf_opens_documents() => Assert.Equal(ConverterPage.Documents, _c.PageFor("a.pdf"));
    [Fact] public void Unknown_opens_tools() => Assert.Equal(ConverterPage.Tools, _c.PageFor("a.xyz"));

    [Fact]
    public void Images_page_hides_same_format()
        => Assert.Equal(["img.webp"], _c.ActionsFor(ConverterPage.Images, "a.png").Select(a => a.Id));

    [Fact]
    public void Pages_order_starts_with_best_page_and_skips_empty()
        => Assert.Equal([ConverterPage.Images, ConverterPage.Tools], _c.PagesOrder("a.png"));

    [Fact]
    public void Unknown_extension_has_no_pages_when_nothing_handles()
        => Assert.Empty(_c.PagesOrder("a.xyz"));

    [Fact]
    public void Actions_for_many_files_are_intersection()
        => Assert.Equal(["tool.compress"], _c.ActionsFor(ConverterPage.Tools, ["a.png", "b.jpg"]).Select(a => a.Id));
}
