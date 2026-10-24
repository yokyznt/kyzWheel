using System.Text.Json;
using System.Text.RegularExpressions;

namespace KyzWheel.Core.Projects;

public sealed record ProjectInfo(string Name, string Path, DateTime Touched, string? GitHubUrl, bool HasApkScript);

public static partial class ProjectScanner
{
    [GeneratedRegex(@"url\s*=\s*(\S+)")]
    private static partial Regex RemoteUrl();

    [GeneratedRegex(@"github\.com[:/]([^/\s]+)/([^/\s]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex GitHub();

    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Trabajos", "Proyectos");

    public static string? NormalizeRemote(string remote)
    {
        var m = GitHub().Match(remote.Trim());
        return m.Success ? $"https://github.com/{m.Groups[1].Value}/{m.Groups[2].Value}" : null;
    }

    private static DateTime Activity(DirectoryInfo d)
    {
        var t = d.LastWriteTimeUtc;
        foreach (var marker in new[] { ".git/index", ".git/HEAD", ".git/FETCH_HEAD" })
        {
            var f = new FileInfo(Path.Combine(d.FullName, marker));
            if (f.Exists && f.LastWriteTimeUtc > t) t = f.LastWriteTimeUtc;
        }
        return t;
    }

    private static string? Remote(string dir)
    {
        var cfg = Path.Combine(dir, ".git", "config");
        if (!File.Exists(cfg)) return null;
        var text = File.ReadAllText(cfg);
        var i = text.IndexOf("[remote \"origin\"]", StringComparison.Ordinal);
        var m = RemoteUrl().Match(i >= 0 ? text[i..] : text);
        return m.Success ? NormalizeRemote(m.Groups[1].Value) : null;
    }

    private static bool Apk(string dir)
    {
        var pkg = Path.Combine(dir, "package.json");
        if (!File.Exists(pkg)) return false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
            return doc.RootElement.TryGetProperty("scripts", out var s) && s.TryGetProperty("android:apk", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static IReadOnlyList<ProjectInfo> Scan(string? root = null)
    {
        root ??= DefaultRoot;
        if (!Directory.Exists(root)) return [];
        return new DirectoryInfo(root).EnumerateDirectories()
            .Where(d => !d.Name.StartsWith('.') && (d.Attributes & FileAttributes.Hidden) == 0)
            .Select(d => new ProjectInfo(d.Name, d.FullName, Activity(d), Remote(d.FullName), Apk(d.FullName)))
            .OrderByDescending(p => p.Touched)
            .ToList();
    }
}
