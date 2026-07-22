# Nexus for ComfyUI 개발 가이드

언어: [English](DEVELOPERS.md) | [한국어](DEVELOPERS.ko.md)

이 문서는 Nexus의 작업 계약입니다.<br>
짧고, 명확하고, 현재 상태를 유지합니다.

## 문서 지도

- Root `README.md`: 사용자용 제품 개요, 설치, 요구 사항, build 진입점.
- `DEVELOPERS.ko.md`: engineering rule과 현재 구현 계약.
- `APP_MANAGER_SPEC.ko.md`: app-runtime 소유권과 static/instance 경계.
- `PROJECTINFO.ko.md`: 제품 identity와 대외 naming 규칙.
- `CHANGELOG.ko.md`: 사용자에게 보이는 릴리즈 변경 사항.
- `NEXUS_TODO.md`: 의도적으로 미룬 작업.
- `PRIVACY.ko.md`: 사용자용 개인정보 처리방침.

feature-specific specification은 오래 유지할 외부 계약을 설명할 때만 둡니다.
과거 계획은 활성 문서 옆이 아니라 `docs/archive/`에 둡니다.

## 제품 경계

Nexus는 ComfyUI를 위한 네이티브 Windows 동반 앱입니다.

- ComfyUI는 그래프 실행, 렌더링, 웹 앱을 담당합니다.
- Nexus는 setup, runtime lifecycle, desktop UI, 파일, 복구, 진단을 담당합니다.
- bridge는 두 영역 사이의 명시적 통신을 담당합니다.

대외 문구에는 **Nexus for ComfyUI**를 사용합니다. 브랜딩 기준은 [PROJECTINFO.ko.md](PROJECTINFO.ko.md)를 봅니다.

## 핵심 원칙

1. lifecycle에는 owner가 하나입니다.
2. 흐름에는 state machine이 하나입니다.
3. 오래된 결과를 쌓지 않고 최신 결과 하나만 반영합니다.
4. UI 작업은 UI dispatcher에서 처리합니다.
5. view가 사라지기 전에 native subscription을 해제합니다.
6. timeout은 느린 작업을 알릴 뿐, 유효한 작업을 취소하지 않습니다.

## 책임 지도

`NexusAppManager`는 앱 수명 전체의 단일 composition root입니다.<br>
settings, tooling lease, server process state, managed installation, GPU discovery,<br>
server lifecycle, 선택적 Control Deck, 제한된 background worker, WebP frame cache, coalesced UI-post queue를 소유합니다.<br>
새 app-wide `Instance`를 추가하지 말고 manager에게서 이름이 있는 component를 받습니다.

| 영역 | Owner | 책임 |
| --- | --- | --- |
| Startup route | `StartupRouteDecider` | setup, boot, reattach, direct load 결정 |
| Server lifecycle | `NexusServerLifecycleCoordinator` | startup, restart, maintenance, server stop, app exit hand-off |
| Server process | `ComfyServerProcessService` / `ComfyServerProcessRegistry` | launch, readiness, process registration, native tree termination, verification |
| Local server 검사 | `LocalServerProbe` | listener 상태와 one-shot HTTP probe |
| Setup sequence | `InitiationSequenceRunner` | 필수 단계 순서와 완료 |
| Setup scroll | `ProductSetupView` | focus owner와 모든 setup scroll |
| Popup lifecycle | `NexusPopupManager` | shell, animation, refresh, close |
| UI 작업 | `NexusOperationController` | 최신 refresh, 순서 보장 메시지, 제한된 background 작업 |
| 반복 motion | `NexusMotionController` | UI thread motion, lifecycle stop, resting state |
| Animated WebP 상태 | `NexusVisualStateAnimator` / `NexusAnimatedWebpFrameCache` | 범위별 cache, state mapping, loop, one-shot, 마지막 frame 유지 |
| Loading 해제 | `LoadingOverlayController` | server, bridge, shell, 실제 UI 준비 전까지 input 차단 |
| 관리 custom-node 의존성 | `ManagedCustomNodeDependencyInstaller` | 명시적인 repository, requirements, bootstrap install mode |
| Web bridge | `NexusWebViewBridge` | typed C# to JS call |

새 기능이 owner를 말할 수 없다면 코드를 추가하기 전에 멈춥니다.

## Setup Action 계약

설정 가능한 모든 diagnostic action은 setup이 소유하는 하나의 흐름을 따릅니다.<br>
즉시 진행중 feedback, option 선택, domain recovery, 명시적인 outcome, 최종 UI settlement 순서입니다.

`ProductSetupView.Diagnostics`는 UI 흐름을 소유합니다.<br>
diagnostic node는 option 선택, health probe, recovery 작업만 소유합니다.<br>
각 option은 recovery를 실행할지, scoped tooling-path lease가 필요한지, 취소할 수 있는지와 완료 시 local health를 검증할지, 사용자의 명시적 선택으로 완료할지를 선언합니다.<br>
예를 들어 외부 browser download는 recovery로 browser를 열지만, 사용자가 책임지는 선택으로 즉시 완료되며 local file 검증을 기다리지 않습니다.

| Outcome | Setup 결과 |
| --- | --- |
| `Completed` | health를 다시 확인하고 step과 readiness를 갱신합니다. |
| `AwaitingUserChoice` | action 선택지를 복구하고 step을 waiting 상태로 유지합니다. |
| `Cancelled` | 취소 결과를 보여주고 선택지를 복구한 뒤 step을 waiting 상태로 유지합니다. |
| `Failed` | 실패 결과를 보여주고 step을 failed 상태로 표시합니다. |

action gate, cancellation token, progress UI, inline action은 outer action flow만 열고 닫습니다.<br>
UI는 node ID나 action ID로 이 행동을 추론하지 않습니다.<br>
folder와 executable 선택은 view별 branch가 아니라 node capability입니다.<br>
이 settlement를 우회하는 버튼별 cleanup branch를 추가하지 않습니다.

## View 코드 구성

큰 XAML surface는 하나의 partial type으로 유지하되, 오래 유지될 기능 ownership 기준으로 나눕니다.<br>
줄 수를 줄이기 위해서만 view를 나누지 않습니다.

| Surface | 기준 파일 | 기능 partial |
| --- | --- | --- |
| Settings | SettingsOverlayView.xaml.cs | RuntimeConfiguration, RuntimeTools, ComfyAndExtensions, Maintenance, RuntimeBackup, ModelLibraries |
| Product Setup | ProductSetupView.xaml.cs | Console, Diagnostics, NativeInitiationScroll |
| Media Assets | MediaAssetsView.xaml.cs | Rendering, FileInspection, ScopeAndWatching, Interaction |
| Asset Browser | AssetsBrowserView.xaml.cs | 기존 search, chrome, context-menu, asset-kind partial |

구체적인 XAML surface를 직접 소유하는 controller는 그 surface 아래에 둡니다.<br>
loading overlay는 Views/Overlays/Controllers, shell surface는 Views/Shell/Controllers를 사용합니다.<br>
NexusMotionController, NexusOperationController, NexusAnimatedWebpClip, NexusUiPostCoordinator처럼
재사용 가능한 primitive는 Ui에 둡니다.

MainPage partial 이름은 포함한 흐름을 드러내야 합니다.<br>
예를 들어 startup route, Core Link 선택, bridge repair는 MainPage.StartupAndCoreLink.cs에 둡니다.
## Runtime 모드

| 모드 | Runtime ownership | 필수 setup |
| --- | --- | --- |
| Vanguard | Nexus 관리형 ComfyUI runtime | Git, Python, ComfyUI Core & Venv, Base Model, Extensions |
| Architect | 사용자 관리형 ComfyUI workspace | Git, Python, Extensions |

선택 설정은 필수 sequence를 막지 않습니다.

- Virtual Environment
- pip cache
- 외부 모델 라이브러리

`HealthState`는 probe 결과이고, `SetupDiagnosticStep`은 sequence 진행 상태입니다. 둘을 섞지 않습니다.

## Async와 Lifecycle

최신 결과만 중요한 refresh에는 `NexusOperationController`의 latest 작업을 사용합니다.

- workflow index refresh
- media snapshot burst
- GPU selector discovery
- rail scan과 deferred presentation

새 요청은 pending 요청만 바꿉니다. 실행 중인 작업을 취소하지 않습니다.<br>
side effect 직전에 lease를 확인하고, stale 결과는 버립니다.

boot-ready, lifecycle request처럼 도착 순서를 보존해야 하는 메시지에는
ordered serial 작업을 사용합니다.<br>
고빈도 telemetry에 serial key를 사용하지 않습니다.

취소는 실제 ownership 경계에서만 사용합니다.

- download 또는 repair의 사용자 취소
- view unload 또는 dispose
- WebView teardown
- restart 또는 app shutdown

`CancelAfter`, cancellation debounce, 고정 delay로 정상 UI 흐름을 맞추지 않습니다.<br>
state, layout readiness, event, dispatcher timer를 사용합니다.

Server shutdown은 순차적으로 처리합니다.<br>
shell service를 quiesce하고, server process와 listener 종료를 검증한 뒤 maintenance, boot, app exit로 넘어갑니다.<br>
lifecycle code에서는 `Process.Kill(entireProcessTree: true)`를 사용하지 않습니다.<br>
native process snapshot을 종료하고 모든 target의 종료를 검증합니다.

Loading 초기화 owner는 `BeginSystemLoadingOnMainThreadAsync` 하나입니다.<br>
startup, Core Link 선택, refresh는 WebView navigation 전에 이 경로로 진입합니다.<br>
navigation event는 상태를 알릴 뿐 loading visual이나 progress를 reset하지 않습니다.

## MAUI와 WinUI 안정성

MAUI UI는 retained native scene graph처럼 다룹니다.

권장:

- 긴 console 출력에는 `LogTailView` 사용
- batch update와 row 재사용
- 표시 문자열과 row 수 제한
- scroll과 animation의 owner 하나 유지
- unload 시 subscription, timer, motion 중단
- typed XAML binding과 compiled `DataTemplate` binding 사용

금지:

- 계속 커지는 log를 하나의 `Label.Text`에 반복 대입
- log마다 `FormattedString`, `Span` 생성
- hidden visual tree 반복 rebuild
- 시각 효과만을 위한 layout size animation
- detach된 handler 또는 stale view 갱신
- worker thread loop에서 UI 변경

Windows surface에는 MAUI `Shadow` property를 사용하지 않습니다.<br>
native alpha-mask 경로가 비동기 handler-lifetime 실패를 일으킨 이력이 있어 Nexus UI에서는 의도적으로 제거했습니다.

반복 시각 효과는 authored asset이 맞는 경우 animated WebP를 사용합니다.<br>
surface가 interactive 상태가 되기 전에 owner-scoped cache를 확보하고,
hide 또는 unload에서 해제합니다.<br>
`Image.Source` 교체나 visual tree 재생성으로 clip을 재시작하지 않습니다.

## Managed Custom Node

`CustomNodeSetting.install_mode`가 설치 계약입니다.<br>
node 전용 installer branch를 추가하지 않습니다.

| Mode | 동작 |
| --- | --- |
| `repository` | clone 또는 sync만 수행 |
| `requirements` | clone 또는 sync 뒤 upstream `requirements.txt` 실행 |
| `bootstrap` | 선언된 ABI wheel/file 설치, 필요 시 upstream requirements 실행, 선언된 import 검증 |

`dependencies`는 `requirements`, `wheels`, `files`, `verify_imports` 데이터만 가집니다.<br>
wheel은 `PythonRuntimeInfoService`가 현재 Python ABI와 architecture에 맞춰 선택합니다.<br>
지원하지 않는 ABI/platform은 명확히 실패해야 하며, 암묵적인 source build fallback을 사용하지 않습니다.

## Popup과 Rail 계약

작은 popup surface는 모두 `INexusPopupSurface`를 구현합니다.

Open 순서:

1. peer popup input 차단
2. layout은 가능하지만 시각적으로 숨긴 shell 준비
3. shell layout readiness 대기
4. input 활성화
5. show animation
6. 무거운 content refresh

Rail file watcher는 해당 tool과 root가 실제 활성 상태일 때만 동작합니다.<br>
닫히거나 숨겨진 rail은 scan이나 UI dispatch를 하지 않으며, 다음 open에서 한 번 reconcile합니다.

## Bridge 계약

- C# to JS action은 `BridgeActions`에 추가하고 `NexusWebViewBridge`로 호출합니다.
- JS to C# message는 `BridgeMessageTypes`에 추가하고 `MainPage.WebViewMessages.cs`에서 routing합니다.
- DOM selector보다 ComfyUI store, API, direct public function을 우선합니다.
- selector가 꼭 필요하면 지역화된 버튼 문구에 의존하지 않습니다.

Nexus bridge는 local package extension입니다.<br>
HUD는 optional managed custom node입니다. 두 복구 정책은 분리합니다.

## Portable Runtime

`LocalRuntime`은 source code가 아니라 제품 runtime data입니다.

| 폴더 | 용도 |
| --- | --- |
| `Packages` | setup package와 bridge payload |
| `Installed` | Nexus 관리형 runtime 설치본 |
| `Logs` | Nexus와 ComfyUI 로그 |
| `State` | 작은 persisted runtime state |
| `Work` | setup과 recovery 임시 작업 |

Runtime repair는 models, workflows, inputs, outputs, custom nodes, external model paths를 보존합니다.<br>
관리형 core source는 교체할 수 있어도 runtime data는 지우지 않습니다.

## Tooling Path Lease

Git, pip, archive extraction, virtual environment 생성은 `NexusRuntimeEnvironment.RunToolingAsync` 안에서 실행합니다.<br>
각 outer call은 하나의 tooling request입니다. request 시작에만 짧은 Windows drive alias를 acquire하고, call이 끝나기 전에 반드시 반환합니다.<br>
server launch와 일반 runtime/UI 작업은 이 scope에 들어가지 않으며 항상 물리 path만 사용합니다.

- 저장되는 settings, server launch, logs, backups, diagnostics, file tools는 물리 경로만 사용합니다.
- 중첩 tooling 호출은 현재 lease를 재사용하고, 마지막 child process가 종료된 뒤 해제합니다.
- 각 Nexus process는 instance ID, process ID, process start time을 사용자별 registry 하나에 기록합니다.<br>
  동시에 실행 중인 Nexus instance는 같은 alias를 공유할 수 있으며, 마지막 live owner만 이를 해제합니다.
- startup maintenance는 PID reuse까지 확인해 stale Nexus owner만 정리합니다.
- Nexus가 만든 alias만 ownership 기록에 남깁니다. 기존 user mapping은 재사용할 수 있지만 제거하지 않습니다.
- 지원 drive letter가 없으면 tooling은 명확히 실패합니다. junction이나 긴 tooling path로 fallback하지 않습니다.
- `pip_cache_mode`, `pip_cache_path`는 사용자에게 보이는 cache storage 정책으로 유지하며, alias는 pip child process에만 전달합니다.

## Build와 검증

### 개발 도구 진입점

repository root에는 Batch, PowerShell, Git Bash 형식의 `dev-help`만 둡니다.<br>
자주 쓰는 명령과 정확한 위치는 `dev-help.bat`, `./dev-help`, `.\dev-help.ps1`에서 확인합니다.

- Command Prompt: `tools\windows\dev-*.bat`
- PowerShell: `tools\windows\dev-*.ps1`
- Git Bash: `./tools/windows/dev-*`
- `tools/windows`: Windows 전용 개발 도구 전체

```powershell
dotnet build ComfyUI-Nexus.csproj -f net10.0-windows10.0.19041.0 -p:UseAppHost=false -p:OutputPath=artifacts\verify-build\ --no-restore
git diff --check
```

검증 build가 성공하면 `artifacts/verify-*`를 삭제합니다.

Release packaging:

```bat
tools\windows\dev-build-as-binary.bat Release folder archive
```

서명 Windows Release는 `--cert`가 clean, restore, publish 전에 쓸 수 있는 로컬 Authenticode certificate을 검증합니다.

```bat
tools\windows\dev-build-as-binary.bat Release folder clean archive --cert Release
```

Microsoft Store 제출은 Partner Center identity를 무시되는 `Store.Build.props`에만 두고 아래처럼 실행합니다.

```bat
tools\windows\dev-build-as-binary.bat Release app-store clean
```

Store profile MSIX를 로컬에서 검증할 때는 업로드 번들을 수정하지 않고 서명된 사본을 사용합니다.

```powershell
.\tools\windows\dev-test-store-package.ps1 help
.\tools\windows\dev-test-store-package.ps1 install -ResetData -Launch
```

Git Bash에서는 확장자 없이 같은 helper를 호출할 수 있습니다.

```bash
./tools/windows/dev-test-store-package help
./tools/windows/dev-test-store-package install -ResetData -Launch
```

테스트 helper는 로컬 Partner Center Publisher 값과 같은 subject의 인증서를 만든 뒤,<br>
`build\Store_*\local-test` 아래 사본만 서명해 설치합니다.<br>
첫 설치에서는 MSIX 설치에 필요한 로컬 컴퓨터 신뢰 저장소에 공개 테스트 인증서를 등록하기 위해 한 번 관리자 권한을 요청합니다.<br>
Partner Center 제출용 `.msixupload` 파일은 변경하지 않습니다.<br>
로컬 테스트 도구는 `signtool.exe`가 없으면 `winget`으로 Windows SDK Signing Tools를 설치합니다.<br>
Store 설정과 runtime data는 LocalState에 유지합니다.<br>
setup tooling 실행 중에만 Nexus가 관리형 또는 외부 ComfyUI root와 선택한 custom pip cache에 짧은 drive alias를 lease합니다.<br>
사용자에게 보이는 설정, server process, logs, backups, file tools는 항상 물리 경로를 유지합니다.<br>
각 alias는 process별 ownership record를 사용하므로, 동시에 실행한 Nexus build는 마지막 live owner가 끝날 때까지 shared alias를 유지합니다.<br>
startup cleanup은 process identity를 확인한 뒤 stale Nexus mapping만 정리하며, 사용자 mapping은 절대 건드리지 않습니다.<br>
로컬 QA가 끝난 뒤에는 `remove -ResetData -RemoveCertificate`로 앱 데이터, 생성된 local-test MSIX 사본,<br>
Nexus 소유의 임시 tooling drive mapping, 현재 사용자와 로컬 컴퓨터 저장소의 테스트 인증서를 함께 제거할 수 있습니다.<br>

`build/Store_Release_<timestamp>/` 아래에 MSIX와 `.appxsym` symbols를 포함한 `.msixupload` 하나를 만듭니다.<br>
Store flow에서는 portable 전용인 `archive`, `zip`, `--cert` 옵션을 사용할 수 없습니다.<br>
Store MSIX는 네 번째 버전을 `0`으로 예약하고, portable build는 전체 `NexusVersion` 값을 유지합니다.

## Smoke Checklist

- First run: setup route, setup completion, server boot
- Server: launch, retry, restart, shutdown, reattach
- Setup: Vanguard/Architect 필수 순서, optional edit state
- Rail: assets, media, workflows, root 전환, close/open
- Settings: library, backup, restore, pending maintenance
- Bridge: tab, queue, GPU telemetry, manager action
- Diagnostics: `LocalRuntime/Logs/nexus-latest.log`와 해당 ComfyUI server log

## Crash Triage

1. `LocalRuntime/Logs/nexus-latest.log`를 읽습니다.
2. Python 또는 server 실패는 해당 `comfy-server-*.log`를 읽습니다.
3. native app exit는 runtime service를 고치기 전에 Windows Event Viewer와 crash dump부터 봅니다.
4. UI lifetime, WebView/bridge, server process, runtime package 중 하나로 분류합니다.

managed log에 예외가 없다고 native UI가 정상이라는 뜻은 아닙니다.

UI가 멈춘 것처럼 보이면 timer나 cancellation을 추가하기 전에
`[UI_TRACE]`와 `[CONCURRENCY]`를 함께 비교합니다.<br>
renderer가 보인다고 UI dispatcher가 정상이라는 뜻은 아닙니다.
