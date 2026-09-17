using System.Text.Json.Serialization;
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
    public double ExpireSeconds { get; set; } = 10;

    /// <summary>이 타이머의 화면 알림 배너가 뜨는 위치.</summary>
    public OverlayPosition OverlayPosition { get; set; } = OverlayPosition.Top;
}

/// <summary>30초 알림(반복형) 타이머 설정. 확인 버튼 없이 Interval + Gap 주기로 계속 울린다.</summary>
public sealed class RepeatTimerSettings
{
    public string Name { get; set; } = "";

    public double IntervalSeconds { get; set; }

    /// <summary>알림 후 다음 카운트까지의 휴식(초). 알람 주기는 Interval + Gap.</summary>
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

/// <summary>
/// 텔레그램 봇 알림 설정. 켜면 Windows 토스트와 함께 봇 메시지도 간다.
/// <para>봇 토큰은 이 설정 파일(%LocalAppData%\MapleDom\settings.json)에 평문으로 저장된다. 저장소에 올리지 말 것.</para>
/// </summary>
public sealed class TelegramSettings
{
    public bool Enabled { get; set; }

    /// <summary>@BotFather가 준 토큰.</summary>
    public string BotToken { get; set; } = "";

    /// <summary>받을 사람(또는 그룹)의 채팅 ID.</summary>
    public string ChatId { get; set; } = "";

    /// <summary>30초 알림도 보낼지. 30초마다 메시지가 가므로 기본은 끔.</summary>
    public bool SendMinute { get; set; }

    public bool SendRune { get; set; } = true;

    public bool SendDoping { get; set; } = true;

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken) && !string.IsNullOrWhiteSpace(ChatId);

    public TelegramSettings Clone() => (TelegramSettings)MemberwiseClone();
}

/// <summary>화면 알림 배너가 뜨는 위치(작업 영역 기준).</summary>
public enum OverlayPosition
{
    TopLeft,
    Top,
    TopRight,
    Center,
    BottomLeft,
    Bottom,
    BottomRight,
}

/// <summary>
/// 화면 알림(오버레이 배너) 설정. 알림이 울릴 때 화면에 크게 띄워 Windows 토스트보다 눈에 잘 띄게 한다.
/// 배너는 클릭을 통과시키고 포커스를 뺏지 않으므로 게임 조작에 방해가 되지 않는다.
/// </summary>
public sealed class OverlaySettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>배너가 떠 있는 시간(초).</summary>
    public double DurationSeconds { get; set; } = 4;

    /// <summary>제목 글자 크기(px). 본문은 이보다 작게 그려진다.</summary>
    public double FontSize { get; set; } = 46;

    /// <summary>배너 불투명도(0.3~1).</summary>
    public double Opacity { get; set; } = 0.92;
}

/// <summary>실행 중인 창 하나를 가리키는 조건. 프로세스 이름(확장자 없이) + 제목 포함 문자열, 대소문자 무시. 둘 다 비면 "지정 안 함".</summary>
public sealed class WindowSelector
{
    public string ProcessName { get; set; } = "";

    /// <summary>창 제목에 포함되어야 하는 문자열. 비어 있으면 제목은 보지 않는다.</summary>
    public string TitleContains { get; set; } = "";

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(ProcessName) && string.IsNullOrWhiteSpace(TitleContains);

    public WindowSelector Clone() => new() { ProcessName = ProcessName, TitleContains = TitleContains };
}

/// <summary>
/// 창 순서(Z-order) 고정 설정. Windows는 위(앞)에서 아래(맨 뒤) 순서이며, 마지막 창은 바닥(HWND_BOTTOM)에 놓인다.
/// 적용 상태에서는 목록의 창을 활성화해도 이 순서가 유지되고, 해제하면 Windows 기본대로 최근 창이 앞에 온다.
/// <para>Windows 목록은 파일에 저장하지 않는다(세션 한정): 앱을 켜면 항상 비어 있고 매번 다시 고른다. Topmost만 저장된다.</para>
/// </summary>
public sealed class WindowOrderSettings
{
    public const int MaxEntries = 16;

    /// <summary>true면 마지막(맨 뒤) 창을 뺀 나머지를 "항상 위" 층에 놓는다.</summary>
    public bool Topmost { get; set; }

    public List<WindowSelector> Windows { get; set; } = new();
}

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 4;

    public static readonly TimeSpan MaxInterval = TimeSpan.FromHours(24);

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public RepeatTimerSettings Minute { get; set; } = SettingsDefaults.Minute();

    public AckTimerSettings Rune { get; set; } = SettingsDefaults.Rune();

    public AckTimerSettings Doping { get; set; } = SettingsDefaults.Doping();

    public NotificationSettings Notifications { get; set; } = new();

    public WindowOrderSettings WindowOrder { get; set; } = new();

    public TelegramSettings Telegram { get; set; } = new();

    public OverlaySettings Overlay { get; set; } = new();

    public static AppSettings CreateDefault() => new();

    /// <summary>비어 있거나 0 이하인 값을 기본값으로 채운다(구버전 파일, 손편집 대비). 자기 자신을 돌려준다.</summary>
    public AppSettings Normalize()
    {
        Minute = NormalizeRepeat(Minute, SettingsDefaults.Minute());
        Rune = NormalizeAck(Rune, SettingsDefaults.Rune());
        Doping = NormalizeAck(Doping, SettingsDefaults.Doping());

        if (Notifications is null) Notifications = new NotificationSettings();
        if (!(Notifications.MinSpacingSeconds >= 0)) Notifications.MinSpacingSeconds = 2;
        if (Notifications.MaxQueueLength < 1) Notifications.MaxQueueLength = 16;

        if (Overlay is null) Overlay = new OverlaySettings();
        if (!(Overlay.DurationSeconds > 0)) Overlay.DurationSeconds = 4;
        if (!(Overlay.FontSize > 0)) Overlay.FontSize = 46;
        if (!(Overlay.Opacity > 0)) Overlay.Opacity = 0.92;

        if (Telegram is null) Telegram = new TelegramSettings();
        Telegram.BotToken = (Telegram.BotToken ?? "").Trim();
        Telegram.ChatId = (Telegram.ChatId ?? "").Trim();

        if (WindowOrder is null) WindowOrder = new WindowOrderSettings();
        if (WindowOrder.Windows is null) WindowOrder.Windows = new List<WindowSelector>();
        WindowOrder.Windows.RemoveAll(w => w is null);
        foreach (var window in WindowOrder.Windows)
        {
            window.ProcessName = (window.ProcessName ?? "").Trim();
            window.TitleContains = (window.TitleContains ?? "").Trim();
        }

        SchemaVersion = CurrentSchemaVersion;
        return this;
    }

    /// <summary>오류 목록(비어 있으면 유효). UI는 저장 전에 이것을 보여준다.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        ValidateRepeat(Minute, errors);
        ValidateAck(Rune, errors);
        ValidateAck(Doping, errors);

        if (!(Notifications.MinSpacingSeconds >= 0)) errors.Add("알림 최소 간격은 0 이상이어야 합니다.");
        if (Notifications.MaxQueueLength < 1) errors.Add("알림 큐 길이는 1 이상이어야 합니다.");

        if (Overlay.Enabled)
        {
            if (!(Overlay.DurationSeconds is > 0 and <= 60)) errors.Add("화면 알림: 표시 시간은 0보다 크고 60초 이하여야 합니다.");
            if (!(Overlay.FontSize is >= 12 and <= 200)) errors.Add("화면 알림: 글자 크기는 12~200 사이여야 합니다.");
            if (!(Overlay.Opacity is >= 0.3 and <= 1)) errors.Add("화면 알림: 불투명도는 30~100% 사이여야 합니다.");
        }

        if (Telegram.Enabled)
        {
            if (string.IsNullOrWhiteSpace(Telegram.BotToken)) errors.Add("텔레그램: 봇 토큰을 입력하세요.");
            else if (!Telegram.BotToken.Contains(':')) errors.Add("텔레그램: 봇 토큰 형식이 올바르지 않습니다(숫자:문자 형태).");
            if (string.IsNullOrWhiteSpace(Telegram.ChatId)) errors.Add("텔레그램: 채팅 ID를 입력하세요. [채팅 ID 찾기]를 눌러 자동으로 넣을 수 있습니다.");
        }

        if (WindowOrder.Windows.Count > WindowOrderSettings.MaxEntries)
            errors.Add($"창 순서: 창은 최대 {WindowOrderSettings.MaxEntries}개까지 넣을 수 있습니다.");
        for (var i = 0; i < WindowOrder.Windows.Count; i++)
        {
            if (WindowOrder.Windows[i].IsEmpty)
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
        if (!Enum.IsDefined(p.OverlayPosition)) p.OverlayPosition = d.OverlayPosition;
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

/// <summary>기본값. 30초 알림 28초+휴식 2초(주기 30초), 룬 15분, 도핑 30분-5초, 리마인드 1분, 알림 유지 10초.</summary>
public static class SettingsDefaults
{
    public static RepeatTimerSettings Minute() => new()
    {
        Name = "30초",
        IntervalSeconds = 28,   // 28초 카운트 + 2초 휴식 = 정확히 30초 주기
        GapSeconds = 2,
        AutoStart = false,
        Notification = new NotificationProfile
        {
            Title = "30초 알림",
            MainMessage = "30초 지났습니다. 움직이세요!",
            ReminderMessage = "",
            Sound = AlarmSound.Default,
            ReminderSound = AlarmSound.Default,
            Scenario = AlarmScenario.Default,
            ExpireSeconds = 10,
            OverlayPosition = OverlayPosition.BottomRight,
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
