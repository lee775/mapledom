using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MapleDom.App.Infrastructure;
using MapleDom.Core.Settings;

namespace MapleDom.App;

/// <summary>
/// 알림이 울릴 때 화면에 크게 뜨는 배너. Windows 토스트가 잘 안 보일 때(전체화면 게임 등)를 위한 것이다.
/// <para>
/// 포커스를 뺏지 않고(WS_EX_NOACTIVATE) 클릭을 통과시키므로(WS_EX_TRANSPARENT) 게임 조작을 방해하지 않는다.
/// 작업표시줄/Alt+Tab에도 나오지 않는다(WS_EX_TOOLWINDOW).
/// </para>
/// </summary>
public partial class AlertOverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_TOOLWINDOW = 0x00000080;

    public AlertOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        var style = NativeMethods.GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(handle, GWL_EXSTYLE, (nint)(style | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW));
    }

    /// <summary>배너 내용을 채우고 지정한 위치에 띄운다. 이미 떠 있으면 내용만 바꾼다.</summary>
    public void ShowAlert(string title, string message, OverlaySettings settings, OverlayPosition position = OverlayPosition.Top)
    {
        ArgumentNullException.ThrowIfNull(settings);

        TitleText.Text = title;
        TitleText.FontSize = settings.FontSize;
        MessageText.Text = message ?? "";
        MessageText.FontSize = Math.Max(12, settings.FontSize * 0.42);
        MessageText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        Opacity = settings.Opacity;

        if (!IsVisible) Show();
        UpdateLayout();
        Place(position);

        // 다른 '항상 위' 창(게임 등)보다 앞에 오도록 다시 세운다. 활성화는 하지 않는다.
        Topmost = false;
        Topmost = true;
    }

    private void Place(OverlayPosition position)
    {
        const double Margin = 24;
        var area = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Root.DesiredSize.Width;
        var height = ActualHeight > 0 ? ActualHeight : Root.DesiredSize.Height;
        var middleX = area.Left + Math.Max(0, (area.Width - width) / 2);
        var rightX = area.Right - width - Margin;

        Left = position switch
        {
            OverlayPosition.TopLeft or OverlayPosition.BottomLeft => area.Left + Margin,
            OverlayPosition.TopRight or OverlayPosition.BottomRight => Math.Max(area.Left, rightX),
            _ => middleX,
        };
        Top = position switch
        {
            OverlayPosition.Center => area.Top + Math.Max(0, (area.Height - height) / 2),
            OverlayPosition.Bottom => area.Bottom - height - area.Height * 0.12,
            OverlayPosition.BottomLeft or OverlayPosition.BottomRight => area.Bottom - height - Margin,
            OverlayPosition.TopLeft or OverlayPosition.TopRight => area.Top + Margin,
            _ => area.Top + area.Height * 0.08,
        };
    }

    /// <summary>타이머별 테두리 색. 룬·도핑은 초록/빨강 계열로 구분한다.</summary>
    public void SetAccent(string timerId)
    {
        var (border, title) = timerId switch
        {
            "rune" => ("#7FD69A", "#D8FFE6"),
            "doping" => ("#FF8A8A", "#FFE0E0"),
            _ => ("#FFB357", "#FFD9A0"),
        };
        Root.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border));
        TitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(title));
    }
}
