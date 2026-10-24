using System.Runtime.CompilerServices;
using System.Threading.Channels;
using KyzWheel.Core.Config;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace KyzWheel.Mail;

public sealed class ImapMailSource(MailAccount account, ISecretStore secrets) : IMailSource, IAsyncDisposable
{
    private const int Recent = 12;
    private static readonly TimeSpan IdleRefresh = TimeSpan.FromMinutes(9);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ImapClient? _client;
    private CancellationTokenSource? _idleDone;

    public string Account => account.Email;

    public static async Task<string?> TestAsync(MailAccount account, string password, CancellationToken ct)
    {
        try
        {
            using var c = new ImapClient { Timeout = 15000 };
            await c.ConnectAsync(account.ImapHost, account.ImapPort, SecureSocketOptions.SslOnConnect, ct);
            await c.AuthenticateAsync(account.Email, password, ct);
            await c.DisconnectAsync(true, ct);
            return null;
        }
        catch (AuthenticationException)
        {
            return "Correo o contraseña de aplicación incorrectos";
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return "No se pudo conectar: " + e.Message;
        }
    }

    public async IAsyncEnumerable<MailEvent> WatchAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<MailEvent>();
        var loop = Task.Run(() => LoopAsync(channel.Writer, ct), ct);
        await foreach (var e in channel.Reader.ReadAllAsync(ct)) yield return e;
        await loop;
    }

    private async Task LoopAsync(ChannelWriter<MailEvent> w, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                w.TryWrite(new StatusChanged(new AccountStatus(Account, AccountState.Connecting, null)));
                var password = secrets.Get(ProviderPresets.SecretKey(Account));
                if (string.IsNullOrEmpty(password))
                {
                    w.TryWrite(new StatusChanged(new AccountStatus(Account, AccountState.AuthFailed, "Falta la contraseña de aplicación")));
                    return;
                }
                var client = new ImapClient { Timeout = 30000 };
                await client.ConnectAsync(account.ImapHost, account.ImapPort, SecureSocketOptions.SslOnConnect, ct);
                await client.AuthenticateAsync(Account, password, ct);
                _client = client;
                var inbox = client.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadWrite, ct);
                w.TryWrite(new MailSnapshot(Account, await FetchAsync(inbox, null, ct)));
                w.TryWrite(new StatusChanged(new AccountStatus(Account, AccountState.Online, null)));
                delay = TimeSpan.FromSeconds(2);
                var known = inbox.Count;
                var arrived = false;
                inbox.CountChanged += (_, _) =>
                {
                    arrived = true;
                    _idleDone?.Cancel();
                };
                while (!ct.IsCancellationRequested && client.IsConnected)
                {
                    await _gate.WaitAsync(ct);
                    try
                    {
                        if (arrived)
                        {
                            arrived = false;
                            if (inbox.Count > known)
                                foreach (var m in await FetchAsync(inbox, known, ct)) w.TryWrite(new MailArrived(m));
                            known = inbox.Count;
                        }
                        using var done = new CancellationTokenSource(IdleRefresh);
                        _idleDone = done;
                        if (client.Capabilities.HasFlag(ImapCapabilities.Idle))
                            await client.IdleAsync(done.Token, ct);
                        else
                        {
                            await Task.Delay(TimeSpan.FromMinutes(2), CancellationTokenSource.CreateLinkedTokenSource(ct, done.Token).Token).ContinueWith(_ => { }, TaskScheduler.Default);
                            await client.NoOpAsync(ct);
                        }
                        _idleDone = null;
                    }
                    finally
                    {
                        _gate.Release();
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (AuthenticationException)
            {
                w.TryWrite(new StatusChanged(new AccountStatus(Account, AccountState.AuthFailed, "Revisa la contraseña de aplicación")));
                await DropAsync();
                return;
            }
            catch (Exception)
            {
                w.TryWrite(new StatusChanged(new AccountStatus(Account, AccountState.Offline, "Sin conexión, reintentando")));
            }
            await DropAsync();
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            delay = TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 2));
        }
        await DropAsync();
        w.TryComplete();
    }

    private async Task<IReadOnlyList<MailSummary>> FetchAsync(IMailFolder inbox, int? fromIndex, CancellationToken ct)
    {
        if (inbox.Count == 0) return [];
        var start = fromIndex ?? Math.Max(0, inbox.Count - Recent);
        var items = await inbox.FetchAsync(start, -1,
            MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.PreviewText | MessageSummaryItems.BodyStructure | MessageSummaryItems.InternalDate, ct);
        return items.Select(ToSummary).OrderByDescending(m => m.Date).ToList();
    }

    private MailSummary ToSummary(IMessageSummary s)
    {
        var from = s.Envelope?.From?.Mailboxes.FirstOrDefault();
        var date = s.Envelope?.Date ?? s.InternalDate ?? DateTimeOffset.Now;
        return new MailSummary(
            Account,
            s.Envelope?.MessageId ?? $"{Account}:{s.UniqueId.Id}",
            s.UniqueId.Id,
            string.IsNullOrWhiteSpace(from?.Name) ? from?.Address ?? "(sin remitente)" : from.Name,
            from?.Address ?? "",
            string.IsNullOrWhiteSpace(s.Envelope?.Subject) ? "(sin asunto)" : s.Envelope.Subject,
            PreviewText.From(s.PreviewText, null),
            date,
            !(s.Flags?.HasFlag(MessageFlags.Seen) ?? false),
            s.Attachments?.Any() ?? false);
    }

    private async Task<T> WithInboxAsync<T>(Func<IMailFolder, Task<T>> action, CancellationToken ct)
    {
        var client = _client ?? throw new InvalidOperationException("Sin conexión");
        _idleDone?.Cancel();
        await _gate.WaitAsync(ct);
        try
        {
            return await action(client.Inbox);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<MailBody> GetBodyAsync(uint uid, CancellationToken ct) => WithInboxAsync(async inbox =>
    {
        var msg = await inbox.GetMessageAsync(new UniqueId(uid), ct);
        var attachments = new List<MailAttachmentInfo>();
        var inline = new Dictionary<string, string>();
        var index = 0;
        foreach (var part in msg.BodyParts.OfType<MimePart>())
        {
            index++;
            if (part.IsAttachment)
            {
                attachments.Add(new MailAttachmentInfo(part.FileName ?? $"adjunto_{index}", part.Content?.Stream?.Length ?? 0, index.ToString()));
            }
            else if (!string.IsNullOrEmpty(part.ContentId) && part.ContentType.MediaType == "image" && part.Content is not null)
            {
                using var ms = new MemoryStream();
                await part.Content.DecodeToAsync(ms, ct);
                if (ms.Length < 4_000_000)
                    inline[part.ContentId.Trim('<', '>')] = $"data:{part.ContentType.MimeType};base64,{Convert.ToBase64String(ms.ToArray())}";
            }
        }
        return new MailBody(msg.HtmlBody, msg.TextBody, attachments, inline);
    }, ct);

    public Task MarkReadAsync(uint uid, CancellationToken ct) => WithInboxAsync(async inbox =>
    {
        await inbox.AddFlagsAsync(new UniqueId(uid), MessageFlags.Seen, true, ct);
        return true;
    }, ct);

    public Task<string> SaveAttachmentAsync(uint uid, MailAttachmentInfo attachment, string folder, CancellationToken ct) => WithInboxAsync(async inbox =>
    {
        var msg = await inbox.GetMessageAsync(new UniqueId(uid), ct);
        var part = msg.BodyParts.OfType<MimePart>().ElementAt(int.Parse(attachment.PartSpecifier) - 1);
        Directory.CreateDirectory(folder);
        var name = string.Concat(attachment.FileName.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(folder, name);
        if (File.Exists(path)) path = KyzWheel.Core.Files.OutputNaming.NextFreePath(path, Path.GetExtension(path));
        await using var fs = File.Create(path);
        await (part.Content ?? throw new InvalidOperationException("Adjunto vacío")).DecodeToAsync(fs, ct);
        return path;
    }, ct);

    private async Task DropAsync()
    {
        var c = _client;
        _client = null;
        if (c is null) return;
        try
        {
            if (c.IsConnected) await c.DisconnectAsync(true);
        }
        catch (Exception)
        {
        }
        c.Dispose();
    }

    public async ValueTask DisposeAsync() => await DropAsync();
}
