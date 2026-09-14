using System.Diagnostics;
using MapleDom.Core.Notifications;
using MapleDom.Core.Timers;
using Xunit;

namespace MapleDom.Core.Tests;

public class NotificationDispatcherTests
{
    private static AlarmNotification N(string tag, string message = "msg")
        => new(tag, tag, AlarmKind.Main, tag, message, AlarmSound.Default, AlarmScenario.Default, null);

    [Fact]
    public void Simultaneous_notifications_are_spaced_apart()
    {
        var stamps = new List<long>();
        using var done = new ManualResetEventSlim();
        var inner = new CallbackNotifier(_ =>
        {
            lock (stamps)
            {
                stamps.Add(Stopwatch.GetTimestamp());
                if (stamps.Count == 3) done.Set();
            }
        });
        using var dispatcher = new NotificationDispatcher(inner, TimeSpan.FromMilliseconds(100));

        dispatcher.Notify(N("a"));
        dispatcher.Notify(N("b"));
        dispatcher.Notify(N("c"));

        Assert.True(done.Wait(TimeSpan.FromSeconds(3)), "not all notifications delivered");
        for (var i = 1; i < 3; i++)
        {
            var gap = Stopwatch.GetElapsedTime(stamps[i - 1], stamps[i]);
            Assert.True(gap >= TimeSpan.FromMilliseconds(90), $"gap {i} was {gap.TotalMilliseconds:F1} ms");
        }
    }

    [Fact]
    public void Queued_notification_with_same_tag_is_replaced_by_newer_one()
    {
        var delivered = new List<string>();
        using var gate = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        var inner = new CallbackNotifier(n =>
        {
            gate.Wait();
            lock (delivered)
            {
                delivered.Add(n.Message);
                if (delivered.Count == 3) done.Set();
            }
        });
        using var dispatcher = new NotificationDispatcher(inner, TimeSpan.Zero);

        dispatcher.Notify(N("first"));          // 워커가 꺼내서 gate에서 대기
        Thread.Sleep(100);
        dispatcher.Notify(N("rune", "+1분"));
        dispatcher.Notify(N("doping", "도핑"));
        dispatcher.Notify(N("rune", "+2분"));  // 대기 중인 "+1분"을 교체
        gate.Set();

        Assert.True(done.Wait(TimeSpan.FromSeconds(3)));
        Assert.Equal(new[] { "msg", "도핑", "+2분" }, delivered);
        Assert.Equal(1, dispatcher.Replaced);
        Assert.Equal(0, dispatcher.Dropped);
    }

    [Fact]
    public void Overflow_drops_oldest_queued_notification()
    {
        var delivered = new List<string>();
        using var gate = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        var inner = new CallbackNotifier(n =>
        {
            gate.Wait();
            lock (delivered)
            {
                delivered.Add(n.Tag);
                if (delivered.Count == 3) done.Set();
            }
        });
        using var dispatcher = new NotificationDispatcher(inner, TimeSpan.Zero, maxQueueLength: 2);

        dispatcher.Notify(N("first"));
        Thread.Sleep(100);
        dispatcher.Notify(N("a"));
        dispatcher.Notify(N("b"));
        dispatcher.Notify(N("c"));       // 큐 길이 2 초과 → "a" 버려짐
        gate.Set();

        Assert.True(done.Wait(TimeSpan.FromSeconds(3)));
        Assert.Equal(new[] { "first", "b", "c" }, delivered);
        Assert.Equal(1, dispatcher.Dropped);
    }

    [Fact]
    public void Inner_failure_is_reported_and_processing_continues()
    {
        var calls = 0;
        using var done = new ManualResetEventSlim();
        AlarmNotification? failed = null;
        var inner = new CallbackNotifier(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("toast unavailable");
            done.Set();
        });
        using var dispatcher = new NotificationDispatcher(inner, TimeSpan.Zero);
        dispatcher.DeliveryFailed += (n, _) => failed = n;

        dispatcher.Notify(N("bad"));
        dispatcher.Notify(N("good"));

        Assert.True(done.Wait(TimeSpan.FromSeconds(3)));
        Assert.Equal("bad", failed?.Tag);
    }

    [Fact]
    public void Composite_notifier_calls_all_and_aggregates_errors()
    {
        var hits = 0;
        var composite = new CompositeNotifier(
            new CallbackNotifier(_ => hits++),
            new CallbackNotifier(_ => throw new InvalidOperationException()),
            new CallbackNotifier(_ => hits++));

        Assert.Throws<AggregateException>(() => composite.Notify(N("x")));
        Assert.Equal(2, hits);
    }

    [Fact]
    public void Notify_after_dispose_is_ignored()
    {
        var dispatcher = new NotificationDispatcher(NullNotifier.Instance, TimeSpan.Zero);
        dispatcher.Dispose();

        dispatcher.Notify(N("x"));

        Assert.Equal(0, dispatcher.Pending);
    }
}
