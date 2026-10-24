using KyzWheel.Core.Config;
using KyzWheel.Mail;

namespace KyzWheel.Mail.Tests;

public sealed class ProviderPresetsTests
{
    [Theory]
    [InlineData("a@gmail.com", "imap.gmail.com", 993, "smtp.gmail.com", 465, SmtpSecurity.SslOnConnect)]
    [InlineData("a@googlemail.com", "imap.gmail.com", 993, "smtp.gmail.com", 465, SmtpSecurity.SslOnConnect)]
    [InlineData("a@outlook.com", "outlook.office365.com", 993, "smtp-mail.outlook.com", 587, SmtpSecurity.StartTls)]
    [InlineData("a@hotmail.com", "outlook.office365.com", 993, "smtp-mail.outlook.com", 587, SmtpSecurity.StartTls)]
    [InlineData("a@live.com.mx", "outlook.office365.com", 993, "smtp-mail.outlook.com", 587, SmtpSecurity.StartTls)]
    [InlineData("a@yahoo.com", "imap.mail.yahoo.com", 993, "smtp.mail.yahoo.com", 465, SmtpSecurity.SslOnConnect)]
    [InlineData("a@icloud.com", "imap.mail.me.com", 993, "smtp.mail.me.com", 587, SmtpSecurity.StartTls)]
    public void Presets(string email, string imap, int ip, string smtp, int sp, SmtpSecurity sec)
    {
        var a = ProviderPresets.For(email);
        Assert.Equal(email, a.Email);
        Assert.Equal((imap, ip, smtp, sp, sec), (a.ImapHost, a.ImapPort, a.SmtpHost, a.SmtpPort, a.SmtpSecurity));
    }

    [Fact]
    public void Unknown_domain_guesses_imap_and_smtp_subdomains()
    {
        var a = ProviderPresets.For("Yo@Cecyte.Edu.MX");
        Assert.Equal("yo@cecyte.edu.mx", a.Email);
        Assert.Equal(("imap.cecyte.edu.mx", 993, "smtp.cecyte.edu.mx", 465), (a.ImapHost, a.ImapPort, a.SmtpHost, a.SmtpPort));
    }

    [Fact]
    public void Gmail_has_app_password_help()
        => Assert.Equal("https://myaccount.google.com/apppasswords", ProviderPresets.AppPasswordHelp("a@gmail.com"));

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("a@")]
    public void Invalid_email_throws(string e) => Assert.Throws<ArgumentException>(() => ProviderPresets.For(e));

    [Fact]
    public void Secret_key_is_namespaced() => Assert.Equal("kyzWheel:mail:a@gmail.com", ProviderPresets.SecretKey("A@Gmail.com"));
}
