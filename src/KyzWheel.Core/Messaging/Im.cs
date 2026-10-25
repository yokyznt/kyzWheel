using System.Text.Json;
using KyzWheel.Core.WhatsApp;

namespace KyzWheel.Core.Messaging;

public sealed record ImService(string Id, string Name, string Glyph, string ColorHex);

public sealed record ImStatus(string Svc, string State, string? Need, string? Hint, string? Qr, string? Me, string? Error)
{
    public bool Open => State == "open";
}

public sealed record ImSticker(string Id, string Path);

public static class Im
{
    public static readonly ImService WhatsApp = new("whatsapp", "WhatsApp", "\uE8BD", "#25D366");
    public static readonly ImService Telegram = new("telegram", "Telegram", "\uE724", "#2AABEE");
    public static readonly ImService Discord = new("discord", "Discord", "\uE7FC", "#5865F2");
    public static readonly ImService Instagram = new("instagram", "Instagram", "\uE722", "#E1306C");

    public static readonly IReadOnlyList<ImService> All = [WhatsApp, Telegram, Discord, Instagram];

    public static ImService Find(string? id) => All.FirstOrDefault(s => s.Id == id) ?? WhatsApp;

    public static string Key(string svc, string chatId) => svc + ":" + chatId;

    public static (string Svc, string ChatId) Split(string key)
    {
        var i = key.IndexOf(':');
        if (i <= 0 || All.All(s => s.Id != key[..i] || s == WhatsApp)) return ("whatsapp", key);
        return (key[..i], key[(i + 1)..]);
    }

    private static string? S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static ImStatus Status(JsonElement e) => new(S(e, "svc") ?? "", S(e, "state") ?? "off", S(e, "need"), S(e, "hint"), S(e, "qr"), S(e, "me"), S(e, "error"));

    public static WaFeedItem Item(JsonElement e)
    {
        var svc = S(e, "svc") ?? "";
        var chat = S(e, "chatId") ?? "";
        var ts = e.TryGetProperty("ts", out var t) && t.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(t.GetInt64()) : DateTimeOffset.MinValue;
        var msg = new WaMessage(S(e, "id") ?? "", e.TryGetProperty("fromMe", out var f) && f.ValueKind == JsonValueKind.True, S(e, "text") ?? "", ts, null, S(e, "sender"));
        return new WaFeedItem(Key(svc, chat), S(e, "chatName") ?? svc, e.TryGetProperty("isGroup", out var g) && g.ValueKind == JsonValueKind.True, msg);
    }

    public static string ServiceOf(string feedJid) => Split(feedJid).Svc;
}
