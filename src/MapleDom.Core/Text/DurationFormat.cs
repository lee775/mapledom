using System.Globalization;
using MapleDom.Core.Timers;

namespace MapleDom.Core.Text;

public static class DurationFormat
{
    /// <summary>"15분", "29분 55초", "58초", "1시간 2분", "0초"</summary>
    public static string Korean(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = duration.Negate();
        var total = (long)Math.Round(duration.TotalSeconds);
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var seconds = total % 60;

        var parts = new List<string>(3);
        if (hours > 0) parts.Add(hours.ToString(CultureInfo.InvariantCulture) + "시간");
        if (minutes > 0) parts.Add(minutes.ToString(CultureInfo.InvariantCulture) + "분");
        if (seconds > 0 || parts.Count == 0) parts.Add(seconds.ToString(CultureInfo.InvariantCulture) + "초");
        return string.Join(' ', parts);
    }

    /// <summary>카운트다운 표시용 "14:59", "1:02:03". 올림이므로 0.4초 남았을 때 "0:01", 정확히 0일 때 "0:00".</summary>
    public static string Clock(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        var total = (long)Math.Ceiling(duration.TotalSeconds - 1e-9);
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var seconds = total % 60;

        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}");
    }
}

/// <summary>알림 문구 템플릿 치환.</summary>
public static class AlarmTemplate
{
    /// <summary>
    /// {name} 타이머 이름 / {interval} 설정 간격(예: 15분) / {n} 리마인드 회차 / {overdue} 본 알림 이후 경과(예: 3분)
    /// </summary>
    public static string Format(string template, AlarmEvent evt, TimerSnapshot timer)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(timer);
        if (string.IsNullOrEmpty(template)) return "";

        return template
            .Replace("{name}", evt.TimerName, StringComparison.Ordinal)
            .Replace("{interval}", DurationFormat.Korean(timer.Interval), StringComparison.Ordinal)
            .Replace("{n}", evt.ReminderIndex.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{overdue}", DurationFormat.Korean(timer.ReminderInterval * evt.ReminderIndex), StringComparison.Ordinal);
    }
}
