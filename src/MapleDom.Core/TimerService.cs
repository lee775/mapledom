using MapleDom.Core.Engine;
using MapleDom.Core.Notifications;
using MapleDom.Core.Settings;
using MapleDom.Core.Text;
using MapleDom.Core.Time;
using MapleDom.Core.Timers;

namespace MapleDom.Core;

/// <summary>
/// UI가 사용하는 단일 진입점.
/// 설정(<see cref="AppSettings"/>)으로 타이머 3개(1분/룬/도핑)를 구성하고,
/// 알람 이벤트를 알림(제목/본문/사운드)으로 바꿔 <see cref="NotificationDispatcher"/>로 겹치지 않게 발송한다.
/// <para>스레드 규칙: 모든 이벤트는 백그라운드 스레드에서 발생한다. UI는 Dispatcher로 마샬링할 것.</para>
/// </summary>
public sealed class TimerService : IDisposable
{
    public const string MinuteId = "minute";
    public const string RuneId = "rune";
    public const string DopingId = "doping";

    /// <summary>메인 창 카드 순서.</summary>
    public static readonly IReadOnlyList<string> TimerIds = [MinuteId, RuneId, DopingId];

    private readonly object _gate = new();
    private readonly TimerEngine _engine;
    private readonly NotificationDispatcher _dispatcher;
    private AppSettings _settings;
    private volatile bool _muted;
    private bool _disposed;

    /// <param name="settings">복사되어 보관된다. 이후 변경은 <see cref="ApplySettings"/>로.</param>
    /// <param name="notifier">실제 알림 백엔드(Windows 토스트 등). 발송은 내부 큐가 직렬화한다.</param>
    /// <param name="clock">null이면 단조 시계.</param>
    public TimerService(AppSettings settings, IAlarmNotifier notifier, IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(notifier);

        settings = settings.Clone().Normalize();
        ThrowIfInvalid(settings);
        _settings = settings;

        _engine = new TimerEngine(clock);
        _dispatcher = new NotificationDispatcher(
            notifier,
            Seconds(settings.Notifications.MinSpacingSeconds),
            settings.Notifications.MaxQueueLength,
            _engine.Clock);

        _engine.Add(new RepeatTimer(
            MinuteId,
            settings.Minute.Name,
            Seconds(settings.Minute.IntervalSeconds),
            Seconds(settings.Minute.GapSeconds)));

        _engine.Add(new AckTimer(
            RuneId,
            settings.Rune.Name,
            Seconds(settings.Rune.IntervalSeconds),
            Seconds(settings.Rune.LeadSeconds),
            Seconds(settings.Rune.ReminderSeconds)));

        _engine.Add(new AckTimer(
            DopingId,
            settings.Doping.Name,
            Seconds(settings.Doping.IntervalSeconds),
            Seconds(settings.Doping.LeadSeconds),
            Seconds(settings.Doping.ReminderSeconds)));

        _engine.AlarmFired += OnAlarmFired;

        if (settings.Minute.AutoStart) _engine.Start(MinuteId);
        if (settings.Rune.AutoStart) _engine.Start(RuneId);
        if (settings.Doping.AutoStart) _engine.Start(DopingId);
    }

    public TimerEngine Engine => _engine;

    public NotificationDispatcher Dispatcher => _dispatcher;

    /// <summary>단조 시계의 현재 값. 스냅샷의 NextDue와 비교해 부드러운 카운트다운을 그릴 때 사용.</summary>
    public TimeSpan Now => _engine.Now;

    /// <summary>현재 설정의 복사본. 수정한 뒤 <see cref="ApplySettings"/>로 반영한다.</summary>
    public AppSettings Settings
    {
        get { lock (_gate) return _settings.Clone(); }
    }

    /// <summary>true면 알림만 끈다. 타이머는 계속 돈다.</summary>
    public bool Muted
    {
        get => _muted;
        set => _muted = value;
    }

    /// <summary>알람 발동(알림 발송 여부와 무관). 엔진 스레드.</summary>
    public event Action<AlarmEvent>? AlarmFired
    {
        add => _engine.AlarmFired += value;
        remove => _engine.AlarmFired -= value;
    }

    /// <summary>타이머 상태 변경(시작/정지/확인/설정/알람). 엔진 스레드 또는 호출자 스레드.</summary>
    public event Action<TimerSnapshot>? TimerChanged
    {
        add => _engine.TimerChanged += value;
        remove => _engine.TimerChanged -= value;
    }

    /// <summary>이벤트 핸들러가 예외를 던졌을 때. 엔진은 계속 동작한다.</summary>
    public event Action<Exception>? HandlerFailed
    {
        add => _engine.HandlerFailed += value;
        remove => _engine.HandlerFailed -= value;
    }

    /// <summary>알림이 발송 큐에 들어갈 때(발송 직전). 앱 내 로그용. 엔진 스레드.</summary>
    public event Action<AlarmNotification>? NotificationQueued;

    /// <summary>알림 백엔드가 실패했을 때(토스트 비활성화 등). 알림 워커 스레드.</summary>
    public event Action<AlarmNotification, Exception>? NotificationFailed
    {
        add => _dispatcher.DeliveryFailed += value;
        remove => _dispatcher.DeliveryFailed -= value;
    }

    /// <summary>
    /// 설정을 실행 중에 반영한다. 간격이 바뀌면 현재 구간 시작점 기준으로 남은 시간이 즉시 재계산된다
    /// (예: 15분 중 5분 경과 상태에서 10분으로 바꾸면 5분 남음). 유효하지 않으면 ArgumentException.
    /// </summary>
    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var next = settings.Clone().Normalize();
        ThrowIfInvalid(next);

        lock (_gate) _settings = next;

        _engine.Configure<RepeatTimer>(MinuteId, t =>
        {
            t.Name = next.Minute.Name;
            t.Set(Seconds(next.Minute.IntervalSeconds), Seconds(next.Minute.GapSeconds));
        });
        _engine.Configure<AckTimer>(RuneId, t =>
        {
            t.Name = next.Rune.Name;
            t.Set(Seconds(next.Rune.IntervalSeconds), Seconds(next.Rune.LeadSeconds), Seconds(next.Rune.ReminderSeconds));
        });
        _engine.Configure<AckTimer>(DopingId, t =>
        {
            t.Name = next.Doping.Name;
            t.Set(Seconds(next.Doping.IntervalSeconds), Seconds(next.Doping.LeadSeconds), Seconds(next.Doping.ReminderSeconds));
        });

        _dispatcher.MinSpacing = Seconds(next.Notifications.MinSpacingSeconds);
    }

    public IReadOnlyList<TimerSnapshot> Snapshot() => _engine.Snapshot();

    public TimerSnapshot Get(string id) => _engine.Get(id);

    public TimerSnapshot Start(string id) => _engine.Start(id);

    public TimerSnapshot Stop(string id) => _engine.Stop(id);

    public TimerSnapshot Toggle(string id) => _engine.Get(id).IsRunning ? _engine.Stop(id) : _engine.Start(id);

    /// <summary>"룬 깟음" / "도핑했음". 정지 상태였다면 시작과 같다. 반복형(1분)은 재시작.</summary>
    public TimerSnapshot Acknowledge(string id) => _engine.Acknowledge(id);

    public void StartAll() => _engine.StartAll();

    public void StopAll() => _engine.StopAll();

    /// <summary>알람 이벤트를 알림(제목/본문/사운드)으로 변환한다. 설정 미리보기용으로 공개.</summary>
    public AlarmNotification BuildNotification(AlarmEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        NotificationProfile profile;
        lock (_gate) profile = ProfileFor(_settings, evt.TimerId);

        var timer = _engine.Get(evt.TimerId);
        var isReminder = evt.Kind == AlarmKind.Reminder;
        var template = isReminder && !string.IsNullOrWhiteSpace(profile.ReminderMessage)
            ? profile.ReminderMessage
            : profile.MainMessage;

        return new AlarmNotification(
            TimerId: evt.TimerId,
            Tag: evt.TimerId,
            Kind: evt.Kind,
            Title: AlarmTemplate.Format(profile.Title, evt, timer),
            Message: AlarmTemplate.Format(template, evt, timer),
            Sound: isReminder ? profile.ReminderSound : profile.Sound,
            Scenario: profile.Scenario,
            ExpireAfter: profile.ExpireSeconds > 0 ? Seconds(profile.ExpireSeconds) : null,
            OverlayPosition: profile.OverlayPosition);
    }

    /// <summary>지정 타이머의 알림을 지금 보낸다(설정 화면의 "테스트 알림"). Muted와 무관하게 발송된다.</summary>
    public AlarmNotification SendTest(string id, AlarmKind kind = AlarmKind.Main)
    {
        var timer = _engine.Get(id);
        var now = _engine.Now;
        var evt = new AlarmEvent(id, timer.Name, kind, kind == AlarmKind.Reminder ? 1 : 0, now, now);
        var notification = BuildNotification(evt);
        _dispatcher.Notify(notification);
        return notification;
    }

    private void OnAlarmFired(AlarmEvent evt)
    {
        bool enabled;
        lock (_gate) enabled = _settings.Notifications.Enabled;
        if (_muted || !enabled) return;

        var notification = BuildNotification(evt);
        NotificationQueued?.Invoke(notification);
        _dispatcher.Notify(notification);
    }

    private static NotificationProfile ProfileFor(AppSettings settings, string id) => id switch
    {
        MinuteId => settings.Minute.Notification,
        RuneId => settings.Rune.Notification,
        DopingId => settings.Doping.Notification,
        _ => throw new KeyNotFoundException($"Timer '{id}' has no notification profile."),
    };

    private static void ThrowIfInvalid(AppSettings settings)
    {
        var errors = settings.Validate();
        if (errors.Count > 0)
            throw new ArgumentException("Invalid settings: " + string.Join("; ", errors), nameof(settings));
    }

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.AlarmFired -= OnAlarmFired;
        _engine.Dispose();
        _dispatcher.Dispose();
    }
}
