namespace KyzWheel.Core.Config;

public enum SmtpSecurity { SslOnConnect, StartTls }

public sealed record MailAccount(string Email, string DisplayName, string ImapHost, int ImapPort, string SmtpHost, int SmtpPort, SmtpSecurity SmtpSecurity, string ColorHex);
