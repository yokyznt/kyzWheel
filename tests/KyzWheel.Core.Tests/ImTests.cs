using System.Text.Json;
using KyzWheel.Core.Messaging;

namespace KyzWheel.Core.Tests;

public sealed class ImTests
{
    [Fact]
    public void Parses_items_into_feed_items_keyed_by_service()
    {
        var e = JsonDocument.Parse("""{"svc":"discord","chatId":"123","chatName":"Beto","isGroup":true,"id":"m1","fromMe":false,"sender":"Ana","text":"hola","ts":1700000000}""").RootElement;
        var i = Im.Item(e);
        Assert.Equal("discord:123", i.Jid);
        Assert.Equal("Beto", i.ChatName);
        Assert.True(i.IsGroup);
        Assert.Equal("Ana", i.Message.Sender);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), i.Message.Ts);
        Assert.Equal("discord", Im.ServiceOf(i.Jid));
    }

    [Fact]
    public void Split_keeps_whatsapp_jids_whole()
    {
        Assert.Equal(("whatsapp", "521555@s.whatsapp.net"), Im.Split("521555@s.whatsapp.net"));
        Assert.Equal(("telegram", "-100:5"), Im.Split("telegram:-100:5"));
        Assert.Equal(("whatsapp", "foo:bar"), Im.Split("foo:bar"));
    }

    [Fact]
    public void Status_parses_need_and_qr()
    {
        var s = Im.Status(JsonDocument.Parse("""{"svc":"telegram","state":"need","need":"password","hint":"gato"}""").RootElement);
        Assert.Equal("password", s.Need);
        Assert.False(s.Open);
        Assert.Equal(4, Im.All.Count);
    }
}
