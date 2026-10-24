using System.Text.RegularExpressions;

namespace KyzWheel.Core.Text;

public static partial class CodeFinder
{
    [GeneratedRegex(@"(?<![\d$.,\-])(?:G-)?(\d{4,8})(?![\d.,\-])")]
    private static partial Regex Digits();

    [GeneratedRegex(@"c[oó]digo|code|verif|otp|pin\b|contrase[nñ]a temporal|one[- ]time|seguridad|security", RegexOptions.IgnoreCase)]
    private static partial Regex Hint();

    public static string? Find(string? subject, string? body)
    {
        subject ??= "";
        body ??= "";
        if (!Hint().IsMatch(subject + " " + body)) return null;
        foreach (var text in new[] { subject, body })
            foreach (Match m in Digits().Matches(text))
            {
                var v = m.Groups[1].Value;
                if (v.Length == 4 && v is ['1', '9', _, _] or ['2', '0', _, _] && !Hint().IsMatch(text)) continue;
                return v;
            }
        return null;
    }
}
