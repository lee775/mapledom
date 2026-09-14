# MapleDom UI 구현 요청 (Codex용 프롬프트)

아래 내용을 그대로 Codex에 전달하면 된다. 저장소 루트는 `C:\WOOK\mapledom`.

---

## 0. 역할과 범위

- 너는 **`src/MapleDom.App`** (WPF, .NET 10) 프로젝트를 새로 만들어 **UI만** 구현한다.
- 타이머/알림 로직은 `src/MapleDom.Core`, `src/MapleDom.Windows` 에 이미 완성되어 있고 `tests/MapleDom.Core.Tests` (xunit 80개)가 통과한다.
  **Core / Windows / Cli / Tests 프로젝트의 코드는 수정하지 않는다.** 꼭 필요한 API가 없으면 `docs/UI_NOTES.md` 에 요청 사항을 적고, UI 쪽에서 우회한다.
- "특정 창 감시" 기능은 이번 범위가 아니다. 그 자리(메뉴, 버튼, 설정)도 만들지 않는다.
- 완료 후 `dotnet build MapleDom.slnx -c Release` 경고 0, `dotnet test MapleDom.slnx -c Release` 전부 통과여야 한다.

## 1. 제품 목표

메이플스토리 플레이 중 화면 구석에 띄워두는 **초경량 타이머 창**.
게임에 포커스가 가 있어도 알림(Windows 토스트)은 Core가 알아서 보내므로, UI는 **상태 표시 + 버튼 + 설정** 만 담당한다.
사용자가 게임 도중 눈으로 남은 시간을 확인하고, 룬을 깐 뒤 / 도핑한 뒤 버튼 하나만 누르면 된다.

## 2. 기술 제약

- WPF, `TargetFramework` = `net10.0-windows10.0.17763.0`, `<UseWPF>true</UseWPF>`, `<SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>` (루트의 `Directory.Build.props` 가 일부를 이미 설정한다).
- `<OutputType>WinExe</OutputType>`, `<AssemblyName>MapleDom</AssemblyName>`, `<Product>MapleDom</Product>` (토스트에 표시되는 앱 이름이 여기서 나온다).
- **외부 NuGet 추가 금지.** MVVM 프레임워크 없이 `INotifyPropertyChanged` 직접 구현. 트레이 아이콘은 `System.Windows.Forms.NotifyIcon` (`<UseWindowsForms>true</UseWindowsForms>`) 사용 가능.
- 가볍게: 애니메이션/그라데이션 최소, 이미지 리소스 없음(아이콘은 `.ico` 1개만, 없으면 코드로 그린 단순 아이콘), 시작 시간 짧게, 100ms 폴링 외 백그라운드 작업 없음.
- 프로젝트 참조: `..\MapleDom.Core\MapleDom.Core.csproj`, `..\MapleDom.Windows\MapleDom.Windows.csproj`. `MapleDom.slnx` 에 추가 (`dotnet sln MapleDom.slnx add src/MapleDom.App/MapleDom.App.csproj`).
- 한국어 UI. 글꼴은 시스템 기본(맑은 고딕).

## 3. Core 공개 API (그대로 사용)

### 3.1 진입점 `MapleDom.Core.TimerService`

```csharp
public sealed class TimerService : IDisposable
{
    public const string DongkkopId = "dongkkop";   // 동꼽방지 (반복형)
    public const string RuneId     = "rune";       // 룬 (확인형)
    public const string DopingId   = "doping";     // 도핑 (확인형)
    public static readonly IReadOnlyList<string> TimerIds;

    public TimerService(AppSettings settings, IAlarmNotifier notifier, IClock? clock = null);

    public TimeSpan Now { get; }                     // 단조 시계. 스냅샷의 NextDue 와 비교해 부드러운 표시 가능
    public AppSettings Settings { get; }             // 복사본. 수정 후 ApplySettings 로 반영
    public bool Muted { get; set; }                  // true = 알림만 끔, 타이머는 계속 돎

    public IReadOnlyList<TimerSnapshot> Snapshot();  // 3개 타이머 상태 (순서: dongkkop, rune, doping)
    public TimerSnapshot Get(string id);
    public TimerSnapshot Start(string id);
    public TimerSnapshot Stop(string id);
    public TimerSnapshot Toggle(string id);
    public TimerSnapshot Acknowledge(string id);     // "룬 깟음" / "도핑했음". 정지 상태면 시작과 동일
    public void StartAll();
    public void StopAll();

    public void ApplySettings(AppSettings settings); // 실행 중 즉시 반영. 유효하지 않으면 ArgumentException
    public AlarmNotification SendTest(string id, AlarmKind kind = AlarmKind.Main); // 테스트 알림 즉시 발송(Muted 무시)
    public AlarmNotification BuildNotification(AlarmEvent evt);                      // 미리보기용

    public event Action<AlarmEvent>? AlarmFired;                  // 알람 발동
    public event Action<TimerSnapshot>? TimerChanged;             // 시작/정지/확인/설정/알람 후 상태 변경
    public event Action<AlarmNotification>? NotificationQueued;   // 알림이 발송 큐에 들어감(앱 내 로그용)
    public event Action<AlarmNotification, Exception>? NotificationFailed; // 토스트 실패(알림 꺼짐 등)
    public event Action<Exception>? HandlerFailed;                // 이벤트 핸들러가 예외를 던짐
}
```

### 3.2 `MapleDom.Core.Timers.TimerSnapshot` (불변 record)

```csharp
string Id; string Name;
TimerKind Kind;            // Repeat(동꼽방지) | Acknowledge(룬/도핑)
TimerStatus Status;        // Stopped | Counting | Overdue(본 알림 후 미확인, 리마인드 반복 중)
TimeSpan Remaining;        // 다음 만기까지 (Overdue 면 다음 리마인드까지). 정지면 0
TimeSpan? NextDue;         // 단조 시계 기준 만기 시각
TimeSpan Interval, Gap, Lead, ReminderInterval;
TimeSpan EffectiveInterval;// Interval - Lead (도핑: 29분 55초)
int ReminderCount;         // 본 알림 후 울린 리마인드 횟수
TimeSpan OverdueFor;       // 본 알림 이후 경과 (Overdue 아닐 때 0)
int FireCount; TimeSpan? LastFiredAt; TimeSpan CapturedAt;
bool IsRunning;            // Status != Stopped
```

### 3.3 설정 `MapleDom.Core.Settings`

```csharp
AppSettings settings = SettingsStore.LoadOrDefault(null, out Exception? error); // 기본 경로 %LocalAppData%\MapleDom\settings.json
SettingsStore.Save(settings);                          // 원자적 저장
IReadOnlyList<string> errors = settings.Validate();    // 비어 있으면 유효. 저장 전에 사용자에게 보여줄 것
AppSettings copy = settings.Clone();

// AppSettings 구조 (모든 시간은 초 단위 double)
settings.Dongkkop : RepeatTimerSettings { Name, IntervalSeconds(58), GapSeconds(2), AutoStart, Notification }
settings.Rune     : AckTimerSettings    { Name, IntervalSeconds(900), LeadSeconds(0), ReminderSeconds(60), AutoStart, Notification }
settings.Doping   : AckTimerSettings    { Name, IntervalSeconds(1800), LeadSeconds(5), ReminderSeconds(60), AutoStart, Notification }
settings.Notifications : { Enabled, MinSpacingSeconds(2), MaxQueueLength(16) }
NotificationProfile : { Title, MainMessage, ReminderMessage, Sound(AlarmSound), ReminderSound(AlarmSound), Scenario(AlarmScenario), ExpireSeconds }
// 문구 토큰: {name} {interval} {n} {overdue}
// enum AlarmSound { Default, IM, Mail, Reminder, SMS, Alarm, Alarm2, Alarm3, Alarm4, Alarm5, Call, Silent }
// enum AlarmScenario { Default, Reminder, Alarm }
```

### 3.4 표시 도우미 `MapleDom.Core.Text.DurationFormat`

```csharp
DurationFormat.Clock(TimeSpan)   // "14:59", "1:02:03" (올림: 0.4초 → "0:01")
DurationFormat.Korean(TimeSpan)  // "15분", "29분 55초", "58초"
```

### 3.5 알림 백엔드 `MapleDom.Windows.WindowsToastNotifier`

```csharp
IAlarmNotifier notifier = new WindowsToastNotifier();   // Windows 토스트
WindowsToastNotifier.ClearHistory();                    // 종료 시 알림 센터 정리(선택)
```

### 3.6 스레드 규칙 (중요)

- `TimerService` 의 **모든 이벤트는 백그라운드 스레드**에서 온다. 핸들러 안에서는 반드시 `Dispatcher.BeginInvoke` 로 UI 스레드로 넘긴다. 핸들러가 예외를 던지면 `HandlerFailed` 로 전달되고 엔진은 계속 돌지만, 절대 던지지 말 것.
- `Snapshot()`, `Get()`, `Start()` 등 메서드는 어느 스레드에서 호출해도 안전하다(내부 락). UI 스레드에서 호출하면 된다.
- 표시 갱신은 **`DispatcherTimer` 100ms 폴링** 으로 `Snapshot()` 을 읽는 방식이 기본. `TimerChanged` 는 버튼 클릭 직후 즉시 반영용으로만 쓴다(폴링만으로도 충분하면 생략 가능).

## 4. 화면 요구사항

### 4.1 메인 창 `MainWindow`

- 작은 창(대략 320×240 이하), 리사이즈 불가. **항상 위** 토글(기본 켬). 제목표시줄 없이 창 아무 곳이나 드래그로 이동(`DragMove`). 닫기(X)는 우상단 작은 버튼.
- 반투명 배경(선택, 설정 가능). 게임 위에 떠 있어도 방해되지 않도록 어두운 테마.
- **타이머 카드 3개** (위에서부터 동꼽방지, 룬, 도핑):
  - 이름(`Name`), 큰 숫자로 남은 시간 `DurationFormat.Clock(Remaining)`.
  - 상태 표시: `Stopped` → 회색 "정지", `Counting` → 기본색, `Overdue` → **빨간 배경/글씨** 로 `+{Clock(OverdueFor)} 초과 · 리마인드 {ReminderCount}회`.
  - 남은 시간 10초 이하이면 숫자를 주황색으로.
  - 반복형(동꼽방지): `[시작]`/`[정지]` 토글 버튼 1개.
  - 확인형(룬/도핑): `[시작]`/`[정지]` 토글 + **`[룬 깟음]`** / **`[도핑했음]`** 버튼(`Acknowledge`). 이 버튼은 크고 누르기 쉽게. Overdue 상태에서는 강조(빨강/깜빡임 없이 색만).
- **하단 바**: `[전체 시작]` `[전체 정지]` `[음소거]`(토글, 켜지면 아이콘 변경) `[설정]` `[테스트 알림]`(룬 알림 1건 발송).
- 음소거 상태와 항상 위 상태는 트레이 메뉴에서도 토글 가능.

### 4.2 설정 창 `SettingsWindow` (모달)

- 탭 또는 그룹 3개(동꼽방지 / 룬 / 도핑) + "알림" 그룹.
  - 공통: 이름, 간격(초 또는 분:초 입력, 내부는 초), 자동 시작.
  - 동꼽방지: 휴식 시간(초).
  - 룬/도핑: 선행 시간(초, "버튼 누르는 시간 보정"), 리마인드 간격(초).
  - 알림 프로필: 제목, 본문(메인), 본문(리마인드, 룬/도핑만), 사운드(`AlarmSound` 콤보), 표시 방식(`AlarmScenario` 콤보: 일반/유지/알람), 유지 시간(초). 토큰 설명 툴팁(`{name} {interval} {n} {overdue}`).
  - 알림 그룹: 알림 켬/끔, 최소 간격(초).
- `[저장]`: `settings.Validate()` 오류가 있으면 목록을 보여주고 저장하지 않는다. 없으면 `service.ApplySettings(settings)` → `SettingsStore.Save(settings)`.
- `[기본값 복원]`: `AppSettings.CreateDefault()` 로 채운 뒤 저장은 사용자가 누르게.
- `[테스트 알림]` 버튼: 현재 편집 중인 값으로 미리 듣고 싶을 때 → 임시로 `ApplySettings` 후 `SendTest`, 취소 시 원래 설정으로 되돌린다. (복잡하면 저장된 설정 기준 `SendTest` 만.)

### 4.3 트레이

- 창 닫기(X) → 트레이로 최소화(UI 설정 `MinimizeToTray`, 기본 켬). 트레이 아이콘 더블클릭 → 창 복원.
- 트레이 우클릭 메뉴: 열기 / 전체 시작 / 전체 정지 / 룬 깟음 / 도핑했음 / 음소거 / 항상 위 / 종료.

### 4.4 앱 수명주기 `App.xaml.cs`

```csharp
// 시작
var settings = SettingsStore.LoadOrDefault(null, out var error);   // error 가 있으면 첫 화면에 한 줄 경고
var service  = new TimerService(settings, new WindowsToastNotifier());
// 종료 (창 닫기/트레이 종료/세션 종료)
uiSettings.Save(); service.Dispose();
```

- 단일 인스턴스(`Mutex`): 두 번째 실행 시 기존 창을 앞으로.
- UI 전용 설정(창 위치, 항상 위, 투명도, 트레이 최소화 여부)은 **`AppSettings` 에 넣지 말고** 별도 파일 `%LocalAppData%\MapleDom\ui.json` 에 저장한다(단순 JSON, `System.Text.Json`).
- 미처리 예외: `DispatcherUnhandledException` 에서 메시지박스 + `%LocalAppData%\MapleDom\error.log` 기록 후 종료.
- `service.NotificationFailed` 가 오면 메인 창 하단에 작은 경고 줄("Windows 알림이 꺼져 있거나 집중 지원 중입니다") 표시.

## 5. 선택 기능 (기본 요구가 끝난 뒤, 시간이 남으면)

- **전역 단축키** (Win32 `RegisterHotKey`, 게임에 포커스가 있어도 동작해야 하므로 WPF `KeyBinding` 은 불가): `Ctrl+Alt+R` 룬 깟음, `Ctrl+Alt+D` 도핑했음, `Ctrl+Alt+S` 전체 시작/정지 토글. 설정 창에서 켜고 끌 수 있게.
- 창 투명도 슬라이더(0.4~1.0).
- 최근 알림 5개 로그(`NotificationQueued` 구독) 를 설정 창 하단에.

## 6. 완료 조건 체크리스트

- [ ] `dotnet build MapleDom.slnx -c Release` 경고 0, 오류 0. `dotnet test` 전부 통과(기존 80개 + 추가분).
- [ ] 앱 실행 → `[전체 시작]` → 3개 카운트다운이 100ms 단위로 부드럽게 감소.
- [ ] `[룬 깟음]` 누르면 룬이 15:00 으로 리셋, `[도핑했음]` 누르면 도핑이 29:55 로 리셋.
- [ ] 룬을 Overdue 상태로 만들면(설정에서 간격 10초로 줄여 확인) 카드가 빨갛게 되고 리마인드 횟수가 증가.
- [ ] 설정 저장 후 앱 재시작 시 값 유지. 잘못된 값(선행 ≥ 간격)은 저장 거부되고 이유가 표시됨.
- [ ] 트레이 최소화/복원/종료 동작. 종료 시 프로세스가 남지 않음(`service.Dispose()` 호출 확인).
- [ ] 알림 도착 시 UI 스레드 예외 없음(이벤트는 백그라운드 스레드에서 옴).
- [ ] 작업 관리자 기준 유휴 CPU 0~0.1%, 메모리 60MB 이하.

## 7. 참고

- 동작 미리 보기: `dotnet run --project src/MapleDom.Cli -c Release -- --demo` (동꼽 6초/룬 12초/도핑 15초로 축소).
- 토스트 확인: `dotnet run --project src/MapleDom.Cli -c Release -- --selftest`.
- 설계/설정 상세: `README.md`.
