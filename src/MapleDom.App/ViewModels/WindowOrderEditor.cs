using System.Collections.ObjectModel;
using MapleDom.App.Infrastructure;
using MapleDom.Core.Settings;
using MapleDom.Core.WindowOrder;
using MapleDom.Windows;

namespace MapleDom.App.ViewModels;

/// <summary>창 순서 목록의 한 줄. 프로세스 이름과 제목은 직접 고칠 수 있다.</summary>
public sealed class WindowOrderEntryEditor : ObservableObject
{
    private readonly WindowOrderEditor _owner;
    private string _processName;
    private string _titleContains;
    private string _status = "확인 전";
    private bool _found;
    private int _index;

    public WindowOrderEntryEditor(WindowOrderEditor owner, string processName, string titleContains)
    {
        _owner = owner;
        _processName = processName;
        _titleContains = titleContains;
        MoveUpCommand = new RelayCommand(() => _owner.Move(this, -1));
        MoveDownCommand = new RelayCommand(() => _owner.Move(this, +1));
        RemoveCommand = new RelayCommand(() => _owner.Remove(this));
    }

    public string ProcessName
    {
        get => _processName;
        set { if (Set(ref _processName, value)) _owner.RefreshStatus(); }
    }

    public string TitleContains
    {
        get => _titleContains;
        set { if (Set(ref _titleContains, value)) _owner.RefreshStatus(); }
    }

    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsFound { get => _found; private set => Set(ref _found, value); }
    public int Index { get => _index; set => Set(ref _index, value); }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand RemoveCommand { get; }

    public WindowOrderEntry ToEntry() => new() { ProcessName = ProcessName.Trim(), TitleContains = TitleContains.Trim() };

    internal void SetStatus(ResolvedWindow resolved)
    {
        IsFound = resolved.Found;
        Status = resolved.Found ? "찾음" : "없음";
    }
}

/// <summary>설정 창 "창 순서" 탭의 편집 상태. 실행 중인 창 목록은 <see cref="RefreshWindows"/>로 다시 읽는다.</summary>
public sealed class WindowOrderEditor : ObservableObject
{
    private bool _enabled;
    private bool _topmost;
    private WindowInfo? _selected;
    private IReadOnlyList<WindowInfo> _windows = [];

    public WindowOrderEditor()
    {
        RefreshCommand = new RelayCommand(RefreshWindows);
        AddCommand = new RelayCommand(AddSelected);
    }

    public ObservableCollection<WindowOrderEntryEditor> Entries { get; } = [];
    public ObservableCollection<WindowInfo> Available { get; } = [];
    public RelayCommand RefreshCommand { get; }
    public RelayCommand AddCommand { get; }

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool Topmost { get => _topmost; set => Set(ref _topmost, value); }

    public WindowInfo? Selected
    {
        get => _selected;
        set { if (Set(ref _selected, value)) OnPropertyChanged(nameof(CanAdd)); }
    }

    public bool CanAdd => Selected is not null;

    public string Summary => Entries.Count == 0
        ? "목록이 비어 있습니다. 위에서 창을 골라 추가하세요."
        : $"{Entries.Count}개 중 {Entries.Count(e => e.IsFound)}개 실행 중";

    public static WindowOrderEditor From(WindowOrderSettings settings)
    {
        var editor = new WindowOrderEditor { Enabled = settings.Enabled, Topmost = settings.Topmost };
        foreach (var entry in settings.Windows)
            editor.Entries.Add(new WindowOrderEntryEditor(editor, entry.ProcessName, entry.TitleContains));
        editor.Reindex();
        editor.RefreshWindows();
        return editor;
    }

    /// <summary>실행 중인 창 목록을 다시 읽고 각 항목의 찾음/없음 상태를 갱신한다.</summary>
    public void RefreshWindows()
    {
        try
        {
            _windows = WindowArranger.EnumerateWindows();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _windows = [];
        }

        var keep = Selected?.Handle;
        Available.Clear();
        foreach (var window in _windows) Available.Add(window);
        Selected = Available.FirstOrDefault(w => w.Handle == keep) ?? Available.FirstOrDefault();
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        var resolved = WindowOrderResolver.Resolve(Entries.Select(e => e.ToEntry()).ToList(), _windows);
        for (var i = 0; i < Entries.Count; i++) Entries[i].SetStatus(resolved[i]);
        OnPropertyChanged(nameof(Summary));
    }

    public void AddSelected()
    {
        if (Selected is null) return;
        var entry = WindowOrderResolver.EntryFor(Selected);
        Entries.Add(new WindowOrderEntryEditor(this, entry.ProcessName, entry.TitleContains));
        Reindex();
        RefreshStatus();
    }

    public void Move(WindowOrderEntryEditor item, int delta)
    {
        var from = Entries.IndexOf(item);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Entries.Count) return;
        Entries.Move(from, to);
        Reindex();
        RefreshStatus();
    }

    public void Remove(WindowOrderEntryEditor item)
    {
        if (!Entries.Remove(item)) return;
        Reindex();
        RefreshStatus();
    }

    public WindowOrderSettings Build() => new()
    {
        Enabled = Enabled,
        Topmost = Topmost,
        Windows = Entries.Select(e => e.ToEntry()).ToList(),
    };

    private void Reindex()
    {
        for (var i = 0; i < Entries.Count; i++) Entries[i].Index = i + 1;
    }
}
