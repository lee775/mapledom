namespace MapleDom.Core.Timers;

/// <summary>
/// 타이머 상태의 불변 스냅샷. UI는 이것만 읽어 표시한다.
/// Remaining/OverdueFor는 CapturedAt 시점 값이며, 부드러운 표시가 필요하면
/// NextDue와 <see cref="Engine.TimerEngine.Now"/>로 직접 계산해도 된다.
/// </summary>
public sealed record TimerSnapshot(
    string Id,
    string Name,
    TimerKind Kind,
    TimerStatus Status,
    TimeSpan Remaining,
    TimeSpan? NextDue,
    TimeSpan Interval,
    TimeSpan Gap,
    TimeSpan Lead,
    TimeSpan ReminderInterval,
    int ReminderCount,
    TimeSpan OverdueFor,
    int FireCount,
    TimeSpan? LastFiredAt,
    TimeSpan CapturedAt)
{
    public bool IsRunning => Status != TimerStatus.Stopped;

    /// <summary>Acknowledge 타이머의 실제 카운트 길이(Interval - Lead). Repeat 타이머는 Interval.</summary>
    public TimeSpan EffectiveInterval => Interval - Lead;
}
