using KyzWheel.App.Shell;
using KyzWheel.Core.Config;
using KyzWheel.Mail;
using Microsoft.UI.Dispatching;

namespace KyzWheel.App.Mail;

public sealed class MailService(ISecretStore secrets, DispatcherQueue ui)
{
    private sealed record Running(MailAccount Account, ImapMailSource Source, CancellationTokenSource Cts);

    private readonly Dictionary<string, Running> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _primed = new(StringComparer.OrdinalIgnoreCase);

    public UnifiedInbox Inbox { get; } = new();
    public ISecretStore Secrets => secrets;
    public SmtpSender Sender { get; } = new(secrets);
    public IReadOnlyList<MailAccount> Accounts { get; private set; } = [];

    public event Action? Changed;
    public event Action<MailSummary>? Arrived;

    public void Sync(IReadOnlyList<MailAccount> accounts)
    {
        Accounts = accounts;
        foreach (var gone in _running.Keys.Where(k => accounts.All(a => !a.Email.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            Stop(gone);
            Inbox.Remove(gone);
        }
        foreach (var a in accounts)
        {
            if (_running.TryGetValue(a.Email, out var r) && r.Account == a) continue;
            if (r is not null) Stop(a.Email);
            Start(a);
        }
        ui.TryEnqueue(() => Changed?.Invoke());
    }

    public void Restart(string email)
    {
        var a = Accounts.FirstOrDefault(x => x.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        if (a is null) return;
        Stop(email);
        Start(a);
    }

    public MailAccount? AccountOf(string email) => Accounts.FirstOrDefault(a => a.Email.Equals(email, StringComparison.OrdinalIgnoreCase));

    public IMailSource? SourceOf(string email) => _running.TryGetValue(email, out var r) ? r.Source : null;

    private void Start(MailAccount a)
    {
        var cts = new CancellationTokenSource();
        var src = new ImapMailSource(a, secrets);
        _running[a.Email] = new Running(a, src, cts);
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var e in src.WatchAsync(cts.Token))
                {
                    Inbox.Apply(e);
                    if (e is MailSnapshot) _primed.Add(a.Email);
                    var arrived = e is MailArrived m && _primed.Contains(a.Email) ? m.Item : null;
                    ui.TryEnqueue(() =>
                    {
                        Changed?.Invoke();
                        if (arrived is not null) Arrived?.Invoke(arrived);
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        });
    }

    private void Stop(string email)
    {
        if (!_running.Remove(email, out var r)) return;
        r.Cts.Cancel();
        _primed.Remove(email);
        _ = r.Source.DisposeAsync().AsTask();
    }

    public void StopAll()
    {
        foreach (var k in _running.Keys.ToList()) Stop(k);
    }
}
