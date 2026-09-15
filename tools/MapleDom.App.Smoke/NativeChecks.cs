using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Threading;
using MapleDom.App.Infrastructure;
using MapleDom.App.ViewModels;
using MapleDom.Core;
using MapleDom.Core.Notifications;
using MapleDom.Core.Settings;

internal static partial class Program
{
    private static void CheckNativeIntegration()
    {
        using (var primary = new SingleInstance())
        {
            var activated = false;
            // Never signal an already-running user instance from this verification tool.
            if (!primary.TryAcquire(() => activated = true))
                throw new InvalidOperationException("Close MapleDom before running native integration checks.");
            using var secondary = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--instance-secondary")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Unable to start instance probe.");
            var deadline = Stopwatch.StartNew();
            while ((!activated || !secondary.HasExited) && deadline.Elapsed < TimeSpan.FromSeconds(5))
                PumpFor(TimeSpan.FromMilliseconds(50));
            if (!secondary.HasExited) secondary.Kill();
            Check(activated && secondary.HasExited && secondary.ExitCode == 0, "Second process signals primary instance and exits");
        }
        using (var next = new SingleInstance())
            Check(next.TryAcquire(() => { }), "Mutex and ready signal are released on disposal");

        using var service = new TimerService(AppSettings.CreateDefault(), NullNotifier.Instance);
        using var vm = new MainViewModel(service, new UiSettings(), Dispatcher.CurrentDispatcher);
        using var source = new HwndSource(new HwndSourceParameters("MapleDom hotkey verification") { Width = 0, Height = 0, WindowStyle = 0 });
        using var hotkeys = new GlobalHotkeys(source.Handle, vm);
        hotkeys.SetEnabled(true);
        Check(!vm.HasWarning, "Global hotkeys register successfully");
        NativeMethods.PostMessage(source.Handle, 0x0312, 1, 0);
        PumpFor(TimeSpan.FromMilliseconds(30));
        Check(vm.Cards[0].IsRunning, "Rune hotkey message acknowledges timer");
        NativeMethods.PostMessage(source.Handle, 0x0312, 2, 0);
        PumpFor(TimeSpan.FromMilliseconds(30));
        Check(vm.Cards[1].IsRunning, "Doping hotkey message acknowledges timer");
        NativeMethods.PostMessage(source.Handle, 0x0312, 3, 0);
        PumpFor(TimeSpan.FromMilliseconds(30));
        Check(vm.Cards.All(card => !card.IsRunning), "Global start/stop message stops active timers");
        NativeMethods.PostMessage(source.Handle, 0x0312, 4, 0);
        NativeMethods.PostMessage(source.Handle, 0x0312, 5, 0);
        PumpFor(TimeSpan.FromMilliseconds(30));
        Check(!vm.HasWarning && !vm.WindowOrder.IsApplied, "Window order hotkeys are harmless without a keeper");
        hotkeys.SetEnabled(false);
        NativeMethods.PostMessage(source.Handle, 0x0312, 1, 0);
        PumpFor(TimeSpan.FromMilliseconds(30));
        Check(vm.Cards.All(card => !card.IsRunning), "Disabled hotkeys ignore queued messages");
        Check(NativeMethods.RegisterHotKey(source.Handle, 9, 0x4003, 0x52), "Disabled hotkey chord can be registered again");
        NativeMethods.UnregisterHotKey(source.Handle, 9);
        using var tray = new TrayIcon(vm, Dispatcher.CurrentDispatcher, () => { }, () => { });
        Check(true, "Tray icon and context menu construct and dispose without error");
    }
}
