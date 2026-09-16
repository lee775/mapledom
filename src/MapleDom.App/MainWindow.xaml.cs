using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MapleDom.App.Infrastructure;
using MapleDom.App.ViewModels;
using MapleDom.Core;
using MapleDom.Windows;

namespace MapleDom.App;

public partial class MainWindow : Window
{
    private readonly TimerService _service;
    private readonly Action _exit;
    private GlobalHotkeys? _hotkeys;
    private SettingsWindow? _settingsWindow;
    private bool _exiting;

    public MainWindow(TimerService service, MainViewModel viewModel, Action exit)
    {
        _service = service;
        _exit = exit;
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += OnSourceInitialized;
        IsVisibleChanged += (_, _) => viewModel.IsVisible = IsVisible;
        LocationChanged += (_, _) => SavePosition();
        viewModel.PropertyChanged += OnViewModelChanged;
        viewModel.WindowOrderApplied += ReassertTopmost;
        Closing += OnClosing;
        Closed += (_, _) => { _hotkeys?.Dispose(); viewModel.PropertyChanged -= OnViewModelChanged; viewModel.WindowOrderApplied -= ReassertTopmost; };
        RestorePosition();
    }

    public MainViewModel ViewModel { get; }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        _hotkeys = new GlobalHotkeys(new WindowInteropHelper(this).Handle, ViewModel);
        _hotkeys.SetEnabled(ViewModel.UiSettings.HotkeysEnabled);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.UiSettings))
            _hotkeys?.SetEnabled(ViewModel.UiSettings.HotkeysEnabled);
    }

    public void RestoreFromTray()
    {
        if (_exiting) return;
        RestorePosition();
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (_settingsWindow is { } settings) { settings.WindowState = WindowState.Normal; settings.Activate(); }
    }

    /// <summary>창 순서 정렬로 다른 창이 '항상 위' 층 맨 앞에 오면 이 창을 다시 그 위로 올린다.</summary>
    public void ReassertTopmost()
    {
        if (_exiting || !IsVisible || !ViewModel.Topmost) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != 0) WindowArranger.SetTopmost(handle, true);
    }

    public void PrepareForExit()
    {
        if (_exiting) return;
        _exiting = true;
        SavePosition();
        _settingsWindow?.Close();
        _hotkeys?.Dispose();
        _hotkeys = null;
    }

    public void SavePosition()
    {
        if (WindowState != WindowState.Normal || !double.IsFinite(Left) || !double.IsFinite(Top)) return;
        ViewModel.UiSettings.Left = Left;
        ViewModel.UiSettings.Top = Top;
    }

    private void RestorePosition()
    {
        var work = SystemParameters.WorkArea;
        // Keep the complete small window reachable after monitor removal or a DPI change.
        var left = ViewModel.UiSettings.Left ?? work.Right - Width - 18;
        var top = ViewModel.UiSettings.Top ?? work.Top + 18;
        var areas = System.Windows.Forms.Screen.AllScreens.Select(screen => screen.WorkingArea).ToArray();
        var dpi = VisualTreeHelper.GetDpi(this);
        var reachable = areas.Any(area => area.Contains((int)(left * dpi.DpiScaleX), (int)(top * dpi.DpiScaleY))
            && area.Contains((int)((left + Width - 1) * dpi.DpiScaleX), (int)((top + Height - 1) * dpi.DpiScaleY)));
        Left = reachable ? left : Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - Width));
        Top = reachable ? top : Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - Height));
    }

    private void OnDragWindow(object sender, MouseButtonEventArgs args)
    {
        for (var current = args.OriginalSource as DependencyObject; current is not null; current = ParentOf(current))
            if (current is ButtonBase) return;
        if (args.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private static DependencyObject? ParentOf(DependencyObject child) => child is Visual
        ? VisualTreeHelper.GetParent(child)
        : LogicalTreeHelper.GetParent(child);

    private void OnCloseClick(object sender, RoutedEventArgs args) => Close();

    private void OnBackWindowDropDownOpened(object sender, EventArgs args) => ViewModel.WindowOrder.RefreshAvailable();

    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_exiting) return;
        args.Cancel = true;
        SavePosition();
        if (ViewModel.UiSettings.MinimizeToTray) Hide();
        else Dispatcher.BeginInvoke(_exit);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs args)
    {
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        SavePosition();
        _settingsWindow = new SettingsWindow(_service, ViewModel) { Owner = this };
        try { _settingsWindow.ShowDialog(); }
        finally { _settingsWindow = null; }
    }
}
