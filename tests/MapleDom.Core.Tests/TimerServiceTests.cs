using MapleDom.Core.Notifications;
using MapleDom.Core.Settings;
using MapleDom.Core.Timers;
using Xunit;

namespace MapleDom.Core.Tests;

public class TimerServiceTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    /// <summary>발송된 알림을 모으고, index 번째 알림이 도착할 때까지 기다린다.</summary>
    private sealed class Collector : IAlarmNotifier
    {
        private readonly List<AlarmNotification> _items = new();
        private readonly AutoResetEvent _signal = new(false);

        public int Count
        {
            get { lock (_items) return _items.Count; }
        }

        public void Notify(AlarmNotification notification)
        {
            lock (_items) _items.Add(notification);
            _signal.Set();
        }

        public AlarmNotification WaitFor(int index, int timeoutMs = 3000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (true)
            {
                lock (_items)
                {
                    if (_items.Count > index) return _items[index];
                }
                var remaining = deadline - Environment.TickCount64;
                if (remaining <= 0) throw new TimeoutException($"notification #{index} did not arrive");
                _signal.WaitOne((int)remaining);
            }
        }

        public bool WaitForAny(int timeoutMs) => Count > 0 || _signal.WaitOne(timeoutMs);
    }

    [Fact]
    public void Defaults_build_three_timers_with_requested_values()
    {
        using var svc = new TimerService(AppSettings.CreateDefault(), NullNotifier.Instance, new FakeClock());

        var snaps = svc.Snapshot().ToDictionary(s => s.Id);

        Assert.Equal(3, snaps.Count);
        Assert.Equal(TimerKind.Repeat, snaps[TimerService.MinuteId].Kind);
        Assert.Equal(S(60), snaps[TimerService.MinuteId].Interval);
        Assert.Equal(S(0), snaps[TimerService.MinuteId].Gap);
        Assert.Equal(TimerKind.Acknowledge, snaps[TimerService.RuneId].Kind);

        Assert.Equal(S(900), snaps[TimerService.RuneId].Interval);
        Assert.Equal(S(0), snaps[TimerService.RuneId].Lead);
        Assert.Equal(S(60), snaps[TimerService.RuneId].ReminderInterval);

        Assert.Equal(S(1800), snaps[TimerService.DopingId].Interval);
        Assert.Equal(S(5), snaps[TimerService.DopingId].Lead);
        Assert.Equal(S(1795), snaps[TimerService.DopingId].EffectiveInterval);
        Assert.Equal(S(60), snaps[TimerService.DopingId].ReminderInterval);

        Assert.All(snaps.Values, s => Assert.False(s.IsRunning));
    }

    [Fact]
    public void Rune_main_alarm_then_reminder_then_acknowledge()
    {
        var clock = new FakeClock();
        var sink = new Collector();
        using var svc = new TimerService(AppSettings.CreateDefault(), sink, clock);

        svc.Start(TimerService.RuneId);
        Assert.Equal(S(900), svc.Get(TimerService.RuneId).Remaining);

        clock.Advance(900);
        svc.Engine.Pump();
        var main = sink.WaitFor(0);
        Assert.Equal(AlarmKind.Main, main.Kind);
        Assert.Equal("룬 타이머", main.Title);
        Assert.Contains("룬 깟음", main.Message);
        Assert.Equal(TimerService.RuneId, main.Tag);
        Assert.Equal(AlarmScenario.Reminder, main.Scenario);

        clock.Advance(60);
        svc.Engine.Pump();
        var reminder = sink.WaitFor(1);
        Assert.Equal(AlarmKind.Reminder, reminder.Kind);
        Assert.Contains("1분", reminder.Message);
        Assert.Equal(TimerStatus.Overdue, svc.Get(TimerService.RuneId).Status);

        svc.Acknowledge(TimerService.RuneId);
        var snap = svc.Get(TimerService.RuneId);
        Assert.Equal(TimerStatus.Counting, snap.Status);
        Assert.Equal(S(900), snap.Remaining);
        Assert.Equal(0, snap.ReminderCount);
    }

    [Fact]
    public void Minute_timer_repeats_without_acknowledgement()
    {
        var clock = new FakeClock();
        var sink = new Collector();
        using var svc = new TimerService(AppSettings.CreateDefault(), sink, clock);

        svc.Start(TimerService.MinuteId);
        Assert.Equal(S(60), svc.Get(TimerService.MinuteId).Remaining);

        clock.Advance(60);
        svc.Engine.Pump();
        var first = sink.WaitFor(0);
        Assert.Equal("1분 알림", first.Title);
        Assert.Equal(AlarmKind.Main, first.Kind);
        var snap = svc.Get(TimerService.MinuteId);
        Assert.Equal(TimerStatus.Counting, snap.Status);   // 확인 버튼 없이 바로 다음 주기
        Assert.Equal(S(60), snap.Remaining);

        clock.Advance(60);
        svc.Engine.Pump();
        sink.WaitFor(1);
        Assert.Equal(2, svc.Get(TimerService.MinuteId).FireCount);
    }

    [Fact]
    public void Minute_timer_gap_extends_the_cycle()
    {
        var clock = new FakeClock();
        var settings = AppSettings.CreateDefault();
        settings.Minute.IntervalSeconds = 58;
        settings.Minute.GapSeconds = 2;
        using var svc = new TimerService(settings, NullNotifier.Instance, clock);

        svc.Start(TimerService.MinuteId);
        clock.Advance(58);
        svc.Engine.Pump();

        Assert.Equal(S(60), svc.Get(TimerService.MinuteId).Remaining);   // 휴식 2초 + 58초
    }

    [Fact]
    public void Doping_start_counts_29m55s()
    {
        using var svc = new TimerService(AppSettings.CreateDefault(), NullNotifier.Instance, new FakeClock());

        var snap = svc.Start(TimerService.DopingId);

        Assert.Equal(S(29 * 60 + 55), snap.Remaining);
    }

    [Fact]
    public void Muted_suppresses_notifications_but_timer_keeps_running()
    {
        var clock = new FakeClock();
        var sink = new Collector();
        using var svc = new TimerService(AppSettings.CreateDefault(), sink, clock) { Muted = true };

        svc.Start(TimerService.RuneId);
        clock.Advance(900);
        svc.Engine.Pump();

        Assert.False(sink.WaitForAny(300));
        var snap = svc.Get(TimerService.RuneId);
        Assert.Equal(TimerStatus.Overdue, snap.Status);
        Assert.Equal(1, snap.FireCount);
    }

    [Fact]
    public void Notifications_disabled_in_settings_suppresses_notifications()
    {
        var clock = new FakeClock();
        var sink = new Collector();
        var settings = AppSettings.CreateDefault();
        settings.Notifications.Enabled = false;
        using var svc = new TimerService(settings, sink, clock);

        svc.Start(TimerService.RuneId);
        clock.Advance(900);
        svc.Engine.Pump();

        Assert.False(sink.WaitForAny(300));
    }

    [Fact]
    public void ApplySettings_changes_running_timer_live()
    {
        var clock = new FakeClock();
        using var svc = new TimerService(AppSettings.CreateDefault(), NullNotifier.Instance, clock);
        svc.Start(TimerService.RuneId);
        clock.Advance(300);

        var s = svc.Settings;
        s.Rune.IntervalSeconds = 600;
        s.Rune.Name = "룬2";
        s.Notifications.MinSpacingSeconds = 0.5;
        svc.ApplySettings(s);

        var snap = svc.Get(TimerService.RuneId);
        Assert.Equal(S(300), snap.Remaining);
        Assert.Equal("룬2", snap.Name);
        Assert.Equal(600, svc.Settings.Rune.IntervalSeconds);
        Assert.Equal(S(0.5), svc.Dispatcher.MinSpacing);
    }

    [Fact]
    public void ApplySettings_rejects_invalid_and_keeps_previous()
    {
        using var svc = new TimerService(AppSettings.CreateDefault(), NullNotifier.Instance, new FakeClock());
        var s = svc.Settings;
        s.Doping.LeadSeconds = 5000;

        Assert.Throws<ArgumentException>(() => svc.ApplySettings(s));

        Assert.Equal(5, svc.Settings.Doping.LeadSeconds);
        Assert.Equal(S(5), svc.Get(TimerService.DopingId).Lead);
    }

    [Fact]
    public void Settings_getter_returns_a_copy()
    {
        using var svc = new TimerService(AppSettings.CreateDefault(), NullNotifier.Instance, new FakeClock());

        svc.Settings.Rune.IntervalSeconds = 1;

        Assert.Equal(900, svc.Settings.Rune.IntervalSeconds);
    }

    [Fact]
    public void SendTest_delivers_even_when_muted()
    {
        var sink = new Collector();
        using var svc = new TimerService(AppSettings.CreateDefault(), sink, new FakeClock()) { Muted = true };

        var built = svc.SendTest(TimerService.DopingId);

        Assert.Equal("도핑 타이머", built.Title);
        Assert.Equal(built, sink.WaitFor(0));
    }

    [Fact]
    public void SendTest_reminder_uses_reminder_template()
    {
        var sink = new Collector();
        using var svc = new TimerService(AppSettings.CreateDefault(), sink, new FakeClock());

        var built = svc.SendTest(TimerService.RuneId, AlarmKind.Reminder);

        Assert.Contains("아직", built.Message);
        Assert.Contains("1분", built.Message);
    }

    [Fact]
    public void Toggle_flips_running_state()
    {
        using var svc = new TimerService(AppSettings.CreateDefault(), NullNotifier.Instance, new FakeClock());

        Assert.True(svc.Toggle(TimerService.RuneId).IsRunning);
        Assert.False(svc.Toggle(TimerService.RuneId).IsRunning);
    }

    [Fact]
    public void AutoStart_starts_timers_on_construction()
    {
        var settings = AppSettings.CreateDefault();
        settings.Doping.AutoStart = true;
        using var svc = new TimerService(settings, NullNotifier.Instance, new FakeClock());

        Assert.True(svc.Get(TimerService.DopingId).IsRunning);
        Assert.False(svc.Get(TimerService.RuneId).IsRunning);
    }

    [Fact]
    public void Simultaneous_alarms_are_delivered_separately_in_order()
    {
        var clock = new FakeClock();
        var sink = new Collector();
        var settings = AppSettings.CreateDefault();
        settings.Rune.IntervalSeconds = 60;
        settings.Doping.IntervalSeconds = 60;
        settings.Doping.LeadSeconds = 0;
        settings.Notifications.MinSpacingSeconds = 0.05;
        using var svc = new TimerService(settings, sink, clock);

        svc.Start(TimerService.RuneId);
        svc.Start(TimerService.DopingId);
        clock.Advance(60);
        svc.Engine.Pump();

        Assert.Equal(TimerService.RuneId, sink.WaitFor(0).TimerId);
        Assert.Equal(TimerService.DopingId, sink.WaitFor(1).TimerId);
    }
}
