using System.Collections.ObjectModel;
using MapleDom.App.Infrastructure;
using MapleDom.Core.Settings;
using MapleDom.Core.Windowing;
using MapleDom.Windows;

namespace MapleDom.App.ViewModels;

/// <summary>
/// 메인 창의 "창 순서" 카드. 맨 뒤에 둘 창을 고르는 콤보(실행 중인 창, 현재 Z 순서 번호 포함)와 [적용]/[해제] 토글.
/// 콤보에서 고르면 목록의 마지막 항목(맨 뒤 창)이 바뀌고 바로 저장된다. 그 앞에 둘 창은 설정 > 창 순서에서 정한다.
/// 키퍼가 없으면(검증 도구 등) 적용 버튼은 비활성이다.
/// </summary>
public sealed class WindowOrderViewModel : ObservableObject
{
    private readonly WindowOrderKeeper? _keeper;
    private readonly Func<WindowOrderSettings> _getSettings;
    private readonly Action<WindowInfo>? _selectBack;
    private string _status = "";
    private bool _applied;
    private bool _enabled;
    private WindowInfo? _selectedBack;
    private bool _syncingSelection;
    private long _lastRefreshTicks = long.MinValue;

    public WindowOrderViewModel(WindowOrderKeeper? keeper, Action<Action> post, Func<WindowOrderSettings> getSettings, Action<WindowInfo>? selectBack = null)
    {
        _keeper = keeper;
        _getSettings = getSettings;
        _selectBack = selectBack;
        ToggleCommand = new RelayCommand(Toggle);
        PeekCommand = new RelayCommand(Peek);
        if (keeper is not null) keeper.Changed += () => post(Refresh);
        RefreshAvailable();
        Refresh();
    }

    /// <summary>실행 중인 창(앞→뒤). 콤보가 열릴 때와 상태가 바뀔 때 다시 읽는다.</summary>
    public ObservableCollection<WindowInfo> Available { get; } = [];

    public RelayCommand ToggleCommand { get; }
    public RelayCommand PeekCommand { get; }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsEnabled { get => _enabled; private set => Set(ref _enabled, value); }
    public bool CanSelect => _selectBack is not null;

    public bool IsApplied
    {
        get => _applied;
        private set { if (Set(ref _applied, value)) OnPropertyChanged(nameof(ToggleLabel)); }
    }

    public string ToggleLabel => IsApplied ? "해제" : "적용";

    /// <summary>맨 뒤에 둘 창(목록의 마지막 항목). 사용자가 고르면 바로 저장된다.</summary>
    public WindowInfo? SelectedBack
    {
        get => _selectedBack;
        set
        {
            if (!Set(ref _selectedBack, value) || _syncingSelection || value is null) return;
            _selectBack?.Invoke(value);
        }
    }

    public void Toggle()
    {
        if (_keeper is null || !_keeper.HasWindows) return;
        _keeper.Toggle();
        Refresh();
    }

    public void Peek() => _keeper?.Peek();

    public void RefreshAvailable()
    {
        IReadOnlyList<WindowInfo> windows;
        try
        {
            windows = WindowArranger.EnumerateWindows();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            windows = [];
        }

        _syncingSelection = true;
        try
        {
            Available.Clear();
            foreach (var window in windows) Available.Add(window);
        }
        finally
        {
            _syncingSelection = false;
        }
        SyncSelection();
    }

    /// <summary>창 열거 비용이 있으므로 폴링에서는 1초에 한 번만 다시 계산한다.</summary>
    public void RefreshThrottled()
    {
        if (Environment.TickCount64 - _lastRefreshTicks < 1000) return;
        Refresh();
    }

    public void Refresh()
    {
        _lastRefreshTicks = Environment.TickCount64;
        var settings = _getSettings();
        IsApplied = _keeper?.IsApplied ?? false;
        IsEnabled = _keeper is not null && settings.Windows.Count > 0;

        if (settings.Windows.Count == 0)
        {
            Status = _keeper is null ? "창 순서 기능 없음" : "맨 뒤에 둘 창을 고르세요. 그 앞에 둘 창은 설정 > 창 순서";
            SyncSelection();
            return;
        }

        var resolved = _keeper is not null ? _keeper.Resolve() : WindowMatcher.Resolve(settings.Windows, Available);
        var found = resolved.Count(r => r.Found);
        var count = resolved.Count == 1 ? "창 1개" : $"창 {resolved.Count}개";
        Status = IsApplied
            ? (found == resolved.Count ? $"{count} · 적용 중 · 활성화해도 순서 유지" : $"{count} · 적용 중 · {found}/{resolved.Count}개 실행 중")
            : (found == 0 ? $"{count} · 해제됨 · 창이 실행 중이 아님" : $"{count} · 해제됨 · Windows 기본 (최근 창이 앞)");
        SyncSelection();
    }

    /// <summary>설정의 마지막 항목(맨 뒤 창)과 일치하는 실행 중인 창을 콜백 없이 선택 상태로 맞춘다.</summary>
    private void SyncSelection()
    {
        var settings = _getSettings();
        var last = settings.Windows.Count > 0 ? settings.Windows[^1] : null;
        var match = last is null ? null : WindowMatcher.Find(last, Available);
        if (match is null && last is not null) return; // 목록에 없으면(창이 안 떠 있음) 이전 선택을 유지

        _syncingSelection = true;
        try
        {
            SelectedBack = match;
        }
        finally
        {
            _syncingSelection = false;
        }
    }
}
