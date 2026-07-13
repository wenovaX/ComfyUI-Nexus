# Nexus for ComfyUI 개발 가이드

언어: [English](DEVELOPERS.md) | [한국어](DEVELOPERS.ko.md)

이 문서는 Nexus의 작업 계약입니다. 짧고, 명확하고, 현재 상태를 유지합니다.

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

| 영역 | Owner | 책임 |
| --- | --- | --- |
| Startup route | `StartupRouteDecider` | setup, boot, reattach, direct load 결정 |
| Server lifecycle | `NexusServerLifecycleCoordinator` | startup, restart, maintenance, server stop, app exit hand-off |
| Server process | `ComfyServerProcessService` / `ComfyServerProcessRegistry` | launch, readiness, process registration, native tree termination, verification |
| Local server 검사 | `LocalServerProbe` | listener 상태와 one-shot HTTP probe |
| Setup sequence | `InitiationSequenceRunner` | 필수 단계 순서와 완료 |
| Setup scroll | `ProductSetupView` | focus owner와 모든 setup scroll |
| Popup lifecycle | `NexusPopupManager` | shell, animation, refresh, close |
| Latest-wins 작업 | `NexusLatestOperationCoordinator` | 실행 중 하나, 최신 pending 하나 |
| 반복 motion | `NexusMotionController` | UI thread motion, lifecycle stop, resting state |
| 관리 custom-node 의존성 | `ManagedCustomNodeDependencyInstaller` | 명시적인 repository, requirements, bootstrap install mode |
| Web bridge | `NexusWebViewBridge` | typed C# to JS call |

새 기능이 owner를 말할 수 없다면 코드를 추가하기 전에 멈춥니다.

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

최신 결과만 중요한 refresh에는 latest-wins를 사용합니다.

- workflow index refresh
- media snapshot burst
- GPU selector discovery
- rail scan과 deferred presentation

새 요청은 pending 요청만 바꿉니다. 실행 중인 작업을 취소하지 않습니다.<br>
side effect 직전에 lease를 확인하고, stale 결과는 버립니다.

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

## Managed Custom Node

`CustomNodeSetting.install_mode`가 설치 계약입니다. node 전용 installer branch를 추가하지 않습니다.

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

Nexus bridge는 local package extension입니다. HUD는 optional managed custom node입니다. 두 복구 정책은 분리합니다.

## Portable Runtime

`LocalRuntime`은 source code가 아니라 제품 runtime data입니다.

| 폴더 | 용도 |
| --- | --- |
| `Packages` | setup package와 bridge payload |
| `Installed` | Nexus 관리형 runtime 설치본 |
| `Logs` | Nexus와 ComfyUI 로그 |
| `State` | 작은 persisted runtime state |
| `Work` | setup과 recovery 임시 작업 |

Runtime repair는 models, workflows, inputs, outputs, custom nodes, external model paths를 보존합니다.
<br>관리형 core source는 교체할 수 있어도 runtime data는 지우지 않습니다.

## Build와 검증

```powershell
dotnet build ComfyUI-Nexus.csproj -f net10.0-windows10.0.19041.0 -p:UseAppHost=false -p:OutputPath=artifacts\verify-build\ --no-restore
git diff --check
```

검증 build가 성공하면 `artifacts/verify-*`를 삭제합니다.

Release packaging:

```bat
dev-build-as-binary.bat Release folder archive
```

서명 Windows Release는 `--cert`가 clean, restore, publish 전에 쓸 수 있는 로컬 Authenticode certificate을 검증합니다.

```bat
dev-build-as-binary.bat Release folder clean archive --cert Release
```

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
