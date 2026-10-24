using KyzWheel.Core.Input;

namespace KyzWheel.Core.Tests;

public sealed class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Shift+Space", "Ctrl+Shift+Space")]
    [InlineData("shift + ctrl + space", "Ctrl+Shift+Space")]
    [InlineData("Alt+Q", "Alt+Q")]
    [InlineData("Mouse4", "Mouse4")]
    [InlineData("Ctrl+Mouse5", "Ctrl+Mouse5")]
    [InlineData("MouseMiddle", "MouseMiddle")]
    [InlineData("Ctrl+F12", "Ctrl+F12")]
    [InlineData("Win+Shift+W", "Shift+Win+W")]
    [InlineData("Alt+1", "Alt+1")]
    public void Roundtrip(string input, string canonical)
    {
        Assert.True(Hotkey.TryParse(input, out var h));
        Assert.Equal(canonical, h.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Foo+K")]
    [InlineData("Ctrl+A+B")]
    public void Invalid(string s) => Assert.False(Hotkey.TryParse(s, out _));

    [Theory]
    [InlineData("Win+L")]
    [InlineData("Ctrl+Alt+Delete")]
    [InlineData("Alt+Tab")]
    [InlineData("Alt+F4")]
    [InlineData("Q")]
    public void Reserved(string s)
    {
        Assert.True(Hotkey.TryParse(s, out var h));
        Assert.True(Hotkey.IsReserved(h));
    }

    [Fact]
    public void Default_is_not_reserved()
    {
        Assert.True(Hotkey.TryParse("Ctrl+Shift+Space", out var h));
        Assert.False(Hotkey.IsReserved(h));
        Assert.Equal(Mods.Ctrl | Mods.Shift, h.Mods);
        Assert.Equal(0x20, h.VirtualKey);
    }

    [Fact]
    public void Modifier_only_combo_parses_and_roundtrips()
    {
        Assert.True(Hotkey.TryParse("Ctrl+Shift+Alt", out var h));
        Assert.Equal(Mods.Ctrl | Mods.Shift | Mods.Alt, h.Mods);
        Assert.Equal(0, h.VirtualKey);
        Assert.True(h.ModifierOnly);
        Assert.Equal("Ctrl+Alt+Shift", h.ToString());
        Assert.False(Hotkey.IsReserved(h));
    }

    [Fact]
    public void Single_modifier_alone_is_not_valid()
    {
        Assert.False(Hotkey.TryParse("Ctrl", out _));
        Assert.False(Hotkey.TryParse("Ctrl+Shift", out _));
    }
}
