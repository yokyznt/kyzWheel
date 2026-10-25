using System.Text.RegularExpressions;

namespace KyzWheel.Core.Phone;

public sealed record PhoneDevice(string Id, string Name);

public sealed record PhoneStatus(string Id, string Name, bool Paired, bool Reachable);

public static partial class PhoneCli
{
    [GeneratedRegex(@"^([0-9A-Za-z_\-{}]{8,})\s+(.+)$")]
    private static partial Regex Line();

    [GeneratedRegex(@"^\d+\s+(dispositivos?|devices?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Count();

    public static readonly string[] ListAvailable = ["-a", "--id-name-only"];

    public static readonly string[] ListAll = ["-l"];

    [GeneratedRegex(@"^-\s*(.+?):\s*([0-9A-Za-z_\-{}]{8,})\b.*?(?:\(([^)]*)\))?\s*$")]
    private static partial Regex StatusLine();

    [GeneratedRegex(@"\b(no vinculad|not paired|unpaired|sin vincular)", RegexOptions.IgnoreCase)]
    private static partial Regex NotPaired();

    [GeneratedRegex(@"\b(no accesible|not reachable|unreachable|inaccesible)", RegexOptions.IgnoreCase)]
    private static partial Regex NotReachable();

    public static IReadOnlyList<PhoneStatus> ParseStatus(string output)
    {
        var list = new List<PhoneStatus>();
        foreach (var raw in output.Split('\n'))
        {
            var m = StatusLine().Match(raw.Trim());
            if (!m.Success) continue;
            var st = m.Groups[3].Value;
            var paired = (st.Contains("vinculad", StringComparison.OrdinalIgnoreCase) || st.Contains("paired", StringComparison.OrdinalIgnoreCase)) && !NotPaired().IsMatch(st);
            var reach = (st.Contains("accesible", StringComparison.OrdinalIgnoreCase) || st.Contains("reachable", StringComparison.OrdinalIgnoreCase)) && !NotReachable().IsMatch(st);
            list.Add(new PhoneStatus(m.Groups[2].Value, m.Groups[1].Value.Trim(), paired, reach));
        }
        return list;
    }

    public static string[] Pair(string id) => ["--pair", "-d", id];

    public static readonly string[] Refresh = ["--refresh"];

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
