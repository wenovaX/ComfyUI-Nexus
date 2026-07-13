# Nexus for ComfyUI: Developer Guide

Language: [English](DEVELOPERS.md) | [한국어](DEVELOPERS.ko.md)

This is the working contract for Nexus. Keep it short, explicit, and current.

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

| Area | Owner | Keep it responsible for |
| --- | --- | --- |
| Startup route | `StartupRouteDecider` | Setup, boot, reattach, or direct load decision |
| Server lifecycle | `NexusServerLifecycleCoordinator` | Startup, restart, maintenance, server stop, and app-exit hand-off |
| Server process | `ComfyServerProcessService` / `ComfyServerProcessRegistry` | Launch, readiness, process registration, native tree termination, and verification |
| Local server checks | `LocalServerProbe` | Listener state and one-shot HTTP probes |
| Setup sequence | `InitiationSequenceRunner` | Required step order and completion |
| Setup scrolling | `ProductSetupView` | Focus owner and all setup scroll movement |
| Popup lifecycle | `NexusPopupManager` | Shell, animation, refresh, close |
| Latest-wins work | `NexusLatestOperationCoordinator` | One running operation and one newest pending request |
| Repeating motion | `NexusMotionController` | UI-thread motion, lifecycle stop, resting state |
| Managed custom-node dependencies | `ManagedCustomNodeDependencyInstaller` | Explicit repository, requirements, or bootstrap install mode |
| Web bridge | `NexusWebViewBridge` | Typed C# to JS calls |

If a new feature cannot name its owner, stop before adding code.

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

Use latest-wins work for refreshes where only the newest result matters:

- workflow index refresh
- media snapshot bursts
- GPU selector discovery
- rail scans and deferred presentation work

New requests replace the pending request. They do not cancel the running request.<br>
Before a side effect, check the operation lease. Drop stale results.

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

## Build And Check

```powershell
dotnet build ComfyUI-Nexus.csproj -f net10.0-windows10.0.19041.0 -p:UseAppHost=false -p:OutputPath=artifacts\verify-build\ --no-restore
git diff --check
```

Delete `artifacts/verify-*` after a successful verification build.

Release packaging:

```bat
dev-build-as-binary.bat Release folder archive
```

For a signed Windows release, `--cert` validates a usable local Authenticode certificate before clean, restore, or publish:

```bat
dev-build-as-binary.bat Release folder clean archive --cert Release
```

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
