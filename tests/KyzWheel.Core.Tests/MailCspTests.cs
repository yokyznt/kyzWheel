using KyzWheel.Core.Html;

namespace KyzWheel.Core.Tests;

public sealed class MailCspTests
{
    private const string Strict = "default-src 'none'; style-src 'unsafe-inline'; img-src data: cid:; font-src data:";

    [Fact]
    public void Strict_policy_without_remote_images()
        => Assert.Contains($"<meta http-equiv=\"Content-Security-Policy\" content=\"{Strict}\">", MailCsp.Wrap("<p>x</p>", false));

    [Fact]
    public void Remote_images_add_https()
        => Assert.Contains("img-src data: cid: https:;", MailCsp.Wrap("<p>x</p>", true));

    [Fact]
    public void Removes_scripts_iframes_and_handlers()
    {
        var h = MailCsp.Wrap("<p onclick=\"evil()\" ONLOAD='x'>hi</p><script>alert(1)</script><iframe src=x></iframe><object data=y></object>", false);
        Assert.DoesNotContain("<script", h, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", h, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<object", h, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", h, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", h, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hi", h);
    }

    [Fact]
    public void Neutralizes_javascript_urls()
        => Assert.DoesNotContain("javascript:", MailCsp.Wrap("<a href=\"JavaScript:alert(1)\">x</a>", false), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Adds_base_target_blank_and_dark_reader_style()
    {
        var h = MailCsp.Wrap("<p>x</p>", false);
        Assert.Contains("<base target=\"_blank\">", h);
        Assert.Contains("user-select: text", h);
    }

    [Fact]
    public void Plain_text_is_escaped_and_preformatted()
    {
        var h = MailCsp.WrapText("a < b\nlinea 2");
        Assert.Contains("a &lt; b", h);
        Assert.Contains("white-space: pre-wrap", h);
    }
}
