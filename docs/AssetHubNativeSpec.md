# Asset Hub Native Spec

## Goal

Move non-node HUD Asset Hub behavior out of the WebView and into the native `ComfyUI-Nexus` shell.

The JS source of truth for the current behavior is:

- `E:\DevelopWorkspace\AI\Image\ComfyUI\custom_nodes\ComfyUI-HUD\js\asset_hub_browser.js`
- supporting files:
  - `file_manager_api.js`
  - `asset_hub_state.js`
  - `asset_hub_browser_utils.js`
  - `asset_hub_browser_styles.js`

## Current JS Feature Surface

The browser-based HUD Asset Hub currently bundles these concerns into one module:

- Window shell
  - floating modal window
  - draggable / resizable
  - show / hide toggle
- Navigation
  - current path
  - back / forward history
  - navigate up
  - address / breadcrumb rendering
  - bookmark sidebar
- File listing
  - list directory contents via `/hud/file-manager/list`
  - sort by date/name
  - search filter
  - pooled card rendering for large sets
- Selection state
  - single select
  - multi-select
  - range select
  - select all / deselect
- Clipboard actions
  - copy
  - cut
  - paste
- File actions
  - refresh
  - create folder
  - delete
  - rename
  - open in OS
  - add/remove bookmark
  - upload local files
  - pick directory
- Media affordances
  - image / video thumbnail awareness
  - media viewer integration
- Live update
  - background poll / dirty hash refresh
- DnD bridge
  - drag items out of hub
  - drop onto supported ComfyUI nodes

## Native Migration Principle

Anything that is not fundamentally tied to graph-node canvas interaction should live in the native shell.

That means the following should move native first:

- window / panel shell
- path navigation
- file listing
- search / sort
- bookmarks
- selection / clipboard state
- file operations
- open in OS
- local picker integration

The following remain WebView-side for now or need a separate bridge design:

- dropping assets directly onto ComfyUI nodes
- canvas highlight during dragover
- node-specific payload injection

## Native V1 Scope

V1 should implement these native capabilities:

- asset item model
- path history model
- selection model
- clipboard model
- bookmark persistence in local app data
- filesystem-backed listing for:
  - `ComfyUI/input`
  - `ComfyUI/output`
  - absolute directories
- file type inference:
  - file vs directory
  - image/video flags
- operations:
  - create folder
  - delete
  - copy
  - move
  - rename
  - open in OS

## Deferred After V1

- native Asset Hub UI panel
- thumbnail generation / caching
- virtualized native grid
- live file system watch instead of polling
- bridge command to inject selected asset into current node
- native drag and drop into WebView graph targets

## State Mapping

### JS -> Native

- `HUDSelectionState` -> `AssetHubSelectionState`
- `HUDClipboardState` -> `AssetHubClipboardState`
- `HUDPathHistory` -> `AssetHubPathHistory`
- `HUDFileManagerApi` -> `AssetHubNativeService`

## Persistence

Native Asset Hub bookmarks should live in local app data:

- `%LocalAppData%/ComfyUI-Nexus/asset-hub-bookmarks.json`

This avoids coupling native bookmarks to the WebView plugin lifecycle.

## Service Contract

`AssetHubNativeService` should provide:

- `ListAsync(path)`
- `GetDefaultRoots(comfyRoot)`
- `LoadBookmarksAsync()`
- `SaveBookmarksAsync(bookmarks)`
- `AddBookmarkAsync(path)`
- `RemoveBookmarkAsync(path)`
- `CreateFolderAsync(parent, name)`
- `DeleteAsync(paths)`
- `CopyAsync(sources, destinationDirectory)`
- `MoveAsync(sources, destinationDirectory)`
- `RenameAsync(path, newName)`
- `OpenInOs(path)`

## UI Direction

The future native UI should feel like a shell tool, not a web popup:

- docked or modal native panel
- native keyboard handling
- OS picker integration
- WebView used only when graph-specific context is required

