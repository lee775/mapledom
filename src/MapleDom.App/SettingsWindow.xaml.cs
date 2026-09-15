using System.IO;
using System.Windows;
using MapleDom.App.ViewModels;
using MapleDom.App.Infrastructure;
using MapleDom.Core;
using MapleDom.Core.Settings;
using MapleDom.Windows;

namespace MapleDom.App;

public partial class SettingsWindow : Window
{
    private readonly TimerService _service;
    private readonly MainViewModel _main;

    public SettingsWindow(TimerService service, MainViewModel main)
    {
        _service = service;
        _main = main;
        ViewModel = new SettingsViewModel(service.Settings, main.UiSettings, main.RecentNotifications);
        InitializeComponent();
        DataContext = ViewModel;
        MaxHeight = SystemParameters.WorkArea.Height;
        MaxWidth = SystemParameters.WorkArea.Width;
        MinHeight = Math.Min(MinHeight, MaxHeight);
        MinWidth = Math.Min(MinWidth, MaxWidth);
    }

    public SettingsViewModel ViewModel { get; }

    private void OnRestoreDefaults(object sender, RoutedEventArgs args) => ViewModel.RestoreDefaults();

    private void OnTest(object sender, RoutedEventArgs args) =>
        _main.SendTest(SettingsTabs.SelectedIndex == 1 ? TimerService.DopingId : TimerService.RuneId);

    /// <summary>편집 중인 목록으로 한 번 정렬해 본다(저장·적용 상태 변경 없음). 정렬 뒤 이 창을 다시 앞으로 가져온다.</summary>
    private void OnArrangeNow(object sender, RoutedEventArgs args)
    {
        var count = WindowArranger.Apply(ViewModel.WindowOrder.Build());
        ViewModel.WindowOrder.RefreshWindows();
        ViewModel.SetErrors(count == 0
            ? ["창 순서: 정렬할 창을 찾지 못했습니다. 목록의 프로세스 이름과 제목을 확인하세요."]
            : []);
        (Owner as MainWindow)?.ReassertTopmost();
        Activate();
    }

    private void OnSave(object sender, RoutedEventArgs args)
    {
        if (!ViewModel.TryBuild(out var settings, out var uiSettings)) return;
        var previous = _service.Settings;
        var previousUi = _main.UiSettings.Clone();
        // Preserve a position changed while this dialog was being opened.
        uiSettings.Left = previousUi.Left;
        uiSettings.Top = previousUi.Top;
        var settingsSaved = false;
        try
        {
            _service.ApplySettings(settings);
            _main.WindowOrderKeeper?.Update(settings.WindowOrder);
            SettingsStore.Save(settings);
            settingsSaved = true;
            uiSettings.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            ErrorLog.Write(error);
            _service.ApplySettings(previous);
            _main.WindowOrderKeeper?.Update(previous.WindowOrder);
            if (settingsSaved)
            {
                try { SettingsStore.Save(previous); }
                catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
                {
                    ErrorLog.Write(rollbackError);
                    ViewModel.SetErrors(["저장 및 이전 설정 복원에 실패했습니다. 파일 접근 권한을 확인한 뒤 다시 저장하세요."]);
                    return;
                }
            }
            ViewModel.SetErrors([$"설정을 저장하지 못했습니다: {error.Message}"]);
            return;
        }
        _main.ApplyUiSettings(uiSettings);
        _main.Refresh();
        DialogResult = true;
    }
}
