using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KyzWheel.App.Shell;
using KyzWheel.Core.WhatsApp;
using Microsoft.UI.Dispatching;

namespace KyzWheel.App.WhatsApp;

public sealed class WaException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class WhatsAppBridge(DispatcherQueue ui) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _write = new(1, 1);
    private Process? _proc;
    private int _next;
    private bool _stopping;
    private int _backoffMs = 1000;

    public WaStatus Status { get; private set; } = new("idle", null, null, 0);
    public string? QrDataUrl { get; private set; }
    public bool Running => _proc is { HasExited: false };

    public event Action? StatusChanged;
    public event Action? ChatsChanged;
    public event Action<WaChat, WaMessage>? MessageArrived;

    public void Start()
    {
        if (Running) return;
        _stopping = false;
        var exe = Path.Combine(AppContext.BaseDirectory, "wa-sidecar.exe");
        ProcessStartInfo psi;
        if (File.Exists(exe)) psi = new ProcessStartInfo(exe, "--autologin");
        else if (Environment.GetEnvironmentVariable("KYZ_WA_DEV") is { } dev && File.Exists(dev)) psi = new ProcessStartInfo("node", $"\"{dev}\" --autologin");
        else
        {
            Status = new WaStatus("missing", null, null, 0);
            StatusChanged?.Invoke();
            return;
        }
        psi.UseShellExecute = false;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardInputEncoding = new UTF8Encoding(false);
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 } line) OnLine(line); };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log.Write("wa.log", e.Data); };
        p.Exited += (_, _) => OnExited();
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _proc = p;
        Status = Status with { State = "connecting" };
        Raise(StatusChanged);
    }

    private void OnExited()
    {
        foreach (var (_, t) in _pending) t.TrySetException(new WaException("exited", "WhatsApp se reinició"));
        _pending.Clear();
        _proc = null;
        if (_stopping) return;
        Status = Status with { State = "reconnecting" };
        Raise(StatusChanged);
        var delay = _backoffMs;
        _backoffMs = Math.Min(30000, _backoffMs * 2);
        _ = Task.Delay(delay).ContinueWith(_ => ui.TryEnqueue(Start), TaskScheduler.Default);
    }

    private void Raise(Action? a)
    {
        if (a is not null) ui.TryEnqueue(() => a());
    }

    private void OnLine(string line)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(line).RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }
        if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
        {
            if (!_pending.TryRemove(id.GetInt32(), out var tcs)) return;
            if (root.TryGetProperty("error", out var err))
                tcs.TrySetException(new WaException(err.GetProperty("code").GetString() ?? "internal", err.GetProperty("message").GetString() ?? ""));
            else tcs.TrySetResult(root.TryGetProperty("result", out var r) ? r : default);
            return;
        }
        if (!root.TryGetProperty("event", out var ev)) return;
        var data = root.TryGetProperty("data", out var d) ? d : default;
        switch (ev.GetString())
        {
            case "qr":
                QrDataUrl = data.GetProperty("png").GetString();
                Status = Status with { State = "qr" };
                Raise(StatusChanged);
                break;
            case "connected":
                _backoffMs = 1000;
                QrDataUrl = null;
                var me = data.TryGetProperty("me", out var m) ? m : default;
                Status = new WaStatus("open", me.ValueKind == JsonValueKind.Object ? me.GetProperty("jid").GetString() : null, me.ValueKind == JsonValueKind.Object ? me.GetProperty("name").GetString() : null, Status.Unread);
                Raise(StatusChanged);
                Raise(ChatsChanged);
                break;
            case "disconnected":
                var reason = data.TryGetProperty("reason", out var rs) ? rs.GetString() : null;
                Status = Status with { State = reason == "logged_out" ? "logged_out" : "reconnecting" };
                Raise(StatusChanged);
                break;
            case "chats.changed":
                Raise(ChatsChanged);
                break;
            case "message.new":
                var jid = data.GetProperty("chat").GetString() ?? "";
                var msg = WaJson.Message(data.GetProperty("message"), jid);
                if (!msg.FromMe)
                    ui.TryEnqueue(async () =>
                    {
                        var chats = await RecentChatsAsync(30);
                        var chat = chats.FirstOrDefault(c => c.Jid == jid) ?? new WaChat(jid, msg.Sender ?? jid.Split('@')[0], msg.Text, msg.Ts, 1, jid.EndsWith("@g.us"));
                        MessageArrived?.Invoke(chat, msg);
                    });
                break;
        }
    }

    public async Task<JsonElement> CallAsync(string method, object? args = null, int timeoutMs = 60000)
    {
        var p = _proc;
        if (p is null || p.HasExited) throw new WaException("not_running", "WhatsApp no está activo");
        var id = Interlocked.Increment(ref _next);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        var payload = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = args is null ? new JsonObject() : JsonSerializer.SerializeToNode(args) };
        await _write.WaitAsync();
        try
        {
            await p.StandardInput.WriteLineAsync(payload.ToJsonString());
            await p.StandardInput.FlushAsync();
        }
        finally
        {
            _write.Release();
        }
        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
        if (done != tcs.Task)
        {
            _pending.TryRemove(id, out _);
            throw new WaException("timeout", "WhatsApp tardó demasiado");
        }
        return await tcs.Task;
    }

    public async Task RefreshStatusAsync()
    {
        try
        {
            var s = WaJson.Status(await CallAsync("status"));
            Status = Status with { Unread = s.Unread, State = Status.State == "qr" && s.State != "open" ? "qr" : s.State };
            StatusChanged?.Invoke();
        }
        catch (WaException)
        {
        }
    }

    public async Task<IReadOnlyList<WaChat>> RecentChatsAsync(int limit = 8)
        => WaJson.List(await CallAsync("chats.recent", new { limit }), WaJson.Chat);

    public async Task<IReadOnlyList<WaMessage>> MessagesAsync(string jid, int limit = 20)
        => WaJson.List(await CallAsync("messages.list", new { jid, limit }), e => WaJson.Message(e, jid));

    public async Task<IReadOnlyList<WaFile>> RecentFilesAsync(int limit = 8)
        => WaJson.List(await CallAsync("files.recent", new { limit }), e => WaJson.File(e));

    public async Task<IReadOnlyList<WaContact>> SearchAsync(string q)
        => WaJson.List(await CallAsync("contacts.search", new { q }), e => new WaContact(e.GetProperty("jid").GetString()!, e.GetProperty("name").GetString()!));

    public Task SendTextAsync(string jid, string text) => CallAsync("messages.send", new { jid, text });

    public Task SendFileAsync(string jid, string path) => CallAsync("files.send", new { jid, path }, 300000);

    public async Task<string> DownloadAsync(WaFile f) => (await CallAsync("media.download", new { jid = f.Jid, messageId = f.MessageId }, 300000)).GetProperty("path").GetString()!;

    public async Task<string?> AvatarAsync(string jid)
    {
        var r = await CallAsync("avatar.get", new { jid }, 15000);
        return r.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    }

    public async Task<string> MediaPathAsync(WaFile f) => (await CallAsync("media.cache", new { jid = f.Jid, messageId = f.MessageId }, 300000)).GetProperty("path").GetString()!;

    public Task MarkReadAsync(string jid) => CallAsync("chats.read", new { jid });

    public Task LogoutAsync() => CallAsync("logout");

    public async ValueTask DisposeAsync()
    {
        _stopping = true;
        var p = _proc;
        if (p is null) return;
        try
        {
            p.StandardInput.Close();
            await p.WaitForExitAsync(new CancellationTokenSource(3000).Token);
        }
        catch (Exception)
        {
            try { p.Kill(); } catch (Exception) { }
        }
    }
}
