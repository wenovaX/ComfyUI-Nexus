# Nexus for ComfyUI: Developer Guide

Language: [English](DEVELOPERS.md) | [한국어](DEVELOPERS.ko.md)

This is the working contract for Nexus. Keep it short, explicit, and current.

## Documentation Map

- Root `README.md`: user-facing product overview, install, requirements, and build entry points.
- `DEVELOPERS.md`: engineering rules and active implementation contracts.
- `APP_MANAGER_SPEC.md`: app-runtime ownership and static-versus-instance boundary.
- `PROJECTINFO.md`: product identity and public naming rules.
- `CHANGELOG.md`: released user-visible changes.
- `NEXUS_TODO.md`: intentionally deferred work.
- `PRIVACY.md`: user-facing privacy policy.

Keep feature-specific specifications only when they describe a durable external contract.<br>
Historical plans belong in `docs/archive/`, not beside active documentation.

## Product Boundary

Nexus is the native Windows companion for ComfyUI.

- ComfyUI owns graph execution, rendering, and its web application.
- Nexus owns setup, runtime lifecycle, desktop UI, files, recovery, and diagnostics.
- The bridge owns explicit communication between those two worlds.

Use **Nexus for ComfyUI** in public copy. See [PROJECTINFO.md](PROJECTINFO.md) for branding rules.

## Core Rules

1. One owner per lifecycle.
2. One state machine per flow.
3. One latest result, not a queue of stale results.
4. UI work stays on the UI dispatcher.
5. Close or unload stops native subscriptions before the view disappears.
6. A timeout reports a slow operation. It does not cancel useful work.

## Ownership Map

`NexusAppManager` is the one app-lifetime composition root.<br>
It owns settings, tooling leases, server process state, managed installation, GPU discovery,<br>
server lifecycle, the optional Control Deck, bounded background workers, the WebP frame cache, and the coalesced UI-post queue.<br>
Do not add a new app-wide `Instance`; receive the named component from the manager instead.

| Area | Owner | Keep it responsible for |
| --- | --- | --- |
| Startup route | `StartupRouteDecider` | Setup, boot, reattach, or direct load decision |
| Server lifecycle | `NexusServerLifecycleCoordinator` | Startup, restart, maintenance, server stop, and app-exit hand-off |
| Server process | `ComfyServerProcessService` / `ComfyServerProcessRegistry` | Launch, readiness, process registration, native tree termination, and verification |
| Local server checks | `LocalServerProbe` | Listener state and one-shot HTTP probes |
| Setup sequence | `InitiationSequenceRunner` | Required step order and completion |
| Setup scrolling | `ProductSetupView` | Focus owner and all setup scroll movement |
| Popup lifecycle | `NexusPopupManager` | Shell, animation, refresh, close |
| UI operations | `NexusOperationController` | Latest refreshes, ordered serial messages, bounded background work |
| Repeating motion | `NexusMotionController` | UI-thread motion, lifecycle stop, resting state |
| Animated WebP states | `NexusVisualStateAnimator` / `NexusAnimatedWebpFrameCache` | Scoped cache, state mapping, loop, one-shot, final-frame hold |
| Loading release | `LoadingOverlayController` | Block input until server, bridge, shell, and visible UI are ready |
| Managed custom-node dependencies | `ManagedCustomNodeDependencyInstaller` | Explicit repository, requirements, or bootstrap install mode |
| Web bridge | `NexusWebViewBridge` | Typed C# to JS calls |

If a new feature cannot name its owner, stop before adding code.

## Setup Action Contract

Every configurable diagnostic action follows one setup-owned flow:<br>
immediate working feedback, option selection, domain recovery, an explicit outcome, then one final UI settlement.

`ProductSetupView.Diagnostics` owns the UI flow.<br>
Diagnostic nodes own only their option selection, health probe, and recovery work.<br>
Each option declares whether it runs recovery, requires a scoped tooling-path lease, can be cancelled, and whether completion verifies local health or records an explicit user choice.<br>
For example, external browser downloads launch recovery but complete as an accepted external choice and never wait for local file verification.

| Outcome | Setup result |
| --- | --- |
| `Completed` | Probe health, update the step, then refresh readiness. |
| `AwaitingUserChoice` | Restore the action choices and keep the step waiting. |
| `Cancelled` | Show the cancellation result, restore choices, and keep the step waiting. |
| `Failed` | Show the failure result and mark the step failed. |

Only the outer action flow opens or clears the action gate, cancellation token, progress UI, and inline actions.<br>
The UI must not infer these behaviors from a node ID or action ID; folder and executable selection are node capabilities, not view-specific branches.<br>
Do not add button-specific cleanup branches that bypass this settlement.

## View Code Organization

Keep a large XAML surface as one partial type, but divide its code by durable feature ownership.<br>
Do not split a view merely to reduce line count.

| Surface | Primary file | Feature partials |
| --- | --- | --- |
| Settings | SettingsOverlayView.xaml.cs | RuntimeConfiguration, RuntimeTools, ComfyAndExtensions, Maintenance, RuntimeBackup, ModelLibraries |
| Product Setup | ProductSetupView.xaml.cs | Console, Diagnostics, NativeInitiationScroll |
| Media Assets | MediaAssetsView.xaml.cs | Rendering, FileInspection, ScopeAndWatching, Interaction |
| Asset Browser | AssetsBrowserView.xaml.cs | Existing search, chrome, context-menu, and asset-kind partials |

A controller that directly owns a concrete XAML surface belongs below that surface:<br>
Views/Overlays/Controllers for loading overlays and Views/Shell/Controllers for shell surfaces.<br>
Keep reusable primitives such as NexusMotionController, NexusOperationController,
NexusAnimatedWebpClip, and NexusUiPostCoordinator under Ui.

MainPage partial names must state the flow they contain.<br>
For example, startup route, Core Link selection, and bridge repair share MainPage.StartupAndCoreLink.cs.
## Runtime Modes

| Mode | Runtime ownership | Required setup |
| --- | --- | --- |
| Vanguard | Nexus-managed ComfyUI runtime | Git, Python, ComfyUI Core & Venv, Base Model, Extensions |
| Architect | User-managed ComfyUI workspace | Git, Python, Extensions |

Optional settings never block the required sequence:

- Virtual Environment
- pip cache
- External model libraries

`HealthState` describes a probe. `SetupDiagnosticStep` describes sequence progress.<br>
Do not merge those meanings.

## Async And Lifecycle

Use `NexusOperationController` latest work where only the newest result matters:

- workflow index refresh
- media snapshot bursts
- GPU selector discovery
- rail scans and deferred presentation work

New requests replace the pending request. They do not cancel the running request.<br>
Before a side effect, check the operation lease. Drop stale results.

Use ordered serial work for messages that must retain arrival order,
such as boot-ready and lifecycle requests.<br>
Do not use a serial key for high-rate telemetry.

Use cancellation only for real ownership boundaries:

- explicit user cancel for downloads or repair
- view unload or disposal
- WebView teardown
- restart or app shutdown

Do not use `CancelAfter`, cancellation as debounce, or fixed delays to coordinate normal UI flow.<br>
Use state, layout readiness, events, and dispatcher timers instead.

Server shutdown is sequential: quiesce shell services, stop and verify the server process and listener, then continue to maintenance, boot, or app exit.<br>
Do not call `Process.Kill(entireProcessTree: true)` from lifecycle code;<br>
terminate a captured native process snapshot and verify every target exits.

Loading initialization has one owner: `BeginSystemLoadingOnMainThreadAsync`.<br>
Startup, Core Link selection, and refresh must enter it before WebView navigation.<br>
Navigation events report state; they must not reset loading visuals or progress.

## MAUI And WinUI Safety

Treat MAUI UI as a retained native scene graph.

Do:

- use `LogTailView` for long console output
- batch updates and reuse rows
- bound visible text and row counts
- keep one owner for scroll and animation
- stop subscriptions, timers, and motion at unload
- use typed XAML bindings and compiled `DataTemplate` bindings

Do not:

- replace one giant `Label.Text` for a growing log
- create `FormattedString` or `Span` objects for every log line
- repeatedly rebuild hidden visual trees
- animate layout size for a visual-only effect
- update a detached handler or stale view
- use worker-thread loops to mutate UI

Do not use MAUI `Shadow` properties on Windows surfaces.<br>
The native alpha-mask path has caused asynchronous handler-lifetime failures and is intentionally absent from Nexus UI.

Use animated WebP for repeating visual effects when an authored asset fits the job.<br>
Acquire the owner-scoped cache before a surface becomes interactive, then release it on hide or unload.<br>
Do not restart a clip by replacing `Image.Source` or by rebuilding the visual tree.

## Managed Custom Nodes

`CustomNodeSetting.install_mode` is the install contract. Do not add node-specific installer branches.

| Mode | Behavior |
| --- | --- |
| `repository` | Clone or sync only. |
| `requirements` | Clone or sync, then run the upstream `requirements.txt`. |
| `bootstrap` | Install declared ABI wheels and files, run upstream requirements when requested, then verify declared imports. |

`dependencies` is data: `requirements`, `wheels`, `files`, and `verify_imports`.<br>
For a wheel, select the current Python ABI and architecture from `PythonRuntimeInfoService`.<br>
Unsupported ABI/platform pairs must fail clearly; never fall back to an implicit source build.

## Popup And Rail Contract

All small popup surfaces implement `INexusPopupSurface`.

Open order:

1. Close peer input.
2. Prepare a visible-layout, visually hidden shell.
3. Await shell layout readiness.
4. Activate input.
5. Animate show.
6. Refresh heavy content.

Rail file watchers are active only while their tool and root are active.<br>
A closed or hidden rail never scans or dispatches file changes.<br>
On the next open, reconcile once.

## Bridge Contract

- Add C# to JS actions in `BridgeActions` and call them through `NexusWebViewBridge`.
- Add JS to C# messages in `BridgeMessageTypes` and route them through `MainPage.WebViewMessages.cs`.
- Prefer a ComfyUI store, API, or direct public function over DOM selectors.
- If a selector is unavoidable, never depend on localized button text.

The Nexus bridge is a local package extension. HUD is an optional managed custom node.<br>
Keep their repair policies separate.

## Portable Runtime

`LocalRuntime` is product runtime data, not source code.

| Folder | Purpose |
| --- | --- |
| `Packages` | Setup packages and bridge payload |
| `Installed` | Nexus-managed runtime installation |
| `Logs` | Nexus and ComfyUI logs |
| `State` | Small persisted runtime state |
| `Work` | Temporary setup and recovery work |

Runtime repair preserves user data: models, workflows, inputs, outputs, custom nodes, and external model paths.<br>
Core source recovery may replace managed source, but it must not erase runtime data.

## Tooling Path Lease

Git, pip, archive extraction, and virtual-environment creation run inside `NexusRuntimeEnvironment.RunToolingAsync`.<br>
Each outer call is one tooling request: it acquires a short Windows drive alias only for that request and returns it before the call completes.<br>
Server launch and normal runtime/UI work never enter this scope and always use physical paths.

- Persisted settings, server launch, logs, backups, diagnostics, and file tools use physical paths only.
- A nested tooling call reuses the current lease. Release happens after its last child process exits.
- Each Nexus process records an instance ID, process ID, and process start time in one per-user registry.<br>
  Multiple running Nexus instances can share the same alias; only the last live owner releases it.
- Startup maintenance removes only stale Nexus owners after validating the process identity, including PID reuse.
- Nexus records only aliases it creates. Existing user mappings may be reused but are never removed.
- If no supported drive letter is available, tooling fails clearly. Do not fall back to a junction or a long tooling path.
- `pip_cache_mode` and `pip_cache_path` remain a user-facing cache storage policy; the alias is passed only to the pip child process.

## Build And Check

### Developer Tool Entry Points

The repository root intentionally contains only `dev-help` in Batch, PowerShell, and Git Bash forms.<br>
Use `dev-help.bat`, `./dev-help`, or `.\dev-help.ps1` to print the common commands and their exact locations.

- Command Prompt: `tools\windows\dev-*.bat`.
- PowerShell: `tools\windows\dev-*.ps1`.
- Git Bash: `./tools/windows/dev-*`.
- `tools/windows`: all Windows-only developer tooling.

```powershell
dotnet build ComfyUI-Nexus.csproj -f net10.0-windows10.0.19041.0 -p:UseAppHost=false -p:OutputPath=artifacts\verify-build\ --no-restore
git diff --check
```

Delete `artifacts/verify-*` after a successful verification build.

Release packaging:

```bat
tools\windows\dev-build-as-binary.bat Release folder archive
```

For a signed Windows release, `--cert` validates a usable local Authenticode certificate before clean, restore, or publish:

```bat
tools\windows\dev-build-as-binary.bat Release folder clean archive --cert Release
```

For Microsoft Store submission, keep the Partner Center identity in the ignored `Store.Build.props` file and run:

```bat
tools\windows\dev-build-as-binary.bat Release app-store clean
```

To test the resulting Store-profile MSIX locally, use a signed copy rather than modifying the upload bundle:

```powershell
.\tools\windows\dev-test-store-package.ps1 help
.\tools\windows\dev-test-store-package.ps1 install -ResetData -Launch
```

Git Bash can call the same helper without an extension:

```bash
./tools/windows/dev-test-store-package help
./tools/windows/dev-test-store-package install -ResetData -Launch
```

The test helper creates a certificate whose subject matches the local Partner Center Publisher value,<br>
signs only a copy under `build\Store_*\local-test`, and installs that copy.<br>
The first install requests elevation once to trust the public test certificate in local-machine stores, which MSIX installation requires.<br>
It never changes the `.msixupload` file prepared for Partner Center.<br>
The local test tool installs Windows SDK Signing Tools through `winget` when `signtool.exe` is unavailable.<br>
Store settings and runtime data stay in LocalState.<br>
During setup tooling only, Nexus leases short drive aliases for managed or external ComfyUI roots and an optional custom pip cache.<br>
The user-facing settings, server process, logs, backups, and file tools always retain their physical paths.<br>
Each alias uses a per-process ownership record, so concurrent Nexus builds keep a shared alias until its final live owner exits.<br>
Startup cleanup validates process identity before removing stale Nexus mappings and never touches user-created mappings.<br>
Use `remove -ResetData -RemoveCertificate` after local QA to remove local app data, generated local-test MSIX copies,<br>
Nexus-owned temporary tooling drive mappings, and the test certificate from current-user and local-machine stores.<br>

This creates `build/Store_Release_<timestamp>/*.msixupload` containing the MSIX and `.appxsym` symbols.<br>
The Store flow rejects portable-only `archive`, `zip`, and `--cert` options.<br>
Store MSIX packages reserve their fourth version part as `0`; portable builds keep the full `NexusVersion` value.

## Smoke Checklist

- First run: setup route, setup completion, server boot.
- Server: launch, retry, restart, shutdown, reattach.
- Setup: Vanguard and Architect required order; optional edit state.
- Rail: assets, media, workflows, root switch, close and reopen.
- Settings: libraries, backup, restore, pending maintenance.
- Bridge: tabs, queue, GPU telemetry, manager actions.
- Diagnostics: `LocalRuntime/Logs/nexus-latest.log` and the matching ComfyUI server log.

## Crash Triage

1. Read `LocalRuntime/Logs/nexus-latest.log`.
2. Read the matching `comfy-server-*.log` for Python or server failures.
3. For a native app exit, inspect Windows Event Viewer and the crash dump before changing runtime services.
4. Classify the failure: UI lifetime, WebView/bridge, server process, or runtime package.

Do not use a managed log absence as proof that the native UI was healthy.

For a blocked UI, compare `[UI_TRACE]` with `[CONCURRENCY]` before changing timers or adding cancellation.<br>
The renderer can remain visible while the UI dispatcher is delayed.
