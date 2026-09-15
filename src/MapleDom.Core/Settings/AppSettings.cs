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

/// <summary>창 순서 고정 항목. 프로세스 이름(확장자 없이)과 제목 포함 문자열로 실행 중인 창을 찾는다. 둘 다 대소문자 무시.</summary>
public sealed class WindowOrderEntry
{
    public string ProcessName { get; set; } = "";

    /// <summary>창 제목에 포함되어야 하는 문자열. 비어 있으면 제목은 보지 않는다.</summary>
    public string TitleContains { get; set; } = "";
}

/// <summary>창 순서 고정(Z-order) 설정. Windows 목록은 위(앞)에서 아래(뒤) 순서.</summary>
public sealed class WindowOrderSettings
{
    public const int MaxEntries = 16;

    /// <summary>true면 목록의 창이 활성화될 때마다 목록 순서를 다시 적용한다(활성 창은 그대로).</summary>
    public bool Enabled { get; set; }

    /// <summary>true면 목록의 창을 모두 "항상 위" 층에 놓고 그 안에서 정렬한다.</summary>
    public bool Topmost { get; set; }

    public List<WindowOrderEntry> Windows { get; set; } = new();
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

    public WindowOrderSettings WindowOrder { get; set; } = new();

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

        if (WindowOrder is null) WindowOrder = new WindowOrderSettings();
        if (WindowOrder.Windows is null) WindowOrder.Windows = new List<WindowOrderEntry>();
        WindowOrder.Windows.RemoveAll(e => e is null);
        foreach (var entry in WindowOrder.Windows)
        {
            entry.ProcessName = (entry.ProcessName ?? "").Trim();
            entry.TitleContains = (entry.TitleContains ?? "").Trim();
        }

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

        if (WindowOrder.Windows.Count > WindowOrderSettings.MaxEntries)
            errors.Add($"창 순서: 창은 최대 {WindowOrderSettings.MaxEntries}개까지 넣을 수 있습니다.");
        for (var i = 0; i < WindowOrder.Windows.Count; i++)
        {
            var entry = WindowOrder.Windows[i];
            if (string.IsNullOrWhiteSpace(entry.ProcessName) && string.IsNullOrWhiteSpace(entry.TitleContains))
                errors.Add($"창 순서 {i + 1}번: 프로세스 이름 또는 제목을 입력하세요.");
        }
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
            ExpireSeconds = 10,
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
            ExpireSeconds = 10,
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
            ExpireSeconds = 10,
        },
    };
}
