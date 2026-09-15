using MapleDom.App.Infrastructure;
using MapleDom.Core;
using MapleDom.Core.Text;
using MapleDom.Core.Timers;

namespace MapleDom.App.ViewModels;

public sealed class TimerCardViewModel : ObservableObject
{
    private string _name = "";
    private string _remaining = "0:00";
    private string _status = "정지";
    private bool _running;
    private bool _overdue;
    private bool _urgent;

    public TimerCardViewModel(string id, Action<string> toggle, Action<string> acknowledge)
    {
        Id = id;
        ToggleCommand = new RelayCommand(() => toggle(id));
        AcknowledgeCommand = new RelayCommand(() => acknowledge(id));
    }

    public string Id { get; }
    public string Name { get => _name; private set => Set(ref _name, value); }
    public string Remaining { get => _remaining; private set => Set(ref _remaining, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsRunning { get => _running; private set { if (Set(ref _running, value)) OnPropertyChanged(nameof(ToggleLabel)); } }
    public bool IsOverdue { get => _overdue; private set => Set(ref _overdue, value); }
    public bool IsUrgent { get => _urgent; private set => Set(ref _urgent, value); }
    public bool CanAcknowledge => true;
    public string AcknowledgeLabel => Id == TimerService.RuneId ? "룬 깟음" : "도핑했음";
    public string ToggleLabel => IsRunning ? "정지" : "시작";
    public RelayCommand ToggleCommand { get; }
    public RelayCommand AcknowledgeCommand { get; }

    public void Update(TimerSnapshot snapshot)
    {
        Name = snapshot.Name;
        Remaining = DurationFormat.Clock(snapshot.Remaining);
        IsRunning = snapshot.IsRunning;
        IsOverdue = snapshot.Status == TimerStatus.Overdue;
        IsUrgent = IsRunning && snapshot.Remaining <= TimeSpan.FromSeconds(10);
        Status = snapshot.Status switch
        {
            TimerStatus.Stopped => "정지",
            TimerStatus.Overdue => $"+{DurationFormat.Clock(snapshot.OverdueFor)} 초과 · 리마인드 {snapshot.ReminderCount}회",
            _ => "진행 중",
        };
    }
}
