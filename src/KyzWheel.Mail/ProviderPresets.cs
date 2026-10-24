using KyzWheel.Core.Config;

namespace KyzWheel.Mail;

public static class ProviderPresets
{
    private static readonly (string[] Domains, string Imap, int ImapPort, string Smtp, int SmtpPort, SmtpSecurity Sec, string Color, string? Help)[] Known =
    [
        (["gmail.com", "googlemail.com"], "imap.gmail.com", 993, "smtp.gmail.com", 465, SmtpSecurity.SslOnConnect, "#EA4335", "https://myaccount.google.com/apppasswords"),
        (["outlook.com", "hotmail.com", "live.com", "msn.com", "outlook.es", "hotmail.es", "live.com.mx", "outlook.com.mx", "hotmail.com.mx"], "outlook.office365.com", 993, "smtp-mail.outlook.com", 587, SmtpSecurity.StartTls, "#38BDF8", "https://account.live.com/proofs/AppPassword"),
        (["yahoo.com", "yahoo.com.mx", "ymail.com"], "imap.mail.yahoo.com", 993, "smtp.mail.yahoo.com", 465, SmtpSecurity.SslOnConnect, "#A78BFA", "https://login.yahoo.com/myaccount/security/app-password"),
        (["icloud.com", "me.com", "mac.com"], "imap.mail.me.com", 993, "smtp.mail.me.com", 587, SmtpSecurity.StartTls, "#E5E7EB", "https://account.apple.com/account/manage"),
    ];

    public static string Normalize(string email)
    {
        var e = (email ?? "").Trim().ToLowerInvariant();
        var at = e.IndexOf('@');
        if (at <= 0 || at == e.Length - 1 || e.IndexOf('@', at + 1) >= 0 || !e[(at + 1)..].Contains('.'))
            throw new ArgumentException("Correo inválido", nameof(email));
        return e;
    }

    public static MailAccount For(string email)
    {
        var e = Normalize(email);
        var domain = e[(e.IndexOf('@') + 1)..];
        var k = Known.FirstOrDefault(p => p.Domains.Contains(domain));
        if (k.Imap is not null)
            return new MailAccount(e, e, k.Imap, k.ImapPort, k.Smtp, k.SmtpPort, k.Sec, k.Color);
        return new MailAccount(e, e, "imap." + domain, 993, "smtp." + domain, 465, SmtpSecurity.SslOnConnect, "#FACC15");
    }

    public static string? AppPasswordHelp(string email)
    {
        var domain = Normalize(email)[(Normalize(email).IndexOf('@') + 1)..];
        return Known.FirstOrDefault(p => p.Domains.Contains(domain)).Help;
    }

    public static string SecretKey(string email) => "kyzWheel:mail:" + Normalize(email);
}
