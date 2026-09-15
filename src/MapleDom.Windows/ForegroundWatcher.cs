using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MapleDom.Windows;

/// <summary>
/// 포그라운드(활성) 창이 바뀔 때 알려 준다. SetWinEventHook(EVENT_SYSTEM_FOREGROUND) 기반.
/// 메시지 루프가 있는 스레드(WPF UI 스레드)에서 만들어야 하며, 이벤트도 그 스레드에서 발생한다.
/// 이 프로세스의 창 활성화는 무시된다(WINEVENT_SKIPOWNPROCESS).
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int OBJID_WINDOW = 0;

    private delegate void WinEventProc(nint hook, uint eventId, nint hwnd, int idObject, int idChild, uint thread, uint time);

    private readonly WinEventProc _callback; // 훅이 살아 있는 동안 GC되지 않도록 필드로 보관
    private nint _hook;

    public event Action<nint>? ForegroundChanged;

    public ForegroundWatcher()
    {
        _callback = OnEvent;
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, _callback, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWinEventHook failed.");
    }

    public bool IsActive => _hook != 0;

    private void OnEvent(nint hook, uint eventId, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW || hwnd == 0) return;
        try
        {
            ForegroundChanged?.Invoke(hwnd);
        }
        catch
        {
            // 네이티브 콜백 안에서 예외가 새어 나가면 프로세스가 죽는다.
        }
    }

    public void Dispose()
    {
        if (_hook == 0) return;
        UnhookWinEvent(_hook);
        _hook = 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);
}
