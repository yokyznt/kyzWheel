using System.IO.Pipes;

namespace KyzWheel.App.Shell;

public sealed class SingleInstance : IDisposable
{
    private const string PipeName = "kyzWheel.ipc";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();

    public bool IsFirst { get; }

    public event Action<string>? Command;

    public SingleInstance()
    {
        _mutex = new Mutex(true, @"Local\kyzWheel.app", out var first);
        IsFirst = first;
        if (first) _ = ListenAsync(_cts.Token);
    }

    public static void Signal(string command)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            c.Connect(500);
            using var w = new StreamWriter(c);
            w.WriteLine(command);
        }
        catch (Exception)
        {
        }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await s.WaitForConnectionAsync(ct);
                using var r = new StreamReader(s);
                var line = await r.ReadLineAsync(ct);
                if (!string.IsNullOrWhiteSpace(line)) Command?.Invoke(line.Trim());
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (IsFirst) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
