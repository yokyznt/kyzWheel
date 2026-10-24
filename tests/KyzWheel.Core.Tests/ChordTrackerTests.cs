using KyzWheel.Core.Input;

namespace KyzWheel.Core.Tests;

public sealed class ChordTrackerTests
{
    private const int Ctrl = 0xA2, Shift = 0xA0, Alt = 0xA4, Space = 0x20, K = 0x4B;
    private readonly ChordTracker _t = new();

    public ChordTrackerTests() => _t.Set("global", new Hotkey(Mods.Ctrl | Mods.Shift, Space, null));

    [Fact]
    public void Fires_in_normal_order_and_swallows_main_key()
    {
        Assert.Null(_t.Down(Ctrl).Fired);
        Assert.Null(_t.Down(Shift).Fired);
        var r = _t.Down(Space);
        Assert.Equal("global", r.Fired);
        Assert.True(r.Swallow);
        Assert.True(_t.Up(Space).Swallow);
    }

    [Fact]
    public void Fires_when_main_key_lands_first()
    {
        Assert.Null(_t.Down(Space).Fired);
        Assert.Null(_t.Down(Ctrl).Fired);
        Assert.Equal("global", _t.Down(Shift).Fired);
    }

    [Fact]
    public void Fires_once_while_held_even_with_repeats()
    {
        _t.Down(Ctrl);
        _t.Down(Shift);
        Assert.Equal("global", _t.Down(Space).Fired);
        Assert.Null(_t.Down(Space).Fired);
        Assert.True(_t.Down(Space).Swallow);
        _t.Up(Space);
        Assert.Equal("global", _t.Down(Space).Fired);
    }

    [Fact]
    public void Extra_modifier_does_not_match()
    {
        _t.Down(Ctrl);
        _t.Down(Shift);
        _t.Down(Alt);
        Assert.Null(_t.Down(Space).Fired);
    }

    [Fact]
    public void Left_and_right_modifiers_are_the_same()
    {
        _t.Down(0xA3);
        _t.Down(0xA1);
        Assert.Equal("global", _t.Down(Space).Fired);
    }

    [Fact]
    public void Released_key_resets()
    {
        _t.Down(Ctrl);
        _t.Up(Ctrl);
        _t.Down(Shift);
        Assert.Null(_t.Down(Space).Fired);
    }

    [Fact]
    public void Stale_keys_can_be_pruned()
    {
        _t.Down(Alt);
        _t.Prune(vk => vk != 0x12);
        _t.Down(Ctrl);
        _t.Down(Shift);
        Assert.Equal("global", _t.Down(Space).Fired);
    }

    [Fact]
    public void Multiple_hotkeys()
    {
        _t.Set("slot:x", new Hotkey(Mods.Alt, K, null));
        _t.Down(Alt);
        Assert.Equal("slot:x", _t.Down(K).Fired);
    }

    [Fact]
    public void Modifier_only_fires_on_release_after_all_held()
    {
        var t = new ChordTracker();
        t.Set("m", new Hotkey(Mods.Ctrl | Mods.Shift | Mods.Alt, 0, null));
        Assert.Null(t.Down(Ctrl).Fired);
        Assert.Null(t.Down(Alt).Fired);
        var d = t.Down(Shift);
        Assert.Null(d.Fired);
        Assert.False(d.Swallow);
        var u = t.Up(Alt);
        Assert.Equal("m", u.Fired);
        Assert.False(u.Swallow);
        Assert.Null(t.Up(Ctrl).Fired);
        Assert.Null(t.Up(Shift).Fired);
    }

    [Fact]
    public void Modifier_only_cancelled_by_other_key()
    {
        var t = new ChordTracker();
        t.Set("m", new Hotkey(Mods.Ctrl | Mods.Shift | Mods.Alt, 0, null));
        t.Down(Ctrl);
        t.Down(Shift);
        t.Down(Alt);
        t.Down(K);
        t.Up(K);
        Assert.Null(t.Up(Alt).Fired);
    }

    [Fact]
    public void Modifier_only_needs_exactly_those_mods()
    {
        var t = new ChordTracker();
        t.Set("m", new Hotkey(Mods.Ctrl | Mods.Shift | Mods.Alt, 0, null));
        t.Down(Ctrl);
        t.Down(Shift);
        Assert.Null(t.Up(Shift).Fired);
    }
}
