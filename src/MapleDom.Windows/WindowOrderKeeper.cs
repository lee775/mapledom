using MapleDom.Core.Settings;
using MapleDom.Core.Windowing;

namespace MapleDom.Windows;

/// <summary>
/// 창 순서 고정의 런타임 조정자.
/// <para>
/// <see cref="Apply"/>(적용): 목록 순서로 지금 정렬하고, 이후 목록의 창이 활성화될 때마다 잠시 뒤 다시 정렬한다(활성 창은 그대로).
/// <see cref="Release"/>(해제): 감시를 멈춘다. 아무것도 되돌리지 않으므로 Windows 기본대로 최근에 활성화한 창이 앞에 온다.
/// 목록에 없는 창이 활성화될 때는 아무것도 하지 않는다.
/// </para>
/// <para><see cref="Apply"/>, <see cref="Release"/>, <see cref="Update"/>는 UI 스레드에서 호출한다(포그라운드 훅이 그 스레드의 메시지 루프를 쓴다).</para>
/// </summary>
public sealed class WindowOrderKeeper : IDisposable
{
    /// <summary>활성화 직후 Windows가 Z-order를 마저 조정하므로 이만큼 기다렸다가 적용한다.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(80);

    private readonly object _gate = new();
    private WindowOrderSettings _settings = new();
    private ForegroundWatcher? _watcher;
    private Timer? _debounce;
    private nint _pendingForeground;
    private bool _applied;
    private bool _disposed;

    /// <summary>적용/해제 상태나 설정이 바뀐 뒤. 어느 스레드에서든 발생할 수 있다.</summary>
    public event Action? Changed;

    /// <summary>순서를 실제로 정렬한 직후(적용 버튼, 자동 재정렬 모두). 어느 스레드에서든 발생할 수 있다.</summary>
    public event Action? Applied;

    /// <summary>Win32 호출이나 훅 설치가 실패했을 때. 어느 스레드에서든 발생할 수 있다.</summary>
    public event Action<Exception>? Failed;

    public bool IsApplied
    {
        get { lock (_gate) return _applied; }
    }

    public WindowOrderSettings Settings
    {
        get { lock (_gate) return Copy(_settings); }
    }

    public bool HasWindows
    {
        get { lock (_gate) return _settings.Windows.Count > 0; }
    }

    /// <summary>설정을 바꾼다. 적용 중이면 새 목록으로 즉시 다시 정렬한다.</summary>
    public void Update(WindowOrderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate) _settings = Copy(settings);
        if (IsApplied)
        {
            if (HasWindows) ApplyNow();
            else Release();
        }
        Changed?.Invoke();
    }

    public IReadOnlyList<ResolvedWindow> Resolve()
    {
        WindowOrderSettings settings;
        lock (_gate) settings = _settings;
        return WindowArranger.Resolve(settings);
    }

    /// <summary>상태 변경 없이 지금 한 번 정렬한다. 반환: 찾아서 정렬한 창 수.</summary>
    public int ApplyNow()
    {
        WindowOrderSettings settings;
        lock (_gate) settings = _settings;
        try
        {
            var count = WindowArranger.Apply(settings);
            if (count > 0) Applied?.Invoke();
            return count;
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex);
            return 0;
        }
    }

    /// <summary>적용: 지금 정렬하고 이후 순서를 유지한다.</summary>
    /// <returns>정렬한 창 수(0이면 목록의 창을 하나도 못 찾음. 상태는 적용으로 유지되어 창이 뜨면 정렬된다).</returns>
    public int Apply()
    {
        lock (_gate)
        {
            if (_disposed) return 0;
            _applied = true;
        }
        EnsureWatcher();
        var count = ApplyNow();
        Changed?.Invoke();
        return count;
    }

    /// <summary>
    /// 해제: 감시를 멈추고, 지금 활성인 창을 맨 앞으로 올린다.
    /// 아무것도 하지 않았을 때처럼 "최근에 쓰던(활성) 창이 앞에 있는" Windows 기본 상태가 된다.
    /// </summary>
    public void Release()
    {
        lock (_gate) _applied = false;
        RemoveWatcher();
        Peek();
        Changed?.Invoke();
    }

    /// <returns>토글 후 적용 상태.</returns>
    public bool Toggle()
    {
        if (IsApplied)
        {
            Release();
            return false;
        }
        Apply();
        return true;
    }

    /// <summary>지금 활성인 창을 맨 앞으로 올린다(잠깐 보기). 적용 중이면 다음에 목록 창이 활성화될 때 다시 정렬된다.</summary>
    public bool Peek()
    {
        try
        {
            var foreground = WindowArranger.ForegroundWindow;
            return foreground != 0 && WindowArranger.BringToFront(foreground);
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex);
            return false;
        }
    }

    private void EnsureWatcher()
    {
        if (_watcher is not null) return;
        try
        {
            _watcher = new ForegroundWatcher();
            _watcher.ForegroundChanged += OnForegroundChanged;
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex);
        }
    }

    private void RemoveWatcher()
    {
        if (_watcher is null) return;
        _watcher.ForegroundChanged -= OnForegroundChanged;
        _watcher.Dispose();
        _watcher = null;
    }

    private void OnForegroundChanged(nint hwnd)
    {
        lock (_gate)
        {
            if (_disposed || !_applied) return;
            _pendingForeground = hwnd;
            _debounce ??= new Timer(_ => ReapplyPending(), null, Timeout.Infinite, Timeout.Infinite);
            _debounce.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void ReapplyPending()
    {
        WindowOrderSettings settings;
        nint foreground;
        lock (_gate)
        {
            if (_disposed || !_applied) return;
            settings = _settings;
            foreground = _pendingForeground;
        }

        try
        {
            var handles = WindowArranger.Resolve(settings)
                .Where(r => r.Window is not null)
                .Select(r => r.Window!.Handle)
                .ToList();
            if (!handles.Contains(foreground)) return;

            WindowArranger.Apply(handles, settings.Topmost);
            Applied?.Invoke();
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex);
        }
    }

    private static WindowOrderSettings Copy(WindowOrderSettings s) => new()
    {
        ApplyOnStart = s.ApplyOnStart,
        Topmost = s.Topmost,
        Windows = s.Windows.Select(w => w.Clone()).ToList(),
    };

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _applied = false;
            _debounce?.Dispose();
            _debounce = null;
        }
        RemoveWatcher();
    }
}
