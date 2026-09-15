using System.Collections.ObjectModel;
using System.Globalization;
using MapleDom.App.Infrastructure;
using MapleDom.Core.Notifications;
using MapleDom.Core.Settings;

namespace MapleDom.App.ViewModels;

public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public sealed class TimerSettingsEditor
{
    public bool IsAcknowledge { get; init; }
    public bool IsRepeat => !IsAcknowledge;
    public string Name { get; set; } = "";
    public string Interval { get; set; } = "";
    public string Gap { get; set; } = "";
    public string Lead { get; set; } = "";
    public string Reminder { get; set; } = "";
    public bool AutoStart { get; set; }
    public string Title { get; set; } = "";
    public string MainMessage { get; set; } = "";
    public string ReminderMessage { get; set; } = "";
    public AlarmSound Sound { get; set; }
    public AlarmSound ReminderSound { get; set; }
    public AlarmScenario Scenario { get; set; }
    public string Expire { get; set; } = "";

    public static IReadOnlyList<Choice<AlarmSound>> Sounds { get; } =
    [
        new(AlarmSound.Default, "기본"), new(AlarmSound.IM, "메신저"), new(AlarmSound.Mail, "메일"),
        new(AlarmSound.Reminder, "미리 알림"), new(AlarmSound.SMS, "문자"), new(AlarmSound.Alarm, "알람 1"),
        new(AlarmSound.Alarm2, "알람 2"), new(AlarmSound.Alarm3, "알람 3"), new(AlarmSound.Alarm4, "알람 4"),
        new(AlarmSound.Alarm5, "알람 5"), new(AlarmSound.Call, "전화"), new(AlarmSound.Silent, "무음"),
    ];
    public static IReadOnlyList<Choice<AlarmScenario>> Scenarios { get; } =
    [new(AlarmScenario.Default, "일반"), new(AlarmScenario.Reminder, "유지"), new(AlarmScenario.Alarm, "알람")];

    public static TimerSettingsEditor From(RepeatTimerSettings settings) =>
        FromProfile(new() { Name = settings.Name, Interval = Format(settings.IntervalSeconds), Gap = Format(settings.GapSeconds), AutoStart = settings.AutoStart }, settings.Notification);

    public static TimerSettingsEditor From(AckTimerSettings settings) =>
        FromProfile(new() { IsAcknowledge = true, Name = settings.Name, Interval = Format(settings.IntervalSeconds), Lead = Format(settings.LeadSeconds), Reminder = Format(settings.ReminderSeconds), AutoStart = settings.AutoStart }, settings.Notification);

    private static TimerSettingsEditor FromProfile(TimerSettingsEditor editor, NotificationProfile profile)
    {
        editor.Title = profile.Title;
        editor.MainMessage = profile.MainMessage;
        editor.ReminderMessage = profile.ReminderMessage;
        editor.Sound = profile.Sound;
        editor.ReminderSound = profile.ReminderSound;
        editor.Scenario = profile.Scenario;
        editor.Expire = Format(profile.ExpireSeconds);
        return editor;
    }

    public RepeatTimerSettings BuildRepeat(List<string> errors) => new()
    {
        Name = ReadName(errors), IntervalSeconds = Number(Interval, $"{Name}: 간격", errors),
        GapSeconds = Number(Gap, $"{Name}: 휴식 시간", errors), AutoStart = AutoStart, Notification = Profile(errors),
    };

    public AckTimerSettings BuildAcknowledge(List<string> errors) => new()
    {
        Name = ReadName(errors), IntervalSeconds = Number(Interval, $"{Name}: 간격", errors),
        LeadSeconds = Number(Lead, $"{Name}: 선행 시간", errors),
        ReminderSeconds = Number(Reminder, $"{Name}: 리마인드 간격", errors),
        AutoStart = AutoStart, Notification = Profile(errors),
    };

    private string ReadName(List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("타이머 이름을 입력하세요.");
        return Name.Trim();
    }

    private NotificationProfile Profile(List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(Title)) errors.Add($"{Name}: 알림 제목을 입력하세요.");
        if (string.IsNullOrWhiteSpace(MainMessage)) errors.Add($"{Name}: 알림 본문을 입력하세요.");
        if (!Enum.IsDefined(Sound) || !Enum.IsDefined(ReminderSound) || !Enum.IsDefined(Scenario))
            errors.Add($"{Name}: 사운드와 표시 방식을 목록에서 선택하세요.");
        return new NotificationProfile
        {
            Title = Title, MainMessage = MainMessage, ReminderMessage = ReminderMessage,
            Sound = Sound, ReminderSound = ReminderSound, Scenario = Scenario,
            ExpireSeconds = Number(Expire, $"{Name}: 유지 시간", errors, allowNegative: true),
        };
    }

    internal static string Format(double number) => number.ToString("G", CultureInfo.CurrentCulture);

    internal static double Number(string text, string label, List<string> errors, bool allowNegative = false)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) || !double.IsFinite(number))
        {
            errors.Add($"{label}: 올바른 숫자(초)를 입력하세요.");
            return 0;
        }
        // Core does not bound every duration. Keep TimeSpan arithmetic safe on the UI side.
        if (Math.Abs(number) > AppSettings.MaxInterval.TotalSeconds || (!allowNegative && number < 0))
            errors.Add($"{label}: {(allowNegative ? "-86400" : "0")}~86400초 범위로 입력하세요.");
        if (number > 0 && number < 0.001) errors.Add($"{label}: 0보다 큰 시간은 0.001초 이상이어야 합니다.");
        return number;
    }
}

public sealed class SettingsViewModel : ObservableObject
{
    private TimerSettingsEditor _dongkkop = null!;
    private TimerSettingsEditor _rune = null!;
    private TimerSettingsEditor _doping = null!;
    private WindowOrderEditor _windowOrder = null!;
    private bool _notificationsEnabled;
    private string _minSpacing = "";
    private bool _topmost;
    private bool _minimizeToTray;
    private bool _hotkeysEnabled;
    private double _opacity;
    private readonly AppSettings _original;
    private readonly UiSettings _originalUi;

    public SettingsViewModel(AppSettings settings, UiSettings uiSettings, ObservableCollection<string> recentNotifications)
    {
        _original = settings.Clone();
        _originalUi = uiSettings.Clone();
        RecentNotifications = recentNotifications;
        Load(settings, uiSettings);
    }

    public TimerSettingsEditor Dongkkop { get => _dongkkop; private set => Set(ref _dongkkop, value); }
    public TimerSettingsEditor Rune { get => _rune; private set => Set(ref _rune, value); }
    public TimerSettingsEditor Doping { get => _doping; private set => Set(ref _doping, value); }
    public WindowOrderEditor WindowOrder { get => _windowOrder; private set => Set(ref _windowOrder, value); }
    public bool NotificationsEnabled { get => _notificationsEnabled; set => Set(ref _notificationsEnabled, value); }
    public string MinSpacing { get => _minSpacing; set => Set(ref _minSpacing, value); }
    public bool Topmost { get => _topmost; set => Set(ref _topmost, value); }
    public bool MinimizeToTray { get => _minimizeToTray; set => Set(ref _minimizeToTray, value); }
    public bool HotkeysEnabled { get => _hotkeysEnabled; set => Set(ref _hotkeysEnabled, value); }
    public double Opacity { get => _opacity; set => Set(ref _opacity, value); }
    public ObservableCollection<string> Errors { get; } = [];
    public bool HasErrors => Errors.Count > 0;
    public ObservableCollection<string> RecentNotifications { get; }

    private void Load(AppSettings settings, UiSettings uiSettings)
    {
        Dongkkop = TimerSettingsEditor.From(settings.Dongkkop);
        Rune = TimerSettingsEditor.From(settings.Rune);
        Doping = TimerSettingsEditor.From(settings.Doping);
        WindowOrder = WindowOrderEditor.From(settings.WindowOrder);
        NotificationsEnabled = settings.Notifications.Enabled;
        MinSpacing = TimerSettingsEditor.Format(settings.Notifications.MinSpacingSeconds);
        Topmost = uiSettings.Topmost;
        MinimizeToTray = uiSettings.MinimizeToTray;
        HotkeysEnabled = uiSettings.HotkeysEnabled;
        Opacity = uiSettings.Opacity;
        SetErrors([]);
    }

    public void RestoreDefaults() => Load(AppSettings.CreateDefault(), new UiSettings());

    public void SetErrors(IEnumerable<string> errors)
    {
        Errors.Clear();
        foreach (var error in errors.Distinct()) Errors.Add(error);
        OnPropertyChanged(nameof(HasErrors));
    }

    public bool TryBuild(out AppSettings settings, out UiSettings uiSettings)
    {
        var errors = new List<string>();
        settings = _original.Clone();
        settings.Dongkkop = Dongkkop.BuildRepeat(errors);
        settings.Rune = Rune.BuildAcknowledge(errors);
        settings.Doping = Doping.BuildAcknowledge(errors);
        settings.Notifications.Enabled = NotificationsEnabled;
        settings.Notifications.MinSpacingSeconds = TimerSettingsEditor.Number(MinSpacing, "알림 최소 간격", errors);
        settings.WindowOrder = WindowOrder.Build();
        errors.AddRange(settings.Validate());
        foreach (var timer in new[] { settings.Rune, settings.Doping })
        {
            var effective = timer.IntervalSeconds - timer.LeadSeconds;
            if (effective is > 0 and < 0.001) errors.Add($"{timer.Name}: 간격과 선행 시간의 차이는 0.001초 이상이어야 합니다.");
        }
        uiSettings = _originalUi.Clone();
        uiSettings.Topmost = Topmost;
        uiSettings.MinimizeToTray = MinimizeToTray;
        uiSettings.HotkeysEnabled = HotkeysEnabled;
        uiSettings.Opacity = Opacity;
        if (!double.IsFinite(Opacity) || Opacity is < 0.4 or > 1) errors.Add("창 투명도는 40~100% 범위여야 합니다.");
        SetErrors(errors);
        return !HasErrors;
    }
}
