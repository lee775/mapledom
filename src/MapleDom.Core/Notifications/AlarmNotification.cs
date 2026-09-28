using MapleDom.Core.Settings;
using MapleDom.Core.Timers;

namespace MapleDom.Core.Notifications;

/// <summary>Windows 토스트의 ms-winsoundevent 사운드에 대응한다.</summary>
public enum AlarmSound
{
    Default,
    IM,
    Mail,
    Reminder,
    SMS,
    Alarm,
    Alarm2,
    Alarm3,
    Alarm4,
    Alarm5,
    Call,
    Silent,
}

public enum AlarmScenario
{
    /// <summary>일반 토스트. 몇 초 뒤 자동으로 사라진다.</summary>
    Default,

    /// <summary>사용자가 닫을 때까지 화면에 남는다.</summary>
    Reminder,

    /// <summary>알람 취급. 화면에 남고, 집중 지원 "알람만" 모드에서도 표시된다.</summary>
    Alarm,
}

/// <summary>발송 준비가 끝난 알림 1건. Tag가 같은 알림은 이전 토스트를 교체한다(알림 센터 누적 방지).</summary>
public sealed record AlarmNotification(
    string TimerId,
    string Tag,
    AlarmKind Kind,
    string Title,
    string Message,
    AlarmSound Sound,
    AlarmScenario Scenario,
    TimeSpan? ExpireAfter,
    OverlayPosition? OverlayPosition = null);

/// <summary>알림 백엔드. Windows 토스트 구현은 MapleDom.Windows 프로젝트에 있다.</summary>
public interface IAlarmNotifier
{
    void Notify(AlarmNotification notification);
}

public sealed class NullNotifier : IAlarmNotifier
{
    public static readonly NullNotifier Instance = new();

    public void Notify(AlarmNotification notification)
    {
    }
}

/// <summary>콜백 기반 알림기. 테스트, 로그, 앱 내 알림 목록 등에 사용.</summary>
public sealed class CallbackNotifier(Action<AlarmNotification> callback) : IAlarmNotifier
{
    private readonly Action<AlarmNotification> _callback = callback ?? throw new ArgumentNullException(nameof(callback));

    public void Notify(AlarmNotification notification) => _callback(notification);
}

/// <summary>여러 알림기에 순서대로 전달한다. 하나가 실패해도 나머지는 실행되며, 예외는 마지막에 모아서 던진다.</summary>
public sealed class CompositeNotifier(params IAlarmNotifier[] notifiers) : IAlarmNotifier
{
    private readonly IAlarmNotifier[] _notifiers = notifiers ?? throw new ArgumentNullException(nameof(notifiers));

    public void Notify(AlarmNotification notification)
    {
        List<Exception>? errors = null;
        foreach (var notifier in _notifiers)
        {
            try
            {
                notifier.Notify(notification);
            }
            catch (Exception ex)
            {
                (errors ??= new()).Add(ex);
            }
        }

        if (errors is not null)
            throw new AggregateException("One or more notifiers failed.", errors);
    }
}
