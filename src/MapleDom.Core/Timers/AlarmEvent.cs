namespace MapleDom.Core.Timers;

/// <summary>타이머가 알람을 발동했음을 나타내는 이벤트. 시각은 모두 단조 시계 기준.</summary>
/// <param name="ReminderIndex">본 알림은 0, 리마인드는 1, 2, 3, ...</param>
/// <param name="ScheduledAt">예정 만기 시각.</param>
/// <param name="FiredAt">실제 처리 시각. (FiredAt - ScheduledAt)이 지연이다.</param>
public sealed record AlarmEvent(
    string TimerId,
    string TimerName,
    AlarmKind Kind,
    int ReminderIndex,
    TimeSpan ScheduledAt,
    TimeSpan FiredAt)
{
    public TimeSpan Lateness => FiredAt - ScheduledAt;
}
