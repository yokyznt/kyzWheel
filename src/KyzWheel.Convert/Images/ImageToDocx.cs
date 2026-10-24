using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ImageMagick;
using KyzWheel.Core.Files;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace KyzWheel.Convert.Images;

internal sealed class ImageToDocx : IConversion
{
    private const long EmuPerPixel = 9525;
    private const long MaxWidthEmu = 5943600;

    public string Id => "img.docx";
    public string Label => "DOCX";
    public string Glyph => "\uE8A5";
    public ConverterPage Page => ConverterPage.Images;

    public bool CanHandle(string path) => ImageConversions.IsImage(path);

    public Task<ConversionResult> RunAsync(string path, IProgress<double>? progress, CancellationToken ct) => Task.Run(() =>
    {
        using var images = ImageConversions.Load(path);
        var img = images[0];
        var png = img.ToByteArray(MagickFormat.Png);
        var output = OutputNaming.NextFreePath(path, ".docx");
        var tmp = output + ".kyztmp";
        using (var doc = WordprocessingDocument.Create(tmp, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var part = main.AddImagePart(ImagePartType.Png);
            using (var ms = new MemoryStream(png)) part.FeedData(ms);
            var cx = (long)img.Width * EmuPerPixel;
            var cy = (long)img.Height * EmuPerPixel;
            if (cx > MaxWidthEmu)
            {
                cy = cy * MaxWidthEmu / cx;
                cx = MaxWidthEmu;
            }
            main.Document = new Document(new Body(new Paragraph(new Run(Drawing(main.GetIdOfPart(part), cx, cy, Path.GetFileName(path))))));
            main.Document.Save();
        }
        File.Move(tmp, output);
        progress?.Report(1);
        return ImageConversions.Result(path, output);
    }, ct);

    private static Drawing Drawing(string relId, long cx, long cy, string name) =>
        new(new DW.Inline(
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.DocProperties { Id = 1U, Name = name },
            new A.Graphic(new A.GraphicData(
                new PIC.Picture(
                    new PIC.NonVisualPictureProperties(
                        new PIC.NonVisualDrawingProperties { Id = 0U, Name = name },
                        new PIC.NonVisualPictureDrawingProperties()),
                    new PIC.BlipFill(new A.Blip { Embed = relId }, new A.Stretch(new A.FillRectangle())),
                    new PIC.ShapeProperties(
                        new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = cx, Cy = cy }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });
}
