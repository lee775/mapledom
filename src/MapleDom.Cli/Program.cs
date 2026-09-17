using System.Text;
using MapleDom.Core;
using MapleDom.Core.Notifications;
using MapleDom.Core.Settings;
using MapleDom.Core.Text;
using MapleDom.Core.Timers;
using MapleDom.Windows;

namespace MapleDom.Cli;

/// <summary>
/// UI 없이 타이머 엔진과 Windows 토스트를 검증하는 콘솔 도구.
///   --selftest        토스트 1건 발송 후 알림 센터에 남았는지 확인하고 종료
///   --demo            축소된 간격(1분 6s, 룬 12s/리마인드 5s, 도핑 15s-2s/리마인드 5s)으로 실행
///   --run N           키 입력 없이 N초 동안 전체 시작 상태로 실행 후 종료
///   --silent          토스트 대신 콘솔 출력만
///   --settings PATH   설정 파일 경로 지정
/// </summary>
internal static class Program
{
    private static readonly object ConsoleLock = new();
    private static int _statusWidth;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var opts = CliOptions.Parse(args);
        if (opts.Help)
        {
            PrintUsage();
            return 0;
        }

        if (opts.SelfTest) return SelfTest();

        var settings = SettingsStore.LoadOrDefault(opts.SettingsPath, out var loadError);
        if (loadError is not null)
            Console.WriteLine($"[설정] 읽기 실패, 기본값 사용: {loadError.Message}");
        if (opts.Demo) ApplyDemoScale(settings);

        IAlarmNotifier backend = opts.Silent ? NullNotifier.Instance : new WindowsToastNotifier();
        using var telegram = new TelegramNotifier();
        telegram.Update(settings.Telegram);
        telegram.SendFailed += (n, ex) => Log($"[텔레그램 실패] {n.Title}: {ex.Message}");
        var notifier = new CompositeNotifier(
            new CallbackNotifier(n => Log($"[알림] {n.Title}: {n.Message}")),
            backend,
            telegram);

        using var service = new TimerService(settings, notifier);
        service.AlarmFired += e =>
            Log($"[알람] {e.TimerName} {e.Kind}#{e.ReminderIndex} 지연 {e.Lateness.TotalMilliseconds:F2}ms");
        service.NotificationFailed += (n, ex) => Log($"[알림 실패] {n.Title}: {ex.Message}");
        service.HandlerFailed += ex => Log($"[핸들러 오류] {ex}");

        Console.WriteLine($"MapleDom CLI  설정: {opts.SettingsPath ?? SettingsStore.DefaultPath}{(opts.Demo ? "  (데모 축소 간격)" : "")}");

        if (opts.RunSeconds is not null || Console.IsInputRedirected)
        {
            service.StartAll();
            RunHeadless(service, opts.RunSeconds ?? 30);
            return 0;
        }

        RunInteractive(service);
        return 0;
    }

    private static void RunHeadless(TimerService service, int seconds)
    {
        var deadline = service.Now + TimeSpan.FromSeconds(seconds);
        while (service.Now < deadline)
        {
            Log("[상태] " + string.Join("  |  ", service.Snapshot().Select(Format)));
            Thread.Sleep(1000);
        }
    }

    private static void RunInteractive(TimerService service)
    {
        Console.WriteLine("키: [1] 1분 토글  [2] 룬 토글  [3] 도핑 토글  [r] 룬 깟음  [d] 도핑했음  [a] 전체 시작  [s] 전체 정지  [m] 음소거  [t] 테스트 알림  [q] 종료");
        while (true)
        {
            Status(string.Join("  |  ", service.Snapshot().Select(Format)) + (service.Muted ? "  [음소거]" : ""));

            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true).KeyChar;
                switch (char.ToLowerInvariant(key))
                {
                    case '1': service.Toggle(TimerService.MinuteId); break;
                    case '2': service.Toggle(TimerService.RuneId); break;
                    case '3': service.Toggle(TimerService.DopingId); break;
                    case 'r': service.Acknowledge(TimerService.RuneId); Log("[입력] 룬 깟음"); break;
                    case 'd': service.Acknowledge(TimerService.DopingId); Log("[입력] 도핑했음"); break;
                    case 'a': service.StartAll(); Log("[입력] 전체 시작"); break;
                    case 's': service.StopAll(); Log("[입력] 전체 정지"); break;
                    case 'm': service.Muted = !service.Muted; break;
                    case 't': service.SendTest(TimerService.RuneId); Log("[입력] 테스트 알림"); break;
                    case 'q': Log("종료"); return;
                }
            }

            Thread.Sleep(100);
        }
    }

    private static int SelfTest()
    {
        Console.WriteLine("Windows 토스트 자체 테스트: 알림 1건을 보냅니다...");
        try
        {
            var notifier = new WindowsToastNotifier();
            notifier.Notify(new AlarmNotification(
                TimerId: "selftest",
                Tag: "selftest",
                Kind: AlarmKind.Main,
                Title: "MapleDom 테스트",
                Message: "이 알림이 보이면 정상입니다.",
                Sound: AlarmSound.Default,
                Scenario: AlarmScenario.Default,
                ExpireAfter: TimeSpan.FromSeconds(30)));

            Thread.Sleep(1500);
            var count = WindowsToastNotifier.HistoryCount();
            Console.WriteLine($"알림 센터에 남은 MapleDom 토스트: {count}건");
            if (count == 0)
                Console.WriteLine("토스트가 기록되지 않았습니다. Windows 설정 > 시스템 > 알림, 집중 지원(방해 금지) 상태를 확인하세요.");
            return count > 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"실패: {ex}");
            return 2;
        }
    }

    private static void ApplyDemoScale(AppSettings s)
    {
        s.Minute.IntervalSeconds = 6;
        s.Minute.GapSeconds = 2;
        s.Rune.IntervalSeconds = 12;
        s.Rune.LeadSeconds = 0;
        s.Rune.ReminderSeconds = 5;
        s.Doping.IntervalSeconds = 15;
        s.Doping.LeadSeconds = 2;
        s.Doping.ReminderSeconds = 5;
        s.Notifications.MinSpacingSeconds = 2;
    }

    private static string Format(TimerSnapshot s) => s.Status switch
    {
        TimerStatus.Stopped => $"{s.Name} [정지]",
        TimerStatus.Counting => $"{s.Name} {DurationFormat.Clock(s.Remaining)}",
        TimerStatus.Overdue => $"{s.Name} +{DurationFormat.Clock(s.OverdueFor)} 초과(리마인드 {s.ReminderCount}회, 다음 {DurationFormat.Clock(s.Remaining)})",
        _ => s.Name,
    };

    private static void Status(string text)
    {
        lock (ConsoleLock)
        {
            if (text.Length > _statusWidth) _statusWidth = text.Length;
            Console.Write('\r' + text.PadRight(_statusWidth));
        }
    }

    private static void Log(string line)
    {
        lock (ConsoleLock)
        {
            if (_statusWidth > 0) Console.Write('\r' + new string(' ', _statusWidth) + '\r');
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("MapleDom.Cli [--selftest] [--demo] [--run N] [--silent] [--settings PATH]");
        Console.WriteLine("  --selftest      토스트 알림 1건 발송 테스트");
        Console.WriteLine("  --demo          축소된 간격으로 실행(동작 확인용)");
        Console.WriteLine("  --run N         N초 동안 전체 시작 상태로 실행 후 종료(비대화형)");
        Console.WriteLine("  --silent        토스트 대신 콘솔 출력만");
        Console.WriteLine("  --settings PATH 설정 파일 경로");
    }
}

internal sealed class CliOptions
{
    public bool Help { get; private set; }
    public bool SelfTest { get; private set; }
    public bool Demo { get; private set; }
    public bool Silent { get; private set; }
    public int? RunSeconds { get; private set; }
    public string? SettingsPath { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h" or "/?": o.Help = true; break;
                case "--selftest": o.SelfTest = true; break;
                case "--demo": o.Demo = true; break;
                case "--silent": o.Silent = true; break;
                case "--run" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n): o.RunSeconds = n; i++; break;
                case "--settings" when i + 1 < args.Length: o.SettingsPath = args[++i]; break;
                default: Console.WriteLine($"알 수 없는 옵션: {args[i]}"); o.Help = true; break;
            }
        }
        return o;
    }
}
