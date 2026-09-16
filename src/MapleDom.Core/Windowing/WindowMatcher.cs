using MapleDom.Core.Settings;

namespace MapleDom.Core.Windowing;

/// <summary>실행 중인 최상위 창 하나. Handle은 Win32 HWND. ZOrder는 현재 쌓임 순서(1 = 맨 앞, 0 = 모름).</summary>
public sealed record WindowInfo(nint Handle, string Title, string ProcessName, int ProcessId, bool IsTopmost, int ZOrder = 0)
{
    public string Display => (ZOrder > 0 ? $"{ZOrder}. " : "") + (Title.Length == 0 ? ProcessName : $"{Title}  [{ProcessName}]");
}

/// <summary>선택 조건 하나와 그에 매칭된 창(없으면 null).</summary>
public sealed record ResolvedWindow(WindowSelector Selector, WindowInfo? Window)
{
    public bool Found => Window is not null;
}

/// <summary>창 선택 조건 ↔ 실행 중인 창 매칭. 순수 로직이라 Win32 없이 테스트할 수 있다.</summary>
public static class WindowMatcher
{
    public static string NormalizeProcessName(string? name)
    {
        var value = (name ?? "").Trim();
        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        return value;
    }

    public static bool Matches(WindowSelector selector, WindowInfo window)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(window);

        var process = NormalizeProcessName(selector.ProcessName);
        var title = (selector.TitleContains ?? "").Trim();
        if (process.Length == 0 && title.Length == 0) return false;
        if (process.Length > 0 && !string.Equals(process, NormalizeProcessName(window.ProcessName), StringComparison.OrdinalIgnoreCase)) return false;
        if (title.Length > 0 && !window.Title.Contains(title, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>windows(앞→뒤 Z 순서)에서 첫 매치. 없으면 null.</summary>
    public static WindowInfo? Find(WindowSelector selector, IEnumerable<WindowInfo> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        foreach (var window in windows)
            if (Matches(selector, window)) return window;
        return null;
    }

    /// <summary>
    /// 조건 순서대로, windows(앞→뒤 Z 순서)에서 첫 매치를 고른다. 한 창은 한 조건에만 배정된다.
    /// </summary>
    public static IReadOnlyList<ResolvedWindow> Resolve(IEnumerable<WindowSelector> selectors, IReadOnlyList<WindowInfo> windows)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        ArgumentNullException.ThrowIfNull(windows);

        var used = new HashSet<nint>();
        var result = new List<ResolvedWindow>();
        foreach (var selector in selectors)
        {
            WindowInfo? match = null;
            foreach (var window in windows)
            {
                if (used.Contains(window.Handle) || !Matches(selector, window)) continue;
                match = window;
                break;
            }

            if (match is not null) used.Add(match.Handle);
            result.Add(new ResolvedWindow(selector, match));
        }

        return result;
    }

    /// <summary>창 하나를 선택 조건으로 바꾼다(프로세스 이름 + 전체 제목).</summary>
    public static WindowSelector SelectorFor(WindowInfo window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new WindowSelector { ProcessName = NormalizeProcessName(window.ProcessName), TitleContains = window.Title };
    }
}
