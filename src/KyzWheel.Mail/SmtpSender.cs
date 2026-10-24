using KyzWheel.Core.Config;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace KyzWheel.Mail;

public sealed class SmtpSender(ISecretStore secrets)
{
    public const long MaxAttachmentBytes = 25L * 1024 * 1024;

    public static MimeMessage Build(MailAccount from, string to, string subject, string body, IReadOnlyList<string> attachmentPaths)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(from.DisplayName == from.Email ? "" : from.DisplayName, from.Email));
        foreach (var addr in to.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            msg.To.Add(MailboxAddress.Parse(addr));
        if (msg.To.Count == 0) throw new ArgumentException("Falta el destinatario");
        msg.Subject = subject;
        var builder = new BodyBuilder { TextBody = body };
        foreach (var p in attachmentPaths) builder.Attachments.Add(p);
        msg.Body = builder.ToMessageBody();
        return msg;
    }

    public async Task SendAsync(MailAccount from, string to, string subject, string body, IReadOnlyList<string> attachmentPaths, CancellationToken ct)
    {
        var total = attachmentPaths.Sum(p => new FileInfo(p).Length);
        if (total > MaxAttachmentBytes) throw new InvalidOperationException("Los adjuntos pasan de 25 MB");
        var password = secrets.Get(ProviderPresets.SecretKey(from.Email)) ?? throw new InvalidOperationException("Falta la contraseña de aplicación");
        var msg = Build(from, to, subject, body, attachmentPaths);
        using var smtp = new SmtpClient { Timeout = 60000 };
        var sec = from.SmtpSecurity == SmtpSecurity.StartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
        await smtp.ConnectAsync(from.SmtpHost, from.SmtpPort, sec, ct);
        if (from.Auth == MailAuth.Google) await smtp.AuthenticateAsync(await GoogleOAuth.SaslAsync(from.Email, password, ct), ct);
        else await smtp.AuthenticateAsync(from.Email, password, ct);
        await smtp.SendAsync(msg, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}
