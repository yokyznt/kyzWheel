using KyzWheel.Core.Phone;

namespace KyzWheel.Core.Tests;

public sealed class PhoneCliTests
{
    [Fact]
    public void Parses_id_name_lines_and_ignores_counts()
    {
        var d = PhoneCli.ParseDevices("a1b2c3d4_e5f6_7788_99aa_bbccddeeff00 Nothing Phone (2a)\r\n1 dispositivo encontrado\r\n");
        Assert.Single(d);
        Assert.Equal("a1b2c3d4_e5f6_7788_99aa_bbccddeeff00", d[0].Id);
        Assert.Equal("Nothing Phone (2a)", d[0].Name);
    }

    [Fact]
    public void No_devices()
    {
        Assert.Empty(PhoneCli.ParseDevices("0 dispositivos encontrados\r\n"));
        Assert.Empty(PhoneCli.ParseDevices("0 devices found\n"));
        Assert.Empty(PhoneCli.ParseDevices(""));
    }

    [Fact]
    public void Share_repeats_flag_per_file()
        => Assert.Equal(["-d", "dev1", "--share", @"C:\a b\foto.png", "--share", @"C:\x.pdf"], PhoneCli.Share("dev1", [@"C:\a b\foto.png", @"C:\x.pdf"]));

    [Fact]
    public void Other_commands()
    {
        Assert.Equal(["-d", "d", "--send-clipboard"], PhoneCli.SendClipboard("d"));
        Assert.Equal(["-d", "d", "--share-text", "hola"], PhoneCli.ShareText("d", "hola"));
        Assert.Equal(["-d", "d", "--ring"], PhoneCli.Ring("d"));
        Assert.Equal(["-d", "d", "--ping-msg", "kyzWheel"], PhoneCli.Ping("d", "kyzWheel"));
        Assert.Equal(["-a", "--id-name-only"], PhoneCli.ListAvailable);
    }
}
