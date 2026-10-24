using KyzWheel.Mail;

namespace KyzWheel.Mail.Tests;

public sealed class ImapIntegrationTests
{
    private sealed class EnvSecrets : ISecretStore
    {
        public void Set(string key, string secret) { }
        public string? Get(string key) => Environment.GetEnvironmentVariable("KYZ_TEST_PASSWORD");
        public void Remove(string key) { }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Snapshot_then_status_online()
    {
        var email = Environment.GetEnvironmentVariable("KYZ_TEST_EMAIL");
        if (string.IsNullOrEmpty(email)) return;
        await using var src = new ImapMailSource(ProviderPresets.For(email), new EnvSecrets());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var inbox = new UnifiedInbox();
        await foreach (var e in src.WatchAsync(cts.Token))
        {
            inbox.Apply(e);
            if (e is StatusChanged { Status.State: AccountState.Online }) break;
            if (e is StatusChanged { Status.State: AccountState.AuthFailed }) Assert.Fail("auth");
        }
        Assert.True(inbox.Top().Count <= 8);
    }
}
