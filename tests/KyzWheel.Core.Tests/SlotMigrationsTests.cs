using KyzWheel.Core.Config;

namespace KyzWheel.Core.Tests;

public sealed class SlotMigrationsTests
{
    private static Slot S(string id, SlotType t, bool notify = false) => new(id, id, "x", "#FFFFFF", t, null, null, notify);

    [Fact]
    public void Whatsapp_and_mail_become_one_messages_slot_in_place_of_the_first()
    {
        var r = SlotMigrations.MergeMessages([S("conv", SlotType.Converter), S("wa", SlotType.WhatsApp, true), S("clip", SlotType.Clipboard), S("mail", SlotType.Mail)]);
        Assert.Equal([SlotType.Converter, SlotType.Messages, SlotType.Clipboard], r.Select(x => x.Type));
        Assert.True(r[1].Notify);
        Assert.Equal("Mensajes", r[1].Name);
    }

    [Fact]
    public void Nothing_to_merge_keeps_slots()
    {
        var input = new[] { S("conv", SlotType.Converter) };
        Assert.Equal(input, SlotMigrations.MergeMessages(input));
    }

    [Fact]
    public void Existing_messages_slot_just_drops_the_others()
    {
        var r = SlotMigrations.MergeMessages([S("m", SlotType.Messages), S("wa", SlotType.WhatsApp)]);
        Assert.Equal([SlotType.Messages], r.Select(x => x.Type));
    }
}
