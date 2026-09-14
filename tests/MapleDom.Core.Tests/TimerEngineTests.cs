using MapleDom.Core.Engine;
using MapleDom.Core.Timers;
using Xunit;

namespace MapleDom.Core.Tests;

public class TimerEngineTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Pump_fires_due_timers_and_returns_earliest_next_due()
    {
        var clock = new FakeClock();
        using var engine = new TimerEngine(clock, runScheduler: false);
        var fired = new List<AlarmEvent>();
        engine.AlarmFired += fired.Add;

        engine.Add(new RepeatTimer("a", "A", Ms(1000), Ms(0)));
        engine.Add(new AckTimer("b", "B", Ms(1000), Ms(0), Ms(500)));
        engine.StartAll();

        Assert.Equal(clock.Now + Ms(1000), engine.Pump());
        Assert.Empty(fired);

        clock.Advance(Ms(1000));
        var next = engine.Pump();

        Assert.Equal(new[] { "a", "b" }, fired.Select(e => e.TimerId));
        Assert.Equal(clock.Now + Ms(500), next); // B의 리마인드가 가장 빠름
        Assert.Equal(TimerStatus.Overdue, engine.Get("b").Status);
    }

    [Fact]
    public void Pump_with_no_running_timers_returns_null()
    {
        using var engine = new TimerEngine(new FakeClock(), runScheduler: false);
        engine.Add(new RepeatTimer("a", "A", Ms(1000), Ms(0)));

        Assert.Null(engine.Pump());
    }

    [Fact]
    public void Acknowledge_on_repeat_timer_restarts_it()
    {
        var clock = new FakeClock();
        using var engine = new TimerEngine(clock, runScheduler: false);
        engine.Add(new RepeatTimer("a", "A", Ms(1000), Ms(0)));
        engine.Start("a");
        clock.Advance(Ms(700));

        var snap = engine.Acknowledge("a");

        Assert.Equal(Ms(1000), snap.Remaining);
    }

    [Fact]
    public void TimerChanged_is_raised_on_mutations_and_alarms()
    {
        var clock = new FakeClock();
        using var engine = new TimerEngine(clock, runScheduler: false);
        var changes = new List<TimerSnapshot>();
        engine.TimerChanged += changes.Add;

        engine.Add(new RepeatTimer("a", "A", Ms(100), Ms(0)));
        engine.Start("a");
        clock.Advance(Ms(100));
        engine.Pump();
        engine.Stop("a");

        Assert.Equal(
            new[] { TimerStatus.Stopped, TimerStatus.Counting, TimerStatus.Counting, TimerStatus.Stopped },
            changes.Select(c => c.Status));
        Assert.Equal(1, changes[2].FireCount);
    }

    [Fact]
    public void Get_unknown_id_throws_and_find_returns_null()
    {
        using var engine = new TimerEngine(new FakeClock(), runScheduler: false);

        Assert.Null(engine.Find("nope"));
        Assert.Throws<KeyNotFoundException>(() => engine.Get("nope"));
        Assert.Throws<KeyNotFoundException>(() => engine.Start("nope"));
    }

    [Fact]
    public void Duplicate_id_throws()
    {
        using var engine = new TimerEngine(new FakeClock(), runScheduler: false);
        engine.Add(new RepeatTimer("a", "A", Ms(1000), Ms(0)));

        Assert.Throws<ArgumentException>(() => engine.Add(new AckTimer("a", "A2", Ms(1000), Ms(0), Ms(10))));
    }

    [Fact]
    public void Configure_with_wrong_type_throws()
    {
        using var engine = new TimerEngine(new FakeClock(), runScheduler: false);
        engine.Add(new RepeatTimer("a", "A", Ms(1000), Ms(0)));

        Assert.Throws<InvalidCastException>(() => engine.Configure<AckTimer>("a", _ => { }));
    }

    [Fact]
    public void Scheduler_fires_on_time_with_real_clock()
    {
        using var engine = new TimerEngine();
        var events = new List<AlarmEvent>();
        using var done = new ManualResetEventSlim();
        engine.AlarmFired += e =>
        {
            lock (events)
            {
                events.Add(e);
                if (events.Count == 5) done.Set();
            }
        };

        engine.Add(new RepeatTimer("t", "T", Ms(120), Ms(30)));
        engine.Start("t");

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)), "timer did not fire 5 times");

        AlarmEvent[] snapshot;
        lock (events) snapshot = events.ToArray();
        for (var i = 0; i < snapshot.Length; i++)
        {
            Assert.True(snapshot[i].Lateness >= TimeSpan.Zero, "fired before schedule");
            Assert.True(snapshot[i].Lateness < Ms(25), $"alarm {i} late by {snapshot[i].Lateness.TotalMilliseconds:F2} ms");
            if (i > 0)
                Assert.Equal(Ms(150), snapshot[i].ScheduledAt - snapshot[i - 1].ScheduledAt);
        }
    }

    [Fact]
    public void Configure_wakes_scheduler_immediately()
    {
        using var engine = new TimerEngine();
        using var fired = new ManualResetEventSlim();
        engine.AlarmFired += _ => fired.Set();

        engine.Add(new RepeatTimer("t", "T", TimeSpan.FromSeconds(30), Ms(0)));
        engine.Start("t");
        Thread.Sleep(50);

        engine.Configure<RepeatTimer>("t", t => t.Interval = Ms(100));

        Assert.True(fired.Wait(TimeSpan.FromSeconds(2)), "scheduler did not pick up the new interval");
    }

    [Fact]
    public void Handler_exception_does_not_stop_scheduler()
    {
        using var engine = new TimerEngine();
        var count = 0;
        Exception? failure = null;
        using var second = new ManualResetEventSlim();
        engine.HandlerFailed += ex => failure = ex;
        engine.AlarmFired += _ =>
        {
            if (Interlocked.Increment(ref count) == 1) throw new InvalidOperationException("boom");
            second.Set();
        };

        engine.Add(new RepeatTimer("t", "T", Ms(50), Ms(0)));
        engine.Start("t");

        Assert.True(second.Wait(TimeSpan.FromSeconds(2)), "engine stopped after handler exception");
        Assert.IsType<InvalidOperationException>(failure);
    }

    [Fact]
    public void Stop_prevents_firing()
    {
        using var engine = new TimerEngine();
        var fired = 0;
        engine.AlarmFired += _ => Interlocked.Increment(ref fired);

        engine.Add(new RepeatTimer("t", "T", Ms(100), Ms(0)));
        engine.Start("t");
        engine.Stop("t");
        Thread.Sleep(300);

        Assert.Equal(0, fired);
        Assert.False(engine.Get("t").IsRunning);
    }

    [Fact]
    public void Dispose_stops_thread()
    {
        var engine = new TimerEngine();
        engine.Add(new RepeatTimer("t", "T", Ms(50), Ms(0)));
        engine.Start("t");
        Assert.True(engine.IsRunning);

        engine.Dispose();

        Assert.False(engine.IsRunning);
    }
}
