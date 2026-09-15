using System.Windows.Interop;
using MapleDom.App.ViewModels;
using MapleDom.Core;

namespace MapleDom.App.Infrastructure;

internal sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource _source;
    private readonly MainViewModel _viewModel;
    private readonly List<int> _registered = [];

    public GlobalHotkeys(nint handle, MainViewModel viewModel)
    {
        _source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("창 핸들을 찾을 수 없습니다.");
        _viewModel = viewModel;
        _source.AddHook(OnMessage);
    }

    public void SetEnabled(bool enabled)
    {
        Unregister();
        if (!enabled) return;
        (int Id, uint Key, string Label)[] keys = [(1, 0x52, "R"), (2, 0x44, "D"), (3, 0x53, "S"), (4, 0x5A, "Z"), (5, 0x58, "X")];
        foreach (var (id, key, label) in keys)
        {
            // MOD_CONTROL | MOD_ALT | MOD_NOREPEAT: holding a key never repeatedly resets a timer.
            if (NativeMethods.RegisterHotKey(_source.Handle, id, 0x4003, key)) _registered.Add(id);
            else _viewModel.ShowWarning($"Ctrl+Alt+{label} 단축키를 등록하지 못했습니다. 다른 앱의 단축키를 확인하세요");
        }
    }

    private nint OnMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x0312 || !_registered.Contains((int)wParam)) return 0;
        handled = true;
        switch ((int)wParam)
        {
            case 1: _viewModel.Acknowledge(TimerService.RuneId); break;
            case 2: _viewModel.Acknowledge(TimerService.DopingId); break;
            case 3: _viewModel.ToggleAll(); break;
            case 4: _viewModel.ToggleWindowOrder(); break;
            case 5: _viewModel.PeekWindow(); break;
        }
        return 0;
    }

    private void Unregister()
    {
        foreach (var id in _registered) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
    }

    public void Dispose() { Unregister(); _source.RemoveHook(OnMessage); }
}
