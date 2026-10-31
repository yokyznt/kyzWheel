namespace KyzWheel.Convert;

public sealed class ConversionCatalog(IEnumerable<IConversion> all)
{
    private static readonly HashSet<string> ImageExts = [.. Images.ImageConversions.Extensions];
    private static readonly HashSet<string> MediaExts = [.. Media.MediaConversions.Extensions.Where(e => e != "gif")];
    private static readonly HashSet<string> DocExts = [.. Documents.PdfConversions.Extensions];

    private readonly IReadOnlyList<IConversion> _all = all.ToList();

    public IReadOnlyList<IConversion> All => _all;

    public ConverterPage PageFor(string path)
    {
        var e = FileGuards.Ext(path);
        if (ImageExts.Contains(e)) return ConverterPage.Images;
        if (MediaExts.Contains(e)) return ConverterPage.Media;
        if (DocExts.Contains(e)) return ConverterPage.Documents;
        return ConverterPage.Tools;
    }

    public IReadOnlyList<IConversion> ActionsFor(ConverterPage page, string path) => ActionsFor(page, [path]);

    public IReadOnlyList<IConversion> ActionsFor(ConverterPage page, IReadOnlyList<string> paths)
        => _all.Where(c => c.Page == page && paths.Count > 0 && paths.All(c.CanHandle)).ToList();

    public IReadOnlyList<ConverterPage> PagesOrder(string path) => PagesOrder([path]);

    public IReadOnlyList<ConverterPage> PagesOrder(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return [];
        var best = PageFor(paths[0]);
        var order = new List<ConverterPage> { best };
        order.AddRange(new[] { ConverterPage.Tools, ConverterPage.Images, ConverterPage.Media, ConverterPage.Documents }.Where(p => p != best));
        return order.Where(p => ActionsFor(p, paths).Count > 0).ToList();
    }
}
