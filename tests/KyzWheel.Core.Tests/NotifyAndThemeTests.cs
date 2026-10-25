using KyzWheel.Core.Notifications;
using KyzWheel.Core.Phone;
using KyzWheel.Core.Theming;

namespace KyzWheel.Core.Tests;

public sealed class NotifyAndThemeTests
{
    private static readonly ScreenRect Main = new(0, 0, 1920, 1040);
    private static readonly ScreenRect Top = new(0, -1080, 1920, 1040);

    [Theory]
    [InlineData("top-center", 760, 16)]
    [InlineData("top-right", 1504, 16)]
    [InlineData("top-left", 16, 16)]
    [InlineData("bottom-right", 1504, 914)]
    [InlineData("bottom-center", 760, 914)]
    [InlineData("bottom-left", 16, 914)]
    [InlineData("nonsense", 760, 16)]
    public void Positions_on_primary(string pos, int x, int y)
    {
        var p = NotifyPlacement.Place([Top, Main], 1, (100, -500), "primary", pos, 400, 110);
        Assert.Equal((x, y), p);
    }

    [Fact]
    public void Monitor_choice()
    {
        Assert.Equal(-1064, NotifyPlacement.Place([Top, Main], 1, (100, -500), "cursor", "top-center", 400, 110).Y);
        Assert.Equal(-1064, NotifyPlacement.Place([Top, Main], 1, (5, 5), "1", "top-center", 400, 110).Y);
        Assert.Equal(16, NotifyPlacement.Place([Top, Main], 1, (5, -5), "2", "top-center", 400, 110).Y);
        Assert.Equal(16, NotifyPlacement.Place([Top, Main], 1, (5, -5), "9", "top-center", 400, 110).Y);
    }

    [Fact]
    public void Themes_are_distinct_in_shape_not_only_color()
    {
        Assert.Equal(Themes.All.Count, Themes.All.Select(t => t.Id).Distinct().Count());
        Assert.True(Themes.All.Count >= 10);
        var styles = Themes.All.Select(t => (t.Font, t.Gap, t.CornerOut, t.Upper)).Distinct().Count();
        Assert.True(styles >= 8, $"solo {styles} estilos distintos");
        Assert.Equal("liquid", Themes.Find("nope").Id);
    }

    [Fact]
    public void Parses_device_status_spanish_and_english()
    {
        var es = "- Nothing Phone 3a: f5518aac81594d9a93d784bfb19ff050 en 192.168.1.61 vía LAN (vinculado y accesible)\r\n- Pixel: abcd1234abcd1234 en 192.168.1.9 vía LAN (accesible)\r\n- Viejo: ffff0000ffff0000 (vinculado)\r\n3 dispositivos encontrados";
        var d = PhoneCli.ParseStatus(es);
        Assert.Equal(3, d.Count);
        Assert.Equal(new PhoneStatus("f5518aac81594d9a93d784bfb19ff050", "Nothing Phone 3a", true, true), d[0]);
        Assert.Equal(new PhoneStatus("abcd1234abcd1234", "Pixel", false, true), d[1]);
        Assert.Equal(new PhoneStatus("ffff0000ffff0000", "Viejo", true, false), d[2]);
        var en = PhoneCli.ParseStatus("- Phone: 1234567890ab (paired and reachable)\n- Tab: abcdefabcdef (reachable)");
        Assert.True(en[0].Paired && en[0].Reachable);
        Assert.False(en[1].Paired);
        Assert.Equal(["--pair", "-d", "x1"], PhoneCli.Pair("x1"));
    }
}
