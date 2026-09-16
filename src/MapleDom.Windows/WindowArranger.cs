using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MapleDom.Core.Settings;
using MapleDom.Core.Windowing;

namespace MapleDom.Windows;

/// <summary>
/// 실행 중인 최상위 창 열거와 Z-order 조정(Win32 SetWindowPos).
/// 모든 호출은 SWP_NOACTIVATE 라서 키 입력을 받는 창(활성 창)은 바뀌지 않는다.
/// <para>
/// 실측: 다른 프로세스의 창은 HWND_TOP 으로는 활성 창 위로 올라가지 않는다(호출은 성공, 효과 없음).
/// HWND_BOTTOM(맨 뒤로), HWND_TOPMOST→HWND_NOTOPMOST 토글(맨 앞으로), hWndInsertAfter=핸들(특정 창 뒤)은 확실히 동작한다.
/// </para>
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

    private static readonly nint HWND_BOTTOM = 1;
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
            var info = Describe(hwnd, names);
            if (info is null) return true;
            if (!includeOwnProcess && info.ProcessId == ownPid) return true;
            result.Add(info with { ZOrder = result.Count + 1 });
            return true;
        };

        EnumWindows(callback, 0);
        GC.KeepAlive(callback);
        return result;
    }

    /// <summary>창 하나의 정보. 보이지 않거나 제목이 없거나 도구/숨김 창이면 null.</summary>
    public static WindowInfo? Describe(nint hwnd) => Describe(hwnd, new Dictionary<int, string>());

    public static WindowInfo? Find(WindowSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return selector.IsEmpty ? null : WindowMatcher.Find(selector, EnumerateWindows());
    }

    public static IReadOnlyList<ResolvedWindow> Resolve(WindowOrderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return WindowMatcher.Resolve(settings.Windows, EnumerateWindows());
    }

    /// <summary>목록 순서를 적용한다. 반환: 찾아서 정렬한 창 수.</summary>
    public static int Apply(WindowOrderSettings settings)
    {
        var handles = Resolve(settings).Where(r => r.Window is not null).Select(r => r.Window!.Handle).ToList();
        return Apply(handles, settings.Topmost);
    }

    /// <summary>
    /// 앞→뒤 순서의 창들을 배치한다. 첫 창은 맨 앞(또는 항상 위 층), 그다음 창들은 각각 "바로 앞 창의 뒤",
    /// 마지막 창은 Z-order 바닥으로 보낸다. 활성 상태는 바뀌지 않는다.
    /// </summary>
    public static int Apply(IReadOnlyList<nint> handles, bool topmost)
    {
        ArgumentNullException.ThrowIfNull(handles);
        if (handles.Count == 0) return 0;

        var failed = 0;
        var lastError = 0;
        void Track(bool ok)
        {
            if (ok) return;
            failed++;
            lastError = Marshal.GetLastWin32Error();
        }

        var front = handles.Count - 1; // 마지막 창을 뺀 앞쪽 창 수
        for (var i = 0; i < front; i++)
        {
            if (topmost) Track(SetWindowPos(handles[i], HWND_TOPMOST, 0, 0, 0, 0, OrderFlags));
            else if (i == 0) Track(BringToFront(handles[0]));
            if (i > 0) Track(SetWindowPos(handles[i], handles[i - 1], 0, 0, 0, 0, OrderFlags));
        }

        Track(SendToBack(handles[^1]));
        if (failed > 0)
            throw new Win32Exception(lastError, $"창 {failed}개를 옮기지 못했습니다 (Win32 오류 {lastError}).");
        return handles.Count;
    }

    /// <summary>Win32 오류 5(액세스 거부)인지. 관리자 권한 창을 일반 권한에서 옮기려 할 때 난다(UIPI).</summary>
    public static bool IsAccessDenied(Exception error) => error is Win32Exception { NativeErrorCode: 5 };

    /// <summary>창을 Z-order 맨 뒤로 보낸다. 활성 상태는 바뀌지 않는다(활성 창도 뒤로 갈 수 있다).</summary>
    public static bool SendToBack(nint hwnd)
        => hwnd != 0 && SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, OrderFlags);

    /// <summary>
    /// 창을 활성화 없이 맨 앞으로 올린다. 다른 프로세스 창은 HWND_TOP 이 무시되므로 "항상 위"를 켰다가 바로 끈다.
    /// 원래 항상 위였던 창은 그 층의 맨 앞으로 가고 항상 위를 유지한다.
    /// </summary>
    public static bool BringToFront(nint hwnd)
    {
        if (hwnd == 0) return false;
        var keepTopmost = IsTopmost(hwnd);
        var ok = SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, OrderFlags);
        if (!keepTopmost) ok &= SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, OrderFlags);
        return ok;
    }

    public static bool IsTopmost(nint hwnd)
        => hwnd != 0 && (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOPMOST) != 0;

    /// <summary>창을 "항상 위" 층에 넣거나(true) 뺀다(false). 활성 상태는 바뀌지 않는다.</summary>
    public static bool SetTopmost(nint hwnd, bool topmost)
        => hwnd != 0 && SetWindowPos(hwnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, OrderFlags);

    public static nint ForegroundWindow => GetForegroundWindow();

    private static WindowInfo? Describe(nint hwnd, Dictionary<int, string> names)
    {
        if (hwnd == 0 || !IsWindowVisible(hwnd)) return null;
        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((exStyle & WS_EX_TOOLWINDOW) != 0) return null;
        if (IsCloaked(hwnd)) return null;

        var title = GetTitle(hwnd);
        if (title.Length == 0) return null;

        GetWindowThreadProcessId(hwnd, out var pid);
        return new WindowInfo(hwnd, title, ProcessNameOf((int)pid, names), (int)pid, (exStyle & WS_EX_TOPMOST) != 0);
    }

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
