namespace MapleDom.Core.Timers;

/// <summary>
/// 스레드 없는 순수 상태머신. 엔진이 <see cref="IsDue"/>를 확인하고 <see cref="Fire"/>를 호출한다.
/// 스레드 안전하지 않으므로 반드시 <see cref="Engine.TimerEngine"/>을 통해서만 조작한다.
/// </summary>
public abstract class TimerBase
{
    private string _name;

    protected TimerBase(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Timer id is required.", nameof(id));

        Id = id;
        _name = string.IsNullOrWhiteSpace(name) ? id : name;
    }

    public string Id { get; }

    public string Name
    {
        get => _name;
        set => _name = string.IsNullOrWhiteSpace(value) ? Id : value;
    }

    public abstract TimerKind Kind { get; }

    public TimerStatus Status { get; protected set; } = TimerStatus.Stopped;

    /// <summary>다음 만기 시각(단조 시계). 정지 상태면 null.</summary>
    public TimeSpan? NextDue { get; protected set; }

    public TimeSpan? LastFiredAt { get; protected set; }

    public int FireCount { get; protected set; }

    /// <summary>현재 구간의 기준 시각. NextDue는 항상 Anchor + 구간 길이로 계산된다.</summary>
    protected TimeSpan Anchor { get; set; }

    public bool IsRunning => Status != TimerStatus.Stopped;

    public TimeSpan Remaining(TimeSpan now)
    {
        if (NextDue is not { } due) return TimeSpan.Zero;
        var remaining = due - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public bool IsDue(TimeSpan now) => NextDue is { } due && due <= now;

    public abstract void Start(TimeSpan now);

    public virtual void Stop()
    {
        Status = TimerStatus.Stopped;
        NextDue = null;
    }

    /// <summary>만기된 알람을 최대 1개 발생시키고 다음 만기를 계산한다. 만기 전이거나 정지 상태면 null.</summary>
    public abstract AlarmEvent? Fire(TimeSpan now);

    public abstract TimerSnapshot Snapshot(TimeSpan now);

    /// <summary>설정 변경 후 Anchor 기준으로 NextDue를 재계산한다. 정지 상태면 아무것도 하지 않는다.</summary>
    protected abstract void Reschedule();

    protected AlarmEvent Record(AlarmKind kind, int reminderIndex, TimeSpan scheduledAt, TimeSpan now)
    {
        FireCount++;
        LastFiredAt = now;
        return new AlarmEvent(Id, Name, kind, reminderIndex, scheduledAt, now);
    }

    /// <summary>
    /// 다음 구간의 기준점. 정상 범위의 지연이면 예정 시각(due)에 체인해 누적 오차를 없애고,
    /// 절전 등으로 한 구간(period) 이상 늦었으면 현재 시각으로 재정렬한다(밀린 알람 폭주 방지).
    /// </summary>
    protected static TimeSpan ChainAnchor(TimeSpan due, TimeSpan now, TimeSpan period)
        => now - due > period ? now : due;

    protected static void RequirePositive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be greater than zero.");
    }

    protected static void RequireNonNegative(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(name, value, $"{name} must not be negative.");
    }
}
