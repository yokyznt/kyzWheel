namespace KyzWheel.App.Wheel;

public sealed record WheelItem(string Id, string Label, string Glyph, string ColorHex, int Badge = 0, object? Tag = null);

public sealed record WheelPage(string Title, IReadOnlyList<WheelItem> Items, object? Tag = null);

public enum WheelMode { Root, Drag }
