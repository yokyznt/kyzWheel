using KyzWheel.Core.Config;

namespace KyzWheel.Core.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kyz").FullName;
    private string PathOf => Path.Combine(_dir, "config.json");

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Default_has_three_slots_and_hotkey()
    {
        var c = AppConfig.Default;
        Assert.Equal("Ctrl+Alt+Space", c.GlobalHotkey);
        Assert.Equal(new[] { SlotType.Messages, SlotType.Converter, SlotType.Clipboard, SlotType.Phone }, c.Slots.Select(s => s.Type));
        Assert.True(c.StartWithWindows);
    }

    [Fact]
    public void Missing_file_returns_default_and_writes_it()
    {
        var c = new ConfigStore(PathOf).Load();
        Assert.Equal(4, c.Slots.Count);
        Assert.True(File.Exists(PathOf));
    }

    [Fact]
    public void Roundtrip_preserves_values()
    {
        var store = new ConfigStore(PathOf);
        var c = AppConfig.Default with { GlobalHotkey = "Alt+Q", AccentHex = "#FF00AA" };
        store.Save(c);
        var back = store.Load();
        Assert.Equal("Alt+Q", back.GlobalHotkey);
        Assert.Equal("#FF00AA", back.AccentHex);
        Assert.Equal(c.Slots, back.Slots);
        Assert.Contains("\"Messages\"", File.ReadAllText(PathOf));
    }

    [Fact]
    public void Corrupt_file_is_backed_up_as_bak_and_default_returned()
    {
        File.WriteAllText(PathOf, "{ not json");
        var c = new ConfigStore(PathOf).Load();
        Assert.Equal("Ctrl+Alt+Space", c.GlobalHotkey);
        var backups = Directory.GetFiles(Path.GetDirectoryName(PathOf)!, Path.GetFileName(PathOf) + ".*.bak");
        Assert.Single(backups);
        Assert.Equal("{ not json", File.ReadAllText(backups[0]));
    }

    [Fact]
    public void Invalid_config_on_disk_falls_back_to_default()
    {
        var store = new ConfigStore(PathOf);
        File.WriteAllText(PathOf, "{\"Version\":1,\"GlobalHotkey\":\"Alt+Q\",\"Slots\":[],\"AccentHex\":\"#FF8A3D\",\"StartWithWindows\":true}");
        Assert.Equal(4, store.Load().Slots.Count);
    }

    [Fact]
    public void Save_is_atomic_via_tmp_then_replace()
    {
        var store = new ConfigStore(PathOf);
        store.Save(AppConfig.Default);
        store.Save(AppConfig.Default with { GlobalHotkey = "Alt+W" });
        Assert.False(File.Exists(PathOf + ".tmp"));
        Assert.Equal("Alt+W", store.Load().GlobalHotkey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void Validate_rejects_slot_count(int n)
    {
        var slots = Enumerable.Range(0, n).Select(i => AppConfig.Default.Slots[0] with { Id = "s" + i, Hotkey = null }).ToList();
        Assert.NotEmpty(ConfigStore.Validate(AppConfig.Default with { Slots = slots }));
    }

    [Fact]
    public void Validate_accepts_default()
    {
        Assert.Empty(ConfigStore.Validate(AppConfig.Default));
    }

    [Fact]
    public void Validate_rejects_duplicate_ids_and_duplicate_hotkeys()
    {
        var s = AppConfig.Default.Slots;
        var dupId = AppConfig.Default with { Slots = [s[0], s[1] with { Id = s[0].Id }] };
        var dupKey = AppConfig.Default with { Slots = [s[0] with { Hotkey = "Alt+1" }, s[1] with { Hotkey = "alt + 1" }] };
        var clashGlobal = AppConfig.Default with { Slots = [s[0] with { Hotkey = "Ctrl+Alt+Space" }] };
        Assert.NotEmpty(ConfigStore.Validate(dupId));
        Assert.NotEmpty(ConfigStore.Validate(dupKey));
        Assert.NotEmpty(ConfigStore.Validate(clashGlobal));
    }
}
