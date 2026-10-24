using System.Text.Json;
using KyzWheel.Core.WhatsApp;

namespace KyzWheel.Core.Tests;

public sealed class WaModelsTests
{
    private static JsonElement J(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void Chat_from_json()
    {
        var c = WaJson.Chat(J("""{"jid":"52@s.whatsapp.net","name":"Ana","lastText":"hola","lastTs":1761235200,"unread":3,"isGroup":false}"""));
        Assert.Equal(new WaChat("52@s.whatsapp.net", "Ana", "hola", DateTimeOffset.FromUnixTimeSeconds(1761235200), 3, false), c);
    }

    [Fact]
    public void Chat_without_name_uses_jid()
        => Assert.Equal("1@g.us", WaJson.Chat(J("""{"jid":"1@g.us","isGroup":true}""")).Name);

    [Fact]
    public void Message_with_file()
    {
        var m = WaJson.Message(J("""{"id":"A1","fromMe":true,"text":"📄 a.pdf","ts":10,"file":{"fileName":"a.pdf","mime":"application/pdf","size":1200},"sender":null}"""), "x@s.whatsapp.net");
        Assert.True(m.FromMe);
        Assert.Equal(new WaFile("a.pdf", "application/pdf", 1200, "A1", "x@s.whatsapp.net"), m.File);
        Assert.Null(m.Sender);
    }

    [Fact]
    public void Recent_file_list()
    {
        var l = WaJson.List(J("""[{"jid":"x","chatName":"Ana","messageId":"m","ts":5,"fileName":"f.jpg","mime":"image/jpeg","size":9}]"""), e => WaJson.File(e));
        Assert.Equal("Ana", l.Single().ChatName);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(5), l.Single().Ts);
    }

    [Fact]
    public void Status_with_me()
    {
        var s = WaJson.Status(J("""{"state":"open","me":{"jid":"52:1@s.whatsapp.net","name":"Yo"},"unread":4}"""));
        Assert.Equal(new WaStatus("open", "52:1@s.whatsapp.net", "Yo", 4), s);
    }
}
