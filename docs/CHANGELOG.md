# Changelog

Language: [English](CHANGELOG.md) | [한국어](CHANGELOG.ko.md)

All notable user-facing changes are recorded here.

## [1.0.0.2] - 2026-07-21

### Stability

- Removed Windows MAUI native shadow usage from the interactive shell to avoid alpha-mask failures and unnecessary compositor work.
- Consolidated server start, stop, restart, recovery, and exit through the server lifecycle coordinator.
- Added owner-scoped operation lanes and lifecycle generation checks so stale UI, watcher, media, GPU, and bridge results are discarded safely.
- Scoped asset watchers to the active rail tool and root only; closed or hidden tools no longer scan or patch the UI.

### Loading And Visuals

- Made loading a real release gate: the workspace is revealed only after the server, bridge, shell services, and visible UI surfaces are ready.
- Added cached frame-based WebP playback for setup, loading, server boot, header, command deck, and gauge visuals.
- Replaced recurring transform-based effects in the main user flows with lifecycle-owned visual controllers.
- Added server boot idle, booting, success, and failure visual states with retry feedback.

### Runtime And Tools

- Added managed custom-node dependency plans, including Python ABI-aware Dlib wheels and FaceAnalysis model downloads.
- Unified local server probing around listener-first HTTP readiness checks.
- Added a standalone Control Deck for server-only diagnostics and recovery actions.
- Improved portable release packaging, optional local signing, archive output, and interactive version updates.

### Diagnostics

- Added lifecycle, concurrency, browser-host, motion, and crash-capture snapshots for focused troubleshooting.
- Reduced normal animation cache, frame gauge, and startup prewarm messages to trace-level logging.

### Known Limits

- Nexus is currently Windows-only and expects an NVIDIA CUDA GPU for the managed runtime path.
- ComfyUI remains the workflow engine inside WebView2; Nexus owns the surrounding desktop shell and lifecycle.
