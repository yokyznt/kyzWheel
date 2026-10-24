namespace KyzWheel.Core.Config;

public sealed record AppConfig(int Version, string GlobalHotkey, IReadOnlyList<Slot> Slots, string AccentHex, bool StartWithWindows)
{
    public IReadOnlyList<MailAccount> MailAccounts { get; init; } = [];

    public const int MinSlots = 1;
    public const int MaxSlots = 8;

    public static AppConfig Default { get; } = new(
        1,
        "Ctrl+Shift+Space",
        [
            new Slot("whatsapp", "WhatsApp", "\uE8BD", "#25D366", SlotType.WhatsApp, null, null, true),
            new Slot("mail", "Gmail", "\uE715", "#EA4335", SlotType.Mail, null, null, true),
            new Slot("converter", "Convertidor", "\uE8AB", "#FF8A3D", SlotType.Converter, null, null, false),
        ],
        "#FF8A3D",
        true);

    public bool Equals(AppConfig? other) =>
        other is not null && Version == other.Version && GlobalHotkey == other.GlobalHotkey &&
        AccentHex == other.AccentHex && StartWithWindows == other.StartWithWindows && Slots.SequenceEqual(other.Slots) &&
        (MailAccounts ?? []).SequenceEqual(other.MailAccounts ?? []);

    public override int GetHashCode() => HashCode.Combine(Version, GlobalHotkey, AccentHex, StartWithWindows, Slots.Count);
}
