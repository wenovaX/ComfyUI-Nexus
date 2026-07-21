# ComfyUI-Nexus Refactor Plan

> Historical planning note. The current engineering contract lives in
> [DEVELOPERS.md](DEVELOPERS.md).

## Why We Are Changing Direction

The current codebase has useful intent behind the refactor, but several areas are now split across too many `partial` files while still sharing the same private state.

That creates three problems:

1. Renaming one field or concept forces edits across many files.
2. Ownership of state is unclear, so maintenance gets slower instead of faster.
3. File count goes up without actually reducing coupling.

The goal of this plan is not "split more." The goal is to reduce shared state, reduce rename blast radius, and make each feature have a clear owner.

## Core Principles

1. `partial` is only for XAML code-behind, platform-specific code, or generated/framework-driven split points.
2. Feature separation should prefer new types over more `partial` files.
3. State should live with the feature that owns it.
4. Public access should expose intent, not raw internals.
5. Shared utility code must be stateless. If it needs app state, it is not a utility.
6. Refactoring should happen in small, buildable steps.

## New Rules For `partial`

### Allowed

- `MainPage.xaml` + `MainPage.xaml.cs`
- `*.Windows.cs`, `*.Android.cs`, `*.MacCatalyst.cs`, etc.
- generated or framework-mandated splits

### Discouraged

- `MainPage.Tabs.cs`
- `MainPage.LeftChrome.cs`
- `AssetsBrowserView.Selection.cs`
- any split that exists only to separate methods while still sharing the same private fields

### Decision Test

If a file needs direct access to many fields from another file in the same class, do not make another `partial`.
Extract a real type instead.

## Current Hotspots

### `MainPage`

`MainPage` is currently acting as:

- page composition root
- boot/loading state controller
- bridge message dispatcher
- workflow tab state manager
- left rail layout controller
- control deck visibility controller
- keyboard/input coordinator

This is the biggest source of rename blast radius.

### `AssetsBrowserView`

`AssetsBrowserView` currently owns:

- tree state
- row cache
- selection state
- watcher lifecycle
- clipboard state
- bookmark rendering
- file operations

This is a second large hotspot. The files are separated, but the state is still centralized and shared.

## Target Architecture

## 1. `MainPage` becomes an orchestrator

`MainPage` should mainly do:

- initialize child views
- wire events between views and feature controllers
- hold minimal top-level page state
- forward lifecycle events

`MainPage` should stop directly owning every feature implementation detail.

### Extract from `MainPage`

#### A. Workflow tabs

Create a feature type such as:

- `Ui/Tabs/WorkflowTabController.cs`

Suggested responsibilities:

- store workflow tab state
- rebuild visible tabs
- manage visual order and overflow
- keep active tab visible
- update tab UI

Suggested state to move out of `MainPage`:

- `_lastSyncData`
- `_activeTabIndex`
- `_visualOrder`
- `_overflowMap`
- `_btnAddTab`
- `_btnOverflow`
- `_availableWidth`
- `_isBulkProcessing`
- `_activeWorkflowState`
- `_lastTabRebuildLogUtc`
- `_knownWorkflowFiles`
- `_bookmarkedWorkflows`

Suggested public surface:

- `ApplyWorkflowSync(JsonElement data)`
- `RefreshLayout(double availableWidth)`
- `SetBookmarks(IReadOnlyCollection<string> bookmarks)`
- `int ActiveTabIndex { get; }`

The controller can receive only the UI references it needs, instead of reaching into all of `MainPage`.

#### B. Boot and loading flow

Create a feature type such as:

- `Ui/Boot/NexusBootController.cs`

Suggested responsibilities:

- manage loading overlay state
- run success sequence
- handle reboot visual transitions
- update loading text and loading colors

Suggested state to move:

- `_isBooted`
- `_isRebooting`
- `_bootReadyHandled`
- `_stabilizedVisualStateApplied`
- `_isSystemLoading`
- `_isSuccessSequenceActive`

Suggested public surface:

- `StartLoading()`
- `HandleBootReady(string? agentId)`
- `Task PerformRebootVisualsAsync()`
- `bool IsSystemLoading { get; }`

#### C. Bridge message handling

Create a feature type such as:

- `Services/Bridge/NexusBridgeMessageRouter.cs`

Suggested responsibilities:

- parse raw bridge messages
- route message types
- keep diagnostics counters
- expose typed callbacks instead of letting `MainPage` inspect JSON everywhere

Suggested state to move:

- `_bridgeMessageCounts`
- `_workflowSyncAppliedCount`
- `_workflowSyncSkippedCount`
- `_lastBridgeSummaryUtc`
- `_bridgeDiagnosticsEnabled`

Suggested public surface:

- `Task ProcessAsync(string raw)`
- `bool DiagnosticsEnabled { get; set; }`

#### D. Rail layout and resize

Create a feature type such as:

- `Ui/Rail/RailLayoutController.cs`

Suggested responsibilities:

- track collapsed/expanded width
- animate rail open/close
- handle resize preview and final width
- compute reserved sidebar width

Suggested state to move:

- `_isFileRailExpanded`
- `_isRailAnimating`
- `_expandedRailWidth`
- `_railResizeStartWidth`
- `_pendingRailWidth`
- `_isRailResizeHovering`
- `_wasRailVisible`

Suggested public surface:

- `Task ToggleAsync()`
- `void ApplyState(bool isSystemLoading, bool isSuccessSequenceActive, bool isControlDeckVisible)`
- `double GetReservedWidth(bool isControlDeckVisible)`
- `bool IsExpanded { get; }`

## 2. `AssetsBrowserView` becomes a small view plus feature helpers

Keep `AssetsBrowserView` as the UI shell, but move behavior into collaborating types.

### Extract from `AssetsBrowserView`

#### A. Selection state

Create:

- `Views/Rail/Tools/Assets/Selection/AssetSelectionController.cs`

Suggested responsibilities:

- selected path set
- anchor path
- range selection
- selection normalization
- selected item queries

Suggested state to move:

- `_selectedPaths`
- `_selectionAnchorPath`

#### B. File watcher and refresh scheduling

Create:

- `Views/Rail/Tools/Assets/Watching/AssetTreeWatcher.cs`

Suggested responsibilities:

- watcher lifecycle
- dirty directory tracking
- periodic refresh scheduling

Suggested state to move:

- `_fileWatcher`
- `_refreshLoopCts`
- `_treeDirty`
- `_lastExternalChangeUtc`
- `_dirtyDirectories`

#### C. Clipboard and file operations

Create:

- `Views/Rail/Tools/Assets/FileOps/AssetFileOperationService.cs`

Suggested responsibilities:

- copy
- cut
- paste
- rename
- delete
- reveal in explorer
- clipboard ownership

Suggested state to move:

- `_clipboardPaths`
- `_clipboardCut`

#### D. Bookmark section state

Create:

- `Views/Rail/Tools/Assets/Chrome/AssetBookmarkController.cs`

Suggested responsibilities:

- bookmark list
- expanded/collapsed section state
- bookmark rendering decisions
- bookmark drop behavior

Suggested state to move:

- `_bookmarkedPaths`
- `_isBookmarksSectionExpanded`
- `_isCurrentSectionExpanded`
- `_isChromeAnimating`

### Keep in `AssetsBrowserView`

Only keep view-local state that is tightly coupled to actual row rendering:

- `_rootNodes`
- `_rowMap`
- `_rowCache`
- `_childrenCache`
- `_expandedPaths`
- `_treeLock`
- `_rootPath`

Even here, the long-term direction can still improve later, but this is a good first boundary.

## Public API Cleanup

## Current Problem

Some components expose too much internal structure directly. That makes the whole codebase depend on child view internals and raises the cost of UI changes.

## New Rule

Child views should prefer intent-based methods over public writable controls.

### Prefer this

```csharp
HeaderControl.SetQueueCount(count);
LoadingOverlayControl.SetStatus(text, color);
RailControl.SetExpanded(true);
```

### Avoid this

```csharp
HeaderControl.SomeInternalLabel.Text = "...";
LoadingOverlayControl.StatusLabel.Text = "...";
SomeView.SomeBorder.IsVisible = true;
```

## Review Checklist For Public Exposure

Expose public members only when at least one of these is true:

1. It is required by XAML or MAUI.
2. It represents a stable feature API.
3. It is read-only state that other components legitimately need.

If the caller only needs to "make something happen," prefer a method.

## Utility Class Guidelines

## Good Utility Candidates

These can stay or move into shared utility classes because they are stateless:

- path normalization
- workflow file validation
- pure JSON parsing helpers
- WebView script wrapper helpers
- animation helper methods that do not own state

Existing examples that are already close to this:

- `Ui/WebViewUtility.cs`
- `Ui/AssetActionDispatcher.cs`

## Not Utility Candidates

Do not make these global utilities:

- anything that reaches into `MainPage`
- anything that needs to know multiple child controls
- anything that stores mutable UI state
- anything that mixes view references with file system behavior

If it owns state or coordinates a feature, make it a controller/service, not a utility.

## Naming And Folder Direction

Use folders to reflect ownership, not file splitting.

Suggested direction:

- `Views/`
  - XAML views and code-behind only
- `Ui/`
  - UI-focused helpers and controllers
- `Services/`
  - bridge, file system, OS integration, external coordination
- `AssetHub/`
  - domain models and services dedicated to asset hub logic

Optional next step if the project grows further:

- `Features/Tabs/`
- `Features/Boot/`
- `Features/Rail/`

This is optional. The important part is clear ownership, not a perfect folder taxonomy.

## Recommended Execution Order

### Phase 0. Stabilize first

Before structural work:

1. fix broken string literals and encoding damage
2. make sure the project builds reliably again
3. avoid moving code while the baseline is unstable

### Phase 1. Stop future sprawl

1. stop adding new feature-only `partial` files
2. document the `partial` rules
3. keep new work inside clear owner classes

### Phase 2. Extract `MainPage` tabs

This is the best first extraction because:

- it has high coupling today
- it is logically self-contained
- it will reduce the most rename churn early

Success criteria:

- tab state no longer lives directly on `MainPage`
- tab layout logic is concentrated in one class
- `MainPage` just forwards tab events and size updates

### Phase 3. Extract `AssetsBrowserView` selection

This is the safest second step because:

- selection is a coherent state machine
- it is widely referenced today
- moving it will clarify many later file operations

Success criteria:

- selected-path state is owned by one class
- file ops call selection APIs instead of touching shared fields

### Phase 4. Extract bridge and boot flow

After tabs and selection are stable:

1. move bridge parsing/routing out of `MainPage`
2. move boot/loading visual state into a dedicated controller

### Phase 5. Reduce public UI internals

Once ownership is clearer:

1. replace public control pokes with intent methods
2. shrink the surface area of view internals

## First Concrete Refactor Batch

The first batch should stay small and low risk:

1. add this plan
2. fix encoding/string corruption in boot/loading files
3. extract workflow tab management into a controller
4. keep behavior unchanged

This first batch is enough to prove the direction without destabilizing the whole app.

## Non-Goals

These are intentionally not goals right now:

- rewriting the whole app into MVVM
- introducing DI everywhere
- renaming folders for aesthetics alone
- splitting files further without reducing coupling

## Definition Of Success

We are moving in the right direction when:

1. renaming one field affects far fewer files
2. each feature has an obvious state owner
3. `MainPage` reads like a coordinator, not a giant feature bucket
4. views expose fewer raw internals
5. new code naturally lands in owner types instead of new `partial` files
