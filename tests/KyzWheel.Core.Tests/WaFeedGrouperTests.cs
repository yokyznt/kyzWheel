using KyzWheel.Core.WhatsApp;

namespace KyzWheel.Core.Tests;

public sealed class WaFeedGrouperTests
{
    private static WaFeedItem M(string jid, string id, int ts, string? quote = null, string text = "x")
        => new(jid, jid.Split('@')[0], jid.EndsWith("@g.us"), new WaMessage(id, false, text, DateTimeOffset.FromUnixTimeSeconds(ts), null, null) { QuoteId = quote });

    [Fact]
    public void Groups_by_chat_newest_chat_first_messages_oldest_first()
    {
        var g = WaFeedGrouper.Group([M("a@s", "a2", 50), M("b@s", "b1", 40), M("a@s", "a1", 10), M("c@g.us", "c1", 60)]);
        Assert.Equal(["c@g.us", "a@s", "b@s"], g.Select(x => x.Jid));
        Assert.Equal(["a1", "a2"], g[1].Lines.Select(l => l.Item.Message.Id));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(50), g[1].Last);
    }

    [Fact]
    public void Replies_nest_under_their_parent()
    {
        var g = WaFeedGrouper.Group([M("a@s", "q", 10), M("a@s", "other", 20), M("a@s", "r1", 30, "q"), M("a@s", "r2", 40, "r1"), M("a@s", "r3", 50, "r2")]);
        var lines = g[0].Lines;
        Assert.Equal(["q", "r1", "r2", "r3", "other"], lines.Select(l => l.Item.Message.Id));
        Assert.Equal([0, 1, 2, 2, 0], lines.Select(l => l.Depth));
        Assert.All(lines.Where(l => l.Depth > 0), l => Assert.True(l.ParentInGroup));
    }

    [Fact]
    public void Reply_to_missing_message_stays_at_root_with_flag_off()
    {
        var g = WaFeedGrouper.Group([M("a@s", "r", 10, "gone")]);
        Assert.Equal(0, g[0].Lines[0].Depth);
        Assert.False(g[0].Lines[0].ParentInGroup);
    }

    [Fact]
    public void Caps_groups()
    {
        var items = Enumerable.Range(0, 20).Select(i => M($"c{i}@s", "m" + i, i)).ToList();
        Assert.Equal(5, WaFeedGrouper.Group(items, 5).Count);
    }
}
