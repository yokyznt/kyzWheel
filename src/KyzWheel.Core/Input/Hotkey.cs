using System.Globalization;

namespace KyzWheel.Core.Input;

[Flags]
public enum Mods { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

public enum MouseButton { X1, X2, Middle }

public readonly record struct Hotkey(Mods Mods, int VirtualKey, MouseButton? Mouse)
{
    private static readonly Dictionary<string, int> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20, ["Tab"] = 0x09, ["Enter"] = 0x0D, ["Esc"] = 0x1B, ["Escape"] = 0x1B,
        ["Backspace"] = 0x08, ["Delete"] = 0x2E, ["Del"] = 0x2E, ["Insert"] = 0x2D, ["Home"] = 0x24,
        ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["Left"] = 0x25, ["Up"] = 0x26,
        ["Right"] = 0x27, ["Down"] = 0x28,
    };

    private static readonly Dictionary<string, MouseButton> MouseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mouse4"] = MouseButton.X1, ["Mouse5"] = MouseButton.X2, ["MouseMiddle"] = MouseButton.Middle,
    };

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = Mods.None;
        int? vk = null;
        MouseButton? mouse = null;
        foreach (var raw in text.Split('+'))
        {
            var t = raw.Trim();
            if (t.Length == 0) return false;
            var mod = t.ToLowerInvariant() switch
            {
                "ctrl" or "control" => Mods.Ctrl,
                "alt" => Mods.Alt,
                "shift" => Mods.Shift,
                "win" or "windows" => Mods.Win,
                _ => Mods.None,
            };
            if (mod != Mods.None)
            {
                mods |= mod;
                continue;
            }
            if (vk is not null || mouse is not null) return false;
            if (MouseNames.TryGetValue(t, out var mb)) mouse = mb;
            else if (KeyCode(t) is { } code) vk = code;
            else return false;
        }
        if (vk is null && mouse is null) return false;
        hotkey = new Hotkey(mods, vk ?? 0, mouse);
        return true;
    }

    private static int? KeyCode(string t)
    {
        if (Named.TryGetValue(t, out var n)) return n;
        if (t.Length == 1 && char.IsAsciiLetterOrDigit(t[0])) return char.ToUpperInvariant(t[0]);
        if (t.Length is 2 or 3 && (t[0] is 'F' or 'f') && int.TryParse(t.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var f) && f is >= 1 and <= 24)
            return 0x70 + f - 1;
        return null;
    }

    private string KeyName()
    {
        if (Mouse is { } m) return m switch { MouseButton.X1 => "Mouse4", MouseButton.X2 => "Mouse5", _ => "MouseMiddle" };
        if (VirtualKey is >= 0x70 and <= 0x87) return "F" + (VirtualKey - 0x70 + 1).ToString(CultureInfo.InvariantCulture);
        foreach (var (name, code) in Named)
            if (code == VirtualKey && name is not ("Escape" or "Del")) return name;
        return ((char)VirtualKey).ToString();
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Mods.HasFlag(Mods.Ctrl)) parts.Add("Ctrl");
        if (Mods.HasFlag(Mods.Alt)) parts.Add("Alt");
        if (Mods.HasFlag(Mods.Shift)) parts.Add("Shift");
        if (Mods.HasFlag(Mods.Win)) parts.Add("Win");
        parts.Add(KeyName());
        return string.Join('+', parts);
    }

    public static bool IsReserved(Hotkey h)
    {
        if (h.Mouse is not null) return false;
        if (h.Mods == Mods.None) return true;
        var s = h.ToString();
        return s is "Win+L" or "Ctrl+Alt+Delete" or "Alt+Tab" or "Alt+F4" or "Ctrl+Esc" or "Alt+Esc" or "Ctrl+Shift+Esc" or "Win+D" or "Shift+Alt+Tab";
    }
}
