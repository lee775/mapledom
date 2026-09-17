using System.IO;
using System.Windows;
using MapleDom.App.ViewModels;
using MapleDom.App.Infrastructure;
using MapleDom.Core;
using MapleDom.Core.Notifications;
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
        _main.SendTest(SettingsTabs.SelectedIndex switch
        {
            0 => TimerService.MinuteId,
            2 => TimerService.DopingId,
            _ => TimerService.RuneId,
        });

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

    /// <summary>봇이 받은 최근 메시지에서 채팅 ID를 찾아 넣는다.</summary>
    private async void OnFindChatId(object sender, RoutedEventArgs args)
    {
        var token = ViewModel.Telegram.BotToken;
        ViewModel.Telegram.Status = "채팅 ID를 찾는 중...";
        var result = await Task.Run(() => TelegramNotifier.FindChatId(token));
        if (result.Ok && result.ChatId is { } id) ViewModel.Telegram.ChatId = id;
        ViewModel.Telegram.Status = result.Message;
    }

    /// <summary>편집 중인 토큰·채팅 ID로 메시지를 한 건 보내 본다(저장하지 않음).</summary>
    private async void OnTelegramTest(object sender, RoutedEventArgs args)
    {
        var token = ViewModel.Telegram.BotToken;
        var chatId = ViewModel.Telegram.ChatId;
        ViewModel.Telegram.Status = "보내는 중...";
        var result = await Task.Run(() => TelegramNotifier.SendText(token, chatId, "MapleDom 텔레그램 테스트 알림입니다."));
        ViewModel.Telegram.Status = result.Message;
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
            _main.Telegram?.Update(settings.Telegram);
            SettingsStore.Save(settings);
            settingsSaved = true;
            uiSettings.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            ErrorLog.Write(error);
            _service.ApplySettings(previous);
            _main.WindowOrderKeeper?.Update(previous.WindowOrder);
            _main.Telegram?.Update(previous.Telegram);
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
