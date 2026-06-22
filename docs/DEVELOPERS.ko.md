# ComfyUI-Nexus 개발 문서

언어: [English](DEVELOPERS.md) | [한국어](DEVELOPERS.ko.md)

이 문서는 ComfyUI-Nexus를 계속 개발할 때 빠르게 방향을 잡기 위한 실전 가이드입니다.<br>
어떤 영역이 무엇을 소유하는지, 어떤 규칙을 유지해야 하는지, 테스트할 때 어디를 보면
좋은지에 초점을 둡니다.

## 제품 방향

ComfyUI-Nexus는 단순한 ComfyUI WebView wrapper가 아닙니다.<br>
ComfyUI web은 workflow engine과 canvas 역할을 유지하고,<br>
Nexus는 Windows desktop shell, setup flow, native command surface, rail tools, asset workflow, 상태 조율을 담당합니다.

기본 원칙:

- C#은 native shell 상태, setup, file workflow, desktop UX를 담당합니다.
- JS는 ComfyUI DOM/API에 직접 닿아야 하는 bridge action을 담당합니다.
- 넓은 DOM selector보다 명시적인 bridge action과 message type을 우선합니다.
- 버튼별 임시 상태보다 중앙 state machine을 우선합니다.
- 반복되는 magic string보다 읽기 쉬운 configuration object를 우선합니다.

## Portable Runtime 계약

Release build는 쓰기 가능한 독립 폴더에서 실행되는 것을 전제로 합니다.<br>
빌드 스크립트는 Windows app을 `App/` 아래에 publish한 뒤,
작은 root launcher와 검증된 setup package를 portable release folder에 담습니다.<br>
Release build에서는 `App`과 `LocalRuntime/Packages`가 root `ComfyUI-Nexus.exe` launcher 옆에 있어야 하며,<br>
`PortablePackageBootstrapper`는 이 materialized package set을 그대로 사용합니다.<br>
이 folder 방식 패키지는 `build-as-binary.bat Release folder`로 만듭니다.<br>
이전 compact single-file app layout이 의도적으로 필요할 때는 `build-as-binary.bat Release single`을 사용합니다.<br>
패키지 파일명, hash, 필수 bridge file은 `LocalRuntime/Packages/runtime-package-spec.json`에서 정의합니다.<br>
검증된 Python 또는 Git package를 바꿀 때는 build script가 아니라 이 spec을 수정하세요.<br>
기존 EXE footer payload 방식은 호환 fallback으로만 남아 있습니다.

앱이 소유하는 영구 데이터는 EXE root 기준 상대 경로에 있어야 합니다.

- `nexus_settings.json`: setup과 launch configuration
- `LocalRuntime/Installed`: 관리형 Python, Git, ComfyUI runtime
- `LocalRuntime/Cache`: media thumbnail과 WebView2 user data
- `LocalRuntime/State`: portable preferences, bookmark, process state
- `LocalRuntime/Logs`: server와 session diagnostics
- `Backups`: runtime backup

제품 상태 저장에 MAUI `Preferences`, `FileSystem.AppDataDirectory`,
`Environment.SpecialFolder.LocalApplicationData`를 사용하지 마세요. <br>
`PortablePreferences`나 `LocalRuntime` 아래 domain-specific file을 사용합니다.

## 개발 리듬

평소 작업 순서:

1. 동작을 증명하는 가장 작은 변경을 합니다.
2. `UseAppHost=false`와 격리된 output path로 Windows target build를 실행합니다.
3. 검증 산출물을 삭제합니다.
4. 실행 중인 앱에서 실제 사용자 흐름을 테스트합니다.
5. 앱이 닫히거나 멈추거나 동기화가 어색하면 `LocalRuntime/Logs/nexus-latest.log`를 먼저 봅니다.

일회성 console log보다 제품에서도 쓸 수 있는 짧은 persistent diagnostic log를 선호합니다.<br>
문제 재현에 도움이 되는 로그라면 기존 diagnostics surface를 통해 간결하게 남깁니다.

## Startup And Boot Flow

주요 파일:

- `Source/Boot/LoginSequenceOrchestrator.cs`
- `Source/Diagnostics/BootFlowTracker.cs`
- `Source/Pages/MainPage/MainPage.CoreLink.cs`
- `Source/Setup/Startup/StartupRouteDecider.cs`
- `Source/Setup/Startup/StartupRouteKind.cs`
- `Source/Ui/LoadingOverlayController.cs`
- `Source/Views/Overlays/LoadingOverlayView.xaml.cs`
- `Source/Setup/Services/NexusAppEntryService.cs`

Startup은 순서 기반으로 유지합니다. boot log만 보고도 어떤 async 단계가 어디까지 갔는지
추적 가능해야 합니다.

대표 route:

- `FullSetup`: 필수 설정이나 ComfyUI 파일이 없음.
- `ServerLaunchOnly`: setup은 유효하지만 ComfyUI API가 offline.
- `MaintenanceRecovery`: 일반 route 전에 파괴적이거나 setup-reset 성격의 작업 필요.
- `ServerStartupPending`: 이전 Nexus-launched server process가 아직 살아 있고 boot 중일 가능성 있음.
- `DirectLoading`: ComfyUI API가 이미 응답 중.

## Setup System

주요 파일:

- `Source/Setup/SetupSequenceOrchestrator.cs`
- `Source/Setup/SetupStepCatalog.cs`
- `Source/Setup/Diagnostics/RuntimeDiagnosticCatalog.cs`
- `Source/Setup/Diagnostics/Nodes/*`
- `Source/Setup/Runtime/*`
- `Source/Setup/Services/*`
- `Source/Views/Overlays/ProductSetupView.xaml`
- `Source/Views/Overlays/ProductSetupView.xaml.cs`

Setup에는 두 사용자 경로가 있습니다.

- `VANGUARD`: Nexus 관리형 로컬 런타임 준비.
- `ARCHITECT`: 기존 ComfyUI path에 연결.

`ComfyInstallService`는 setup facade입니다. 낮은 수준의 runtime 작업은 더 좁은 service에 위임합니다.

- `RuntimePackageService`: package extraction과 directory payload copy.
- `RuntimePurgeService`: installed runtime output 안전 삭제.
- `PipCacheService`: managed pip cache mode, environment variable, safe cache clearing.
- `GitRepositoryService`: portable Git resolution과 repo clone/sync.
- `ComfyVenvManager`: `.venv`, dependency CUDA 검증, venv-only action.
- `ModelResourceInstaller`: base model validation/download progress.
- `ExtensionInstaller`: ComfyUI-Manager와 필수 custom node setup.
- `HudBridgeInstaller`: HUD와 Nexus bridge payload deployment.
- `DependencyInstaller`: ComfyUI requirements와 CUDA environment setup.
- `ExtraModelPathsService`: marker 구간만 수정하는 ComfyUI `extra_model_paths.yaml` 업데이트.
- `RuntimeBackupService`: backup analysis, archive/folder backup,
  restore preview, merge restore.
- `ModelDuplicateScanService`: internal/external model root 중복 파일 read-only report.

Managed extension은 initiation/setup 단계에서는 repository와 HUD payload를 준비하는 데 집중합니다.<br>
custom-node Python install script 실행은 boot 또는 pending boot task 쪽에 두는 편이 안전합니다.

## Settings, Help, Maintenance

주요 파일:

- `Source/Settings/SettingsEditorService.cs`
- `Source/Settings/SettingsEditorState.cs`
- `Source/Views/Overlays/SettingsOverlayView.xaml`
- `Source/Views/Overlays/SettingsOverlayView.xaml.cs`
- `Source/Setup/Models/PendingBootTask.cs`
- `Source/Setup/Services/SetupSettingsService.cs`
- `Source/Setup/Services/RuntimePurgeService.cs`

Settings는 draft editing과 scheduling을 담당합니다.<br>
무거운 runtime mutation은 UI thread에서 직접 수행하지 말고, 필요하면 pending boot task로 넘깁니다.

모델 라이브러리는 ComfyUI의 `models` 폴더 구조를 따르는 외부 root입니다.<br>
Nexus는
`extra_model_paths.yaml` 안의 marker section만 관리하고, 사용자가 작성한 YAML block과 주석은 원문 그대로 보존해야 합니다.<br>
모델 라이브러리 변경 후에는 ComfyUI 재시작이 필요합니다.

Shell localization은 Help content와 분리합니다.<br>
XAML/app chrome text는 `Resources/Strings/*.csv`에 key를 추가하고, `en.csv`를 fallback 기준으로 완성해 둡니다.

## Backup And Restore

주요 파일:

- `Source/Setup/Services/RuntimeBackupService.cs`
- `Source/Setup/Models/RuntimeBackupFormats.cs`
- `Source/Setup/Models/RuntimeBackupTargets.cs`
- `Source/Setup/Models/RuntimeRestoreModels.cs`

Backup/restore는 데이터 손실을 막기 위해 보수적으로 동작해야 합니다.

- Backup과 restore는 먼저 dry-run analysis를 수행합니다.
- Restore는 삭제가 아니라 merge입니다. 동일 파일은 건너뛰고,
  내용이 다른 같은 경로만 교체합니다.
- Restore는 journal과 session temp file을 사용합니다.
  중단 후에도 안전하게 정리할 수 있어야 합니다.
- ZIP backup은 archive generation과 hash 계산을 함께 streaming합니다.
- Progress는 너무 자주 UI thread를 밀지 않도록 throttle합니다.

Runtime backup 이름은 `comfyui-nexus-runtime-backup`으로 시작해야 합니다.<br>
사용자가 아무 폴더에 던져 두더라도 Nexus backup임을 알아볼 수 있어야 합니다.

Model duplicate scan은 read-only입니다. 앱은 동일 내용 파일과 위치를 보고하고 폴더를 열어줄 뿐,
어느 파일을 삭제할지는 결정하지 않습니다.

## Server Boot And Process Ownership

주요 파일:

- `Source/Setup/Services/ComfyServerProcessService.cs`
- `Source/Setup/Runtime/ProcessRunner.cs`
- `Source/Setup/Runtime/ComfyServerProcessRegistry.cs`
- `Source/Setup/Runtime/PortProbeService.cs`
- `Source/Setup/Runtime/GpuDiscoveryService.cs`
- `Source/Views/Overlays/LoadingOverlayView.xaml.cs`

서버 실행은 명확히 분리합니다.

- `ComfyServerProcessService`: launch argument, Python mode, GPU id,
  host/port, log attach, port wait.
- `ProcessRunner`: process creation과 log file tailing.
- `ComfyServerProcessRegistry`: process persistence, pid/start-time validation,
  pending-process discovery, shutdown tracking.
- `LoadingOverlayView`: visual state only.

서버 실행 전에는 `PYTHONUTF8=1`을 설정해 ComfyUI/plugin 출력 인코딩 문제를 줄입니다.

## Assets Browser

주요 파일:

- `Source/Views/Rail/Tools/Assets/AssetsBrowserView.xaml.cs`
- `Source/Views/Rail/Tools/Assets/Partials/*`
- `Source/Views/Rail/Tools/Assets/AssetRootProfileProvider.cs`
- `Source/Views/Rail/Tools/Assets/AssetOperationPolicy.cs`
- `Source/Views/Rail/Tools/Assets/AssetTreeRowFactory.cs`
- `Source/Configuration/AssetBrowserOptions.cs`
- `Source/Configuration/AssetWatcherProfiles.cs`

Asset browser는 policy-driven이어야 합니다. 파일 작업 동작은 profile과 policy에서 결정하고,
tab별 예외를 분산시키지 않습니다.

Workflow와 model은 path 소유권이 다릅니다.

- Workflow는 ComfyUI `user/default/workflows` 아래의 실제 파일입니다.<br>
파일이나 폴더가 바뀌면 local tree refresh와 ComfyUI workflow store sync bridge action을 예약합니다.
- Model은 ComfyUI API data에서 오며 내부 models 폴더 또는 외부 model library에 있을 수 있습니다.<br>
  "폴더 열기"와 path copy는 model path resolver를 사용합니다. API synthetic path를 mutation target으로
  취급하지 마세요.

Asset watcher update는 single-flight로 유지합니다.<br>
tree apply 중 추가 watcher batch가 오면 다시
queue하고, 현재 UI apply가 끝난 뒤 한 번 더 실행합니다.<br>
MAUI/WinUI virtual row와 canvas update가 겹치면 native crash로 이어질 수 있습니다.

## WebView Bridge

주요 파일:

- `Source/Ui/NexusWebViewBridge.cs`
- `Source/Pages/MainPage/MainPage.WebViewMessages.cs`
- `Source/Configuration/BridgeActions.cs`
- `Source/Configuration/BridgeMessageTypes.cs`

C#에서 JS로 갈 때는 `NexusWebViewBridge`와 `BridgeActions`를 사용합니다.<br>
관련 없는 feature code에 inline JavaScript를 흩뿌리지 않습니다.

JS에서 C#으로 올 때는 `BridgeMessageTypes`에 등록하고 `MainPage.WebViewMessages.cs`에서 routing합니다.

Workflow tab payload는 DOM이 아니라 ComfyUI workflow store 기준입니다. App Mode는 tab DOM을 복제할 수 있으므로<br>
C#에 보내는 tab 목록은 `workflowStore.openWorkflows` 기준이어야 합니다.<br>
DOM tab matching은 ComfyUI의 modified close prompt나 context menu UX를 보존해야 하는 action에만 사용합니다.

## UI Style Notes

- heavy stroke보다 background, glow, glass, depth를 우선합니다.
- 새로 추가하는 clickable element는 normal, hover, pressed, disabled 상태를 가져야 합니다.
- hover는 border만 바뀌는 느낌보다 surface 전체가 반응하는 느낌이어야 합니다.
- popup/dropdown이 parent 밖으로 나가야 하면 clipped child layout에 기대지 말고 overlay layer를 사용합니다.
- 기능 상태를 Unicode glyph 하나에 의존하지 마세요. text badge나 asset이 더 안정적일 수 있습니다.
- Text input은 shared entry text controller를 사용해 selection color,
  copy, paste, select-all 동작을 통일합니다.

## Visual Tokens And Async Stability

공용 code-behind color는 정말 앱 전체 semantic color일 때만 `Source/Ui/NexusColors.cs`에 둡니다.<br>
feature-specific palette는 소유 view 주변 XAML resource나 local semantic alias에 둡니다.

Async UI work는 single-flight를 선호합니다.<br>
같은 refresh/render가 실행 중이면 최신 요청을 pending으로
표시하고 현재 safe unit이 끝난 뒤 한 번 더 실행합니다.<br>
hard cancel은 shutdown, purge, process lifetime,
WebView dispose, OS file handle처럼 platform/runtime ownership이 명확한 곳에만 사용합니다.

## Build And Checks

일반 검증 빌드:

```powershell
dotnet build ComfyUI-Nexus.csproj -f net10.0-windows10.0.19041.0 -p:UseAppHost=false -p:OutputPath=artifacts\verify-build\
```

빠른 agent 작업용 빌드:

```powershell
dotnet build ComfyUI-Nexus.csproj -f net10.0-windows10.0.19041.0 --no-restore -p:UseAppHost=false -p:BaseOutputPath=obj\CodexBuild\
```

Diff hygiene:

```powershell
git diff --check
```

검증 후 `artifacts/verify-*`, `bin`, `obj` 같은 임시 산출물은 필요할 때 정리합니다.

## Manual Smoke Checks

제품 snapshot 전에 반복해서 확인하면 좋은 흐름:

- 첫 실행: splash, setup route, direct loading route, server launch route.
- Server console: 서버 실행 중 host/port/Python mode가 잠기는지.
- Workflows rail: open, rename, move, duplicate, delete, bookmark, insert,
  external JSON insert.
- Models rail: 내부 model path, 외부 library path, folder open, path copy, search result action.
- Media viewer: image navigation/zoom/delete, video play/pause/navigation/close.
- Settings: model libraries, duplicate scan cancel, backup analysis, backup,
  restore preview, restore.
- Bridge: App Mode의 workflow tab count, Ctrl+S, Ctrl+W, queue/run mode,
  GPU telemetry.
- Crash diagnostics: `LocalRuntime/Logs/nexus-latest.log`,
  timestamped `nexus-runtime-*`, `comfy-server-*` 로그 작성.

## Current Risk Areas

- 큰 view/controller 파일은 아직 책임이 쉽게 커질 수 있습니다.
- Setup UI와 runtime service가 빠르게 진화 중이므로 service boundary를 계속 관리해야 합니다.
- ComfyUI update로 Web selector가 drift될 수 있습니다.
  가능한 store/API/explicit bridge hook을 우선합니다.
- Local runtime package spec, setup package, source-managed bridge package,
  ignored installed artifact를 혼동하지 마세요.
- Startup route regression은 미묘합니다. direct loading,
  offline server launch, pending server reattach, first-run setup을 따로 테스트합니다.
