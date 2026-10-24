namespace KyzWheel.Core.WhatsApp;

public sealed record WaThreadLine(WaFeedItem Item, int Depth, bool ParentInGroup);

public sealed record WaChatGroup(string Jid, string ChatName, bool IsGroup, DateTimeOffset Last, IReadOnlyList<WaThreadLine> Lines);

public static class WaFeedGrouper
{
    public const int MaxDepth = 2;

    public static IReadOnlyList<WaChatGroup> Group(IEnumerable<WaFeedItem> items, int maxGroups = 15)
    {
        return items.GroupBy(i => i.Jid)
            .Select(g => Build(g.Key, g.ToList()))
            .OrderByDescending(g => g.Last)
            .Take(maxGroups)
            .ToList();
    }

    private static WaChatGroup Build(string jid, List<WaFeedItem> list)
    {
        var byId = list.GroupBy(i => i.Message.Id).ToDictionary(g => g.Key, g => g.First());
        var children = new Dictionary<string, List<WaFeedItem>>();
        var roots = new List<WaFeedItem>();
        foreach (var i in byId.Values)
        {
            if (i.Message.QuoteId is { } q && q != i.Message.Id && byId.ContainsKey(q))
                (children.TryGetValue(q, out var c) ? c : children[q] = []).Add(i);
            else roots.Add(i);
        }
        var lines = new List<WaThreadLine>();
        var seen = new HashSet<string>();
        void Emit(WaFeedItem i, int depth, bool parent)
        {
            if (!seen.Add(i.Message.Id)) return;
            lines.Add(new WaThreadLine(i, Math.Min(depth, MaxDepth), parent));
            if (children.TryGetValue(i.Message.Id, out var kids))
                foreach (var k in kids.OrderBy(k => k.Message.Ts)) Emit(k, depth + 1, true);
        }
        foreach (var r in roots.OrderBy(r => r.Message.Ts)) Emit(r, 0, false);
        foreach (var rest in byId.Values.Where(i => !seen.Contains(i.Message.Id)).OrderBy(i => i.Message.Ts)) Emit(rest, 0, false);
        var first = list[0];
        return new WaChatGroup(jid, first.ChatName, first.IsGroup, list.Max(i => i.Message.Ts), lines);
    }
}
