namespace KyzWheel.Core.Config;

public static class SlotMigrations
{
    public static IReadOnlyList<Slot> MergeMessages(IReadOnlyList<Slot> slots)
    {
        static bool Old(Slot s) => s.Type is SlotType.WhatsApp or SlotType.Mail;
        if (!slots.Any(Old)) return slots;
        var hasMessages = slots.Any(s => s.Type == SlotType.Messages);
        var notify = slots.Where(Old).Any(s => s.Notify);
        var result = new List<Slot>();
        foreach (var s in slots)
        {
            if (!Old(s))
            {
                result.Add(s);
                continue;
            }
            if (hasMessages) continue;
            hasMessages = true;
            result.Add(new Slot("messages", "Mensajes", "\uE8F2", "#34D399", SlotType.Messages, s.Hotkey, null, notify));
        }
        return result;
    }
}
