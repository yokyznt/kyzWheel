namespace KyzWheel.Mail;

public sealed record MailSummary(string Account, string MessageId, uint Uid, string FromName, string FromAddress, string Subject, string Preview, DateTimeOffset Date, bool Unread, bool HasAttachments);

public enum AccountState { Connecting, Online, Offline, AuthFailed }

public sealed record AccountStatus(string Account, AccountState State, string? Error);

public abstract record MailEvent;

public sealed record MailSnapshot(string Account, IReadOnlyList<MailSummary> Items) : MailEvent;

public sealed record MailArrived(MailSummary Item) : MailEvent;

public sealed record StatusChanged(AccountStatus Status) : MailEvent;

public sealed record MailAttachmentInfo(string FileName, long Size, string PartSpecifier);

public sealed record MailBody(string? Html, string? Text, IReadOnlyList<MailAttachmentInfo> Attachments, IReadOnlyDictionary<string, string> InlineImages);

public interface IMailSource
{
    string Account { get; }
    IAsyncEnumerable<MailEvent> WatchAsync(CancellationToken ct);
    Task<MailBody> GetBodyAsync(uint uid, CancellationToken ct);
    Task MarkReadAsync(uint uid, CancellationToken ct);
    Task<string> SaveAttachmentAsync(uint uid, MailAttachmentInfo attachment, string folder, CancellationToken ct);
}
