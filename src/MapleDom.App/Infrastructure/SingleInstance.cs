using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MapleDom.App.Infrastructure;

internal sealed class SingleInstance : IDisposable
{
    private const string Identity = "MapleDom.App.605C973F-E8BB-47F1-8258-275CFEE01308";
    private readonly Mutex _mutex = new(false, @"Local\" + Identity);
    private readonly EventWaitHandle _ready = new(false, EventResetMode.ManualReset, @"Local\" + Identity + ".Ready");
    private readonly uint _activateMessage = NativeMethods.RegisterWindowMessage(Identity + ".Activate");
    private HwndSource? _source;
    private bool _ownsMutex;
    private Action? _activate;

    public bool TryAcquire(Action activate)
    {
        try { _ownsMutex = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsMutex = true; }
        if (!_ownsMutex) return false;
        _ready.Reset();
        if (_activateMessage == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        _activate = activate;
        // Hidden top-level message sink exists before the main window is constructed.
        _source = new HwndSource(new HwndSourceParameters(Identity) { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(OnMessage);
        _ready.Set();
        return true;
    }

    public bool ActivateExisting()
    {
        if (!_ready.WaitOne(TimeSpan.FromSeconds(5))) return false;
        var window = NativeMethods.FindWindow(null, Identity);
        if (window == 0) return false;
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        NativeMethods.AllowSetForegroundWindow(processId);
        return NativeMethods.PostMessage(window, _activateMessage, 0, 0);
    }

    private nint OnMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message == _activateMessage)
        {
            handled = true;
            _activate?.Invoke();
        }
        return 0;
    }

    public void Dispose()
    {
        _source?.RemoveHook(OnMessage);
        _source?.Dispose();
        if (_ownsMutex) { _ready.Reset(); _mutex.ReleaseMutex(); _ownsMutex = false; }
        _ready.Dispose();
        _mutex.Dispose();
    }
}
