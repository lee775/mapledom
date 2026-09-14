using MapleDom.Core.Time;
using MapleDom.Core.Timers;

namespace MapleDom.Core.Engine;

/// <summary>
/// 모든 타이머를 하나의 전용 스레드로 구동하는 스케줄러.
/// <para>
/// 정확도: 만기 <see cref="CoarseWaitMargin"/> 전까지는 커널 대기(시스템 타이머 해상도 ~15.6ms의 영향을 받음),
/// 그 이후는 스핀 대기로 만기 시각을 서브밀리초 단위로 맞춘다. 알람당 최대 수십 ms의 스핀이므로 CPU 부담은 무시할 수 있다.
/// </para>
/// <para>
/// 스레드 규칙: <see cref="AlarmFired"/>, <see cref="TimerChanged"/>는 엔진 스레드 또는 호출자 스레드에서 발생한다.
/// UI는 반드시 자신의 스레드로 마샬링(Dispatcher.Invoke 등)해야 한다. 핸들러 예외는 <see cref="HandlerFailed"/>로 전달되고 엔진은 계속 동작한다.
/// </para>
/// </summary>
public sealed class TimerEngine : IDisposable
{
    /// <summary>커널 대기를 끝내고 스핀 대기로 전환하는 만기 전 여유.</summary>
    public static readonly TimeSpan CoarseWaitMargin = TimeSpan.FromMilliseconds(32);

    private static readonly TimeSpan MaxWait = TimeSpan.FromDays(1);

    private readonly object _gate = new();
    private readonly List<TimerBase> _timers = new();
    private readonly IClock _clock;
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly Thread? _thread;
    private volatile bool _disposed;

    public event Action<AlarmEvent>? AlarmFired;
    public event Action<TimerSnapshot>? TimerChanged;
    public event Action<Exception>? HandlerFailed;

    /// <param name="clock">null이면 <see cref="MonotonicClock"/>.</param>
    /// <param name="runScheduler">false면 스레드를 띄우지 않는다. 테스트에서 <see cref="Pump"/>로 직접 구동할 때 사용.</param>
    public TimerEngine(IClock? clock = null, bool runScheduler = true)
    {
        _clock = clock ?? new MonotonicClock();
        if (runScheduler)
        {
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "MapleDom.TimerEngine",
                Priority = ThreadPriority.AboveNormal,
            };
            _thread.Start();
        }
    }

    public IClock Clock => _clock;

    public TimeSpan Now => _clock.Now;

    public bool IsRunning => _thread is { IsAlive: true } && !_disposed;

    public void Add(TimerBase timer)
    {
        ArgumentNullException.ThrowIfNull(timer);
        TimerSnapshot snapshot;
        lock (_gate)
        {
            if (_timers.Any(t => t.Id == timer.Id))
                throw new ArgumentException($"Duplicate timer id '{timer.Id}'.", nameof(timer));
            _timers.Add(timer);
            snapshot = timer.Snapshot(_clock.Now);
        }
        _wake.Set();
        Raise(TimerChanged, snapshot);
    }

    public bool Remove(string id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _timers.RemoveAll(t => t.Id == id) > 0;
        }
        if (removed) _wake.Set();
        return removed;
    }

    public IReadOnlyList<TimerSnapshot> Snapshot()
    {
        lock (_gate)
        {
            var now = _clock.Now;
            var result = new TimerSnapshot[_timers.Count];
            for (var i = 0; i < _timers.Count; i++) result[i] = _timers[i].Snapshot(now);
            return result;
        }
    }

    public TimerSnapshot? Find(string id)
    {
        lock (_gate)
        {
            return FindCore(id)?.Snapshot(_clock.Now);
        }
    }

    public TimerSnapshot Get(string id)
        => Find(id) ?? throw new KeyNotFoundException($"Timer '{id}' not found.");

    public TimerSnapshot Start(string id) => Mutate(id, static (t, now) => t.Start(now));

    public TimerSnapshot Stop(string id) => Mutate(id, static (t, _) => t.Stop());

    /// <summary>확인형 타이머는 확인(재예약), 반복형 타이머는 재시작.</summary>
    public TimerSnapshot Acknowledge(string id) => Mutate(id, static (t, now) =>
    {
        if (t is AckTimer ack) ack.Acknowledge(now);
        else t.Start(now);
    });

    /// <summary>타이머 설정을 엔진 락 안에서 변경한다. 변경 즉시 스케줄이 재계산된다.</summary>
    public TimerSnapshot Configure<T>(string id, Action<T> configure) where T : TimerBase
    {
        ArgumentNullException.ThrowIfNull(configure);
        return Mutate(id, (t, _) =>
        {
            if (t is not T typed)
                throw new InvalidCastException($"Timer '{id}' is {t.GetType().Name}, not {typeof(T).Name}.");
            configure(typed);
        });
    }

    public void StartAll() => MutateAll(static (t, now) => t.Start(now));

    public void StopAll() => MutateAll(static (t, _) => t.Stop());

    /// <summary>
    /// 만기된 타이머를 한 번 처리하고 다음 만기 시각을 돌려준다(없으면 null).
    /// 스케줄러 스레드가 내부적으로 호출하며, runScheduler=false 인 경우 외부에서 직접 구동할 수 있다.
    /// </summary>
    public TimeSpan? Pump()
    {
        List<AlarmEvent>? fired = null;
        List<TimerSnapshot>? changed = null;
        TimeSpan? next = null;

        lock (_gate)
        {
            var now = _clock.Now;
            foreach (var timer in _timers)
            {
                if (timer.IsDue(now) && timer.Fire(now) is { } evt)
                {
                    (fired ??= new()).Add(evt);
                    (changed ??= new()).Add(timer.Snapshot(now));
                }

                if (timer.NextDue is { } due && (next is null || due < next.Value))
                    next = due;
            }
        }

        if (fired is not null)
            foreach (var evt in fired) Raise(AlarmFired, evt);
        if (changed is not null)
            foreach (var snapshot in changed) Raise(TimerChanged, snapshot);

        return next;
    }

    private TimerBase? FindCore(string id)
    {
        foreach (var t in _timers)
            if (t.Id == id) return t;
        return null;
    }

    private TimerSnapshot Mutate(string id, Action<TimerBase, TimeSpan> action)
    {
        TimerSnapshot snapshot;
        lock (_gate)
        {
            var timer = FindCore(id) ?? throw new KeyNotFoundException($"Timer '{id}' not found.");
            var now = _clock.Now;
            action(timer, now);
            snapshot = timer.Snapshot(now);
        }
        _wake.Set();
        Raise(TimerChanged, snapshot);
        return snapshot;
    }

    private void MutateAll(Action<TimerBase, TimeSpan> action)
    {
        TimerSnapshot[] snapshots;
        lock (_gate)
        {
            var now = _clock.Now;
            foreach (var timer in _timers) action(timer, now);
            snapshots = new TimerSnapshot[_timers.Count];
            for (var i = 0; i < _timers.Count; i++) snapshots[i] = _timers[i].Snapshot(now);
        }
        _wake.Set();
        foreach (var snapshot in snapshots) Raise(TimerChanged, snapshot);
    }

    private void Run()
    {
        while (!_disposed)
        {
            // Reset 이후에 일어난 모든 변경(Set)은 아래 대기를 즉시 깨운다.
            _wake.Reset();
            var next = Pump();
            if (_disposed) break;

            if (next is null)
            {
                _wake.Wait();
                continue;
            }

            WaitUntil(next.Value);
        }
    }

    private void WaitUntil(TimeSpan due)
    {
        var remaining = due - _clock.Now;
        if (remaining > CoarseWaitMargin)
        {
            var coarse = remaining - CoarseWaitMargin;
            if (coarse > MaxWait) coarse = MaxWait;
            if (_wake.Wait(coarse)) return; // 상태 변경 또는 Dispose로 깨어남
        }

        // 세밀 구간: 시스템 타이머 해상도에 의존하지 않도록 Sleep(1) 없이 스핀/양보만 사용.
        var spinner = new SpinWait();
        while (!_disposed && !_wake.IsSet && _clock.Now < due)
            spinner.SpinOnce(sleep1Threshold: -1);
    }

    private void Raise<T>(Action<T>? handler, T arg)
    {
        if (handler is null) return;
        try
        {
            handler(arg);
        }
        catch (Exception ex)
        {
            try { HandlerFailed?.Invoke(ex); } catch { /* 마지막 방어선: 엔진 스레드는 죽지 않는다 */ }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wake.Set();

        if (_thread is { } thread && thread != Thread.CurrentThread && thread.IsAlive)
            thread.Join(TimeSpan.FromSeconds(2));

        if (_thread is null || !_thread.IsAlive)
            _wake.Dispose();
    }
}
