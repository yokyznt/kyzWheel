using System.Text.Json;
using System.Text.Json.Serialization;

namespace KyzWheel.Core.WhatsApp;

public sealed record WaChat(string Jid, string Name, string LastText, DateTimeOffset LastTs, int Unread, bool IsGroup)
{
    public string? Phone { get; init; }
}

public sealed record WaFile(string FileName, string Mime, long Size, string MessageId, string Jid, string? ChatName = null, DateTimeOffset? Ts = null)
{
    public string Kind { get; init; } = "document";
    public string? Thumb { get; init; }
    public int Seconds { get; init; }
    public bool Ptt { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

public sealed record WaMessage(string Id, bool FromMe, string Text, DateTimeOffset Ts, WaFile? File, string? Sender)
{
    public string Caption { get; init; } = "";
}

public sealed record WaContact(string Jid, string Name);

public sealed record WaStatus(string State, string? MeJid, string? MeName, int Unread);

public static class WaJson
{
    private static DateTimeOffset Ts(JsonElement e, string name)
        => e.TryGetProperty(name, out var t) && t.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(t.GetInt64()) : DateTimeOffset.MinValue;

    private static string S(JsonElement e, string name, string fallback = "")
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : fallback;

    private static int I(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static long L(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    private static bool B(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? N(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static WaChat Chat(JsonElement e) => new(S(e, "jid"), S(e, "name", S(e, "jid")), S(e, "lastText"), Ts(e, "lastTs"), I(e, "unread"), B(e, "isGroup")) { Phone = N(e, "phone") };

    public static WaFile File(JsonElement e, string? jid = null, string? messageId = null)
        => new(S(e, "fileName"), S(e, "mime"), L(e, "size"), messageId ?? S(e, "messageId"), jid ?? S(e, "jid"), e.TryGetProperty("chatName", out _) ? S(e, "chatName") : null, e.TryGetProperty("ts", out _) ? Ts(e, "ts") : null)
        {
            Kind = S(e, "kind", "document"),
            Thumb = N(e, "thumb"),
            Seconds = I(e, "seconds"),
            Ptt = B(e, "ptt"),
            Width = I(e, "width"),
            Height = I(e, "height"),
        };

    public static WaMessage Message(JsonElement e, string jid)
    {
        var id = S(e, "id");
        WaFile? file = e.TryGetProperty("file", out var f) && f.ValueKind == JsonValueKind.Object ? File(f, jid, id) : null;
        return new WaMessage(id, B(e, "fromMe"), S(e, "text"), Ts(e, "ts"), file, e.TryGetProperty("sender", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null) { Caption = S(e, "caption") };
    }

    public static WaStatus Status(JsonElement e)
    {
        string? jid = null, name = null;
        if (e.TryGetProperty("me", out var me) && me.ValueKind == JsonValueKind.Object)
        {
            jid = S(me, "jid");
            name = S(me, "name");
        }
        return new WaStatus(S(e, "state", "idle"), jid, name, I(e, "unread"));
    }

    public static IReadOnlyList<T> List<T>(JsonElement e, Func<JsonElement, T> map)
        => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(map).ToList() : [];
}
