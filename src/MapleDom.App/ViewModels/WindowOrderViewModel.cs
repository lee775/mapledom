using MapleDom.App.Infrastructure;
using MapleDom.Core.Windowing;
using MapleDom.Windows;

namespace MapleDom.App.ViewModels;

/// <summary>
/// 메인 창의 "창 순서" 카드. [적용]/[해제] 토글과 요약(창 수, 맨 뒤 창)을 보여 준다.
/// 키퍼가 없으면(검증 도구 등) 비활성 상태로만 표시한다.
/// </summary>
public sealed class WindowOrderViewModel : ObservableObject
{
    private readonly WindowOrderKeeper? _keeper;
    private string _summary = "";
    private string _status = "";
    private bool _applied;
    private bool _enabled;
    private long _lastRefreshTicks = long.MinValue;

    public WindowOrderViewModel(WindowOrderKeeper? keeper, Action<Action> post)
    {
        _keeper = keeper;
        ToggleCommand = new RelayCommand(Toggle);
        PeekCommand = new RelayCommand(Peek);
        if (keeper is not null) keeper.Changed += () => post(Refresh);
        Refresh();
    }

    public RelayCommand ToggleCommand { get; }
    public RelayCommand PeekCommand { get; }
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsEnabled { get => _enabled; private set => Set(ref _enabled, value); }

    public bool IsApplied
    {
        get => _applied;
        private set { if (Set(ref _applied, value)) OnPropertyChanged(nameof(ToggleLabel)); }
    }

    public string ToggleLabel => IsApplied ? "해제" : "적용";

    /// <summary>적용 ↔ 해제. 목록이 비어 있으면 아무것도 하지 않는다.</summary>
    public void Toggle()
    {
        if (_keeper is null || !_keeper.HasWindows) return;
        _keeper.Toggle();
        Refresh();
    }

    /// <summary>지금 활성인 창을 맨 앞으로(잠깐 보기).</summary>
    public void Peek() => _keeper?.Peek();

    /// <summary>창 열거 비용이 있으므로 폴링에서는 1초에 한 번만 다시 계산한다.</summary>
    public void RefreshThrottled()
    {
        if (Environment.TickCount64 - _lastRefreshTicks < 1000) return;
        Refresh();
    }

    public void Refresh()
    {
        _lastRefreshTicks = Environment.TickCount64;
        if (_keeper is null)
        {
            IsApplied = false;
            IsEnabled = false;
            Summary = "창 순서 기능 없음";
            Status = "";
            return;
        }

        IsApplied = _keeper.IsApplied;
        var settings = _keeper.Settings;
        IsEnabled = settings.Windows.Count > 0;
        if (!IsEnabled)
        {
            Summary = "지정된 창 없음";
            Status = "설정 > 창 순서에서 창을 추가하세요";
            return;
        }

        var resolved = _keeper.Resolve();
        var found = resolved.Count(r => r.Found);
        var last = resolved[^1];
        var backName = last.Window?.ProcessName ?? WindowMatcher.NormalizeProcessName(last.Selector.ProcessName);
        if (backName.Length == 0) backName = last.Selector.TitleContains;

        Summary = resolved.Count == 1 ? $"맨 뒤: {backName}" : $"{resolved.Count}개 창 · 맨 뒤: {backName}";
        Status = IsApplied
            ? (found == resolved.Count ? "적용 중 · 활성화해도 정한 순서를 유지" : $"적용 중 · {found}/{resolved.Count}개 창 찾음")
            : (found == 0 ? "해제됨 · 목록의 창이 실행 중이 아님" : "해제됨 · Windows 기본 (최근 창이 앞)");
    }
}
