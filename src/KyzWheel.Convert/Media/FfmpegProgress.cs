using System.Globalization;
using System.Text.RegularExpressions;

namespace KyzWheel.Convert.Media;

public static partial class FfmpegProgress
{
    [GeneratedRegex(@"time=(\d+):(\d{2}):(\d{2}(?:\.\d+)?)")]
    private static partial Regex Time();

    [GeneratedRegex(@"Duration: (\d+):(\d{2}):(\d{2}(?:\.\d+)?)")]
    private static partial Regex Duration();

    private static TimeSpan? Parse(Match m) => m.Success
        ? TimeSpan.FromHours(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
          + TimeSpan.FromMinutes(int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))
          + TimeSpan.FromSeconds(double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))
        : null;

    public static TimeSpan? ParseDuration(string line) => Parse(Duration().Match(line));

    public static double? Parse(string stderrLine, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return null;
        var t = Parse(Time().Match(stderrLine));
        return t is null ? null : Math.Clamp(t.Value / duration, 0, 1);
    }
}
