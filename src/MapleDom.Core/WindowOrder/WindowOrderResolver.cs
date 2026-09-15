using MapleDom.Core.Settings;

namespace MapleDom.Core.WindowOrder;

/// <summary>실행 중인 최상위 창 하나. Handle은 Win32 HWND.</summary>
public sealed record WindowInfo(nint Handle, string Title, string ProcessName, int ProcessId, bool IsTopmost)
{
    public string Display => Title.Length == 0 ? ProcessName : $"{Title}  [{ProcessName}]";
}

/// <summary>설정 항목 하나와 그에 매칭된 창(없으면 null).</summary>
public sealed record ResolvedWindow(WindowOrderEntry Entry, WindowInfo? Window)
{
    public bool Found => Window is not null;
}

/// <summary>설정 항목 ↔ 실행 중인 창 매칭. 순수 로직이라 Win32 없이 테스트할 수 있다.</summary>
public static class WindowOrderResolver
{
    public static string NormalizeProcessName(string? name)
    {
        var value = (name ?? "").Trim();
        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        return value;
    }

    public static bool Matches(WindowOrderEntry entry, WindowInfo window)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(window);

        var process = NormalizeProcessName(entry.ProcessName);
        var title = (entry.TitleContains ?? "").Trim();
        if (process.Length == 0 && title.Length == 0) return false;
        if (process.Length > 0 && !string.Equals(process, NormalizeProcessName(window.ProcessName), StringComparison.OrdinalIgnoreCase)) return false;
        if (title.Length > 0 && !window.Title.Contains(title, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>
    /// 항목 순서대로, windows(앞→뒤 Z 순서)에서 첫 매치를 고른다. 한 창은 한 항목에만 배정된다.
    /// </summary>
    public static IReadOnlyList<ResolvedWindow> Resolve(IEnumerable<WindowOrderEntry> entries, IReadOnlyList<WindowInfo> windows)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(windows);

        var used = new HashSet<nint>();
        var result = new List<ResolvedWindow>();
        foreach (var entry in entries)
        {
            WindowInfo? match = null;
            foreach (var window in windows)
            {
                if (used.Contains(window.Handle) || !Matches(entry, window)) continue;
                match = window;
                break;
            }

            if (match is not null) used.Add(match.Handle);
            result.Add(new ResolvedWindow(entry, match));
        }

        return result;
    }

    /// <summary>창 하나를 설정 항목으로 바꾼다(프로세스 이름 + 전체 제목).</summary>
    public static WindowOrderEntry EntryFor(WindowInfo window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new WindowOrderEntry { ProcessName = NormalizeProcessName(window.ProcessName), TitleContains = window.Title };
    }
}
