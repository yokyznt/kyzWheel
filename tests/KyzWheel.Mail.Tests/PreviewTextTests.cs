using KyzWheel.Mail;

namespace KyzWheel.Mail.Tests;

public sealed class PreviewTextTests
{
    [Fact]
    public void Collapses_whitespace() => Assert.Equal("hola que tal", PreviewText.From("  hola\r\n\n  que\ttal ", null));

    [Fact]
    public void Strips_html_styles_scripts_and_decodes_entities()
        => Assert.Equal("Hola & adiós", PreviewText.From(null, "<style>p{color:red}</style><script>x()</script><p>Hola&nbsp;&amp; <b>adiós</b></p>"));

    [Fact]
    public void Cuts_at_110_with_ellipsis()
    {
        var s = PreviewText.From(new string('a', 300), null);
        Assert.Equal(110, s.Length);
        Assert.EndsWith("…", s);
    }

    [Fact]
    public void Prefers_text_over_html() => Assert.Equal("texto", PreviewText.From("texto", "<p>html</p>"));

    [Fact]
    public void Empty_gives_empty() => Assert.Equal("", PreviewText.From(null, null));
}
