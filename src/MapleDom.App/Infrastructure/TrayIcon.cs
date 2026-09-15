using System.Drawing;
using System.Windows.Threading;
using MapleDom.App.ViewModels;
using MapleDom.Core;
using Forms = System.Windows.Forms;

namespace MapleDom.App.Infrastructure;

internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly Icon _drawing;

    public TrayIcon(MainViewModel viewModel, Dispatcher dispatcher, Action restore, Action exit)
    {
        void Invoke(Action action) => dispatcher.BeginInvoke(action);
        void Add(string label, Action action) => _menu.Items.Add(label, null, (_, _) => Invoke(action));
        Add("열기", restore);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        Add("전체 시작", viewModel.StartAll);
        Add("전체 정지", viewModel.StopAll);
        Add("룬 깟음", () => viewModel.Acknowledge(TimerService.RuneId));
        Add("도핑했음", () => viewModel.Acknowledge(TimerService.DopingId));
        Add("창 순서 정렬", viewModel.ArrangeWindows);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        var mute = new Forms.ToolStripMenuItem("음소거", null, (_, _) => Invoke(() => viewModel.Muted = !viewModel.Muted));
        var topmost = new Forms.ToolStripMenuItem("항상 위", null, (_, _) => Invoke(() => viewModel.Topmost = !viewModel.Topmost));
        _menu.Items.Add(mute);
        _menu.Items.Add(topmost);
        _menu.Opening += (_, _) => { mute.Checked = viewModel.Muted; topmost.Checked = viewModel.Topmost; };
        _menu.Items.Add(new Forms.ToolStripSeparator());
        Add("종료", exit);
        _drawing = LoadAppIcon() ?? DrawIcon();
        _icon = new Forms.NotifyIcon { Icon = _drawing, Text = "MapleDom · 타이머", ContextMenuStrip = _menu, Visible = true };
        _icon.DoubleClick += (_, _) => Invoke(restore);
    }

    /// <summary>exe에 들어간 앱 아이콘(Assets/app.ico)을 트레이 크기로 불러온다. 실패하면 null.</summary>
    private static Icon? LoadAppIcon()
    {
        try
        {
            using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("MapleDom.App.Assets.app.ico");
            return stream is null ? null : new Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or BadImageFormatException)
        {
            return null;
        }
    }

    private static Icon DrawIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var background = new SolidBrush(Color.FromArgb(23, 28, 36));
        using var pen = new Pen(Color.FromArgb(255, 182, 107), 3);
        graphics.FillEllipse(background, 1, 1, 30, 30);
        graphics.DrawEllipse(pen, 3, 3, 26, 26);
        graphics.DrawLines(pen, [new Point(16, 8), new Point(16, 16), new Point(23, 19)]);
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { NativeMethods.DestroyIcon(handle); }
    }

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); _menu.Dispose(); _drawing.Dispose(); }
}
