using KyzWheel.Mail;

namespace KyzWheel.Mail.Tests;

public sealed class UnifiedInboxTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-10-23T12:00:00Z");

    private static MailSummary M(string account, uint uid, int minutes, bool unread = false, string? id = null)
        => new(account, id ?? $"<{account}-{uid}>", uid, "Ana", "ana@x.com", "Asunto " + uid, "hola", T0.AddMinutes(minutes), unread, false);

    [Fact]
    public void Merges_two_accounts_sorted_by_date_desc_top_8()
    {
        var inbox = new UnifiedInbox();
        inbox.Apply(new MailSnapshot("a", Enumerable.Range(1, 6).Select(i => M("a", (uint)i, i * 2)).ToList()));
        inbox.Apply(new MailSnapshot("b", Enumerable.Range(1, 6).Select(i => M("b", (uint)i, i * 2 + 1)).ToList()));
        var top = inbox.Top();
        Assert.Equal(8, top.Count);
        Assert.Equal(top.OrderByDescending(m => m.Date).Select(m => m.MessageId), top.Select(m => m.MessageId));
        Assert.Equal("<b-6>", top[0].MessageId);
        Assert.Equal("<a-6>", top[1].MessageId);
    }

    [Fact]
    public void Snapshot_replaces_previous_items_of_same_account()
    {
        var inbox = new UnifiedInbox();
        inbox.Apply(new MailSnapshot("a", [M("a", 1, 1), M("a", 2, 2)]));
        inbox.Apply(new MailSnapshot("a", [M("a", 3, 3)]));
        Assert.Equal(["<a-3>"], inbox.Top().Select(m => m.MessageId));
    }

    [Fact]
    public void Dedupes_same_message_id_across_accounts()
    {
        var inbox = new UnifiedInbox();
        inbox.Apply(new MailSnapshot("a", [M("a", 1, 1, id: "<same>")]));
        inbox.Apply(new MailSnapshot("b", [M("b", 9, 1, id: "<same>")]));
        Assert.Single(inbox.Top());
    }

    [Fact]
    public void Arrival_increments_unread_and_raises_changed()
    {
        var inbox = new UnifiedInbox();
        var changed = 0;
        inbox.Changed += () => changed++;
        inbox.Apply(new MailSnapshot("a", [M("a", 1, 1, unread: true)]));
        inbox.Apply(new MailArrived(M("a", 2, 5, unread: true)));
        Assert.Equal(2, inbox.UnreadCount);
        Assert.Equal(2, changed);
        Assert.Equal("<a-2>", inbox.Top()[0].MessageId);
        inbox.Apply(new MailArrived(M("a", 2, 5, unread: true)));
        Assert.Equal(2, inbox.UnreadCount);
    }

    [Fact]
    public void Marking_read_updates_count()
    {
        var inbox = new UnifiedInbox();
        inbox.Apply(new MailSnapshot("a", [M("a", 1, 1, unread: true)]));
        inbox.MarkRead("a", 1);
        Assert.Equal(0, inbox.UnreadCount);
        Assert.False(inbox.Top()[0].Unread);
    }

    [Fact]
    public void Auth_failed_account_keeps_others_and_reports_status()
    {
        var inbox = new UnifiedInbox();
        inbox.Apply(new MailSnapshot("a", [M("a", 1, 1)]));
        inbox.Apply(new StatusChanged(new AccountStatus("b", AccountState.AuthFailed, "Revisa la contraseña")));
        Assert.Single(inbox.Top());
        Assert.Equal(AccountState.AuthFailed, inbox.Statuses.Single(s => s.Account == "b").State);
    }

    [Fact]
    public void Offline_account_keeps_last_snapshot()
    {
        var inbox = new UnifiedInbox();
        inbox.Apply(new MailSnapshot("a", [M("a", 1, 1)]));
        inbox.Apply(new StatusChanged(new AccountStatus("a", AccountState.Offline, "Sin conexión")));
        Assert.Single(inbox.Top());
    }

    [Fact]
    public void Removing_account_drops_its_mail()
    {
        var inbox = new UnifiedInbox();
        inbox.Apply(new MailSnapshot("a", [M("a", 1, 1, unread: true)]));
        inbox.Remove("a");
        Assert.Empty(inbox.Top());
        Assert.Equal(0, inbox.UnreadCount);
        Assert.Empty(inbox.Statuses);
    }

    [Fact]
    public void Known_senders_are_distinct_addresses()
    {
        var inbox = new UnifiedInbox();
        inbox.Apply(new MailSnapshot("a", [M("a", 1, 1), M("a", 2, 2)]));
        Assert.Equal(["ana@x.com"], inbox.KnownSenders());
    }
}
