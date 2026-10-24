namespace KyzWheel.App.Wheel;

public sealed record WheelItem(string Id, string Label, string Glyph, string ColorHex, int Badge = 0, object? Tag = null, IReadOnlyList<WheelItem>? Children = null);

public sealed record WheelPage(string Title, IReadOnlyList<WheelItem> Items, object? Tag = null, string? Glyph = null, string? ColorHex = null);

public sealed record MoreItems(IReadOnlyList<WheelItem> Items);

public enum WheelMode { Root, Drag, Files }

public static class WheelFit
{
    public const int MaxSlices = 8;

    public static WheelPage Fit(WheelPage page, string colorHex)
    {
        if (page.Items.Count <= MaxSlices) return page;
        var keep = page.Items.Take(MaxSlices - 1).ToList();
        var rest = page.Items.Skip(MaxSlices - 1).ToList();
        keep.Add(new WheelItem("more", $"Más ({rest.Count})", "\uE712", colorHex, 0, new MoreItems(rest)));
        return page with { Items = keep };
    }

    public static WheelPage Group(IReadOnlyList<WheelPage> pages, string title)
    {
        var total = pages.Sum(p => p.Items.Count);
        if (pages.Count == 1 || total <= MaxSlices)
            return new WheelPage(pages.Count == 1 ? pages[0].Title : title, pages.SelectMany(p => p.Items).ToList(), pages.Count == 1 ? pages[0].Tag : null);
        var cats = pages.Where(p => p.Items.Count > 0).Select((p, i) => new WheelItem("cat:" + i, p.Title, p.Glyph ?? p.Items[0].Glyph, p.ColorHex ?? p.Items[0].ColorHex, 0, p.Tag, p.Items)).ToList();
        return new WheelPage(title, cats);
    }
}
