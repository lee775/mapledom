using MapleDom.Core.Timers;
using Xunit;

namespace MapleDom.Core.Tests;

public class AckTimerTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static AckTimer Rune() => new("rune", "룬", S(900), S(0), S(60));

    private static AckTimer Doping() => new("doping", "도핑", S(1800), S(5), S(60));

    [Fact]
    public void Start_uses_interval_minus_lead()
    {
        var d = Doping();
        d.Start(S(100));

        Assert.Equal(S(1795), d.EffectiveInterval);
        Assert.Equal(S(100 + 1795), d.NextDue);
        Assert.Equal(TimerStatus.Counting, d.Status);
    }

    [Fact]
    public void Main_then_reminders_every_minute_until_acknowledged()
    {
        var r = Rune();
        r.Start(S(0));

        var main = r.Fire(S(900));
        Assert.NotNull(main);
        Assert.Equal(AlarmKind.Main, main!.Kind);
        Assert.Equal(0, main.ReminderIndex);
        Assert.Equal(TimerStatus.Overdue, r.Status);
        Assert.Equal(S(960), r.NextDue);
        Assert.Null(r.Fire(S(959)));

        for (var i = 1; i <= 5; i++)
        {
            var reminder = r.Fire(S(900 + 60 * i));
            Assert.NotNull(reminder);
            Assert.Equal(AlarmKind.Reminder, reminder!.Kind);
            Assert.Equal(i, reminder.ReminderIndex);
            Assert.Equal(S(900 + 60 * i), reminder.ScheduledAt);
            Assert.Equal(i, r.ReminderCount);
            Assert.Equal(TimerStatus.Overdue, r.Status);
        }

        Assert.Equal(S(1260), r.NextDue);
        Assert.Equal(6, r.FireCount);
    }

    [Fact]
    public void Acknowledge_after_reminders_resets_to_full_interval()
    {
        var r = Rune();
        r.Start(S(0));
        r.Fire(S(900));
        r.Fire(S(960));

        r.Acknowledge(S(1000));

        Assert.Equal(TimerStatus.Counting, r.Status);
        Assert.Equal(0, r.ReminderCount);
        Assert.Equal(S(1900), r.NextDue);
        Assert.Equal(TimeSpan.Zero, r.Snapshot(S(1001)).OverdueFor);
    }

    [Fact]
    public void Acknowledge_while_counting_restarts_from_now()
    {
        var r = Rune();
        r.Start(S(0));

        r.Acknowledge(S(300));

        Assert.Equal(S(1200), r.NextDue);
    }

    [Fact]
    public void Acknowledge_when_stopped_starts_the_timer()
    {
        var r = Rune();

        r.Acknowledge(S(5));

        Assert.Equal(TimerStatus.Counting, r.Status);
        Assert.Equal(S(905), r.NextDue);
    }

    [Fact]
    public void Snapshot_overdue_for_measures_time_since_main_due()
    {
        var r = Rune();
        r.Start(S(0));
        r.Fire(S(900));

        var snap = r.Snapshot(S(930));

        Assert.Equal(TimerStatus.Overdue, snap.Status);
        Assert.Equal(S(30), snap.OverdueFor);
        Assert.Equal(S(30), snap.Remaining); // 다음 리마인드까지
        Assert.Equal(TimerKind.Acknowledge, snap.Kind);
        Assert.Equal(S(60), snap.ReminderInterval);
    }

    [Fact]
    public void Main_late_beyond_reminder_interval_reanchors_reminders_to_now()
    {
        var r = Rune();
        r.Start(S(0));

        var evt = r.Fire(S(2000));

        Assert.Equal(S(900), evt!.ScheduledAt);
        Assert.Equal(S(2060), r.NextDue);
    }

    [Fact]
    public void Changing_settings_while_running_recomputes_from_anchor()
    {
        var r = Rune();
        r.Start(S(100));

        r.Interval = S(600);
        Assert.Equal(S(700), r.NextDue);

        r.Set(S(600), S(30), S(10));
        Assert.Equal(S(670), r.NextDue);

        r.Fire(S(670));
        Assert.Equal(S(680), r.NextDue);

        r.ReminderInterval = S(20);
        Assert.Equal(S(690), r.NextDue);
    }

    [Fact]
    public void Stop_resets_reminder_count_and_schedule()
    {
        var r = Rune();
        r.Start(S(0));
        r.Fire(S(900));
        r.Fire(S(960));

        r.Stop();

        Assert.Equal(TimerStatus.Stopped, r.Status);
        Assert.Equal(0, r.ReminderCount);
        Assert.Null(r.NextDue);
        Assert.Equal(TimeSpan.Zero, r.Snapshot(S(1000)).OverdueFor);
    }

    [Theory]
    [InlineData(900, 900, 60)]
    [InlineData(900, 1000, 60)]
    [InlineData(900, -1, 60)]
    [InlineData(900, 0, 0)]
    [InlineData(0, 0, 60)]
    public void Invalid_values_throw(double interval, double lead, double reminder)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new AckTimer("a", "a", S(interval), S(lead), S(reminder)));

    [Fact]
    public void Invalid_change_while_running_keeps_previous_values()
    {
        var r = Rune();
        r.Start(S(0));

        Assert.Throws<ArgumentOutOfRangeException>(() => r.Lead = S(900));

        Assert.Equal(S(0), r.Lead);
        Assert.Equal(S(900), r.NextDue);
    }
}
