# MapleDom

메이플스토리용 초경량 타이머/알림 도구. **320×240 WPF 타이머 창**, 타이머 엔진과 Windows 토스트 알림, 검증용 CLI 및 단위 테스트가 들어 있다.
UI 사용법과 검증 범위는 [`docs/UI_NOTES.md`](docs/UI_NOTES.md)를 참고한다. 창 감시 기능은 범위 밖이다.

## 타이머 3종

| 타이머 | 종류 | 기본값 | 동작 |
|---|---|---|---|
| 동꼽방지 | 반복형 | 58초 + 휴식 2초 | 58초 만기 → 알림 → 2초 쉬고 → 다시 58초. 확인 버튼 없음. 주기 정확히 60초. |
| 룬 | 확인형 | 15분, 리마인드 1분 | 15분 만기 → 알림 → **[룬 깟음]** 전까지 1분마다 재알림. 누르면 15분 리셋. |
| 도핑 | 확인형 | 30분 − 5초, 리마인드 1분 | **[도핑했음]** 누르면 29분 55초 뒤 알림(버튼 누르는 시간 보정). 안 누르면 1분마다 재알림. |

- 모든 값은 설정 파일(`%LocalAppData%\MapleDom\settings.json`)에서 바꿀 수 있다.
- 시작 버튼은 확인 버튼과 같은 의미다(시작 시점 = 룬을 깠거나 도핑한 시점).
- 알림은 Windows 시스템 알림(토스트)으로 온다. 여러 알림이 동시에 만기되면 **최소 2초 간격으로 하나씩** 발동되어 겹치지 않는다.

## 정확도 설계

- 시각은 `Stopwatch`(QueryPerformanceCounter) 기반 **단조 시계**만 사용한다. 시스템 시계 변경/NTP 보정의 영향이 없다.
- 다음 만기는 "실제 발동 시각"이 아니라 **"예정 시각"에 체인**된다. 1440사이클(24시간) 시뮬레이션에서 누적 오차 0을 테스트로 보증한다.
- 전용 스케줄러 스레드가 만기 32ms 전까지 커널 대기, 이후 스핀 대기로 만기 시각을 맞춘다. 실측 지연 0.00ms(데모 로그 기준).
- 절전 복귀 등으로 한 주기 이상 밀리면 현재 시각으로 재정렬해 밀린 알람이 폭주하지 않는다.

## 구조

```
MapleDom.slnx
src/
  MapleDom.App/         한국어 WPF UI, 설정, 트레이, 전역 단축키, 창 순서 탭 (외부 패키지 추가 없음)
  MapleDom.Core/        타이머 엔진, 설정, 알림 큐 (UI/Windows 의존 없음, net10.0)
    Time/Clock.cs         IClock, MonotonicClock
    Timers/               RepeatTimer(동꼽방지), AckTimer(룬/도핑), TimerSnapshot, AlarmEvent
    Engine/TimerEngine.cs 스케줄러 스레드
    Notifications/        IAlarmNotifier, NotificationDispatcher(직렬화 큐)
    Settings/             AppSettings(JSON), SettingsStore
    WindowOrder/          창 순서 항목 ↔ 실행 중인 창 매칭(순수 로직)
    Text/                 DurationFormat("14:59", "29분 55초"), AlarmTemplate
    TimerService.cs       UI가 쓰는 단일 진입점
  MapleDom.Windows/     WindowsToastNotifier(토스트), WindowArranger/ForegroundWatcher/WindowOrderController(창 순서, Win32)
  MapleDom.Cli/         콘솔 검증 도구
tests/
  MapleDom.Core.Tests/  xunit 89개
docs/
  UI_PROMPT.md          Codex에게 줄 UI 구현 프롬프트
  UI_NOTES.md           UI 사용법, 구현 결정, 검증 결과
tools/
  MapleDom.App.Smoke/   UI 상태·저장·스레드·Win32 검증 및 화면 PNG 생성
```

## 빌드 / 테스트 / 실행

요구: .NET 10 SDK (Windows 10 1809 이상).

```
dotnet build MapleDom.slnx -c Release
dotnet test  MapleDom.slnx -c Release
```

WPF 앱:

```
dotnet run --project src/MapleDom.App -c Release
```

빌드된 실행 파일: `src/MapleDom.App/bin/Release/net10.0-windows10.0.17763.0/MapleDom.exe`.
배포할 때는 실행 파일이 있는 폴더 전체를 함께 복사한다.

단일 exe(설치 불필요, .NET 런타임 내장):

```
publish.cmd
```

`dist\MapleDom.exe`(약 170MB, 런타임 포함) 하나만 다른 PC에 복사해서 실행하면 된다(Windows 10 1809 이상, x64). 단일 파일 압축과 ReadyToRun은 일부러 끈 상태다: 압축을 켜면 크기는 88MB로 줄지만 실행 중 메모리가 약 120MB 더 들고, ReadyToRun은 크기만 늘리고 시작이 오히려 느렸다. 첫 실행 때 `%TEMP%\.net\MapleDom` 에 네이티브 라이브러리를 풀어 두므로 첫 시작만 1~2초 느릴 수 있다. 설정 파일 위치는 같다(`%LocalAppData%\MapleDom`).
기본적으로 X를 누르면 트레이에 숨겨지고, 트레이 메뉴의 **종료**로 앱을 끝낸다.
설정의 **알림 · 창** 탭에서 투명도, 항상 위, 트레이 숨기기, 전역 단축키를 바꿀 수 있고, **창 순서** 탭에서 창 Z-order 고정을 설정한다.

UI 추가 검증(앱을 종료한 상태에서 실행):

```
dotnet run --project tools/MapleDom.App.Smoke -c Release -- artifacts/ui-smoke
```

CLI:

```
dotnet run --project src/MapleDom.Cli -c Release -- --selftest     # 토스트 1건 발송 테스트
dotnet run --project src/MapleDom.Cli -c Release -- --demo         # 축소 간격(동꼽 6s, 룬 12s, 도핑 15s)으로 대화형 실행
dotnet run --project src/MapleDom.Cli -c Release                   # 실제 설정으로 대화형 실행
dotnet run --project src/MapleDom.Cli -c Release -- --run 60 --silent   # 60초 비대화형, 토스트 없이 로그만
```

대화형 키: `1/2/3` 각 타이머 토글, `r` 룬 깟음, `d` 도핑했음, `a` 전체 시작, `s` 전체 정지, `m` 음소거, `t` 테스트 알림, `q` 종료.

## 창 순서 고정 (Z-order)

설정 > **창 순서** 탭. 실행 중인 창을 골라 위(앞)→아래(뒤) 순서를 정해 두면 그 순서로 창을 쌓는다.
키 입력을 받는 창(활성 창)은 건드리지 않으므로, 예를 들어 게임 창을 조작하면서도 다른 창을 그 앞에 둘 수 있다.

- **지금 정렬** 버튼 또는 전역 단축키 **Ctrl+Alt+Z**: 목록 순서로 정렬(그룹을 맨 앞으로).
- **Ctrl+Alt+X**: 지금 활성인 창을 맨 앞으로 올린다(잠깐 보기). 다음에 목록의 창을 활성화하면 다시 원래 순서로.
- **창 순서 고정 사용**: 목록에 있는 창이 활성화될 때마다 0.15초 뒤 자동으로 순서를 되돌린다. 목록에 없는 창(브라우저 등)을 활성화할 때는 아무것도 하지 않는다.
- **항상 위 층에 배치**: 목록의 창을 모두 '항상 위'로 만들어 그 층 안에서 정렬한다. 끄면 각 창이 있던 층(일반/항상 위) 안에서만 순서가 바뀐다.
- 항목은 프로세스 이름(exe 이름) + 제목 포함 문자열로 창을 찾는다. 제목이 자주 바뀌는 창(브라우저 탭 등)은 제목 칸을 비우거나 짧게 줄인다.
- 전체화면(독점) 게임 창은 Z-order를 무시한다. 창 모드/테두리 없는 창 모드에서 쓴다.
- 구현: `MapleDom.Core.WindowOrder`(매칭, 순수 로직) + `MapleDom.Windows.WindowArranger`(`SetWindowPos`, `SWP_NOACTIVATE`) + `WindowOrderController`(`SetWinEventHook` 포그라운드 감시).

## 앱 아이콘

`src/MapleDom.App/Assets/app.ico` (16~256px). exe 아이콘, 작업표시줄, 트레이, 토스트 로고에 쓰인다.
다시 만들려면:

```
powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
```

미리보기는 `artifacts/icon/preview.png` 에 생성된다.

## 설정 파일

`%LocalAppData%\MapleDom\settings.json` (없으면 기본값으로 동작). 값이 비거나 0 이하면 기본값으로 보정된다.

```json
{
  "Dongkkop": { "Name": "동꼽방지", "IntervalSeconds": 58, "GapSeconds": 2, "AutoStart": false,
    "Notification": { "Title": "동꼽방지", "MainMessage": "움직이세요! ({interval} 경과)",
      "Sound": "Default", "Scenario": "Default", "ExpireSeconds": 10 } },
  "Rune":   { "Name": "룬",  "IntervalSeconds": 900,  "LeadSeconds": 0, "ReminderSeconds": 60, "AutoStart": false,
    "Notification": { "Title": "룬 타이머", "MainMessage": "룬 생성! 룬을 까고 [룬 깟음]을 누르세요.",
      "ReminderMessage": "룬 아직 안 깠어요! (+{overdue} 경과)", "Sound": "Reminder", "Scenario": "Reminder" } },
  "Doping": { "Name": "도핑", "IntervalSeconds": 1800, "LeadSeconds": 5, "ReminderSeconds": 60, "AutoStart": false },
  "Notifications": { "Enabled": true, "MinSpacingSeconds": 2, "MaxQueueLength": 16 }
}
```

- 문구 토큰: `{name}` 타이머 이름, `{interval}` 설정 간격, `{n}` 리마인드 회차, `{overdue}` 본 알림 이후 경과.
- `Sound`: Default, IM, Mail, Reminder, SMS, Alarm, Alarm2~Alarm5, Call, Silent (Windows 알림 사운드).
- `Scenario`: Default(자동으로 사라짐), Reminder(닫을 때까지 유지), Alarm(알람 취급, 집중 지원 "알람만" 모드도 통과).

## UI에서 쓰는 API 요약

```csharp
var settings = SettingsStore.LoadOrDefault(null, out var error);
using var service = new TimerService(settings, new WindowsToastNotifier());

service.StartAll();                             // 전체 시작
service.Acknowledge(TimerService.RuneId);       // 룬 깟음
service.Acknowledge(TimerService.DopingId);     // 도핑했음
service.Toggle(TimerService.DongkkopId);        // 시작/정지 토글
service.Muted = true;                           // 알림만 끔

foreach (var t in service.Snapshot())           // 100ms 폴링해서 표시
    Console.WriteLine($"{t.Name} {DurationFormat.Clock(t.Remaining)} {t.Status}");

service.TimerChanged += snap => { /* 백그라운드 스레드! Dispatcher로 마샬링 */ };
service.AlarmFired   += evt  => { /* 백그라운드 스레드! */ };

var s = service.Settings;                       // 복사본
s.Rune.IntervalSeconds = 600;
service.ApplySettings(s);                       // 실행 중 즉시 반영 (유효하지 않으면 ArgumentException)
SettingsStore.Save(s);
```

자세한 내용은 `docs/UI_PROMPT.md`.

## 주의사항

- **집중 지원(방해 금지)**: Windows 기본 규칙 "게임을 플레이할 때: 알람만"이 켜져 있으면 일반 토스트가 숨겨진다. 설정 > 시스템 > 집중 지원에서 끄거나, 알림 `Scenario` 를 `Alarm` 으로 바꾼다.
- 앱별 알림이 꺼져 있으면 토스트가 오지 않는다. `--selftest` 로 확인할 수 있다.
- 토스트 등록은 exe 경로 기준이라 exe 위치를 옮기면 알림 센터에 새 앱 항목이 생긴다(동작에는 문제 없음).

## 가정 (확인 필요)

- 요청의 "기본적으로 5초"는 "58초"의 오타로 보고 동꼽방지 기본값을 58초로 두었다.
- 동꼽방지의 "2~3초 휴식"은 기본 2초로 두었다(`GapSeconds`).
- 도핑 타이머는 시작 버튼을 눌러도 29분 55초로 예약된다(시작 = 도핑한 시점으로 간주).
