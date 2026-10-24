namespace KyzWheel.Core.Lifecycle;

public sealed class RestartPolicy(TimeProvider clock)
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Stable = TimeSpan.FromMinutes(5);
    private const int MaxCrashesInWindow = 6;
    private const double MaxDelaySeconds = 30;

    private readonly Queue<DateTimeOffset> _recent = new();
    private DateTimeOffset? _lastCrash;
    private int _streak;

    public TimeSpan? OnExit(int exitCode)
    {
        if (exitCode == 0) return null;
        var now = clock.GetUtcNow();
        if (_lastCrash is { } last && now - last >= Stable) _streak = 0;
        _lastCrash = now;
        _recent.Enqueue(now);
        while (_recent.Count > 0 && now - _recent.Peek() >= Window) _recent.Dequeue();
        if (_recent.Count >= MaxCrashesInWindow) return null;
        var delay = Math.Min(MaxDelaySeconds, Math.Pow(2, _streak));
        _streak++;
        return TimeSpan.FromSeconds(delay);
    }
}
