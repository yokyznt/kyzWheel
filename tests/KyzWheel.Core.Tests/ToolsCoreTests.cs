using KyzWheel.Core.Notifications;
using KyzWheel.Core.Projects;
using KyzWheel.Core.Text;

namespace KyzWheel.Core.Tests;

public sealed class ToolsCoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("kyzproj").FullName;
    public void Dispose() => Directory.Delete(_root, true);

    private string Project(string name, DateTime touched, string? remote = null, string? packageJson = null)
    {
        var d = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        if (remote is not null)
        {
            Directory.CreateDirectory(Path.Combine(d, ".git"));
            File.WriteAllText(Path.Combine(d, ".git", "config"), $"[core]\n\tbare = false\n[remote \"origin\"]\n\turl = {remote}\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n");
        }
        if (packageJson is not null) File.WriteAllText(Path.Combine(d, "package.json"), packageJson);
        Directory.SetLastWriteTimeUtc(d, touched);
        return d;
    }

    [Fact]
    public void Scanner_orders_by_recent_and_reads_github_and_apk_script()
    {
        Project("viejo", new DateTime(2025, 1, 1));
        Project("kyzWheel", new DateTime(2026, 10, 24), "git@github.com:yokyznt/kyzWheel.git");
        Project("garfield-apk", new DateTime(2026, 10, 20), "https://github.com/yokyznt/garfield-apk.git", "{\"scripts\":{\"android:apk\":\"node build-apk.js\"}}");
        File.WriteAllText(Path.Combine(_root, "suelto.bundle"), "x");
        var list = ProjectScanner.Scan(_root);
        Assert.Equal(["kyzWheel", "garfield-apk", "viejo"], list.Select(p => p.Name));
        Assert.Equal("https://github.com/yokyznt/kyzWheel", list[0].GitHubUrl);
        Assert.Equal("https://github.com/yokyznt/garfield-apk", list[1].GitHubUrl);
        Assert.False(list[0].HasApkScript);
        Assert.True(list[1].HasApkScript);
        Assert.Null(list[2].GitHubUrl);
    }

    [Fact]
    public void Github_url_normalizes_forms()
    {
        Assert.Equal("https://github.com/a/b", ProjectScanner.NormalizeRemote("git@github.com:a/b.git"));
        Assert.Equal("https://github.com/a/b", ProjectScanner.NormalizeRemote("https://github.com/a/b.git"));
        Assert.Equal("https://github.com/a/b", ProjectScanner.NormalizeRemote("https://token@github.com/a/b"));
        Assert.Null(ProjectScanner.NormalizeRemote("https://gitlab.com/a/b.git"));
    }

    [Fact]
    public void Translation_url_and_target()
    {
        Assert.Equal("en", Translation.TargetFor("hola, ¿cómo estás? quiero que funcione"));
        Assert.Equal("es", Translation.TargetFor("hello, how are you doing today"));
        var u = Translation.Url("a b&c", "es");
        Assert.StartsWith("https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=es&dt=t&q=", u);
        Assert.EndsWith("a%20b%26c", u);
    }

    [Fact]
    public void Translation_parses_segments()
    {
        var json = "[[[\"Hola \",\"Hello \",null,null,10],[\"mundo\",\"world\",null,null,10]],null,\"en\"]";
        var r = Translation.Parse(json);
        Assert.Equal("Hola mundo", r.Text);
        Assert.Equal("en", r.Source);
    }

    [Fact]
    public void Quiet_queue_summarizes_by_kind()
    {
        var q = new QuietQueue();
        q.Add("wa", "Ana", "hola");
        q.Add("wa", "Beto", "yo");
        q.Add("mail", "Banco", "Estado");
        var s = q.Drain();
        Assert.Equal(3, s.Count);
        Assert.Equal("Mientras jugabas: 2 WhatsApp · 1 correo", s.Title);
        Assert.Equal("Ana, Beto, Banco", s.Body);
        Assert.Equal(0, q.Drain().Count);
    }
}
