using System.Collections.ObjectModel;
using System.Windows.Threading;
using MapleDom.App.Infrastructure;
using MapleDom.Core;
using MapleDom.Core.Notifications;
using MapleDom.Core.Timers;

namespace MapleDom.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly TimerService _service;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _refreshTimer;
    private bool _visible;
    private bool _disposed;
    private string _warning = "";
    private UiSettings _uiSettings;

    public MainViewModel(TimerService service, UiSettings uiSettings, Dispatcher dispatcher)
    {
        _service = service;
        _uiSettings = uiSettings;
        _dispatcher = dispatcher;
        Cards = TimerService.TimerIds.Select(id => new TimerCardViewModel(id, Toggle, Acknowledge)).ToArray();
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
        Refresh();
    }

    public IReadOnlyList<TimerCardViewModel> Cards { get; }
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
    public string MuteGlyph => Muted ? "\uE74F" : "\uE767";
    public bool Topmost
    {
        get => _uiSettings.Topmost;
        set { if (_uiSettings.Topmost == value) return; _uiSettings.Topmost = value; OnPropertyChanged(); }
    }
    public double Opacity => _uiSettings.Opacity;
    public bool IsVisible { get => _visible; set { _visible = value; Refresh(); } }

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
        _refreshTimer.IsEnabled = _visible && snapshots.Any(snapshot => snapshot.IsRunning);
    }

    private void OnTick(object? sender, EventArgs args) => Refresh();
    private void OnTimerChanged(TimerSnapshot snapshot) => Post(Refresh);
    private void OnNotificationQueued(AlarmNotification notification) => Post(() => RecordNotification(notification));
    private void OnNotificationFailed(AlarmNotification notification, Exception error) => Post(() =>
    {
        ErrorLog.Write(error);
        ShowWarning("Windows 알림이 꺼져 있거나 집중 지원 중입니다");
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
    }
}
