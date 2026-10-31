using ImageMagick;
using KyzWheel.Convert.Documents;
using KyzWheel.Convert.Images;
using KyzWheel.Convert.Media;

namespace KyzWheel.Convert.Tests;

public sealed class MoreFormatsTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    private static IConversion Img(string id) => ImageConversions.All().Single(c => c.Id == id);
    private static IConversion Doc(string id) => PdfConversions.All().Single(c => c.Id == id);

    [Fact]
    public void Catalog_routes_new_extensions()
    {
        var cat = new ConversionCatalog([.. ImageConversions.All(), .. MediaConversions.All(), .. PdfConversions.All()]);
        Assert.Equal(ConverterPage.Media, cat.PageFor("a.3gp"));
        Assert.Equal(ConverterPage.Media, cat.PageFor("a.amr"));
        Assert.Equal(ConverterPage.Images, cat.PageFor("a.psd"));
        Assert.Equal(ConverterPage.Images, cat.PageFor("a.gif"));
        Assert.Equal(ConverterPage.Documents, cat.PageFor("a.xlsx"));
        Assert.Contains(MediaConversions.All(), c => c.Id == "media.flac" && c.CanHandle("a.mts"));
        Assert.DoesNotContain(MediaConversions.All(), c => c.Id == "media.ogg" && c.CanHandle("a.ogg"));
        Assert.Contains(MediaConversions.All(), c => c.Id == "media.cover" && c.CanHandle("a.mkv"));
        Assert.DoesNotContain(MediaConversions.All(), c => c.Id == "media.cover" && c.CanHandle("a.mp3"));
    }

    [Fact]
    public async Task Image_to_icon_has_all_sizes()
    {
        var r = await Img("img.ico").RunAsync(_t.Png(withGps: false), null, CancellationToken.None);
        using var ico = new MagickImageCollection(r.OutputPath);
        Assert.Equal(6, ico.Count);
        Assert.Contains(ico, f => f.Width == 256);
        Assert.Contains(ico, f => f.Width == 16);
    }

    [Fact]
    public async Task Image_rotate_and_half()
    {
        var src = _t.Jpeg(size: 400);
        using var before = new MagickImage(src);
        var rot = await Img("tool.rotate").RunAsync(src, null, CancellationToken.None);
        var half = await Img("tool.half").RunAsync(src, null, CancellationToken.None);
        using var h = new MagickImage(half.OutputPath);
        Assert.Equal(before.Width / 2, h.Width);
        Assert.EndsWith(".jpg", rot.OutputPath);
        Assert.NotEqual(src, rot.OutputPath);
    }

    [Fact]
    public async Task Image_to_jpeg_xl_roundtrips()
    {
        var r = await Img("img.jxl").RunAsync(_t.Png(withGps: false), null, CancellationToken.None);
        using var back = new MagickImage(r.OutputPath);
        Assert.True(back.Width > 0);
    }

    [Fact]
    public async Task Markdown_to_word_and_back_to_text()
    {
        var md = Path.Combine(_t.Path, "nota.md");
        await File.WriteAllTextAsync(md, "# Título\n- uno\nTexto **fuerte**\n");
        var word = await Doc("doc.textword").RunAsync(md, null, CancellationToken.None);
        Assert.EndsWith(".docx", word.OutputPath);
        Assert.Equal(["Título", "• uno", "Texto fuerte"], OfficeConversions.WordParagraphs(word.OutputPath));
        var txt = await Doc("doc.wordtext").RunAsync(word.OutputPath, null, CancellationToken.None);
        Assert.Equal("Título" + Environment.NewLine + "• uno" + Environment.NewLine + "Texto fuerte", await File.ReadAllTextAsync(txt.OutputPath));
    }

    [Fact]
    public void Csv_parser_handles_quotes_and_semicolons()
    {
        var rows = OfficeConversions.ParseCsv("a;b;c\r\n1;\"x;y\";\"di\"\"jo\"\r\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(["1", "x;y", "di\"jo"], rows[1]);
        Assert.Equal("1,\"x,y\"", OfficeConversions.ToCsv([["1", "x,y"]]));
    }

    [Fact]
    public async Task Csv_to_excel_and_back()
    {
        var csv = Path.Combine(_t.Path, "datos.csv");
        await File.WriteAllTextAsync(csv, "nombre,edad,cp\nAna,30,01234\n\"Pérez, Luis\",41.5,\n");
        var xlsx = await Doc("doc.csvxlsx").RunAsync(csv, null, CancellationToken.None);
        var rows = OfficeConversions.ReadExcel(xlsx.OutputPath);
        Assert.Equal(["nombre", "edad", "cp"], rows[0]);
        Assert.Equal(["Ana", "30", "01234"], rows[1]);
        Assert.Equal("Pérez, Luis", rows[2][0]);
        var back = await Doc("doc.xlsxcsv").RunAsync(xlsx.OutputPath, null, CancellationToken.None);
        Assert.StartsWith("nombre,edad,cp", (await File.ReadAllTextAsync(back.OutputPath)).TrimStart('\uFEFF'));
    }

    [Fact]
    public async Task Pdf_rotates_every_page()
    {
        var txt = Path.Combine(_t.Path, "t.txt");
        await File.WriteAllTextAsync(txt, "hola");
        var pdf = await Doc("doc.topdf").RunAsync(txt, null, CancellationToken.None);
        var r = await Doc("doc.rotate").RunAsync(pdf.OutputPath, null, CancellationToken.None);
        using var d = PdfSharp.Pdf.IO.PdfReader.Open(r.OutputPath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.All(d.Pages.Cast<PdfSharp.Pdf.PdfPage>(), p => Assert.Equal(90, p.Rotate));
    }
}
