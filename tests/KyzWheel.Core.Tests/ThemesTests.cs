using System.Text.RegularExpressions;
using KyzWheel.Core.Config;
using KyzWheel.Core.Theming;

namespace KyzWheel.Core.Tests;

public sealed class ThemesTests
{
    [Fact]
    public void Has_several_themes_with_unique_ids_and_liquid_default()
    {
        Assert.True(Themes.All.Count >= 6);
        Assert.Equal(Themes.All.Count, Themes.All.Select(t => t.Id).Distinct().Count());
        Assert.Equal("liquid", Themes.All[0].Id);
        Assert.Equal("liquid", AppConfig.Default.ThemeId);
    }

    [Fact]
    public void Unknown_id_falls_back_to_default() => Assert.Equal("liquid", Themes.Find("nope").Id);

    [Fact]
    public void All_colors_are_valid_hex()
    {
        var hex = new Regex("^#([0-9A-F]{6}|[0-9A-F]{8})$");
        foreach (var t in Themes.All)
            foreach (var c in new[] { t.Accent, t.SliceTop, t.SliceBottom, t.SliceStroke, t.Sheen, t.HubTop, t.HubBottom, t.Rim, t.PanelTint, t.PanelEdge, t.Glow })
                Assert.Matches(hex, c);
    }

    [Fact]
    public void Theme_survives_config_roundtrip()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("kyzt").FullName, "c.json");
        var store = new ConfigStore(path);
        store.Save(AppConfig.Default with { ThemeId = "neon" });
        Assert.Equal("neon", store.Load().ThemeId);
    }
}
