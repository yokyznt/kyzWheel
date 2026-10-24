using KyzWheel.Core.Lifecycle;
using Microsoft.Extensions.Time.Testing;

namespace KyzWheel.Core.Tests;

public sealed class RestartPolicyTests
{
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.Parse("2026-10-23T12:00:00Z"));

    [Fact]
    public void Exit_zero_stops() => Assert.Null(new RestartPolicy(_clock).OnExit(0));

    [Fact]
    public void Crash_backoff_1_2_4_8_16_30()
    {
        var p = new RestartPolicy(_clock);
        var got = new List<double>();
        for (var i = 0; i < 7; i++)
        {
            got.Add(p.OnExit(-1)!.Value.TotalSeconds);
            _clock.Advance(TimeSpan.FromSeconds(15));
        }
        Assert.Equal(new double[] { 1, 2, 4, 8, 16, 30, 30 }, got);
    }

    [Fact]
    public void Six_crashes_within_60s_stops()
    {
        var p = new RestartPolicy(_clock);
        for (var i = 0; i < 5; i++)
        {
            Assert.NotNull(p.OnExit(1));
            _clock.Advance(TimeSpan.FromSeconds(5));
        }
        Assert.Null(p.OnExit(1));
    }

    [Fact]
    public void Backoff_resets_after_5_minutes_stable()
    {
        var p = new RestartPolicy(_clock);
        p.OnExit(1);
        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(2, p.OnExit(1)!.Value.TotalSeconds);
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, p.OnExit(1)!.Value.TotalSeconds);
    }
}
