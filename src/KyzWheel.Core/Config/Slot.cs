namespace KyzWheel.Core.Config;

public sealed record Slot(string Id, string Name, string Glyph, string ColorHex, SlotType Type, string? Hotkey, string? Target, bool Notify);
