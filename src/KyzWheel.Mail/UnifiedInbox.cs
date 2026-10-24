namespace KyzWheel.Mail;

public sealed class UnifiedInbox
{
    private const int KeepPerAccount = 30;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<MailSummary>> _byAccount = new();
    private readonly Dictionary<string, AccountStatus> _status = new();

    public event Action? Changed;

    public IReadOnlyList<AccountStatus> Statuses
    {
        get { lock (_gate) return _status.Values.OrderBy(s => s.Account).ToList(); }
    }

    public int UnreadCount
    {
        get { lock (_gate) return Distinct().Count(m => m.Unread); }
    }

    public void Apply(MailEvent e)
    {
        lock (_gate)
        {
            switch (e)
            {
                case MailSnapshot s:
                    _byAccount[s.Account] = s.Items.OrderByDescending(m => m.Date).Take(KeepPerAccount).ToList();
                    break;
                case MailArrived a:
                    var list = _byAccount.TryGetValue(a.Item.Account, out var l) ? l : _byAccount[a.Item.Account] = [];
                    list.RemoveAll(m => m.Uid == a.Item.Uid || m.MessageId == a.Item.MessageId);
                    list.Add(a.Item);
                    list.Sort((x, y) => y.Date.CompareTo(x.Date));
                    if (list.Count > KeepPerAccount) list.RemoveRange(KeepPerAccount, list.Count - KeepPerAccount);
                    break;
                case StatusChanged c:
                    _status[c.Status.Account] = c.Status;
                    break;
            }
        }
        Changed?.Invoke();
    }

    public void MarkRead(string account, uint uid)
    {
        lock (_gate)
        {
            if (!_byAccount.TryGetValue(account, out var list)) return;
            var i = list.FindIndex(m => m.Uid == uid);
            if (i < 0 || !list[i].Unread) return;
            list[i] = list[i] with { Unread = false };
        }
        Changed?.Invoke();
    }

    public void Remove(string account)
    {
        lock (_gate)
        {
            _byAccount.Remove(account);
            _status.Remove(account);
        }
        Changed?.Invoke();
    }

    public IReadOnlyList<MailSummary> Top(int n = 8)
    {
        lock (_gate) return Distinct().Take(n).ToList();
    }

    public IReadOnlyList<string> KnownSenders()
    {
        lock (_gate)
            return Distinct().Select(m => m.FromAddress).Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IEnumerable<MailSummary> Distinct()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in _byAccount.Values.SelectMany(v => v).OrderByDescending(m => m.Date))
            if (string.IsNullOrEmpty(m.MessageId) || seen.Add(m.MessageId)) yield return m;
    }
}
