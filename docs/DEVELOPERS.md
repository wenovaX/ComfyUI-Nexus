# ComfyUI-Nexus Developer README

Language: [English](DEVELOPERS.md) | [한국어](DEVELOPERS.ko.md)

This document is the fast orientation guide for working on ComfyUI-Nexus.<br>
It focuses on where the important seams are, what owns each responsibility,<br>
and which conventions should stay stable as the product moves toward production.

## Product Direction

ComfyUI-Nexus is not just a wrapper around the existing ComfyUI web UI.<br>
The web app remains the rendering and workflow engine,<br>
while Nexus owns the desktop shell, setup flow, native command surfaces, rail tools, asset workflows, and state orchestration.

Guiding principles:

- Keep C# responsible for native shell state, setup, file workflows, and
  desktop-grade UX.
- Keep JS responsible for direct ComfyUI DOM/API bridge actions.
- Prefer explicit bridge actions and message types over broad DOM selectors.
- Prefer centralized state machines over per-button one-off state.
- Prefer readable configuration objects over repeated magic strings.

## Portable Runtime Contract

Release builds are designed to run from a writable standalone folder.<br>
The build script publishes the Windows app into `App/`,
then creates a small root launcher and packs verified setup packages in a portable release folder.<br>
`App` and `LocalRuntime/Packages` must stay beside the root `ComfyUI-Nexus.exe` launcher in release builds;<br>
`PortablePackageBootstrapper` accepts that materialized package set directly.
Use `build-as-binary.bat Release folder` for this folder-style package.<br>
Use `build-as-binary.bat Release single` when you intentionally want the older compact single-file app layout.<br>
Package filenames, hashes, and required bridge files are defined by
`LocalRuntime/Packages/runtime-package-spec.json`;<br>
update that spec when swapping verified Python or Git packages instead of editing build scripts.<br>
The old executable-footer payload path is kept only as a compatibility fallback.

Persistent application-owned data must remain relative to the executable root:

- `nexus_settings.json`: setup and launch configuration
- `LocalRuntime/Installed`: managed Python, Git, and ComfyUI runtime
- `LocalRuntime/Cache`: media thumbnails and WebView2 user data
- `LocalRuntime/State`: portable preferences, bookmarks, and process state
- `LocalRuntime/Logs`: server and session diagnostics
- `Backups`: runtime backups

Do not use MAUI `Preferences`, `FileSystem.AppDataDirectory`, or
`Environment.SpecialFolder.LocalApplicationData` for product state.<br>
Use `PortablePreferences` or a domain-specific file below `LocalRuntime`.<br>
Temporary extraction files may use the operating-system temp directory only
when they are removed in a `finally` path.

## Local Development Rhythm

Useful work order for day-to-day changes:

1. Make the smallest feature change that proves the behavior.
2. Run a targeted Windows build with `UseAppHost=false` and an isolated output path.
3. Delete the verification output after the build.
4. Test the user flow in the running app.
5. Check `LocalRuntime/Logs/nexus-latest.log` when anything closes, stalls,
   or feels out of sync.

Prefer short persistent diagnostic logs over temporary console noise.<br>
If a log helps debug a production-style issue, keep it concise and route it through the
existing diagnostics surface.

## Startup And Boot Flow

Primary files:

- `Source/Boot/LoginSequenceOrchestrator.cs`
- `Source/Diagnostics/BootFlowTracker.cs`
- `Source/Pages/MainPage/MainPage.CoreLink.cs`
- `Source/Setup/Startup/StartupRouteDecider.cs`
- `Source/Setup/Startup/StartupRouteKind.cs`
- `Source/Ui/LoadingOverlayController.cs`
- `Source/Views/Overlays/LoadingOverlayView.xaml.cs`
- `Source/Setup/Services/NexusAppEntryService.cs`

Startup is intentionally sequence-driven. The app should be debuggable from the
boot log without guessing which async task happened first.

The startup route is decided before the full app shell is shown:

- `FullSetup`: required settings or ComfyUI files are missing.
- `ServerLaunchOnly`: setup is valid, but the ComfyUI API is offline.
- `MaintenanceRecovery`: destructive or setup-resetting maintenance work must
  run before the normal setup/server route continues.
- `ServerStartupPending`: a previous Nexus-launched server process is still
  alive and likely still booting.
- `DirectLoading`: the ComfyUI API is already responding.

Once the route reaches app loading, the current high-level phases are:

1. Core link check.
2. Product setup, maintenance recovery, server boot, pending-process reattach,
   or direct loading.
3. WebView source assignment.
4. Bridge identity injection.
5. Handshake wait.
6. Rail/node/assets prewarm.
7. Stable hand-off to the app shell.

Use `BootFlowTracker` for startup/F5 timing.<br>
Avoid temporary console logging in boot code unless it becomes part of the product diagnostic surface.

## ComfyUI Endpoint Configuration

Endpoint settings live in `Source/Setup/Models/SetupSettings.cs`.

Defaults:

- `ListenAddress`: `127.0.0.1`
- `ServerPort`: `8188`

Runtime URL construction lives in `Source/Configuration/ComfyApiOptions.cs`.<br>
Do not hardcode `http://127.0.0.1:8188` in feature code. Use:

- `ComfyApiOptions.LocalBaseUrl`
- `ComfyApiOptions.ObjectInfoUrl`
- `ComfyApiOptions.WorkflowProbeUrl`
- `ComfyApiOptions.ModelCategoryUrl(category)`

`ComfyApiOptions` reads the current setup settings each time.<br>
If ComfyUI binds to `0.0.0.0`, the client-side URL maps back to `127.0.0.1` so WebView and API
calls stay local.

The Product Setup command center currently exposes quick host/port editing in
the server console header.<br>
Changes are persisted to `nexus_settings.json` and used by server launch,
port probe, WebView navigation, and API calls.

Do not introduce new hardcoded endpoint strings in feature code.<br>
If a feature needs a new ComfyUI API route, add it to `ComfyApiOptions` or a small
domain-specific configuration helper.

## Setup System

Primary files:

- `Source/Setup/SetupSequenceOrchestrator.cs`
- `Source/Setup/SetupStepCatalog.cs`
- `Source/Setup/SetupStepIds.cs`
- `Source/Setup/Diagnostics/InitiationSequenceRunner.cs`
- `Source/Setup/Diagnostics/RuntimeDiagnosticCatalog.cs`
- `Source/Setup/Diagnostics/Nodes/*`
- `Source/Setup/Runtime/*`
- `Source/Setup/Services/*`
- `Source/Views/Overlays/ProductSetupView.xaml`
- `Source/Views/Overlays/ProductSetupView.xaml.cs`

Setup has two user-facing paths:

- `VANGUARD`: prepare a local Nexus-managed runtime.
- `ARCHITECT`: connect an existing ComfyUI path.

Shared initiation steps should stay reusable.<br>
Server boot should stay isolated behind setup services so the UI only coordinates sequence state and display.

`ComfyInstallService` is the setup facade used by step catalog and diagnostic nodes.<br>
Keep low-level runtime work delegated to narrower services:

- `RuntimePackageService` for package extraction and directory payload copy.
- `RuntimePurgeService` for deleting installed runtime output safely.
- `PipCacheService` for managed pip cache mode, environment variables, and
  safe cache clearing.
- `GitRepositoryService` for portable Git resolution and repo clone/sync.
- `ComfyVenvManager` for `.venv`, dependency CUDA verification, and venv-only
  actions.
- `ModelResourceInstaller` for base model validation/download progress.
- `ExtensionInstaller` for ComfyUI-Manager and essential custom node setup.
- `HudBridgeInstaller` for HUD and Nexus bridge payload deployment.
- `DependencyInstaller` for ComfyUI requirements and CUDA environment setup.
- `ExtraModelPathsService` for marker-scoped ComfyUI
  `extra_model_paths.yaml` updates.
- `RuntimeBackupService` for backup analysis, archive/folder backup,
  restore preview, and merge restore.
- `ModelDuplicateScanService` for read-only duplicate model reports across
  internal and external model roots.

The setup UI should treat diagnostic nodes as data-driven cards.<br>
Required nodes
and optional nodes may be shown in separate visual groups,<br>
but they should still use the same node contracts and action policies where possible.

Managed extensions have two phases:

- Initiation/setup should only make repositories and HUD payloads available.<br>
  Avoid running custom-node Python install scripts during the interactive
  diagnostic sequence.
- Server boot and pending boot tasks may run extension dependency installation
  before launching ComfyUI.

`Recover & Boot` should use the same centralized server boot path as normal
server launch.<br>
Add repair steps to `SetupSequenceOrchestrator.RunServerBootAsync` instead of
creating one-off recovery flows in views.

Access modifiers should stay tight:

- Setup services are `internal` unless they are consumed outside this assembly.
- DTOs or models that must deserialize from JSON can remain `public` when that
  keeps serializer and binding behavior simple.
- XAML-backed views remain `public partial` when required by MAUI tooling.
- Service callbacks such as `OnMessage` and `OnProgress` should be `internal`
  when the owning service is internal.

## Settings, Help, And Maintenance Surfaces

Primary files:

- `Source/Settings/SettingsEditorService.cs`
- `Source/Settings/SettingsEditorState.cs`
- `Source/Views/Overlays/SettingsOverlayView.xaml`
- `Source/Views/Overlays/SettingsOverlayView.xaml.cs`
- `Source/Views/Overlays/HelpOverlayView.xaml`
- `Source/Views/Overlays/HelpOverlayView.xaml.cs`
- `Source/Setup/Models/PendingBootTask.cs`
- `Source/Setup/Services/SetupSettingsService.cs`
- `Source/Setup/Services/RuntimePurgeService.cs`

Settings owns draft editing and scheduling.<br>
It should not perform heavy runtime mutation inline unless the operation is
clearly lightweight.<br>
Prefer queueing a pending boot task when the operation touches installed runtime
files, ComfyUI repositories, extensions, or the active Python environment.

Pending boot tasks are the handoff contract between Settings and boot:

- Settings creates, previews, cancels, and saves tasks.
- `StartupRouteDecider` decides whether tasks require maintenance recovery or
  server boot.
- `SetupSequenceOrchestrator` executes tasks in one place before server launch.

Keep reset semantics distinct:

- Setup reset clears setup path state and returns to first-run style setup.
- Full settings reset clears all saved settings and returns to setup.
- Runtime purge removes local runtime output, then returns to setup.

Runtime backups are stored outside `LocalRuntime/Installed` so runtime purge
does not delete them.<br>
Long file work should run off the UI thread and update the settings surface
through safe UI dispatch helpers.

Model libraries are configured as external roots that follow ComfyUI's own
`models` folder structure.<br>
Nexus writes only its marked section in `extra_model_paths.yaml`; user-authored
YAML blocks must be preserved verbatim.<br>
After changing model libraries, ComfyUI must be restarted before the server
reloads the model search paths.

Nexus Help is a native notebook for practical Nexus/ComfyUI usage notes and
frequent links.<br>
Do not route the toolbar Help button there; toolbar Help belongs
to the ComfyUI web help surface.

Help content is intentionally content-managed:

- `Resources/Help/help.catalog.json` owns section/item ordering and localized
  navigation text.
- `Resources/Help/Articles/<slug>/<language>.txt` owns article bodies.
- `Resources/Help/Images` owns shared and language-specific images.
- Article bodies can use lightweight tags such as `[h:]`, `[link:]`,
  `[article:]`, `[folder:]`, `[copy:]`, `---`, and `[img:]`.

Shell localization is separate from Help content.<br>
Use `Resources/Strings/*.csv` for XAML/app chrome text and keep `en.csv`
complete as the fallback.<br>
Non-English CSV values may be blank when English fallback is intentional,
but keep the key present so translators can see the missing entry.

## Backup And Restore System

Primary files:

- `Source/Setup/Services/RuntimeBackupService.cs`
- `Source/Setup/Models/RuntimeBackupFormats.cs`
- `Source/Setup/Models/RuntimeBackupTargets.cs`
- `Source/Setup/Models/RuntimeRestoreModels.cs`

Backup and restore operations are strictly fortified to prevent data loss and
corrupted runtime states:

- **Pre-flight Analysis**: Both backup and restore start with dry-run analysis
  phases (`AnalyzeBackupAsync`, `AnalyzeRestoreAsync`).<br>
  These calculate required safety margins (2% overhead), check available disk
  space, compare SHA-256 hashes, and predict identical vs. modified files before
  copying any data.
- **Journaling and Safety**: Restore operations write a journal
  (`runtime-restore-journal.json`) and use temporary file markers
  (`.nexus-restore-`).<br>
  If a restore crashes or is interrupted, the next boot safely cleans up pending
  temp files (`CleanupPendingRestoreTemps`) so the ComfyUI installation is never
  left in a broken state.
- **Optimized I/O**: ZIP format backups stream archive generation while
  simultaneously computing file hashes (`CopyStreamWithHashAsync`), minimizing
  disk writes and memory footprint.
- **Progress Throttling**: Granular byte-level progress tracking is throttled
  to 250ms intervals to prevent UI thread congestion during heavy file
  transfers.

Runtime backup names should start with `comfyui-nexus-runtime-backup` so they
remain identifiable even when users drop them into a loose backup folder.<br>
Duplicate model scans are intentionally read-only: the app reports identical
content and opens locations, but the user decides what to delete.

## Server Boot And Process Ownership

Primary files:

- `Source/Setup/Services/ComfyServerProcessService.cs`
- `Source/Setup/Runtime/ProcessRunner.cs`
- `Source/Setup/Runtime/ComfyServerProcessRegistry.cs`
- `Source/Setup/Runtime/PortProbeService.cs`
- `Source/Setup/Runtime/GpuDiscoveryService.cs`
- `Source/Views/Overlays/LoadingOverlayView.xaml.cs`

Server launch is intentionally split:

- `ComfyServerProcessService` owns launch arguments, Python mode, GPU id,
  host/port settings, log attachment, and waiting for the port.
- `ProcessRunner` owns process creation and log file tailing.
- `ComfyServerProcessRegistry` owns process persistence, pid/start-time
  validation, pending-process discovery, and shutdown tracking.
- `LoadingOverlayView` owns visual state only: idle, booting, waiting for port,
  failed, and online.

Important rules:

- Never launch a second Python server if a persisted Nexus-launched process is
  still alive. Reattach to its log and wait for the configured port.
- Keep server logs file-based. The app may exit while the server keeps running,
  so stdout/stderr ownership cannot be tied only to the MAUI process lifetime.
- Treat `OperationCanceledException` during shutdown or route changes as normal
  control flow unless it escapes into user-visible failure.
- If killing the server, mark the process as shutting down before terminating it
  so log tailing and process probes can suppress expected access races.
- Server startup must force required ComfyUI settings, such as Nodes 2.0, before
  launch when the setting is file-backed and safe to apply.
- Server startup should set `PYTHONUTF8=1` before launching ComfyUI so plugin
  output is less likely to fail on Windows console encoding.

## Local Runtime Layout

`LocalRuntime` is a product/runtime area, not normal source code.

- `Packages` contains `runtime-package-spec.json`, prepared setup packages,
  and Nexus bridge overlays.
- `Installed` contains expanded local runtime installs.
- `Logs` contains server boot logs.
- `State` contains lightweight process state, including pending server process
  information.
- `Work` is scratch space for setup/runtime operations.

Keep source-managed bridge payloads clearly separated from installed runtime artifacts.<br>
Do not make feature code depend on machine-specific installed files.

HUD web features and Nexus bridge payloads are separate sources.<br>
The HUD package is synced from the configured `HudRepoUrl`; the installed copy under the active
ComfyUI `custom_nodes/ComfyUI-HUD` folder is not the source of truth.

Native Nexus bridge overlays live under `LocalRuntime/Packages/NexusBridge/js`
and are copied on top of the installed HUD package for Nexus-managed runtime
installs.<br>
Installed runtime copies should be updated through setup, repair, or
managed extension sync rather than edited as the source of truth.

## WebView Bridge

Primary files:

- `Source/Ui/NexusWebViewBridge.cs`
- `Source/Pages/MainPage/MainPage.WebViewMessages.cs`
- `Source/Configuration/BridgeActions.cs`
- `Source/Configuration/BridgeMessageTypes.cs`

C# to JS:

- Prefer `NexusWebViewBridge`.
- Register bridge action names in `BridgeActions`.
- Avoid inline JavaScript strings in unrelated feature code.

JS to C#:

- Register message types in `BridgeMessageTypes`.
- Route messages through `MainPage.WebViewMessages.cs`.

Selectors should be semantic and stable where possible: `data-*`, `aria-*`,
specific icon classes, or explicit bridge-injected IDs.<br>
Avoid broad fallback selectors that can click the wrong web control.

Workflow tabs are sourced from ComfyUI's workflow store, not DOM tab surfaces.<br>
DOM tab matching is only for actions that must preserve ComfyUI's own modified
close prompt or context menu behavior.<br>
App Mode can duplicate tab DOM, so payloads sent to C# must be based on
`workflowStore.openWorkflows`.

## Header And Control Deck

Primary files:

- `Source/Views/Shell/HeaderView.xaml`
- `Source/Views/Shell/HeaderToolbarTrayView.xaml`
- `Source/Views/Shell/HeaderToolbarTrayView.RunMode.cs`
- `Source/Views/Shell/ControlDeckView.xaml.cs`

The header is a product surface, not a mirror of web buttons.<br>
Keep run, stop,
mode, jobs, and queue state synchronized through explicit bridge calls and central state.<br>
Avoid per-button visual hacks that bypass the deck state.

## Rail State

Primary files:

- `Source/Views/Rail/RailView.xaml.cs`
- `Source/Views/Controls/Buttons/*`

Rail state is centralized around tool selection and auxiliary surfaces. Keep
only one active visual owner for each surface family.

Common concepts:

- `RailToolKind` for native rail content tools.
- `RailAuxiliarySurface` for Apps, WebAssets, Templates, Settings, and similar
  web/native popup surfaces.

When adding a rail button, wire it into the state machine instead of letting the
button manage active visuals alone.

## Assets Browser

Primary files:

- `Source/Views/Rail/Tools/Assets/AssetsBrowserView.xaml.cs`
- `Source/Views/Rail/Tools/Assets/Partials/*`
- `Source/Views/Rail/Tools/Assets/AssetRootProfileProvider.cs`
- `Source/Views/Rail/Tools/Assets/AssetOperationPolicy.cs`
- `Source/Views/Rail/Tools/Assets/AssetTreeRowFactory.cs`
- `Source/Configuration/AssetBrowserOptions.cs`
- `Source/Configuration/AssetWatcherProfiles.cs`

The asset browser is intentionally policy-driven.<br>
File operation behavior should come from profiles and policies, not scattered tab-specific exceptions.

Useful rules:

- Default permissions can be broad, but restrictive tabs should express policy
  explicitly.
- Keyboard shortcuts and context menu actions must use the same policy checks.
- Watcher debounce and refresh behavior should remain profile-specific.
- Pool/reuse row UI where object lifetime boundaries are clear.

Workflow and model paths have different ownership rules:

- Workflows are real files under ComfyUI's `user/default/workflows` tree.<br>
  When
  files or folders change, schedule a local tree refresh and call the ComfyUI
  workflow store sync action through the bridge.
- Models come from ComfyUI API data and may live in the internal models folder
  or any configured external model library.<br>
  Use the model path resolver for "open folder" and path-copy actions; do not
  treat synthetic API paths as mutation targets.

Asset watcher updates should be single-flight.<br>
If another watcher batch arrives while the tree is applying, requeue it and run
once more after the current UI apply completes.<br>
This protects MAUI/WinUI virtualized rows from overlapping collection and canvas
updates.

## Node Library

Primary files:

- `Source/Views/Rail/Tools/NodeLibrary/NodeLibraryView.xaml.cs`

Node data mostly changes when the ComfyUI server changes.<br>
Prefer boot/F5 sync over automatic refresh loops.<br>
If a refresh is needed, compare data and update only changed, added, or removed entries.

## Configuration Conventions

Use `Source/Configuration` for shared constants, option names, bridge action
names, message types, layout values, and API route helpers.

Examples:

- `BridgeActions`
- `BridgeMessageTypes`
- `ComfyApiOptions`
- `RunModeOptions`
- `CanvasModeOptions`
- `QueueOperations`
- `PreferenceKeys`
- `ShellLayoutOptions`
- `WindowOptions`
- `AssetBrowserOptions`
- `NodeLibraryOptions`
- `LogOptions`

Domain-specific policy objects can stay near their domain when that makes the
code easier to reason about.

## UI Style Notes

- Prefer background, glow, glass, and depth over heavy strokes.
- Hover should feel like the whole surface reacts, not only the border.
- Any newly clickable element needs normal, hover, pressed, and disabled states.
- Keep product-critical surfaces intentional and compact.
- If a dropdown or popup visually extends beyond its parent, put it in an
  overlay layer rather than relying on clipped child layout.
- Avoid Unicode glyphs for functional state if a text badge or asset is clearer
  and safer across encodings.
- Text input controls should use the shared entry text controller so selection
  color, copy, paste, and select-all behavior stay consistent.

## Visual Tokens And Async Stability

Shared code-behind colors should use `Source/Ui/NexusColors.cs` only when the
color is truly app-wide.<br>
Feature-specific palettes belong near the owning view as local XAML resources
or local semantic aliases.<br>
Do not move one-off gradient, animation, or state-specific colors into global
tokens just to remove raw hex values.

For async UI work, prefer single-flight operations.<br>
If a refresh or render is
requested while the same work is running, mark the latest request pending and
run once more after the current safe unit completes.<br>
Avoid hard-canceling
feature/UI work; reserve hard cancellation for platform/runtime ownership such
as shutdown, purge, process lifetime, WebView disposal, and OS file handles.

## Code Hygiene

Follow the repository hygiene rules:

- UTF-8 without BOM.
- Consistent project line endings.
- Final newline in every text file.
- No trailing whitespace.
- Focused diffs.
- No unrelated formatting churn.
- Existing indentation style per file.
- Project logging system for persistent logs.
- Remove temporary/noisy logs before finishing.

## Build And Checks

Recommended build:

```powershell
dotnet build ComfyUI-Nexus.csproj -f net10.0-windows10.0.19041.0 -p:OutDir=C:\tmp\ComfyUI-Nexus-build\
```

Fast local verification used during agent work:

```powershell
dotnet build ComfyUI-Nexus.csproj -f net10.0-windows10.0.19041.0 --no-restore -p:UseAppHost=false -p:BaseOutputPath=obj\CodexBuild\
```

Diff hygiene:

```powershell
git diff --check
```

If MAUI intermediate files are locked by a running app or Visual Studio, stop
the running process before assuming a source change broke the build.

Manual smoke checks worth repeating before a product snapshot:

- First launch: splash, setup route, direct loading route, and server launch route.
- Server console: edit host/port/Python mode only while the server is not running.
- Workflows rail: open, rename, move, duplicate, delete, bookmark, insert,
  and external JSON insert.
- Models rail: internal model path, external library path, folder open,
  path copy, and search result actions.
- Media viewer: image navigation/zoom/delete and video play, pause, navigation, close.
- Settings: model libraries, duplicate scan cancellation, backup analysis,
  backup, restore preview, restore.
- Bridge: workflow tab count in App Mode, Ctrl+S, Ctrl+W, queue/run mode, GPU telemetry.
- Crash diagnostics: verify `LocalRuntime/Logs/nexus-latest.log`,
  timestamped `nexus-runtime-*`, and `comfy-server-*` logs are written.

## Current Risk Areas

- Large view/controller files can still accumulate responsibility quickly.
- Setup UI and runtime services are evolving fast; keep service boundaries clean.
- Web selectors can drift with ComfyUI updates; use stable selectors or bridge
  hooks whenever possible.
- Local runtime setup packages should remain separated from source-managed
  bridge packages and ignored installed artifacts.
- Startup route regressions can be subtle. Test direct loading, offline server
  launch, pending server reattach, and first-run setup separately.
