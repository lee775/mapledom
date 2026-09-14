namespace MapleDom.Core.Timers;

/// <summary>
/// 동꼽방지형 타이머: Interval 만기 → 알림 → Gap 휴식 → 다시 Interval 카운트.
/// 주기는 Interval + Gap 이며, 다음 만기는 예정 시각에 체인되어 누적 오차가 없다.
/// </summary>
public sealed class RepeatTimer : TimerBase
{
    private TimeSpan _interval;
    private TimeSpan _gap;

    public RepeatTimer(string id, string name, TimeSpan interval, TimeSpan gap)
        : base(id, name)
    {
        RequirePositive(interval, nameof(interval));
        RequireNonNegative(gap, nameof(gap));
        _interval = interval;
        _gap = gap;
    }

    public override TimerKind Kind => TimerKind.Repeat;

    public TimeSpan Interval
    {
        get => _interval;
        set => Set(value, _gap);
    }

    /// <summary>알림 후 다음 카운트 시작 전 휴식 시간.</summary>
    public TimeSpan Gap
    {
        get => _gap;
        set => Set(_interval, value);
    }

    public TimeSpan Period => _interval + _gap;

    /// <summary>두 값을 원자적으로 변경. 실행 중이면 현재 구간 기준으로 즉시 재계산된다.</summary>
    public void Set(TimeSpan interval, TimeSpan gap)
    {
        RequirePositive(interval, nameof(interval));
        RequireNonNegative(gap, nameof(gap));
        _interval = interval;
        _gap = gap;
        Reschedule();
    }

    public override void Start(TimeSpan now)
    {
        Anchor = now;
        Status = TimerStatus.Counting;
        NextDue = now + _interval;
    }

    public override AlarmEvent? Fire(TimeSpan now)
    {
        if (Status == TimerStatus.Stopped || NextDue is not { } due || due > now)
            return null;

        var evt = Record(AlarmKind.Main, 0, due, now);
        Anchor = ChainAnchor(due, now, Period) + _gap;
        NextDue = Anchor + _interval;
        return evt;
    }

    protected override void Reschedule()
    {
        if (Status == TimerStatus.Stopped) return;
        NextDue = Anchor + _interval;
    }

    public override TimerSnapshot Snapshot(TimeSpan now) => new(
        Id, Name, Kind, Status,
        Remaining: Remaining(now),
        NextDue: NextDue,
        Interval: _interval,
        Gap: _gap,
        Lead: TimeSpan.Zero,
        ReminderInterval: TimeSpan.Zero,
        ReminderCount: 0,
        OverdueFor: TimeSpan.Zero,
        FireCount: FireCount,
        LastFiredAt: LastFiredAt,
        CapturedAt: now);
}
