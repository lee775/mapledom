using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MapleDom.Core.Settings;
using MapleDom.Core.WindowOrder;

namespace MapleDom.Windows;

/// <summary>
/// 실행 중인 최상위 창 열거와 Z-order 조정(Win32 SetWindowPos).
/// 모든 호출은 SWP_NOACTIVATE 라서 키 입력을 받는 창(활성 창)은 바뀌지 않는다.
/// </summary>
public static class WindowArranger
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOPMOST = 0x00000008;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const int DWMWA_CLOAKED = 14;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint OrderFlags = SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOOWNERZORDER;

    private static readonly nint HWND_TOP = 0;
    private static readonly nint HWND_TOPMOST = -1;
    private static readonly nint HWND_NOTOPMOST = -2;

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    /// <summary>보이는 최상위 창을 앞→뒤(Z 순서)로 돌려준다. 제목 없는 창, 도구 창, 숨겨진(cloaked) 창, 이 프로세스의 창은 제외.</summary>
    public static IReadOnlyList<WindowInfo> EnumerateWindows(bool includeOwnProcess = false)
    {
        var result = new List<WindowInfo>();
        var ownPid = Environment.ProcessId;
        var names = new Dictionary<int, string>();

        EnumWindowsProc callback = (hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;
            if (IsCloaked(hwnd)) return true;

            var title = GetTitle(hwnd);
            if (title.Length == 0) return true;

            GetWindowThreadProcessId(hwnd, out var pid);
            if (!includeOwnProcess && pid == ownPid) return true;

            result.Add(new WindowInfo(hwnd, title, ProcessNameOf((int)pid, names), (int)pid, (exStyle & WS_EX_TOPMOST) != 0));
            return true;
        };

        EnumWindows(callback, 0);
        GC.KeepAlive(callback);
        return result;
    }

    public static IReadOnlyList<ResolvedWindow> Resolve(WindowOrderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return WindowOrderResolver.Resolve(settings.Windows, EnumerateWindows());
    }

    /// <summary>
    /// 목록 순서를 적용한다. raise=true면 그룹 전체를 맨 앞(또는 Topmost 층)으로 올리고,
    /// false면 첫 창 위치는 두고 나머지를 그 뒤에 순서대로 놓는다. 반환: 찾아서 정렬한 창 수.
    /// </summary>
    public static int Apply(WindowOrderSettings settings, bool raise)
    {
        var handles = Resolve(settings).Where(r => r.Window is not null).Select(r => r.Window!.Handle).ToList();
        return Apply(handles, settings.Topmost, raise);
    }

    public static int Apply(IReadOnlyList<nint> handles, bool topmost, bool raise)
    {
        ArgumentNullException.ThrowIfNull(handles);
        if (handles.Count == 0) return 0;

        if (raise) RaiseWindow(handles[0], topmost);
        for (var i = 1; i < handles.Count; i++)
        {
            // 다른 프로세스 창은 "특정 창 바로 뒤"(hWndInsertAfter=핸들) 지정이 확실하게 먹는다.
            if (raise && topmost) SetWindowPos(handles[i], HWND_TOPMOST, 0, 0, 0, 0, OrderFlags);
            SetWindowPos(handles[i], handles[i - 1], 0, 0, 0, 0, OrderFlags);
        }

        return handles.Count;
    }

    /// <summary>현재 활성(키 입력) 창을 같은 층의 맨 앞으로 올린다. 활성 상태는 바뀌지 않는다.</summary>
    public static bool RaiseForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0) return false;
        RaiseWindow(hwnd, makeTopmost: false);
        return true;
    }

    public static bool IsTopmost(nint hwnd)
        => hwnd != 0 && (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOPMOST) != 0;

    /// <summary>
    /// 창 하나를 활성화 없이 맨 앞으로 올린다. 다른 프로세스의 창은 HWND_TOP 으로는 활성 창 위로 올라가지 않으므로
    /// (호출은 성공하지만 무시됨), "항상 위"를 켰다가 바로 끄는 방식으로 올린다. 이미 항상 위인 창은 그 층의 맨 앞으로 간다.
    /// </summary>
    private static void RaiseWindow(nint hwnd, bool makeTopmost)
    {
        if (hwnd == 0) return;
        var keepTopmost = makeTopmost || IsTopmost(hwnd);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, OrderFlags);
        if (!keepTopmost) SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, OrderFlags);
    }

    public static nint ForegroundWindow => GetForegroundWindow();

    /// <summary>창을 "항상 위" 층에 넣거나(true) 뺀다(false). 활성 상태는 바뀌지 않는다.</summary>
    public static bool SetTopmost(nint hwnd, bool topmost)
        => hwnd != 0 && SetWindowPos(hwnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, OrderFlags);

    private static string GetTitle(nint hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0) return "";
        var buffer = new StringBuilder(length + 1);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString().Trim();
    }

    private static bool IsCloaked(nint hwnd)
        => DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static string ProcessNameOf(int pid, Dictionary<int, string> cache)
    {
        if (cache.TryGetValue(pid, out var cached)) return cached;
        string name;
        try
        {
            using var process = Process.GetProcessById(pid);
            name = process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            name = "?";
        }
        cache[pid] = name;
        return name;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
}
