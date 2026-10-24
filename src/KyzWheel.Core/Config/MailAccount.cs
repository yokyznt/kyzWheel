namespace KyzWheel.Core.Config;

public enum SmtpSecurity { SslOnConnect, StartTls }

public enum MailAuth { AppPassword, Google }

public sealed record MailAccount(string Email, string DisplayName, string ImapHost, int ImapPort, string SmtpHost, int SmtpPort, SmtpSecurity SmtpSecurity, string ColorHex)
{
    public MailAuth Auth { get; init; } = MailAuth.AppPassword;
}
