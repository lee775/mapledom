namespace MapleDom.Core.Timers;

public enum TimerKind
{
    /// <summary>만기 → 알림 → 휴식(Gap) → 다시 카운트. 사용자 확인 불필요. (동꼽방지)</summary>
    Repeat,

    /// <summary>만기 → 본 알림 → 확인(Acknowledge) 전까지 리마인드 반복. (룬, 도핑)</summary>
    Acknowledge,
}

public enum TimerStatus
{
    Stopped,

    /// <summary>만기를 향해 카운트 중.</summary>
    Counting,

    /// <summary>본 알림이 울렸고 아직 확인되지 않음. 리마인드 반복 중. (Acknowledge 타이머 전용)</summary>
    Overdue,
}

public enum AlarmKind
{
    /// <summary>본 알림.</summary>
    Main,

    /// <summary>확인되지 않아 반복되는 리마인드 알림.</summary>
    Reminder,
}
