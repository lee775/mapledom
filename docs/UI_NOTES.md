# MapleDom UI 구현 및 검증

`docs/UI_PROMPT.md`를 기준으로 `src/MapleDom.App`을 추가했다. 기존 Core / Windows / Cli / Tests 프로젝트의 소스는 변경하지 않았다. App과 검증 도구에 외부 NuGet 패키지를 추가하지 않았다.

## 실행

```powershell
dotnet build MapleDom.slnx -c Release
dotnet test MapleDom.slnx -c Release
dotnet run --project src/MapleDom.App -c Release
```

실행 파일은 `src/MapleDom.App/bin/Release/net10.0-windows10.0.17763.0/MapleDom.exe`다. 같은 디렉터리의 DLL 및 런타임 설정 파일이 함께 있어야 한다.

## 구현

- 320×240 고정 크기, 맑은 고딕, 어두운 테마, 제목표시줄 없는 드래그 이동, 항상 위 토글.
- 동꼽방지 / 룬 / 도핑 카드, 개별 및 전체 시작·정지, 룬 깟음 / 도핑했음, 음소거, 룬 테스트 알림.
- 정지·진행·초과 상태, 10초 이하 주황 숫자, 초과 카드와 확인 버튼의 붉은 강조, 리마인드 횟수.
- 모달 설정: 타이머 이름·간격·자동 시작, 휴식·선행·리마인드, 알림 문구·사운드·리마인드 사운드·표시 방식·유지 시간, 알림 사용·최소 간격.
- 설정 오류 목록, 기본값을 편집 상태에만 복원, 저장 시 적용 후 파일 저장. 파일 저장 실패 시 실행 설정을 원래 값으로 복원하고 오류를 표시한다. 두 설정 파일 중 두 번째 저장이 실패하면 첫 번째 파일도 가능한 한 되돌린다.
- 트레이 메뉴 8개와 더블클릭 복원. X는 기본적으로 트레이 숨기기이며, 설정에서 해제하면 종료한다.
- Mutex 단일 인스턴스와 숨겨진 Win32 메시지 창. 두 번째 프로세스가 기존 프로세스에 활성화 신호를 보낸 뒤 종료한다. 준비 이벤트는 초기화 경쟁을 줄이는 용도로만 대기하며, 별도 폴링 작업은 없다.
- UI 설정은 `%LocalAppData%\MapleDom\ui.json`, 타이머 설정은 Core의 `settings.json`에 저장한다. 위치·항상 위·투명도·트레이 숨기기·단축키 사용 여부가 유지된다.
- 저장된 위치가 화면 밖이면 작업 영역으로 복구한다. 설정 창 크기도 작업 영역을 넘지 않도록 제한한다.
- 미처리 Dispatcher 예외는 메시지박스와 `error.log` 기록 후 종료한다. 알림 전송 실패는 메인 창의 경고 줄에 표시하고 전체 내용은 툴팁으로 읽을 수 있다.
- 종료 시 UI 갱신 타이머, 이벤트 구독, 전역 단축키, 트레이 아이콘, Core 서비스, IPC 핸들을 정리한다.

선택 기능도 구현했다.

| 기능 | 동작 |
|---|---|
| 투명도 | 설정 슬라이더 40~100%, 저장 시 적용 |
| 전역 단축키 | 기본 꺼짐. Ctrl+Alt+R: 룬, Ctrl+Alt+D: 도핑, Ctrl+Alt+S: 전체 시작/정지 |
| 최근 알림 | 설정 창 아래에 최대 5개, 테스트 알림 포함 |

전역 단축키는 WPF KeyBinding 대신 `RegisterHotKey`를 사용한다. 키를 계속 누를 때 반복 초기화되지 않도록 `MOD_NOREPEAT`를 지정하고, 등록 충돌은 메인 창에 알린다. [Microsoft API 문서](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)

## UI 쪽 처리와 API 참고

- `INotifyPropertyChanged`를 직접 구현했다. 화면 표시 중 실행 중인 타이머가 있을 때 `DispatcherTimer`가 100ms마다 스냅샷을 읽는다. 전부 정지했거나 창을 숨긴 동안에는 UI 폴링을 멈추고 Core는 계속 동작한다.
- Core 이벤트 구독은 모두 `Dispatcher.BeginInvoke`로 전달하고, 종료 이후 남은 콜백은 UI를 갱신하지 않는다.
- 설정의 **테스트 알림은 저장된 설정 기준**이다. 편집 중인 설정을 임시 적용하지 않으므로 취소할 때 실행 타이머에 영향을 주지 않는다. 테스트는 음소거 및 알림 사용 여부와 관계없이 발송하는 Core API 동작을 따른다.
- `SendTest`는 `NotificationQueued`를 발생시키지 않으므로 반환된 알림을 UI 로그에 직접 추가한다. 알림 로그는 발송 요청 기록이며 전달 성공 보증은 아니다.
- Core의 `Validate()`가 모든 시간 필드의 상한·유한성을 확인하지 않으므로 UI에서 숫자 형식, NaN/무한대, 범위 및 최소 해상도를 추가 검증한다. UI 시간 입력 상한은 86400초, 양수의 최소값과 간격−선행의 최소 차이는 0.001초다. 알림 유지 시간은 -86400~86400초이며 0 이하는 만료 없음이다. 시작 시 손으로 편집한 설정도 같은 검증을 통과한 뒤 Core 서비스를 만든다.
- 필요한 Core API 추가 요청은 없다. 알림이 Windows 정책에 의해 조용히 숨겨진 경우까지 UI에서 판별하지는 않는다. 백엔드가 실패 이벤트를 전달할 때 경고를 표시한다.

## 검증

```powershell
dotnet run --project tools/MapleDom.App.Smoke -c Release -- artifacts/ui-smoke
```

검증 도구는 기존 xunit 프로젝트와 별개인 WPF 콘솔 프로그램이며 외부 패키지가 없다. 설정 파일 시험은 지정한 출력 디렉터리에만 쓴다. 단일 인스턴스 검사 때문에 MapleDom을 종료한 상태에서 실행한다. 전역 단축키 등록 검사 중 다른 앱이 같은 키를 사용하면 해당 검사가 실패할 수 있다.

2026-09-14, Windows 10 / .NET SDK 10.0.400-preview.0.26322.102 환경에서 확인했다.

| 항목 | 결과 |
|---|---|
| 솔루션 Release 빌드 | 경고 0, 오류 0 |
| 기존 xunit 테스트 | 80/80 통과 |
| UI 추가 검증 | 38/38 통과 |
| 바인딩 | WPF DataBinding 오류 0 |
| 정지/시작/리셋 | 시작 시 0:58 / 15:00 / 29:55, 확인 버튼 재예약 검증 |
| 만기/리마인드 | 10초 룬 설정으로 초과 상태와 리마인드 횟수 증가 검증 |
| 설정 | 선행 ≥ 간격, 잘못된 숫자, NaN, 큰 값, 시간 반올림 오류 거부; 파일 저장·재로드; 손상된 UI JSON 복구 |
| 스레드 | 실제 Core 스케줄러의 알림·상태 변경을 Dispatcher 스레드에서 처리 |
| 종료 | 기본 닫기의 숨기기 경로, 명시적 창 닫기, 트레이 옵션 해제 시 종료 요청, 서비스 스케줄러 종료 |
| IPC | 별도 프로세스에서 활성화 신호 전송 후 종료, 잠금 해제 후 재획득 |
| 단축키 | Win32 등록·해제, 메시지로 룬·도핑·전체 정지 실행, 비활성화 후 무시 |
| 트레이 | 아이콘·메뉴 생성 및 자원 해제 |
| 화면 | 정지·진행·초과, 타이머 설정·창 설정·오류 목록 PNG를 렌더링하고 확인 |

렌더링 결과는 `artifacts/ui-smoke/*.png`에 생성된다. 이 폴더는 Git에서 제외했다. 실제 화면 캡처를 대신하는 WPF 렌더링이며 창 테두리와 운영체제 팝업은 포함하지 않는다.

실제 앱 시작과 접근성 트리도 확인했다. 최초 유휴 샘플은 CPU 0%, 전용 작업 집합 약 46.3MiB였다. 전체 작업 집합은 공유 런타임을 포함해 약 135.5MiB였으며, 이 측정은 장시간 사용이나 게임 중 동작 성능 보증이 아니다.

컴퓨터 UI 캡처는 `SetIsBorderRequired` / `0x80004002` 오류로 사용할 수 없었고, 이후 사용자의 물리적 Esc 입력으로 UI 자동화가 중단됐다. 따라서 트레이 더블클릭과 메뉴 클릭의 전체 흐름, 게임에 포커스를 둔 실제 단축키 입력, Windows 토스트의 화면 표시·사운드, 세션 로그오프, 다양한 DPI/모니터 조합은 실제 환경에서의 최종 확인이 남아 있다. 검증용 프로세스는 종료했고 남은 MapleDom 프로세스가 없는 것을 확인했다.

## 창 순서 탭 (2026-09-15 추가, Claude)

설정 창에 **창 순서** 탭이 추가됐다. 실행 중인 창을 콤보박스에서 골라 목록에 넣고 ▲▼로 순서를 정한다(위가 앞).
- 체크: 창 순서 고정 사용(목록 창이 활성화되면 0.15초 뒤 자동 재정렬), 항상 위 층에 배치.
- 버튼: 새로고침(창 목록·찾음/없음 상태 갱신), 추가, 지금 정렬(편집 중인 목록으로 즉시 정렬, 저장 안 함), 각 행의 ▲ ▼ 삭제.
- 전역 단축키에 Ctrl+Alt+Z(정렬), Ctrl+Alt+X(활성 창 맨 앞으로)가 추가됐고, 트레이 메뉴에 "창 순서 정렬"이 생겼다.
- 저장 시 `TimerService.ApplySettings` 다음에 `MainViewModel.WindowOrder`(`WindowOrderController`)에도 반영한다. 정렬이 적용되면 메인 창은 자신의 '항상 위'를 다시 세운다(`MainWindow.ReassertTopmost`).
- 핵심 로직은 UI 밖에 있다: `MapleDom.Core.WindowOrder.WindowOrderResolver`(매칭, xunit), `MapleDom.Windows.WindowArranger`(SetWindowPos), `ForegroundWatcher`(SetWinEventHook), `WindowOrderController`.
- 검증 도구에 창 순서 탭 검사 6개와 `settings-window-order.png` 렌더가 추가돼 총 44개다. 실제 창 3개(메모장)로 정렬·포커스 유지·자동 고정을 확인하는 별도 시험은 `README.md`의 창 순서 절 참고.

## 동꼽방지 제거 · 창 순서 카드 (2026-09-15, Claude)

- 동꼽방지(반복형) 타이머를 제거했다. 메인 창 카드는 룬·도핑 2개이고, 그 위에 **창 순서 카드**가 있다.
- 창 순서 카드: 요약("N개 창 · 맨 뒤: 프로세스")과 **[적용]/[해제]** 토글. 적용 = 설정한 순서로 정렬하고 목록 창을 활성화해도 유지(마지막 창은 바닥). 해제 = 감시를 멈추고 활성 창을 앞으로 올려 Windows 기본 상태(최근 창이 앞)로 돌아감.
- 설정 탭은 룬 / 도핑 / 알림·창 / 창 순서 4개. 창 순서 탭의 체크박스는 "앱 시작 시 바로 적용 상태로 시작"(ApplyOnStart)과 "맨 뒤 창을 뺀 나머지를 항상 위 층에 배치"(Topmost). 목록 마지막 줄에 "맨 뒤" 표시.
- 단축키 Ctrl+Alt+Z = 창 순서 적용/해제 토글, Ctrl+Alt+X = 활성 창을 맨 앞으로(잠깐 보기). 트레이 메뉴 "창 순서 적용"은 체크 표시로 상태를 보여 준다.
- `MainViewModel` 생성자에 선택 인자 `WindowOrderKeeper? windowOrder`가 추가됐다(검증 도구는 null). 카드 뷰모델은 `WindowOrderViewModel`, 설정 탭은 `WindowOrderEditor`.
- 검증 도구: 카드 2개·창 순서 카드 비활성 확인, 맨 뒤 표시, 단축키 4/5 무해성 검사가 추가됐다. 실제 창 3개(메모장)로 적용·재정렬·Peek·해제 후 기본 동작을 확인하는 별도 시험은 README 창 순서 절 참고.

## 맨 뒤 창 콤보 · Z 순서 번호 (2026-09-16, Claude)

- 메인 창 카드의 요약 글자를 **맨 뒤 창 콤보박스**로 바꿨다. 실행 중인 창을 고르면 `MainViewModel.SetBackWindow`가 창 순서 목록의 마지막 항목을 바꾸고(없으면 추가) `ApplySettings` → 키퍼 `Update` → `SettingsStore.Save` 순으로 바로 저장한다. 콤보가 열릴 때(`DropDownOpened`) 목록을 다시 읽는다.
- `WindowInfo.ZOrder`(1 = 맨 앞)가 추가돼 콤보 항목이 "3. 제목  [프로세스]" 형식으로 현재 쌓임 순서를 보여 준다(설정 탭 콤보도 동일).
- `MainViewModel.SettingsPath`로 저장 경로를 바꿀 수 있다. 검증 도구는 출력 폴더의 `settings-card.json`에 저장해 실제 설정을 건드리지 않는다.

## 관리자 권한 실행 (2026-09-16, Claude)

- `app.manifest`의 `requestedExecutionLevel=requireAdministrator`로 앱이 항상 관리자 권한으로 뜬다(UAC 확인창). MapleStory가 관리자 권한으로 실행되어 일반 권한에서는 `SetWindowPos`가 오류 5로 무시되던 문제의 해결책이다(실측: 메모장은 성공, MapleStory는 거부).
- `WindowArranger.Apply`는 이제 옮기지 못한 창이 있으면 `Win32Exception`을 던지고, 키퍼의 `Failed`를 거쳐 메인 창 경고 줄에 "게임이 관리자 권한으로 실행 중…" 문구가 뜬다. Peek(잠깐 보기) 실패도 같은 경로.
- 검증 도구는 App DLL을 자기 프로세스에 올리므로 매니페스트의 영향을 받지 않는다.

## 창 목록 세션 한정 (2026-09-16, Claude)

- 사용자 요청: 앱을 켤 때 이전에 고른 맨 뒤 창을 기억하지 말 것. `SettingsStore.Save`는 창 목록을 비운 복사본을 쓰고 `Load`는 읽은 뒤 목록을 비운다(`Clone`은 그대로 유지해 실행 중 흐름은 정상). "앱 시작 시 바로 적용" 옵션은 의미가 없어져 제거했다.
- 카드의 `SetBackWindow`는 이제 파일을 쓰지 않는다(`ApplySettings` + 키퍼 `Update`만). `MainViewModel.SettingsPath`도 제거.

## 1분 알림 타이머 복원 (2026-09-17, Claude)

- 사용자 요청으로 반복형 타이머를 되살렸다. 메인 창 카드는 위에서부터 **창 순서 → 1분 → 룬 → 도핑** 4개이고 창 높이는 300으로 늘렸다(카드 UniformGrid Rows=3).
- Core: `RepeatTimerSettings`/`SettingsDefaults.Minute()`(58초 + 휴식 2초 = 60초 주기) 복원, `TimerService.MinuteId`와 `RepeatTimer` 등록, `TimerIds` 순서는 minute → rune → doping. SchemaVersion 3(구버전 파일은 Minute 항목이 없으면 기본값으로 채움).
- App: 설정 탭이 **1분 / 룬 / 도핑 / 알림·창 / 창 순서** 5개. `TimerSettingsEditor.From(RepeatTimerSettings)`와 `BuildRepeat`를 되살렸고, `IsAcknowledge` 기본값을 false로 되돌려 반복형 탭에서는 휴식 시간 칸이, 확인형 탭에서는 선행/리마인드 칸이 보인다.
- `TimerCardViewModel.CanAcknowledge`는 `Id != MinuteId` — 1분 카드에는 확인 버튼이 없다.
- 검증 도구: 카드 3개·확인 버튼 유무·1분 탭 빌드/검증 검사 추가, 탭 인덱스 이동(알림·창 3, 창 순서 4), 렌더 `settings-minute.png` 추가.

## 텔레그램 알림 (2026-09-17, Claude)

- 설정 탭이 **1분 / 룬 / 도핑 / 알림·창 / 창 순서 / 텔레그램** 6개가 됐다. 텔레그램 탭: 사용 여부, 봇 토큰, 채팅 ID(+[채팅 ID 찾기]), 타이머별 전송 체크 3개, [테스트 전송], 결과 한 줄(`Telegram.Status`).
- `TelegramNotifier`(Core/Notifications)는 `IAlarmNotifier`라서 `CompositeNotifier(WindowsToastNotifier, TelegramNotifier)`로 묶어 `TimerService`에 넘긴다. App이 소유하고 Dispose한다.
- 전송은 내부 `NotificationDispatcher`(간격 0, 큐 32)에 태워 별도 스레드에서 한다 — `Notify`는 즉시 반환하므로 네트워크 지연이 토스트 큐를 막지 않는다. 실패는 `SendFailed` → 메인 창 경고 줄.
- `ShouldSend(settings, timerId)`가 켜짐·설정 완료·타이머별 토글을 판단한다. 기본은 룬·도핑만 켜짐(1분은 스팸이라 꺼둠).
- 설정 저장 시 `SettingsWindow.OnSave`가 `_main.Telegram?.Update(...)`로 실행 중인 알림기에 반영하고, 저장 실패 롤백에서도 되돌린다.
- [채팅 ID 찾기]/[테스트 전송]은 `Task.Run` + `await`(async void 핸들러)로 UI 스레드를 막지 않는다. 둘 다 저장 없이 편집 중인 값으로 동작한다.

## 30초 알림 · 화면 알림 배너 (2026-09-17, Claude)

- 반복 타이머 기본값을 **28초 + 휴식 2초(주기 30초)**, 이름 "30초"로 바꿨다. 설정의 JSON 키(`Minute`)와 타이머 id(`minute`)는 기존 설정 파일 호환을 위해 그대로 둔다.
- **화면 알림 배너**(`AlertOverlayWindow` + `Infrastructure.AlertOverlay`): 알림이 울리면 화면에 큰 배너를 띄운다. Windows 토스트가 작아 놓치는 문제와 전체화면 게임에서 토스트가 안 보이는 문제를 함께 해결한다.
  - 확장 스타일 `WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW` + `ShowActivated=False`, `IsHitTestVisible=False` → 포커스를 뺏지 않고 클릭을 통과시키며 Alt+Tab에도 안 나온다.
  - 표시할 때마다 `Topmost` 를 껐다 켜서 다른 '항상 위' 창(창 순서 기능 포함)보다 앞에 세운다.
  - 설정: 알림·창 탭의 "화면 알림"(사용, 표시 시간, 위치 위/가운데/아래, 글자 크기, 불투명도, [배너 미리보기]).
  - `MainViewModel.RecordNotification`에서 띄우므로 음소거·알림 끔이면 배너도 안 뜬다. 테스트 알림에서도 뜬다.
- 검증 도구에 오버레이 검사 4개와 `overlay-banner.png` 렌더가 추가돼 총 62개다.

## 화면 알림 위치를 타이머별로 (2026-09-17, Claude)

- `OverlayPosition` 이 7가지(좌/가운데/우 × 위/가운데/아래 + 화면 가운데)로 늘고, **위치는 `NotificationProfile.OverlayPosition`** 으로 타이머마다 따로 정한다. `OverlaySettings` 에는 사용 여부·표시 시간·글자 크기·불투명도만 남았다.
- `AlarmNotification` 에 `OverlayPosition` 이 추가돼(기본 Top) 알림 자체가 위치를 들고 다닌다. `TimerService.BuildNotification` 이 프로필 값을 실어 보내고 `AlertOverlay` 는 그대로 쓴다.
- 기본값: 30초 = `BottomRight`(자주 울려 시야를 가리지 않게), 룬·도핑 = `Top`.
- 설정 UI: 각 타이머 탭 맨 아래 "화면 알림 위치" 콤보. 알림·창 탭의 위치 콤보는 제거했다. [배너 미리보기]는 지금 열려 있는 타이머 탭의 위치로 띄운다.

## 배너를 타이머별로 켜고 끄기 (2026-09-28, Claude)

- `NotificationProfile.ShowOverlay`(기본 true)가 추가됐다. `AlarmNotification.OverlayPosition` 은 `OverlayPosition?` 이 되어 **null 이면 배너를 띄우지 않는다**. `TimerService` 가 `ShowOverlay ? Position : null` 로 채운다.
- 기본값: 30초만 `ShowOverlay = true`(우측 아래). 룬·도핑은 false — 사용자가 "룬·도핑 크게 뜨는 거 없애 달라"고 요청.
- 설정 UI: 각 타이머 탭에 "화면 알림 배너 표시" 체크박스, 그 아래 "배너 위치" 콤보(체크 해제 시 비활성).
