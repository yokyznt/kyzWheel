using KyzWheel.Mail;
using MimeKit;

namespace KyzWheel.Mail.Tests;

public sealed class SmtpSenderTests
{
    [Fact]
    public void Builds_message_with_attachment_and_multiple_recipients()
    {
        var file = Path.Combine(Path.GetTempPath(), "kyz_adj.txt");
        File.WriteAllText(file, "hola");
        var msg = SmtpSender.Build(ProviderPresets.For("yo@gmail.com"), "a@x.com; b@y.com", "Asunto", "Texto", [file]);
        Assert.Equal(["a@x.com", "b@y.com"], msg.To.Mailboxes.Select(m => m.Address));
        Assert.Equal("Asunto", msg.Subject);
        Assert.Equal("Texto", msg.TextBody);
        Assert.Equal("kyz_adj.txt", msg.Attachments.OfType<MimePart>().Single().FileName);
        File.Delete(file);
    }

    [Fact]
    public void Missing_recipient_throws()
        => Assert.Throws<ArgumentException>(() => SmtpSender.Build(ProviderPresets.For("yo@gmail.com"), " ; ", "s", "b", []));

    [Fact]
    public void Bad_recipient_throws()
        => Assert.Throws<ParseException>(() => SmtpSender.Build(ProviderPresets.For("yo@gmail.com"), "no es correo", "s", "b", []));
}
