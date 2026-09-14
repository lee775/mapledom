namespace MapleDom.Core.Timers;

/// <summary>
/// 확인형 타이머(룬, 도핑): (Interval - Lead) 만기 → 본 알림 → 확인 전까지 ReminderInterval 마다 리마인드.
/// <see cref="Acknowledge"/>("룬 깟음", "도핑했음")를 누르면 지금부터 다시 (Interval - Lead) 뒤로 예약된다.
/// Lead는 "버튼 누르는 데 걸리는 시간" 보정용(도핑 5초).
/// </summary>
public sealed class AckTimer : TimerBase
{
    private TimeSpan _interval;
    private TimeSpan _lead;
    private TimeSpan _reminderInterval;
    private TimeSpan? _mainDueAt;

    public AckTimer(string id, string name, TimeSpan interval, TimeSpan lead, TimeSpan reminderInterval)
        : base(id, name)
    {
        Validate(interval, lead, reminderInterval);
        _interval = interval;
        _lead = lead;
        _reminderInterval = reminderInterval;
    }

    public override TimerKind Kind => TimerKind.Acknowledge;

    public TimeSpan Interval
    {
        get => _interval;
        set => Set(value, _lead, _reminderInterval);
    }

    public TimeSpan Lead
    {
        get => _lead;
        set => Set(_interval, value, _reminderInterval);
    }

    public TimeSpan ReminderInterval
    {
        get => _reminderInterval;
        set => Set(_interval, _lead, value);
    }

    /// <summary>실제 카운트 길이 = Interval - Lead.</summary>
    public TimeSpan EffectiveInterval => _interval - _lead;

    /// <summary>본 알림 이후 울린 리마인드 횟수. 확인/정지 시 0.</summary>
    public int ReminderCount { get; private set; }

    /// <summary>세 값을 원자적으로 변경. 실행 중이면 현재 구간 기준으로 즉시 재계산된다.</summary>
    public void Set(TimeSpan interval, TimeSpan lead, TimeSpan reminderInterval)
    {
        Validate(interval, lead, reminderInterval);
        _interval = interval;
        _lead = lead;
        _reminderInterval = reminderInterval;
        Reschedule();
    }

    private static void Validate(TimeSpan interval, TimeSpan lead, TimeSpan reminderInterval)
    {
        RequirePositive(interval, nameof(interval));
        RequireNonNegative(lead, nameof(lead));
        RequirePositive(reminderInterval, nameof(reminderInterval));
        if (interval - lead <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lead), lead, "lead must be smaller than interval.");
    }

    /// <summary>시작은 확인과 동일하다(시작 버튼을 누르는 시점 = 룬을 깠거나 도핑한 시점).</summary>
    public override void Start(TimeSpan now) => Acknowledge(now);

    /// <summary>"룬 깟음"/"도핑했음". 정지 상태였다면 시작과 같다.</summary>
    public void Acknowledge(TimeSpan now)
    {
        Anchor = now;
        Status = TimerStatus.Counting;
        ReminderCount = 0;
        _mainDueAt = null;
        NextDue = now + EffectiveInterval;
    }

    public override void Stop()
    {
        base.Stop();
        ReminderCount = 0;
        _mainDueAt = null;
    }

    public override AlarmEvent? Fire(TimeSpan now)
    {
        if (Status == TimerStatus.Stopped || NextDue is not { } due || due > now)
            return null;

        AlarmEvent evt;
        if (Status == TimerStatus.Counting)
        {
            evt = Record(AlarmKind.Main, 0, due, now);
            Status = TimerStatus.Overdue;
            _mainDueAt = due;
        }
        else
        {
            ReminderCount++;
            evt = Record(AlarmKind.Reminder, ReminderCount, due, now);
        }

        Anchor = ChainAnchor(due, now, _reminderInterval);
        NextDue = Anchor + _reminderInterval;
        return evt;
    }

    protected override void Reschedule()
    {
        NextDue = Status switch
        {
            TimerStatus.Counting => Anchor + EffectiveInterval,
            TimerStatus.Overdue => Anchor + _reminderInterval,
            _ => null,
        };
    }

    public override TimerSnapshot Snapshot(TimeSpan now)
    {
        var overdueFor = Status == TimerStatus.Overdue && _mainDueAt is { } mainDue && now > mainDue
            ? now - mainDue
            : TimeSpan.Zero;

        return new TimerSnapshot(
            Id, Name, Kind, Status,
            Remaining: Remaining(now),
            NextDue: NextDue,
            Interval: _interval,
            Gap: TimeSpan.Zero,
            Lead: _lead,
            ReminderInterval: _reminderInterval,
            ReminderCount: ReminderCount,
            OverdueFor: overdueFor,
            FireCount: FireCount,
            LastFiredAt: LastFiredAt,
            CapturedAt: now);
    }
}
