using System.Text.RegularExpressions;

namespace KyzWheel.Core.Phone;

public sealed record PhoneDevice(string Id, string Name);

public static partial class PhoneCli
{
    [GeneratedRegex(@"^([0-9A-Za-z_\-{}]{8,})\s+(.+)$")]
    private static partial Regex Line();

    [GeneratedRegex(@"^\d+\s+(dispositivos?|devices?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Count();

    public static readonly string[] ListAvailable = ["-a", "--id-name-only"];

    public static IReadOnlyList<PhoneDevice> ParseDevices(string output)
    {
        var list = new List<PhoneDevice>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || Count().IsMatch(line)) continue;
            var m = Line().Match(line);
            if (m.Success) list.Add(new PhoneDevice(m.Groups[1].Value, m.Groups[2].Value.Trim()));
        }
        return list;
    }

    public static string[] Share(string id, IEnumerable<string> files) => ["-d", id, .. files.SelectMany(f => new[] { "--share", f })];

    public static string[] SendClipboard(string id) => ["-d", id, "--send-clipboard"];

    public static string[] ShareText(string id, string text) => ["-d", id, "--share-text", text];

    public static string[] Ring(string id) => ["-d", id, "--ring"];

    public static string[] Ping(string id, string msg) => ["-d", id, "--ping-msg", msg];
}
