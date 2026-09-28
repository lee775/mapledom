using System.Windows.Threading;
using MapleDom.Core.Notifications;
using MapleDom.Core.Settings;

namespace MapleDom.App.Infrastructure;

/// <summary>
/// 화면 알림 배너 관리자. 알림이 울리면 <see cref="AlertOverlayWindow"/>를 띄우고 설정한 시간 뒤 숨긴다.
/// <para>모든 메서드는 UI 스레드에서 호출한다.</para>
/// </summary>
public sealed class AlertOverlay : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _hideTimer;
    private AlertOverlayWindow? _window;
    private OverlaySettings _settings = new();
    private bool _disposed;

    public AlertOverlay(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _hideTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        _hideTimer.Tick += (_, _) => Hide();
    }

    public OverlaySettings Settings => _settings;

    public void Update(OverlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        if (!settings.Enabled) Hide();
    }

    /// <summary>알림 하나를 배너로 띄운다. 꺼져 있거나 종료 중이면 아무것도 하지 않는다.</summary>
    public void Show(AlarmNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (_disposed || !_settings.Enabled || _dispatcher.HasShutdownStarted) return;
        if (notification.OverlayPosition is not { } position) return;   // 이 타이머는 배너를 쓰지 않음

        _window ??= new AlertOverlayWindow();
        _window.SetAccent(notification.TimerId);
        _window.ShowAlert(notification.Title, notification.Message, _settings, position);

        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.DurationSeconds, 0.5, 60));
        _hideTimer.Start();
    }

    public void Hide()
    {
        _hideTimer.Stop();
        if (_window is { IsVisible: true }) _window.Hide();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hideTimer.Stop();
        if (_window is not null)
        {
            _window.Close();
            _window = null;
        }
    }
}
