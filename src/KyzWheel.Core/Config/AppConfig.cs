namespace KyzWheel.Core.Config;

public sealed record AppConfig(int Version, string GlobalHotkey, IReadOnlyList<Slot> Slots, string AccentHex, bool StartWithWindows)
{
    public IReadOnlyList<MailAccount> MailAccounts { get; init; } = [];

    public string ThemeId { get; init; } = KyzWheel.Core.Theming.Themes.DefaultId;

    public bool PhoneSlotOffered { get; init; }

    public bool MessagesMerged { get; init; }

    public string? GoogleClientId { get; init; }

    public string? GoogleClientSecret { get; init; }

    public const int MinSlots = 1;
    public const int MaxSlots = 8;

    public static AppConfig Default { get; } = new(
        1,
        "Ctrl+Alt+Shift",
        [
            new Slot("messages", "Mensajes", "\uE8F2", "#34D399", SlotType.Messages, null, null, true),
            new Slot("converter", "Convertidor", "\uE8AB", "#FF8A3D", SlotType.Converter, null, null, false),
            new Slot("clipboard", "Portapapeles", "\uE77F", "#A78BFA", SlotType.Clipboard, null, null, false),
            new Slot("phone", "Celular", "\uE8EA", "#60A5FA", SlotType.Phone, null, null, false),
        ],
        "#FF8A3D",
        true);

    public bool Equals(AppConfig? other) =>
        other is not null && Version == other.Version && GlobalHotkey == other.GlobalHotkey &&
        AccentHex == other.AccentHex && ThemeId == other.ThemeId && GoogleClientId == other.GoogleClientId && GoogleClientSecret == other.GoogleClientSecret && StartWithWindows == other.StartWithWindows && Slots.SequenceEqual(other.Slots) &&
        (MailAccounts ?? []).SequenceEqual(other.MailAccounts ?? []);

    public override int GetHashCode() => HashCode.Combine(Version, GlobalHotkey, AccentHex, StartWithWindows, Slots.Count);
}
