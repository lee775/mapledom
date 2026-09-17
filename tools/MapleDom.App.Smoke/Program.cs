using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Documents;
using System.Windows.Threading;
using MapleDom.App;
using MapleDom.App.Infrastructure;
using MapleDom.App.ViewModels;
using MapleDom.Core;
using MapleDom.Core.Notifications;
using MapleDom.Core.Settings;
using MapleDom.Core.Time;

internal static partial class Program
{
    private static int _checks;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--instance-secondary")
        {
            using var instance = new SingleInstance();
            return !instance.TryAcquire(() => { }) && instance.ActivateExisting() ? 0 : 2;
        }
        var output = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "MapleDom.UI.Smoke"));
        Directory.CreateDirectory(output);
        var bindingErrors = new StringWriter();
        var listener = new TextWriterTraceListener(bindingErrors);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/MapleDom;component/Themes/Dark.xaml", UriKind.Relative) });
            CheckSettings(output);
            CheckTimersAndRendering(output);
            CheckBackgroundDelivery();
            CheckNativeIntegration();
            listener.Flush();
            Check(string.IsNullOrWhiteSpace(bindingErrors.ToString()), "XAML bindings have no errors: " + bindingErrors);
            Console.WriteLine($"PASS: {_checks} UI checks. Renders: {output}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); listener.Dispose(); }
    }

    private static void CheckSettings(string output)
    {
        var editor = new SettingsViewModel(AppSettings.CreateDefault(), new UiSettings(), []);
        editor.Rune.Interval = "10";
        editor.Rune.Lead = "10";
        Check(!editor.TryBuild(out _, out _) && editor.Errors.Any(error => error.Contains("선행 시간은 간격보다")), "Reject lead >= interval");
        editor.Rune.Lead = "hello";
        Check(!editor.TryBuild(out _, out _), "Reject malformed numbers without keeping an old value");
        editor.Rune.Lead = "NaN";
        Check(!editor.TryBuild(out _, out _), "Reject NaN");
        editor.Rune.Lead = "0";
        editor.MinSpacing = "1e300";
        Check(!editor.TryBuild(out _, out _), "Reject overflowing duration");
        editor.MinSpacing = "2";
        editor.Rune.Interval = "10";
        editor.Rune.Lead = "9.99999999";
        Check(!editor.TryBuild(out _, out _), "Reject effective duration rounding to zero");
        editor.Rune.Lead = "0";
        editor.Doping.Interval = "0.00000001";
        editor.Doping.Lead = "0";
        Check(!editor.TryBuild(out _, out _), "Reject duration rounding to zero");
        editor.RestoreDefaults();
        editor.Rune.Name = "테스트 룬";
        editor.Opacity = 0.65;
        editor.HotkeysEnabled = true;
        Check(editor.TryBuild(out var settings, out var ui), "Build valid editor settings");
        var settingsPath = Path.Combine(output, "settings.json");
        var uiPath = Path.Combine(output, "ui.json");
        SettingsStore.Save(settings, settingsPath);
        ui.Save(uiPath);
        Check(SettingsStore.Load(settingsPath).Rune.Name == "테스트 룬", "Timer settings round trip");
        var loaded = UiSettings.Load(out var loadError, uiPath);
        Check(loadError is null && loaded.Opacity == 0.65 && loaded.HotkeysEnabled, "UI settings round trip");
        File.WriteAllText(uiPath, "{ broken json");
        loaded = UiSettings.Load(out loadError, uiPath);
        Check(loadError is not null && loaded.Topmost && loaded.MinimizeToTray && loaded.Opacity == 1, "Corrupt UI settings recover to defaults");
        editor.RestoreDefaults();
        Check(editor.TryBuild(out settings, out _) && settings.Rune.IntervalSeconds == 900 && settings.Doping.LeadSeconds == 5, "Restore defaults only edits the draft");
    }

    private static void CheckTimersAndRendering(string output)
    {
        var clock = new ManualClock();
        using var service = new TimerService(AppSettings.CreateDefault(), NullNotifier.Instance, clock);
        using var vm = new MainViewModel(service, new UiSettings(), Dispatcher.CurrentDispatcher);
        var window = new MainWindow(service, vm, () => { });
        Check(vm.Cards.Count == 3 && vm.Cards.All(card => card.Status == "정지"), "Three stopped timer cards at startup");
        Check(!vm.Cards[0].CanAcknowledge && vm.Cards[1].CanAcknowledge && vm.Cards[2].CanAcknowledge, "Minute card has no acknowledge button; rune and doping do");
        Check(!vm.WindowOrder.IsEnabled && !vm.WindowOrder.IsApplied, "Window order card is disabled without a keeper");
        vm.WindowOrder.RefreshAvailable();
        Check(vm.WindowOrder.Available.Count > 0 && vm.WindowOrder.Available[0].ZOrder == 1 && vm.WindowOrder.Available[0].Display.StartsWith("1. "), "Back-window combo lists running windows with Z-order numbers");
        vm.WindowOrder.SelectedBack = vm.WindowOrder.Available[0];
        Check(service.Settings.WindowOrder.Windows.Count == 1 && vm.WindowOrder.Status.Contains("창 1개"), "Choosing a back window on the card sets a single-entry order for this session");
        vm.WindowOrder.SelectedBack = vm.WindowOrder.Available[Math.Min(1, vm.WindowOrder.Available.Count - 1)];
        Check(service.Settings.WindowOrder.Windows.Count == 1 && service.Settings.WindowOrder.Windows[0].ProcessName == vm.WindowOrder.SelectedBack!.ProcessName, "Choosing again replaces the back entry instead of appending");
        var cleared = service.Settings;
        cleared.WindowOrder.Windows.Clear();
        service.ApplySettings(cleared);
        vm.WindowOrder.Refresh();
        Check(vm.WindowOrder.SelectedBack is null, "Clearing the order clears the card selection");
        Render(window, "main-stopped.png", output);
        vm.StartAllCommand.Execute(null);
        Check(vm.Cards.Select(card => card.Remaining).SequenceEqual(["0:58", "15:00", "29:55"]), "Start all uses Core durations");
        Render(window, "main-counting.png", output);
        clock.Advance(7);
        vm.Refresh();
        Check(vm.Cards[0].Remaining == "0:51" && vm.Cards[1].Remaining == "14:53" && vm.Cards[2].Remaining == "29:48", "Countdown reflects monotonic clock");
        vm.Cards[1].AcknowledgeCommand.Execute(null);
        vm.Cards[2].AcknowledgeCommand.Execute(null);
        Check(vm.Cards[1].Remaining == "15:00" && vm.Cards[2].Remaining == "29:55", "Acknowledgements reset timers immediately");
        var shortSettings = service.Settings;
        shortSettings.Rune.IntervalSeconds = 10;
        shortSettings.Rune.ReminderSeconds = 2;
        service.ApplySettings(shortSettings);
        vm.Refresh();
        Check(vm.Cards[1].IsUrgent, "Ten seconds remaining highlights timer");
        clock.Advance(10);
        service.Engine.Pump();
        vm.Refresh();
        Check(vm.Cards[1].IsOverdue && vm.Cards[1].Status.Contains("리마인드 0회"), "Overdue status from Core snapshot");
        clock.Advance(2);
        service.Engine.Pump();
        vm.Refresh();
        Check(vm.Cards[1].Status.Contains("리마인드 1회"), "Reminder count increases");
        Render(window, "main-overdue.png", output);
        vm.ToggleMuteCommand.Execute(null);
        Check(service.Muted && vm.Cards.All(card => card.IsRunning), "Mute leaves timers running");
        for (var index = 0; index < 7; index++) vm.SendTest(TimerService.RuneId);
        Check(vm.RecentNotifications.Count == 5, "Recent notifications are bounded to five");
        var dialog = new SettingsWindow(service, vm);
        Render(dialog, "settings-minute.png", output);
        var tabs = (TabControl)dialog.FindName("SettingsTabs");
        tabs.SelectedIndex = 1;
        Render(dialog, "settings-rune.png", output);
        Check(dialog.ViewModel.TryBuild(out var withMinute, out _) && withMinute.Minute.IntervalSeconds == 58 && withMinute.Minute.GapSeconds == 2, "Minute tab builds the repeat timer with its gap");
        dialog.ViewModel.Minute.Interval = "-1";
        Check(!dialog.ViewModel.TryBuild(out _, out _), "Minute tab rejects a negative interval");
        // 아래 룬 검사는 이 다이얼로그가 service.Settings(룬 간격 10초)로 열린 상태를 쓰므로 1분 값만 되돌린다.
        dialog.ViewModel.Minute.Interval = "58";
        dialog.ViewModel.Minute.Gap = "2";
        dialog.ViewModel.Rune.Lead = "10";
        Check(!dialog.ViewModel.TryBuild(out _, out _), "Dialog validation rejects invalid draft");
        Render(dialog, "settings-errors.png", output);
        dialog.ViewModel.RestoreDefaults();
        tabs.SelectedIndex = 3;
        Render(dialog, "settings-window.png", output);
        var order = dialog.ViewModel.WindowOrder;
        Check(order.Available.Count > 0 && order.Selected is not null, "Window order tab lists running windows");
        order.AddSelected();
        order.AddSelected();
        Check(order.Entries.Count == 2 && order.Entries[0].Index == 1 && order.Entries[1].Index == 2, "Adding windows appends numbered entries");
        Check(order.Entries[0].IsFound && !order.Entries[1].IsFound, "Same window is matched once; duplicate shows as missing");
        order.Entries[1].TitleContains = "no-such-window-title-" + Guid.NewGuid().ToString("N");
        order.Move(order.Entries[1], -1);
        Check(order.Entries[0].Index == 1 && !order.Entries[0].IsFound && order.Entries[1].IsFound, "Move up reorders and keeps status per entry");
        order.Topmost = true;
        Check(dialog.ViewModel.TryBuild(out var withOrder, out _) && withOrder.WindowOrder.Topmost && withOrder.WindowOrder.Windows.Count == 2, "Window order draft builds into settings");
        var persistedPath = Path.Combine(output, "settings-persist.json");
        SettingsStore.Save(withOrder, persistedPath);
        var reloaded = SettingsStore.Load(persistedPath);
        Check(reloaded.WindowOrder.Topmost && reloaded.WindowOrder.Windows.Count == 0, "Window list is session-only: not persisted, Topmost is");
        Check(order.Entries[1].IsLast && order.Entries[1].Marker == "맨 뒤" && !order.Entries[0].IsLast, "Last entry is marked as the back window");
        order.Remove(order.Entries[0]);
        Check(order.Entries.Count == 1 && order.Entries[0].Index == 1, "Remove reindexes entries");
        tabs.SelectedIndex = 4;
        Render(dialog, "settings-window-order.png", output);
        order.Remove(order.Entries[0]);
        order.Topmost = false;
        var telegram = dialog.ViewModel.Telegram;
        Check(!telegram.Enabled && telegram.SendRune && telegram.SendDoping && !telegram.SendMinute, "Telegram tab starts off with rune and doping selected");
        telegram.Enabled = true;
        Check(!dialog.ViewModel.TryBuild(out _, out _) && dialog.ViewModel.Errors.Any(e => e.Contains("채팅 ID")), "Telegram tab requires a token and chat id when enabled");
        telegram.BotToken = "123456:test-token";
        telegram.ChatId = "42";
        Check(dialog.ViewModel.TryBuild(out var withTelegram, out _) && withTelegram.Telegram.Enabled && withTelegram.Telegram.ChatId == "42", "Telegram tab builds into settings");
        tabs.SelectedIndex = 5;
        Render(dialog, "settings-telegram.png", output);
        telegram.Enabled = false;
        dialog.Close();
        vm.StopAllCommand.Execute(null);
        Check(vm.Cards.All(card => !card.IsRunning), "Stop all updates every card");
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Close();
        Check(!closed && !window.IsVisible, "Default close cancels destruction and hides to tray");
        window.PrepareForExit();
        window.Close();
        Check(closed, "Explicit exit closes the main window");
        vm.ApplyUiSettings(new UiSettings { MinimizeToTray = false });
        var exitRequested = false;
        var exitWindow = new MainWindow(service, vm, () => exitRequested = true);
        exitWindow.Close();
        PumpFor(TimeSpan.FromMilliseconds(30));
        Check(exitRequested, "Close without tray requests application shutdown");
        exitWindow.PrepareForExit();
        exitWindow.Close();
        vm.Dispose();
        service.Dispose();
        Check(!service.Engine.IsRunning, "Service dispose stops scheduler thread");
    }

    private static void CheckBackgroundDelivery()
    {
        var settings = AppSettings.CreateDefault();
        settings.Rune.IntervalSeconds = 0.15;
        settings.Rune.ReminderSeconds = 0.12;
        settings.Notifications.MinSpacingSeconds = 0;
        using var service = new TimerService(settings, NullNotifier.Instance);
        using var vm = new MainViewModel(service, new UiSettings(), Dispatcher.CurrentDispatcher) { IsVisible = true };
        var uiThread = Environment.CurrentManagedThreadId;
        var wrongThread = false;
        vm.RecentNotifications.CollectionChanged += (_, _) => wrongThread |= Environment.CurrentManagedThreadId != uiThread;
        vm.Cards[1].PropertyChanged += (_, _) => wrongThread |= Environment.CurrentManagedThreadId != uiThread;
        vm.Acknowledge(TimerService.RuneId);
        PumpFor(TimeSpan.FromMilliseconds(500));
        Check(vm.Cards[1].IsOverdue && vm.RecentNotifications.Count >= 2, "Real scheduler sends main and reminder events to UI");
        Check(!wrongThread, "UI notifications stay on Dispatcher thread");
        vm.IsVisible = false;
        vm.StopAll();
    }

    private static void Render(Window window, string filename, string output)
    {
        var root = (FrameworkElement)window.Content;
        // Detach the root so an unshown Window cannot invalidate its arranged size.
        root.DataContext = window.DataContext;
        TextElement.SetForeground(root, window.Foreground);
        TextElement.SetFontFamily(root, window.FontFamily);
        TextElement.SetFontSize(root, window.FontSize);
        window.Content = null;
        var size = new Size(window.Width - (window.WindowStyle == WindowStyle.None ? 0 : 16), window.Height - (window.WindowStyle == WindowStyle.None ? 0 : 39));
        PumpFor(TimeSpan.FromMilliseconds(30));
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        using var stream = File.Create(Path.Combine(output, filename));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        window.Content = root;
    }

    private static void PumpFor(TimeSpan time)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = time };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        _checks++;
        Console.WriteLine("PASS: " + description);
    }

    private sealed class ManualClock : IClock
    {
        private long _ticks;
        public TimeSpan Now => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));
        public void Advance(double seconds) => Interlocked.Add(ref _ticks, TimeSpan.FromSeconds(seconds).Ticks);
    }
}
