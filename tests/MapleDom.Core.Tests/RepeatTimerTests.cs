using MapleDom.Core.Timers;
using Xunit;

namespace MapleDom.Core.Tests;

public class RepeatTimerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(58);
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(2);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static RepeatTimer Create() => new("d", "동꼽방지", Interval, Gap);

    [Fact]
    public void Start_schedules_first_due_after_interval()
    {
        var t = Create();
        t.Start(S(100));

        Assert.Equal(TimerStatus.Counting, t.Status);
        Assert.Equal(S(158), t.NextDue);
        Assert.Equal(S(58), t.Remaining(S(100)));
        Assert.Equal(S(8), t.Remaining(S(150)));
    }

    [Fact]
    public void Fire_before_due_returns_null()
    {
        var t = Create();
        t.Start(S(100));

        Assert.Null(t.Fire(S(157.999)));
        Assert.Equal(0, t.FireCount);
        Assert.Equal(S(158), t.NextDue);
    }

    [Fact]
    public void Fire_at_due_chains_next_due_from_scheduled_time_not_actual_time()
    {
        var t = Create();
        t.Start(S(100));

        var evt = t.Fire(S(158.010)); // 10ms 늦게 처리됨

        Assert.NotNull(evt);
        Assert.Equal(AlarmKind.Main, evt!.Kind);
        Assert.Equal(S(158), evt.ScheduledAt);
        Assert.Equal(TimeSpan.FromMilliseconds(10), evt.Lateness);
        Assert.Equal(S(158 + 2 + 58), t.NextDue); // 휴식 2초 + 58초, 예정 시각 기준
        Assert.Equal(1, t.FireCount);
        Assert.Equal(TimerStatus.Counting, t.Status);
    }

    [Fact]
    public void Full_day_of_cycles_has_zero_drift()
    {
        var t = Create();
        t.Start(TimeSpan.Zero);

        for (var i = 1; i <= 1440; i++)
        {
            var due = t.NextDue!.Value;
            var evt = t.Fire(due + TimeSpan.FromMilliseconds(7)); // 매번 7ms 늦게 처리
            Assert.NotNull(evt);
            Assert.Equal(S(58) + S(60) * (i - 1), evt!.ScheduledAt);
        }

        Assert.Equal(S(58) + S(60) * 1440, t.NextDue);
        Assert.Equal(1440, t.FireCount);
    }

    [Fact]
    public void Late_by_more_than_a_period_reanchors_to_now()
    {
        var t = Create();
        t.Start(S(0));

        var evt = t.Fire(S(500)); // 절전 등으로 크게 밀림

        Assert.Equal(S(58), evt!.ScheduledAt);
        Assert.Equal(S(500 + 2 + 58), t.NextDue);
    }

    [Fact]
    public void Late_within_a_period_still_chains()
    {
        var t = Create();
        t.Start(S(0));

        t.Fire(S(58 + 30));

        Assert.Equal(S(58 + 2 + 58), t.NextDue);
    }

    [Fact]
    public void Changing_settings_while_running_recomputes_from_cycle_start()
    {
        var t = Create();
        t.Start(S(100));

        t.Interval = S(30);
        Assert.Equal(S(130), t.NextDue);

        t.Set(S(10), S(5));
        Assert.Equal(S(110), t.NextDue);
        Assert.Equal(S(15), t.Period);
    }

    [Fact]
    public void Changing_settings_while_stopped_does_not_schedule()
    {
        var t = Create();
        t.Interval = S(10);
        Assert.Null(t.NextDue);
        Assert.Equal(TimerStatus.Stopped, t.Status);
    }

    [Fact]
    public void Stop_clears_schedule()
    {
        var t = Create();
        t.Start(S(1));
        t.Stop();

        Assert.Equal(TimerStatus.Stopped, t.Status);
        Assert.Null(t.NextDue);
        Assert.Null(t.Fire(S(1000)));
        Assert.Equal(TimeSpan.Zero, t.Remaining(S(5)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(10, -1)]
    public void Invalid_values_throw(double interval, double gap)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new RepeatTimer("d", "d", S(interval), S(gap)));

    [Fact]
    public void Snapshot_reports_state()
    {
        var t = Create();
        t.Start(S(100));
        t.Fire(S(158));

        var snap = t.Snapshot(S(160));

        Assert.Equal("d", snap.Id);
        Assert.Equal("동꼽방지", snap.Name);
        Assert.Equal(TimerKind.Repeat, snap.Kind);
        Assert.Equal(TimerStatus.Counting, snap.Status);
        Assert.Equal(S(58), snap.Remaining);
        Assert.Equal(S(58), snap.Interval);
        Assert.Equal(S(2), snap.Gap);
        Assert.Equal(1, snap.FireCount);
        Assert.Equal(S(158), snap.LastFiredAt);
        Assert.True(snap.IsRunning);
    }
}
