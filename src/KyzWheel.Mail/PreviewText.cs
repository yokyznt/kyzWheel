using System.Net;
using System.Text.RegularExpressions;

namespace KyzWheel.Mail;

public static partial class PreviewText
{
    [GeneratedRegex(@"<(style|script|head|title)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Blocks();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    public static string From(string? text, string? html, int max = 110)
    {
        string raw;
        if (!string.IsNullOrWhiteSpace(text)) raw = text;
        else if (!string.IsNullOrWhiteSpace(html)) raw = WebUtility.HtmlDecode(Tags().Replace(Blocks().Replace(html, " "), " "));
        else return "";
        var s = Spaces().Replace(raw.Replace('\u00A0', ' '), " ").Trim();
        if (s.Length <= max) return s;
        var cut = s[..(max - 1)];
        if (char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
        return cut + "…";
    }
}
