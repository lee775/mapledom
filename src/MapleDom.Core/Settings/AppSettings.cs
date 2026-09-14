using MapleDom.Core.Notifications;

namespace MapleDom.Core.Settings;

/// <summary>타이머별 알림 문구/사운드. 문구 토큰: {name} {interval} {n} {overdue}</summary>
public sealed class NotificationProfile
{
    public string Title { get; set; } = "";

    public string MainMessage { get; set; } = "";

    /// <summary>리마인드 문구. 비어 있으면 MainMessage를 사용한다.</summary>
    public string ReminderMessage { get; set; } = "";

    public AlarmSound Sound { get; set; } = AlarmSound.Default;

    public AlarmSound ReminderSound { get; set; } = AlarmSound.Default;

    public AlarmScenario Scenario { get; set; } = AlarmScenario.Default;

    /// <summary>알림 센터에 남는 시간(초). 0 이하면 만료 없음.</summary>
    public double ExpireSeconds { get; set; } = 60;
}

/// <summary>동꼽방지(반복형) 타이머 설정.</summary>
public sealed class RepeatTimerSettings
{
    public string Name { get; set; } = "";

    public double IntervalSeconds { get; set; }

    /// <summary>알림 후 다음 카운트까지의 휴식(초).</summary>
    public double GapSeconds { get; set; }

    public bool AutoStart { get; set; }

    public NotificationProfile Notification { get; set; } = new();
}

/// <summary>룬/도핑(확인형) 타이머 설정.</summary>
public sealed class AckTimerSettings
{
    public string Name { get; set; } = "";

    public double IntervalSeconds { get; set; }

    /// <summary>버튼 누르는 시간 보정(초). 실제 카운트는 IntervalSeconds - LeadSeconds.</summary>
    public double LeadSeconds { get; set; }

    /// <summary>확인 전까지 리마인드 간격(초).</summary>
    public double ReminderSeconds { get; set; }

    public bool AutoStart { get; set; }

    public NotificationProfile Notification { get; set; } = new();
}

public sealed class NotificationSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>연속 알림 사이 최소 간격(초). 동시 만기 시 겹침 방지.</summary>
    public double MinSpacingSeconds { get; set; } = 2;

    public int MaxQueueLength { get; set; } = 16;
}

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public static readonly TimeSpan MaxInterval = TimeSpan.FromHours(24);

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public RepeatTimerSettings Dongkkop { get; set; } = SettingsDefaults.Dongkkop();

    public AckTimerSettings Rune { get; set; } = SettingsDefaults.Rune();

    public AckTimerSettings Doping { get; set; } = SettingsDefaults.Doping();

    public NotificationSettings Notifications { get; set; } = new();

    public static AppSettings CreateDefault() => new();

    /// <summary>비어 있거나 0 이하인 값을 기본값으로 채운다(구버전 파일, 손편집 대비). 자기 자신을 돌려준다.</summary>
    public AppSettings Normalize()
    {
        Dongkkop = NormalizeRepeat(Dongkkop, SettingsDefaults.Dongkkop());
        Rune = NormalizeAck(Rune, SettingsDefaults.Rune());
        Doping = NormalizeAck(Doping, SettingsDefaults.Doping());

        if (Notifications is null) Notifications = new NotificationSettings();
        if (!(Notifications.MinSpacingSeconds >= 0)) Notifications.MinSpacingSeconds = 2;
        if (Notifications.MaxQueueLength < 1) Notifications.MaxQueueLength = 16;

        SchemaVersion = CurrentSchemaVersion;
        return this;
    }

    /// <summary>오류 목록(비어 있으면 유효). UI는 저장 전에 이것을 보여준다.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        ValidateRepeat(Dongkkop, errors);
        ValidateAck(Rune, errors);
        ValidateAck(Doping, errors);

        if (!(Notifications.MinSpacingSeconds >= 0)) errors.Add("알림 최소 간격은 0 이상이어야 합니다.");
        if (Notifications.MaxQueueLength < 1) errors.Add("알림 큐 길이는 1 이상이어야 합니다.");
        return errors;
    }

    public AppSettings Clone() => SettingsStore.Deserialize(SettingsStore.Serialize(this));

    private static RepeatTimerSettings NormalizeRepeat(RepeatTimerSettings? s, RepeatTimerSettings d)
    {
        if (s is null) return d;
        if (string.IsNullOrWhiteSpace(s.Name)) s.Name = d.Name;
        if (!(s.IntervalSeconds > 0)) s.IntervalSeconds = d.IntervalSeconds;
        if (!(s.GapSeconds >= 0)) s.GapSeconds = d.GapSeconds;
        s.Notification = NormalizeProfile(s.Notification, d.Notification);
        return s;
    }

    private static AckTimerSettings NormalizeAck(AckTimerSettings? s, AckTimerSettings d)
    {
        if (s is null) return d;
        if (string.IsNullOrWhiteSpace(s.Name)) s.Name = d.Name;
        if (!(s.IntervalSeconds > 0)) s.IntervalSeconds = d.IntervalSeconds;
        if (!(s.LeadSeconds >= 0)) s.LeadSeconds = d.LeadSeconds;
        if (!(s.ReminderSeconds > 0)) s.ReminderSeconds = d.ReminderSeconds;
        s.Notification = NormalizeProfile(s.Notification, d.Notification);
        return s;
    }

    private static NotificationProfile NormalizeProfile(NotificationProfile? p, NotificationProfile d)
    {
        if (p is null) return d;
        if (string.IsNullOrWhiteSpace(p.Title)) p.Title = d.Title;
        if (string.IsNullOrWhiteSpace(p.MainMessage)) p.MainMessage = d.MainMessage;
        if (string.IsNullOrWhiteSpace(p.ReminderMessage)) p.ReminderMessage = d.ReminderMessage;
        if (double.IsNaN(p.ExpireSeconds)) p.ExpireSeconds = d.ExpireSeconds;
        return p;
    }

    private static void ValidateRepeat(RepeatTimerSettings s, List<string> errors)
    {
        var label = s.Name;
        if (!(s.IntervalSeconds > 0)) errors.Add($"{label}: 간격은 0보다 커야 합니다.");
        if (s.IntervalSeconds > MaxInterval.TotalSeconds) errors.Add($"{label}: 간격은 24시간 이하여야 합니다.");
        if (!(s.GapSeconds >= 0)) errors.Add($"{label}: 휴식 시간은 0 이상이어야 합니다.");
    }

    private static void ValidateAck(AckTimerSettings s, List<string> errors)
    {
        var label = s.Name;
        if (!(s.IntervalSeconds > 0)) errors.Add($"{label}: 간격은 0보다 커야 합니다.");
        if (s.IntervalSeconds > MaxInterval.TotalSeconds) errors.Add($"{label}: 간격은 24시간 이하여야 합니다.");
        if (!(s.LeadSeconds >= 0)) errors.Add($"{label}: 선행 시간은 0 이상이어야 합니다.");
        if (!(s.IntervalSeconds - s.LeadSeconds > 0)) errors.Add($"{label}: 선행 시간은 간격보다 작아야 합니다.");
        if (!(s.ReminderSeconds > 0)) errors.Add($"{label}: 리마인드 간격은 0보다 커야 합니다.");
    }
}

/// <summary>사용자가 요청한 기본값. 동꼽방지 58초+휴식 2초, 룬 15분, 도핑 30분-5초, 리마인드 1분.</summary>
public static class SettingsDefaults
{
    public static RepeatTimerSettings Dongkkop() => new()
    {
        Name = "동꼽방지",
        IntervalSeconds = 58,
        GapSeconds = 2,
        AutoStart = false,
        Notification = new NotificationProfile
        {
            Title = "동꼽방지",
            MainMessage = "움직이세요! ({interval} 경과)",
            ReminderMessage = "",
            Sound = AlarmSound.Default,
            ReminderSound = AlarmSound.Default,
            Scenario = AlarmScenario.Default,
            ExpireSeconds = 30,
        },
    };

    public static AckTimerSettings Rune() => new()
    {
        Name = "룬",
        IntervalSeconds = 15 * 60,
        LeadSeconds = 0,
        ReminderSeconds = 60,
        AutoStart = false,
        Notification = new NotificationProfile
        {
            Title = "룬 타이머",
            MainMessage = "룬 생성! 룬을 까고 [룬 깟음]을 누르세요.",
            ReminderMessage = "룬 아직 안 깠어요! (+{overdue} 경과)",
            Sound = AlarmSound.Reminder,
            ReminderSound = AlarmSound.Reminder,
            Scenario = AlarmScenario.Reminder,
            ExpireSeconds = 120,
        },
    };

    public static AckTimerSettings Doping() => new()
    {
        Name = "도핑",
        IntervalSeconds = 30 * 60,
        LeadSeconds = 5,
        ReminderSeconds = 60,
        AutoStart = false,
        Notification = new NotificationProfile
        {
            Title = "도핑 타이머",
            MainMessage = "도핑 시간! 도핑 후 [도핑했음]을 누르세요.",
            ReminderMessage = "도핑 아직 안 했어요! (+{overdue} 경과)",
            Sound = AlarmSound.Reminder,
            ReminderSound = AlarmSound.Reminder,
            Scenario = AlarmScenario.Reminder,
            ExpireSeconds = 120,
        },
    };
}
