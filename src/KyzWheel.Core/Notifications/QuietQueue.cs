namespace KyzWheel.Core.Notifications;

public sealed record QuietSummary(int Count, string Title, string Body);

public sealed class QuietQueue
{
    private readonly List<(string Kind, string From, string Text)> _items = [];
    private readonly Lock _gate = new();

    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    public void Add(string kind, string from, string text)
    {
        lock (_gate) _items.Add((kind, from, text));
    }

    public QuietSummary Drain()
    {
        List<(string Kind, string From, string Text)> items;
        lock (_gate)
        {
            items = [.. _items];
            _items.Clear();
        }
        if (items.Count == 0) return new QuietSummary(0, "", "");
        static string Label(string kind, int n) => kind switch
        {
            "wa" => $"{n} WhatsApp",
            "mail" => n == 1 ? "1 correo" : $"{n} correos",
            _ => n == 1 ? "1 aviso" : $"{n} avisos",
        };
        var parts = items.GroupBy(i => i.Kind).OrderBy(g => g.Key == "wa" ? 0 : g.Key == "mail" ? 1 : 2).Select(g => Label(g.Key, g.Count()));
        var from = items.Select(i => i.From).Distinct().Take(6).ToList();
        return new QuietSummary(items.Count, "Mientras jugabas: " + string.Join(" · ", parts), string.Join(", ", from) + (items.Select(i => i.From).Distinct().Count() > 6 ? "…" : ""));
    }
}
