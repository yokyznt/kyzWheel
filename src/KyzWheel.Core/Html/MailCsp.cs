using System.Net;
using System.Text.RegularExpressions;

namespace KyzWheel.Core.Html;

public static partial class MailCsp
{
    private const string Strict = "default-src 'none'; style-src 'unsafe-inline'; img-src data: cid:; font-src data:";
    private const string Remote = "default-src 'none'; style-src 'unsafe-inline'; img-src data: cid: https:; font-src data:";

    private const string Style = """
        <style>
        html { color-scheme: dark; }
        body { margin: 0; padding: 18px 22px; background: #0d0d12; color: #e8e8ee; font: 14px/1.55 "Segoe UI Variable Text", "Segoe UI", sans-serif; user-select: text; word-wrap: break-word; }
        a { color: #7cc4ff; }
        img { max-width: 100%; height: auto; }
        table { max-width: 100%; }
        pre.kyz { white-space: pre-wrap; font: inherit; margin: 0; }
        ::selection { background: #ff8a3d55; }
        </style>
        """;

    [GeneratedRegex(@"<(script|iframe|object|embed|frame|frameset|applet|form|meta|link|base)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PairedTags();

    [GeneratedRegex(@"<\/?(script|iframe|object|embed|frame|frameset|applet|form|meta|link|base)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LoneTags();

    [GeneratedRegex(@"\s+on[a-z]+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Handlers();

    [GeneratedRegex(@"(href|src|action|formaction|xlink:href)\s*=\s*([""']?)\s*(javascript|vbscript|data:text/html)[^""'\s>]*", RegexOptions.IgnoreCase)]
    private static partial Regex BadUrls();

    public static string Sanitize(string html)
    {
        var h = PairedTags().Replace(html, "");
        h = LoneTags().Replace(h, "");
        h = Handlers().Replace(h, "");
        h = BadUrls().Replace(h, "$1=$2#");
        return h;
    }

    public static string Wrap(string html, bool allowRemoteImages)
    {
        var csp = allowRemoteImages ? Remote : Strict;
        return $"<!doctype html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"{csp}\"><base target=\"_blank\">{Style}</head><body>{Sanitize(html)}</body></html>";
    }

    public static string WrapText(string text) => Wrap($"<pre class=\"kyz\">{WebUtility.HtmlEncode(text)}</pre>", false);
}
