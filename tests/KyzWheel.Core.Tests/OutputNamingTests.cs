using KyzWheel.Core.Files;

namespace KyzWheel.Core.Tests;

public sealed class OutputNamingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kyz").FullName;
    public void Dispose() => Directory.Delete(@"\\?\" + _dir, true);

    private string Touch(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public void Uses_plain_name_when_free()
        => Assert.Equal(Path.Combine(_dir, "foto.webp"), OutputNaming.NextFreePath(Touch("foto.png"), ".webp"));

    [Fact]
    public void Appends_1_then_2()
    {
        var src = Touch("foto.png");
        Touch("foto.webp");
        Assert.Equal(Path.Combine(_dir, "foto_1.webp"), OutputNaming.NextFreePath(src, "webp"));
        Touch("foto_1.webp");
        Assert.Equal(Path.Combine(_dir, "foto_2.webp"), OutputNaming.NextFreePath(src, ".webp"));
    }

    [Fact]
    public void Same_extension_never_returns_source()
    {
        var src = Touch("foto.png");
        Assert.Equal(Path.Combine(_dir, "foto_1.png"), OutputNaming.NextFreePath(src, ".PNG"));
    }

    [Fact]
    public void Existing_directory_with_same_name_is_skipped()
    {
        var src = Touch("foto.png");
        Directory.CreateDirectory(Path.Combine(_dir, "foto.webp"));
        Assert.Equal(Path.Combine(_dir, "foto_1.webp"), OutputNaming.NextFreePath(src, ".webp"));
    }

    [Fact]
    public void Unicode_and_long_path_work()
    {
        var deep = Path.Combine(_dir, new string('a', 120), new string('b', 120));
        Directory.CreateDirectory(@"\\?\" + deep);
        var src = Path.Combine(deep, "fotó 🍊.png");
        File.WriteAllText(@"\\?\" + src, "x");
        var outPath = OutputNaming.NextFreePath(src, ".webp");
        Assert.Equal(Path.Combine(deep, "fotó 🍊.webp"), outPath);
        Assert.True(outPath.Length > 260);
    }
}
