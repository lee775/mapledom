# MapleDom

메이플스토리용 초경량 타이머/창 관리 도구. **320×300 WPF 창**에 **창 순서(Z-order) 적용/해제** 카드와 30초·룬·도핑 타이머 카드가 있고, 알림은 Windows 토스트·화면 배너·텔레그램으로 온다.
UI 사용법과 검증 범위는 [`docs/UI_NOTES.md`](docs/UI_NOTES.md)를 참고한다.

## 타이머 3종

| 타이머 | 종류 | 기본값 | 동작 |
|---|---|---|---|
| 30초 | 반복형 | 28초 + 휴식 2초 | 28초 카운트 → 알림 → 2초 쉬고 다시 28초. 알림 주기는 정확히 30초. 확인 버튼 없이 계속 반복. |
| 룬 | 확인형 | 15분, 리마인드 1분 | 15분 만기 → 알림 → **[룬 깟음]** 전까지 1분마다 재알림. 누르면 15분 리셋. |
| 도핑 | 확인형 | 30분 − 5초, 리마인드 1분 | **[도핑했음]** 누르면 29분 55초 뒤 알림(버튼 누르는 시간 보정). 안 누르면 1분마다 재알림. |

- 모든 값은 설정 파일(`%LocalAppData%\MapleDom\settings.json`)에서 바꿀 수 있다.
- 시작 버튼은 확인 버튼과 같은 의미다(시작 시점 = 룬을 깠거나 도핑한 시점).
- 알림은 Windows 시스템 알림(토스트)으로 온다. 여러 알림이 동시에 만기되면 **최소 2초 간격으로 하나씩** 발동되어 겹치지 않는다.
- 설정 > **텔레그램** 탭을 켜면 같은 알림이 텔레그램 봇 메시지로도 간다(아래 참고).

## 창 순서 적용 / 해제 (Z-order)

게임 창을 조작하면서도 다른 창들을 그 앞에 정해진 순서로 두고 싶을 때 쓴다. 키 입력을 받는 창(활성 창)은 절대 바꾸지 않는다.

1. 메인 창의 **맨 뒤 창** 콤보에서 항상 맨 뒤(바닥)에 둘 창을 고른다. 그 앞에 여러 창을 순서대로 두고 싶으면 설정 > **창 순서** 탭에서 창을 추가하고 ▲▼로 정한다. 위가 앞, **마지막 줄이 맨 뒤(바닥)** 다. 창 목록의 번호는 지금의 쌓임 순서(1 = 맨 앞)다. **창 목록은 저장하지 않는다**: 앱을 켜면 항상 비어 있고 매번 다시 고른다('항상 위 층' 옵션만 저장).
2. 메인 창의 **창 순서 카드**에서 **[적용]**: 정한 순서로 정렬하고, 이후 목록의 창을 클릭하거나 Alt+Tab으로 활성화해도 잠시 뒤 다시 그 순서로 돌아간다(활성 상태는 유지).
3. **[해제]**: 감시를 멈추고 지금 활성인 창을 앞으로 올린다. 아무것도 하지 않았을 때처럼 Windows 기본대로 최근에 쓴 창이 앞에 온다.

- 전역 단축키 **Ctrl+Alt+Z** = 적용/해제 토글, **Ctrl+Alt+X** = 지금 활성인 창을 맨 앞으로(잠깐 보기, 적용 상태는 유지). 트레이 메뉴에도 "창 순서 적용"이 있다.
- 옵션: "맨 뒤 창을 뺀 나머지를 '항상 위' 층에 배치".
- 항목은 프로세스 이름(exe 이름) + 제목 포함 문자열로 창을 찾는다. 제목이 자주 바뀌는 창(브라우저 탭 등)은 제목 칸을 비우거나 짧게 줄인다.
- 목록에 없는 창(브라우저 등)을 활성화할 때는 아무것도 하지 않는다. 전체화면(독점) 게임 창은 Z-order를 무시하므로 창 모드에서 쓴다.
- **MapleDom은 관리자 권한으로 실행된다(시작 시 UAC 확인창).** MapleStory처럼 관리자 권한으로 도는 게임 창은 일반 권한 프로그램이 옮길 수 없기 때문이다(Windows UIPI, `SetWindowPos` 오류 5). 일반 권한으로 실행하면 카드 아래 경고 줄에 그 사실이 표시된다.
- 구현: `MapleDom.Core.Windowing`(매칭, 순수 로직) + `MapleDom.Windows.WindowArranger`(`SetWindowPos`, `SWP_NOACTIVATE`) + `WindowOrderKeeper`(`SetWinEventHook` 포그라운드 감시).
  실측으로 확인한 Windows 특성: 다른 프로세스 창은 `HWND_TOP`으로는 활성 창 위로 올라가지 않으므로 맨 앞은 "항상 위 켰다 끄기", 맨 뒤는 `HWND_BOTTOM`, 상대 순서는 `hWndInsertAfter`=핸들로 잡는다.

## 화면 알림 배너

Windows 토스트가 작아서 놓치기 쉬우면 설정 > **알림 · 창** 탭의 **화면 알림**을 쓴다(기본 켜짐).
알림이 울릴 때 화면에 큰 배너가 뜬다.

- **클릭을 통과시키고 포커스를 뺏지 않는다**(`WS_EX_NOACTIVATE | WS_EX_TRANSPARENT`). 배너가 떠 있어도 게임 조작이 그대로 된다.
- 표시 시간·글자 크기·불투명도는 설정 > 알림 · 창 탭에서, **배너 표시 여부와 위치는 타이머 탭에서 각각** 고른다(좌/가운데/우 × 위/가운데/아래 7가지). [배너 미리보기]로 확인할 수 있다.
- 기본값: **30초만 배너를 쓰고 우측 아래**에 뜬다. 룬·도핑은 배너 없이 Windows 토스트만 온다(필요하면 타이머 탭에서 켤 수 있다).
- 타이머별로 테두리 색이 다르다(룬 초록, 도핑 빨강, 30초 주황).
- 항상 위 층에 그려지므로 창 모드·테두리 없는 전체화면 게임 위에도 뜬다(독점 전체화면은 제외).
- 구현: `MapleDom.App.AlertOverlayWindow` + `Infrastructure.AlertOverlay`.

## 텔레그램 알림 (선택)

자리를 비웠을 때도 알림을 받고 싶으면 설정 > **텔레그램** 탭을 쓴다.

1. 텔레그램에서 `@BotFather`에게 `/newbot` 으로 봇을 만들고 받은 **토큰**을 붙여 넣는다.
2. 만든 봇과의 대화창에서 **아무 메시지나 한 번 보낸 뒤** [채팅 ID 찾기]를 누르면 채팅 ID가 자동으로 채워진다.
3. 보낼 알림을 고른다(기본: 룬·도핑만. 30초 알림은 30초마다 메시지가 가므로 기본 꺼짐).
4. [테스트 전송]으로 확인하고 저장한다.

- 전송은 별도 큐/스레드에서 이뤄져 네트워크가 느려도 Windows 토스트가 밀리지 않는다. 실패하면 메인 창 경고 줄에 사유가 뜬다.
- 도착까지 보통 1초 안팎 걸리므로 정확한 타이밍은 PC 토스트를 기준으로 본다.
- **봇 토큰은 `%LocalAppData%\MapleDom\settings.json` 에 평문으로 저장된다.** 이 파일은 저장소에 들어가지 않는다. 토큰이 새면 BotFather의 `/revoke` 로 재발급한다.
- 구현: `MapleDom.Core.Notifications.TelegramNotifier` (`IAlarmNotifier`), `CompositeNotifier` 로 토스트와 함께 묶인다.

## 정확도 설계

- 시각은 `Stopwatch`(QueryPerformanceCounter) 기반 **단조 시계**만 사용한다. 시스템 시계 변경/NTP 보정의 영향이 없다.
- 다음 만기는 "실제 발동 시각"이 아니라 **"예정 시각"에 체인**된다. 1440사이클(24시간) 시뮬레이션에서 누적 오차 0을 테스트로 보증한다.
- 전용 스케줄러 스레드가 만기 32ms 전까지 커널 대기, 이후 스핀 대기로 만기 시각을 맞춘다. 실측 지연 0.00ms(데모 로그 기준).
- 절전 복귀 등으로 한 주기 이상 밀리면 현재 시각으로 재정렬해 밀린 알람이 폭주하지 않는다.

## 구조

```
MapleDom.slnx
src/
  MapleDom.App/         한국어 WPF UI, 설정, 트레이, 전역 단축키, 창 순서 카드·탭 (외부 패키지 추가 없음)
  MapleDom.Core/        타이머 엔진, 설정, 알림 큐 (UI/Windows 의존 없음, net10.0)
    Time/Clock.cs         IClock, MonotonicClock
    Timers/               RepeatTimer, AckTimer(룬/도핑), TimerSnapshot, AlarmEvent
    Engine/TimerEngine.cs 스케줄러 스레드
    Notifications/        IAlarmNotifier, NotificationDispatcher(직렬화 큐), TelegramNotifier(봇 전송)
    Settings/             AppSettings(JSON), SettingsStore
    Windowing/            창 선택 조건 ↔ 실행 중인 창 매칭(순수 로직)
    Text/                 DurationFormat("14:59", "29분 55초"), AlarmTemplate
    TimerService.cs       UI가 쓰는 단일 진입점
  MapleDom.Windows/     WindowsToastNotifier(토스트), WindowArranger/ForegroundWatcher/WindowOrderKeeper(창 순서, Win32)
  MapleDom.Cli/         콘솔 검증 도구
tests/
  MapleDom.Core.Tests/  xunit 101개
docs/
  UI_PROMPT.md          최초 UI 구현 때 Codex에게 준 프롬프트(이력)
  UI_NOTES.md           UI 사용법, 구현 결정, 검증 결과
tools/
  MapleDom.App.Smoke/   UI 상태·저장·스레드·Win32 검증 및 화면 PNG 생성 (47개)
  make-icon.ps1         앱 아이콘 생성
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

`dist\MapleDom.exe`(약 170MB, 런타임 포함) 하나만 다른 PC에 복사해서 실행하면 된다(Windows 10 1809 이상, x64). 단일 파일 압축과 ReadyToRun은 일부러 끈 상태다: 압축을 켜면 크기는 88MB로 줄지만 실행 중 메모리가 약 120MB 더 들고, ReadyToRun은 크기만 늘리고 시작이 오히려 느렸다. 첫 실행 때 `%TEMP%\.net\MapleDom` 에 네이티브 라이브러리를 풀어 두므로 첫 시작만 1~2초 느릴 수 있다. 앱이 `dist\MapleDom.exe`로 실행 중이면 파일이 잠겨 빌드가 실패하니 먼저 종료한다. 설정 파일 위치는 같다(`%LocalAppData%\MapleDom`).
기본적으로 X를 누르면 트레이에 숨겨지고, 트레이 메뉴의 **종료**로 앱을 끝낸다.
설정의 **알림 · 창** 탭에서 투명도, 항상 위, 트레이 숨기기, 전역 단축키를 바꿀 수 있고, **창 순서** 탭에서 창 목록을 정한다.

UI 추가 검증(앱을 종료한 상태에서 실행):

```
dotnet run --project tools/MapleDom.App.Smoke -c Release -- artifacts/ui-smoke
```

CLI:

```
dotnet run --project src/MapleDom.Cli -c Release -- --selftest     # 토스트 1건 발송 테스트
dotnet run --project src/MapleDom.Cli -c Release -- --demo         # 축소 간격(룬 12s, 도핑 15s)으로 대화형 실행
dotnet run --project src/MapleDom.Cli -c Release                   # 실제 설정으로 대화형 실행
dotnet run --project src/MapleDom.Cli -c Release -- --run 60 --silent   # 60초 비대화형, 토스트 없이 로그만
```

대화형 키: `1/2/3` 각 타이머 토글, `r` 룬 깟음, `d` 도핑했음, `a` 전체 시작, `s` 전체 정지, `m` 음소거, `t` 테스트 알림, `q` 종료.

## 앱 아이콘

`src/MapleDom.App/Assets/app.ico` (16~256px). exe 아이콘, 작업표시줄, 트레이, 토스트 로고에 쓰인다.
다시 만들려면:

```
powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
```

미리보기는 `artifacts/icon/preview.png` 에 생성된다.

## 설정 파일

`%LocalAppData%\MapleDom\settings.json` (없으면 기본값으로 동작). 값이 비거나 0 이하면 기본값으로 보정된다. 이전 버전의 `Dongkkop` 항목이 있어도 무시하고 읽는다.

```json
{
  "Minute": { "Name": "30초", "IntervalSeconds": 28, "GapSeconds": 2, "AutoStart": false,
    "Notification": { "Title": "30초 알림", "MainMessage": "30초 지났습니다. 움직이세요!",
      "Sound": "Default", "Scenario": "Default", "ExpireSeconds": 10, "ShowOverlay": true, "OverlayPosition": "BottomRight" } },
  "Rune":   { "Name": "룬",  "IntervalSeconds": 900,  "LeadSeconds": 0, "ReminderSeconds": 60, "AutoStart": false,
    "Notification": { "Title": "룬 타이머", "MainMessage": "룬 생성! 룬을 까고 [룬 깟음]을 누르세요.",
      "ReminderMessage": "룬 아직 안 깠어요! (+{overdue} 경과)", "Sound": "Reminder", "Scenario": "Reminder", "ExpireSeconds": 10 } },
  "Doping": { "Name": "도핑", "IntervalSeconds": 1800, "LeadSeconds": 5, "ReminderSeconds": 60, "AutoStart": false },
  "Notifications": { "Enabled": true, "MinSpacingSeconds": 2, "MaxQueueLength": 16 },
  "Overlay": { "Enabled": true, "DurationSeconds": 4, "FontSize": 46, "Opacity": 0.92 },
  "Telegram": { "Enabled": false, "BotToken": "", "ChatId": "", "SendMinute": false, "SendRune": true, "SendDoping": true },
  "WindowOrder": { "Topmost": false }
}
```

- 문구 토큰: `{name}` 타이머 이름, `{interval}` 설정 간격, `{n}` 리마인드 회차, `{overdue}` 본 알림 이후 경과.
- `Sound`: Default, IM, Mail, Reminder, SMS, Alarm, Alarm2~Alarm5, Call, Silent (Windows 알림 사운드).
- `Scenario`: Default(자동으로 사라짐), Reminder(닫을 때까지 유지), Alarm(알람 취급, 집중 지원 "알람만" 모드도 통과).
- `WindowOrder.Windows`(창 목록)는 파일에 저장되지 않는다(세션 한정). 최대 16개.

## UI에서 쓰는 API 요약

```csharp
var settings = SettingsStore.LoadOrDefault(null, out var error);
using var service = new TimerService(settings, new WindowsToastNotifier());
using var keeper = new WindowOrderKeeper();          // UI 스레드에서 생성/호출
keeper.Update(settings.WindowOrder);

service.StartAll();                             // 전체 시작
service.Acknowledge(TimerService.RuneId);       // 룬 깟음
service.Acknowledge(TimerService.DopingId);     // 도핑했음
service.Muted = true;                           // 알림만 끔

keeper.Apply();                                 // 창 순서 적용 (정렬 + 유지)
keeper.Release();                               // 해제 (활성 창을 앞으로, 이후 Windows 기본)
keeper.Peek();                                  // 활성 창을 잠깐 맨 앞으로

foreach (var t in service.Snapshot())           // 100ms 폴링해서 표시
    Console.WriteLine($"{t.Name} {DurationFormat.Clock(t.Remaining)} {t.Status}");

service.TimerChanged += snap => { /* 백그라운드 스레드! Dispatcher로 마샬링 */ };
keeper.Changed += () => { /* 백그라운드 스레드일 수 있음 */ };

var s = service.Settings;                       // 복사본
s.Rune.IntervalSeconds = 600;
service.ApplySettings(s);                       // 실행 중 즉시 반영 (유효하지 않으면 ArgumentException)
keeper.Update(s.WindowOrder);
SettingsStore.Save(s);
```

## 주의사항

- **집중 지원(방해 금지)**: Windows 기본 규칙 "게임을 플레이할 때: 알람만"이 켜져 있으면 일반 토스트가 숨겨진다. 설정 > 시스템 > 집중 지원에서 끄거나, 알림 `Scenario` 를 `Alarm` 으로 바꾼다.
- 앱별 알림이 꺼져 있으면 토스트가 오지 않는다. `--selftest` 로 확인할 수 있다.
- 토스트 등록은 exe 경로 기준이라 exe 위치를 옮기면 알림 센터에 새 앱 항목이 생긴다(동작에는 문제 없음).
- 도핑 타이머는 시작 버튼을 눌러도 29분 55초로 예약된다(시작 = 도핑한 시점으로 간주).
- 30초 타이머는 확인 버튼이 없다. 멈추려면 카드의 [정지]를 누른다.
