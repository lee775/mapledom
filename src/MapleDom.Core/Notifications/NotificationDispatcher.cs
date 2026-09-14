using MapleDom.Core.Time;

namespace MapleDom.Core.Notifications;

/// <summary>
/// 알림을 큐에 넣고 전용 스레드에서 하나씩, 최소 간격(<see cref="MinSpacing"/>)을 두고 발송한다.
/// 여러 타이머가 동시에 만기되어도 토스트/사운드가 겹치지 않고 각각 따로 발동된다.
/// <para>
/// 같은 Tag(=같은 타이머)의 알림이 아직 대기 중이면 새 알림으로 교체한다(오래된 리마인드는 보내지 않음).
/// 큐가 가득 차면 가장 오래된 알림을 버린다(최신 알림이 더 중요).
/// </para>
/// </summary>
public sealed class NotificationDispatcher : IAlarmNotifier, IDisposable
{
    private readonly IAlarmNotifier _inner;
    private readonly IClock _clock;
    private readonly List<AlarmNotification> _queue = new();
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly Thread _worker;
    private TimeSpan _minSpacing;
    private TimeSpan? _lastDispatchAt;
    private int _dropped;
    private int _replaced;
    private bool _disposed;

    public NotificationDispatcher(IAlarmNotifier inner, TimeSpan minSpacing, int maxQueueLength = 16, IClock? clock = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (minSpacing < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(minSpacing));
        if (maxQueueLength < 1) throw new ArgumentOutOfRangeException(nameof(maxQueueLength));

        _clock = clock ?? new MonotonicClock();
        _minSpacing = minSpacing;
        MaxQueueLength = maxQueueLength;
        _worker = new Thread(Run) { IsBackground = true, Name = "MapleDom.Notifications" };
        _worker.Start();
    }

    /// <summary>연속 발송 사이의 최소 간격. 실행 중 변경 가능.</summary>
    public TimeSpan MinSpacing
    {
        get { lock (_queue) return _minSpacing; }
        set
        {
            if (value < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(value));
            lock (_queue) _minSpacing = value;
        }
    }

    public int MaxQueueLength { get; }

    /// <summary>대기 중인 같은 Tag 알림을 새 알림으로 교체할지. 기본 true.</summary>
    public bool CoalesceByTag { get; set; } = true;

    /// <summary>큐 초과로 버려진 알림 수.</summary>
    public int Dropped => Volatile.Read(ref _dropped);

    /// <summary>같은 Tag의 새 알림으로 교체되어 발송되지 않은 알림 수.</summary>
    public int Replaced => Volatile.Read(ref _replaced);

    public int Pending
    {
        get { lock (_queue) return _queue.Count; }
    }

    /// <summary>내부 알림기가 예외를 던졌을 때. 워커 스레드에서 발생.</summary>
    public event Action<AlarmNotification, Exception>? DeliveryFailed;

    /// <summary>발송 성공 직후. 워커 스레드에서 발생.</summary>
    public event Action<AlarmNotification>? Delivered;

    public void Notify(AlarmNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        lock (_queue)
        {
            if (_disposed) return;

            if (CoalesceByTag)
                _replaced += _queue.RemoveAll(n => n.Tag == notification.Tag);

            while (_queue.Count >= MaxQueueLength)
            {
                _queue.RemoveAt(0);
                _dropped++;
            }

            _queue.Add(notification);
            Monitor.Pulse(_queue);
        }
    }

    private void Run()
    {
        while (true)
        {
            AlarmNotification next;
            TimeSpan spacing;
            lock (_queue)
            {
                while (_queue.Count == 0 && !_disposed) Monitor.Wait(_queue);
                if (_disposed) return;
                next = _queue[0];
                _queue.RemoveAt(0);
                spacing = _minSpacing;
            }

            if (_lastDispatchAt is { } last)
            {
                var wait = last + spacing - _clock.Now;
                if (wait > TimeSpan.Zero && _stop.Wait(wait)) return;
            }

            try
            {
                _inner.Notify(next);
                Delivered?.Invoke(next);
            }
            catch (Exception ex)
            {
                try { DeliveryFailed?.Invoke(next, ex); } catch { /* 워커는 죽지 않는다 */ }
            }

            _lastDispatchAt = _clock.Now;
        }
    }

    public void Dispose()
    {
        lock (_queue)
        {
            if (_disposed) return;
            _disposed = true;
            _queue.Clear();
            Monitor.PulseAll(_queue);
        }
        _stop.Set();
        if (_worker != Thread.CurrentThread && _worker.IsAlive)
            _worker.Join(TimeSpan.FromSeconds(2));
    }
}
