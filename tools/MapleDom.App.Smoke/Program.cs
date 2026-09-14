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
        Check(vm.Cards.All(card => card.Status == "정지"), "Three stopped cards at startup");
        Render(window, "main-stopped.png", output);
        vm.StartAllCommand.Execute(null);
        Check(vm.Cards.Select(card => card.Remaining).SequenceEqual(["0:58", "15:00", "29:55"]), "Start all uses Core durations");
        Render(window, "main-counting.png", output);
        clock.Advance(7);
        vm.Refresh();
        Check(vm.Cards[1].Remaining == "14:53" && vm.Cards[2].Remaining == "29:48", "Countdown reflects monotonic clock");
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
        Render(dialog, "settings-repeat.png", output);
        var tabs = (TabControl)dialog.FindName("SettingsTabs");
        tabs.SelectedIndex = 1;
        Render(dialog, "settings-rune.png", output);
        dialog.ViewModel.Rune.Lead = "10";
        Check(!dialog.ViewModel.TryBuild(out _, out _), "Dialog validation rejects invalid draft");
        Render(dialog, "settings-errors.png", output);
        dialog.ViewModel.RestoreDefaults();
        tabs.SelectedIndex = 3;
        Render(dialog, "settings-window.png", output);
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
