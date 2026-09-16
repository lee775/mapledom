using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using MapleDom.App.Infrastructure;
using MapleDom.Core;
using MapleDom.Core.Notifications;
using MapleDom.Core.Settings;
using MapleDom.Core.Windowing;
using MapleDom.Core.Timers;
using MapleDom.Windows;

namespace MapleDom.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly TimerService _service;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _refreshTimer;
    private readonly WindowOrderKeeper? _keeper;
    private bool _visible;
    private bool _disposed;
    private string _warning = "";
    private UiSettings _uiSettings;

    /// <param name="windowOrder">창 순서 키퍼. null이면 카드가 비활성으로만 표시된다(검증 도구).</param>
    public MainViewModel(TimerService service, UiSettings uiSettings, Dispatcher dispatcher, WindowOrderKeeper? windowOrder = null)
    {
        _service = service;
        _uiSettings = uiSettings;
        _dispatcher = dispatcher;
        _keeper = windowOrder;
        Cards = TimerService.TimerIds.Select(id => new TimerCardViewModel(id, Toggle, Acknowledge)).ToArray();
        WindowOrder = new WindowOrderViewModel(windowOrder, Post, () => _service.Settings.WindowOrder, SetBackWindow);
        StartAllCommand = new RelayCommand(StartAll);
        StopAllCommand = new RelayCommand(StopAll);
        ToggleMuteCommand = new RelayCommand(() => Muted = !Muted);
        TestCommand = new RelayCommand(() => SendTest(TimerService.RuneId));
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        _refreshTimer.Tick += OnTick;
        service.TimerChanged += OnTimerChanged;
        service.NotificationQueued += OnNotificationQueued;
        service.NotificationFailed += OnNotificationFailed;
        service.HandlerFailed += OnHandlerFailed;
        if (windowOrder is not null)
        {
            windowOrder.Applied += OnWindowOrderApplied;
            windowOrder.Failed += OnWindowOrderFailed;
        }
        Refresh();
    }

    public IReadOnlyList<TimerCardViewModel> Cards { get; }
    public WindowOrderViewModel WindowOrder { get; }
    public WindowOrderKeeper? WindowOrderKeeper => _keeper;

    /// <summary>카드에서 맨 뒤 창을 고를 때 저장할 설정 파일 경로. null이면 기본 경로. 검증 도구는 임시 경로를 준다.</summary>
    public string? SettingsPath { get; set; }
    public ObservableCollection<string> RecentNotifications { get; } = [];
    public RelayCommand StartAllCommand { get; }
    public RelayCommand StopAllCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }
    public RelayCommand TestCommand { get; }
    public UiSettings UiSettings => _uiSettings;
    public string Warning { get => _warning; private set { if (Set(ref _warning, value)) OnPropertyChanged(nameof(HasWarning)); } }
    public bool HasWarning => Warning.Length > 0;
    public bool Muted
    {
        get => _service.Muted;
        set
        {
            if (_service.Muted == value) return;
            _service.Muted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MuteGlyph));
        }
    }
    public string MuteGlyph => Muted ? "" : "";
    public bool Topmost
    {
        get => _uiSettings.Topmost;
        set { if (_uiSettings.Topmost == value) return; _uiSettings.Topmost = value; OnPropertyChanged(); }
    }
    public double Opacity => _uiSettings.Opacity;
    public bool IsVisible { get => _visible; set { _visible = value; Refresh(); } }

    /// <summary>창 순서가 실제로 적용된 직후(UI 스레드). 메인 창이 자신의 '항상 위'를 다시 세우는 데 쓴다.</summary>
    public event Action? WindowOrderApplied;

    public void ApplyUiSettings(UiSettings settings)
    {
        _uiSettings = settings;
        OnPropertyChanged(nameof(Topmost));
        OnPropertyChanged(nameof(Opacity));
        OnPropertyChanged(nameof(UiSettings));
    }

    public void StartAll() { _service.StartAll(); Refresh(); }
    public void StopAll() { _service.StopAll(); Refresh(); }
    public void ToggleAll() { if (Cards.Any(card => card.IsRunning)) StopAll(); else StartAll(); }
    public void Toggle(string id) { _service.Toggle(id); Refresh(); }
    public void Acknowledge(string id) { _service.Acknowledge(id); Refresh(); }

    /// <summary>창 순서 적용 ↔ 해제 (Ctrl+Alt+Z, 트레이, 카드 버튼).</summary>
    public void ToggleWindowOrder()
    {
        if (_keeper is null) return;
        if (!_keeper.HasWindows)
        {
            ShowWarning("설정 > 창 순서에서 창을 먼저 추가하세요");
            return;
        }
        WindowOrder.Toggle();
    }

    /// <summary>카드의 콤보에서 맨 뒤 창을 고르면 목록의 마지막 항목을 바꾸고(없으면 추가) 바로 저장한다.</summary>
    public void SetBackWindow(WindowInfo window)
    {
        var settings = _service.Settings;
        var selector = WindowMatcher.SelectorFor(window);
        var list = settings.WindowOrder.Windows;
        if (list.Count == 0) list.Add(selector);
        else list[^1] = selector;
        try
        {
            _service.ApplySettings(settings);
            _keeper?.Update(settings.WindowOrder);
            SettingsStore.Save(settings, SettingsPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ErrorLog.Write(error);
            ShowWarning("맨 뒤 창 설정을 저장하지 못했습니다. error.log를 확인하세요");
        }
        WindowOrder.Refresh();
    }

    /// <summary>지금 활성인 창을 맨 앞으로 (Ctrl+Alt+X).</summary>
    public void PeekWindow() => WindowOrder.Peek();

    public void SendTest(string id)
    {
        // SendTest does not raise NotificationQueued in Core; include it explicitly in the UI log.
        RecordNotification(_service.SendTest(id));
    }

    public void ShowWarning(string text)
    {
        if (!Warning.Contains(text, StringComparison.Ordinal))
            Warning = HasWarning ? $"{Warning} · {text}" : text;
    }

    public void Refresh()
    {
        if (_disposed) return;
        var snapshots = _service.Snapshot();
        for (var i = 0; i < Cards.Count; i++) Cards[i].Update(snapshots[i]);
        WindowOrder.RefreshThrottled();
        _refreshTimer.IsEnabled = _visible && (snapshots.Any(snapshot => snapshot.IsRunning) || WindowOrder.IsEnabled);
    }

    private void OnTick(object? sender, EventArgs args) => Refresh();
    private void OnTimerChanged(TimerSnapshot snapshot) => Post(Refresh);
    private void OnNotificationQueued(AlarmNotification notification) => Post(() => RecordNotification(notification));
    private void OnNotificationFailed(AlarmNotification notification, Exception error) => Post(() =>
    {
        ErrorLog.Write(error);
        ShowWarning("Windows 알림이 꺼져 있거나 집중 지원 중입니다");
    });
    private void OnWindowOrderApplied() => Post(() => WindowOrderApplied?.Invoke());
    private void OnWindowOrderFailed(Exception error) => Post(() =>
    {
        ErrorLog.Write(error);
        ShowWarning("창 순서 기능 오류. error.log를 확인하세요");
    });
    private void OnHandlerFailed(Exception error) => Post(() =>
    {
        ErrorLog.Write(error);
        ShowWarning("알림 처리 중 오류가 발생했습니다. error.log를 확인하세요");
    });

    private void Post(Action action)
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
        try
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!_disposed) action(); }));
        }
        catch (InvalidOperationException) when (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            // A callback can race dispatcher shutdown; the UI no longer exists in this case.
        }
    }

    private void RecordNotification(AlarmNotification notification)
    {
        RecentNotifications.Insert(0, $"{DateTime.Now:HH:mm:ss}  {notification.Title} · {notification.Message}");
        while (RecentNotifications.Count > 5) RecentNotifications.RemoveAt(5);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnTick;
        _service.TimerChanged -= OnTimerChanged;
        _service.NotificationQueued -= OnNotificationQueued;
        _service.NotificationFailed -= OnNotificationFailed;
        _service.HandlerFailed -= OnHandlerFailed;
        if (_keeper is not null)
        {
            _keeper.Applied -= OnWindowOrderApplied;
            _keeper.Failed -= OnWindowOrderFailed;
        }
    }
}
