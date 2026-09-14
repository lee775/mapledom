using MapleDom.Core.Notifications;
using Microsoft.Toolkit.Uwp.Notifications;

namespace MapleDom.Windows;

/// <summary>
/// Windows 10/11 시스템 알림(토스트) 백엔드. 패키징되지 않은 일반 exe에서도 동작한다
/// (첫 호출 시 툴킷이 AUMID를 레지스트리 HKCU에 등록).
/// 같은 Tag의 토스트는 교체되므로 58초마다 오는 알림이 알림 센터에 쌓이지 않는다.
/// </summary>
public sealed class WindowsToastNotifier : IAlarmNotifier
{
    public const string GroupName = "MapleDom";

    public void Notify(AlarmNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var builder = new ToastContentBuilder()
            .AddText(notification.Title)
            .AddText(notification.Message);

        builder.AddAudio(notification.Sound == AlarmSound.Silent
            ? new ToastAudio { Silent = true }
            : new ToastAudio { Src = new Uri(SoundUri(notification.Sound)), Loop = false });

        switch (notification.Scenario)
        {
            case AlarmScenario.Reminder:
                builder.SetToastScenario(ToastScenario.Reminder);
                break;
            case AlarmScenario.Alarm:
                builder.SetToastScenario(ToastScenario.Alarm);
                break;
        }

        builder.Show(toast =>
        {
            toast.Tag = notification.Tag;
            toast.Group = GroupName;
            if (notification.ExpireAfter is { } expire && expire > TimeSpan.Zero)
                toast.ExpirationTime = DateTimeOffset.Now + expire;
        });
    }

    public static string SoundUri(AlarmSound sound) => sound switch
    {
        AlarmSound.IM => "ms-winsoundevent:Notification.IM",
        AlarmSound.Mail => "ms-winsoundevent:Notification.Mail",
        AlarmSound.Reminder => "ms-winsoundevent:Notification.Reminder",
        AlarmSound.SMS => "ms-winsoundevent:Notification.SMS",
        AlarmSound.Alarm => "ms-winsoundevent:Notification.Looping.Alarm",
        AlarmSound.Alarm2 => "ms-winsoundevent:Notification.Looping.Alarm2",
        AlarmSound.Alarm3 => "ms-winsoundevent:Notification.Looping.Alarm3",
        AlarmSound.Alarm4 => "ms-winsoundevent:Notification.Looping.Alarm4",
        AlarmSound.Alarm5 => "ms-winsoundevent:Notification.Looping.Alarm5",
        AlarmSound.Call => "ms-winsoundevent:Notification.Looping.Call",
        _ => "ms-winsoundevent:Notification.Default",
    };

    /// <summary>알림 센터에 남아 있는 이 앱의 토스트 수.</summary>
    public static int HistoryCount() => ToastNotificationManagerCompat.History.GetHistory().Count;

    public static void ClearHistory() => ToastNotificationManagerCompat.History.Clear();

    public static void Remove(string tag) => ToastNotificationManagerCompat.History.Remove(tag, GroupName);

    /// <summary>레지스트리 등록 해제(언인스톨 시).</summary>
    public static void Uninstall() => ToastNotificationManagerCompat.Uninstall();
}
