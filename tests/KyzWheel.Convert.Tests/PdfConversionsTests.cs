using DocumentFormat.OpenXml.Packaging;
using KyzWheel.Convert.Documents;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace KyzWheel.Convert.Tests;

public sealed class PdfConversionsTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    private static IConversion Get(string id) => PdfConversions.All().Single(c => c.Id == id);

    private string MakePdf(string name, int pages)
    {
        PdfConversions.EnsureFonts();
        var p = Path.Combine(_t.Path, name);
        using var doc = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            var page = doc.AddPage();
            using var g = XGraphics.FromPdfPage(page);
            g.DrawString($"Pagina {i + 1} kyzWheel", new XFont("Arial", 24), XBrushes.Black, new XPoint(72, 120));
            g.DrawRectangle(XBrushes.SteelBlue, 72, 200, 300, 200);
        }
        doc.Save(p);
        return p;
    }

    private static int Pages(string path)
    {
        using var d = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return d.PageCount;
    }

    [Fact]
    public async Task Split_three_pages_into_folder()
    {
        var r = await Get("doc.split").RunAsync(MakePdf("libro.pdf", 3), null, CancellationToken.None);
        Assert.True(Directory.Exists(r.OutputPath));
        Assert.EndsWith("libro_paginas", r.OutputPath);
        var files = Directory.GetFiles(r.OutputPath, "*.pdf").Order().ToList();
        Assert.Equal(3, files.Count);
        Assert.All(files, f => Assert.Equal(1, Pages(f)));
    }

    [Fact]
    public async Task Merge_keeps_selection_order_and_sums_pages()
    {
        var a = MakePdf("a.pdf", 2);
        var b = MakePdf("b.pdf", 3);
        var r = await PdfConversions.MergeAsync([b, a], CancellationToken.None);
        Assert.Equal(5, Pages(r.OutputPath));
        Assert.EndsWith("b_unido.pdf", r.OutputPath);
    }

    [Fact]
    public async Task To_images_one_png_per_page()
    {
        var r = await Get("doc.toimages").RunAsync(MakePdf("x.pdf", 2), null, CancellationToken.None);
        var pngs = Directory.GetFiles(r.OutputPath, "*.png");
        Assert.Equal(2, pngs.Length);
        Assert.All(pngs, p => Assert.True(new FileInfo(p).Length > 1000));
    }

    [Fact]
    public async Task Compress_not_bigger()
    {
        var src = MakePdf("c.pdf", 4);
        var r = await Get("doc.compress").RunAsync(src, null, CancellationToken.None);
        Assert.True(r.SizeAfter <= r.SizeBefore);
    }

    [Fact]
    public async Task To_word_has_paragraphs_from_text()
    {
        var r = await Get("doc.toword").RunAsync(MakePdf("w.pdf", 2), null, CancellationToken.None);
        using var doc = WordprocessingDocument.Open(r.OutputPath, false);
        var text = doc.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Contains("kyzWheel", text);
    }

    [Fact]
    public async Task Images_to_pdf_one_page_each()
    {
        var a = _t.Png("1.png");
        var b = _t.Png("2.png");
        var r = await PdfConversions.ImagesToPdfAsync([a, b], CancellationToken.None);
        Assert.Equal(2, Pages(r.OutputPath));
    }

    [Fact]
    public async Task Text_to_pdf()
    {
        var t = Path.Combine(_t.Path, "nota.txt");
        File.WriteAllText(t, "hola\nmundo");
        var r = await Get("doc.topdf").RunAsync(t, null, CancellationToken.None);
        Assert.Equal(1, Pages(r.OutputPath));
    }

    [Fact]
    public void Offers_pdf_actions_only_for_pdf()
    {
        Assert.True(Get("doc.split").CanHandle("a.PDF"));
        Assert.False(Get("doc.split").CanHandle("a.png"));
        Assert.True(Get("doc.topdf").CanHandle("a.txt"));
        Assert.False(Get("doc.topdf").CanHandle("a.pdf"));
    }
}
