using MapleDom.Core.Settings;
using MapleDom.Core.WindowOrder;

namespace MapleDom.Windows;

/// <summary>
/// 창 순서 고정 기능의 런타임 조정자. 설정을 받아 두고, 목록의 창이 활성화되면 잠시 뒤 목록 순서를 다시 적용한다.
/// 목록에 없는 창이 활성화될 때는 아무것도 하지 않는다(다른 앱을 가리지 않기 위해).
/// <para><see cref="Update"/>는 UI 스레드에서 호출한다(포그라운드 훅이 그 스레드의 메시지 루프를 쓴다).</para>
/// </summary>
public sealed class WindowOrderController : IDisposable
{
    /// <summary>활성화 직후 Windows가 Z-order를 마저 조정하므로 이만큼 기다렸다가 적용한다.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    private readonly object _gate = new();
    private WindowOrderSettings _settings = new();
    private ForegroundWatcher? _watcher;
    private Timer? _debounce;
    private nint _pendingForeground;
    private bool _disposed;

    /// <summary>Win32 호출이나 훅 설치가 실패했을 때. 어느 스레드에서든 발생할 수 있다.</summary>
    public event Action<Exception>? Failed;

    /// <summary>순서를 적용한 직후(자동/수동 모두). 어느 스레드에서든 발생할 수 있다.</summary>
    public event Action? Applied;

    public bool IsWatching => _watcher is { IsActive: true };

    public void Update(WindowOrderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var copy = new WindowOrderSettings
        {
            Enabled = settings.Enabled,
            Topmost = settings.Topmost,
            Windows = settings.Windows
                .Select(e => new WindowOrderEntry { ProcessName = e.ProcessName, TitleContains = e.TitleContains })
                .ToList(),
        };
        lock (_gate) _settings = copy;

        var wantWatch = copy.Enabled && copy.Windows.Count > 0 && !_disposed;
        if (wantWatch && _watcher is null)
        {
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
        else if (!wantWatch && _watcher is not null)
        {
            _watcher.ForegroundChanged -= OnForegroundChanged;
            _watcher.Dispose();
            _watcher = null;
        }

        if (wantWatch) ApplyNow();
    }

    /// <summary>목록 순서를 지금 적용(그룹을 맨 앞으로). 반환: 찾아서 정렬한 창 수.</summary>
    public int ApplyNow()
    {
        WindowOrderSettings settings;
        lock (_gate) settings = _settings;
        try
        {
            var count = WindowArranger.Apply(settings, raise: true);
            Applied?.Invoke();
            return count;
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex);
            return 0;
        }
    }

    /// <summary>활성(키 입력) 창을 맨 앞으로 올린다. 다음에 목록 창이 활성화될 때까지 유지된다.</summary>
    public bool RaiseActiveWindow()
    {
        try
        {
            return WindowArranger.RaiseForeground();
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex);
            return false;
        }
    }

    public IReadOnlyList<ResolvedWindow> Resolve()
    {
        WindowOrderSettings settings;
        lock (_gate) settings = _settings;
        return WindowArranger.Resolve(settings);
    }

    private void OnForegroundChanged(nint hwnd)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pendingForeground = hwnd;
            _debounce ??= new Timer(_ => ApplyPending(), null, Timeout.Infinite, Timeout.Infinite);
            _debounce.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void ApplyPending()
    {
        WindowOrderSettings settings;
        nint foreground;
        lock (_gate)
        {
            if (_disposed || !_settings.Enabled) return;
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

            WindowArranger.Apply(handles, settings.Topmost, raise: true);
            Applied?.Invoke();
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _debounce?.Dispose();
            _debounce = null;
        }

        if (_watcher is not null)
        {
            _watcher.ForegroundChanged -= OnForegroundChanged;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
