using System.Windows;
using System.Windows.Threading;
using MapleDom.App.Infrastructure;
using MapleDom.App.ViewModels;
using MapleDom.Core;
using MapleDom.Core.Notifications;
using MapleDom.Core.Settings;
using MapleDom.Windows;

namespace MapleDom.App;

public partial class App : Application
{
    private SingleInstance? _instance;
    private TimerService? _service;
    private WindowOrderKeeper? _windowOrder;
    private TelegramNotifier? _telegram;
    private AlertOverlay? _overlay;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private bool _exiting;
    private bool _handlingException;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandledException;
        base.OnStartup(e);
        _instance = new SingleInstance();
        if (!_instance.TryAcquire(() => _window?.RestoreFromTray()))
        {
            if (!_instance.ActivateExisting())
                MessageBox.Show("이미 실행 중인 MapleDom 창을 복원하지 못했습니다. 잠시 후 다시 실행하세요.", "MapleDom", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        var settings = SettingsStore.LoadOrDefault(null, out var settingsError);
        var uiSettings = UiSettings.Load(out var uiError);
        _telegram = new TelegramNotifier();
        var notifier = new CompositeNotifier(new WindowsToastNotifier(), _telegram);
        try
        {
            // Check values before constructing Core workers: hand-edited JSON can contain
            // finite but enormous durations that Core.Validate does not bound in every field.
            var validator = new SettingsViewModel(settings, uiSettings, []);
            if (!validator.TryBuild(out var validated, out _)) throw new ArgumentException(string.Join("; ", validator.Errors));
            _service = new TimerService(validated, notifier);
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        {
            settingsError = error;
            _service = new TimerService(AppSettings.CreateDefault(), notifier);
        }

        _telegram.Update(_service.Settings.Telegram);
        _windowOrder = new WindowOrderKeeper();
        _viewModel = new MainViewModel(_service, uiSettings, Dispatcher, _windowOrder, _telegram);
        _overlay = new AlertOverlay(Dispatcher);
        _overlay.Update(_service.Settings.Overlay);
        _viewModel.Overlay = _overlay;
        if (settingsError is not null) { ErrorLog.Write(settingsError); _viewModel.ShowWarning("타이머 설정을 읽지 못해 기본값을 사용합니다"); }
        if (uiError is not null) { ErrorLog.Write(uiError); _viewModel.ShowWarning("창 설정을 읽지 못해 기본값을 사용합니다"); }

        _windowOrder.Update(_service.Settings.WindowOrder);

        _window = new MainWindow(_service, _viewModel, ExitApplication);
        MainWindow = _window;
        _tray = new TrayIcon(_viewModel, Dispatcher, _window.RestoreFromTray, ExitApplication);
        _window.Show();
    }

    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        _window?.PrepareForExit();
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _exiting = true;
        _window?.PrepareForExit();
        base.OnSessionEnding(e);
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _window?.PrepareForExit();
        try { _viewModel?.UiSettings.Save(); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException) { ErrorLog.Write(error); }
        finally
        {
            _tray?.Dispose();
            _overlay?.Dispose();
            _windowOrder?.Dispose();
            _telegram?.Dispose();
            _viewModel?.Dispose();
            _service?.Dispose();
            _instance?.Dispose();
        }
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ErrorLog.Write(e.Exception);
        if (!_handlingException)
        {
            _handlingException = true;
            MessageBox.Show("예기치 않은 오류로 MapleDom을 종료합니다.\n%LocalAppData%\\MapleDom\\error.log를 확인하세요.\n\n" + e.Exception.Message,
                "MapleDom 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        ExitApplication();
    }
}
