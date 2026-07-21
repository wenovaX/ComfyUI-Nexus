import { app } from "/scripts/app.js";
import { api } from "/scripts/api.js";
import { CANVAS_MODES, getCanvasMode } from "./canvas_mode.js";

export function setupQueueTracker(bridge) {
    let queued = false;
    let animationFrameId = null;
    let disposed = false;

    const checkQueue = () => {
        animationFrameId = null;
        queued = false;
        if (disposed) return;

        const input = document.querySelector('input.comfy-queue-count, input[data-testid="queue-count-input"]');
        if (!input) return;

        const count = parseInt(input.value) || 0;
        if (bridge.lastQueueCount !== count) {
            bridge.lastQueueCount = count;
            bridge.send("QUEUE_UPDATE", { count });
        }
    };

    const scheduleCheck = () => {
        if (disposed || queued) return;
        queued = true;
        animationFrameId = requestAnimationFrame(checkQueue);
    };

    const onQueueInput = (event) => {
        if (event.target?.matches?.('input.comfy-queue-count, input[data-testid="queue-count-input"]')) {
            scheduleCheck();
        }
    };

    bridge.lastQueueCount = -1;
    const fallbackTimer = window.setInterval(scheduleCheck, 1500);
    document.addEventListener("input", onQueueInput, true);
    document.addEventListener("change", onQueueInput, true);
    scheduleCheck();

    return () => {
        if (disposed) return;
        disposed = true;
        window.clearInterval(fallbackTimer);
        document.removeEventListener("input", onQueueInput, true);
        document.removeEventListener("change", onQueueInput, true);
        if (animationFrameId !== null) {
            cancelAnimationFrame(animationFrameId);
            animationFrameId = null;
        }
        queued = false;
    };
}

export function setupModeTracker(bridge) {
    bridge.lastMode = null;
    let queued = false;
    let animationFrameId = null;
    let disposed = false;

    const checkMode = () => {
        animationFrameId = null;
        queued = false;
        if (disposed) return;

        const canvasMode = getCanvasMode();
        const mode = canvasMode === CANVAS_MODES.hand
            ? CANVAS_MODES.handUpper
            : canvasMode === CANVAS_MODES.select
                ? CANVAS_MODES.selectUpper
                : "UNKNOWN";

        if (bridge.lastMode !== mode && mode !== "UNKNOWN") {
            bridge.lastMode = mode;
            bridge.send("MODE_UPDATE", { mode });
        }
    };

    const scheduleCheck = () => {
        if (disposed || queued) return;
        queued = true;
        animationFrameId = requestAnimationFrame(checkMode);
    };

    const fallbackTimer = window.setInterval(scheduleCheck, 1500);
    document.addEventListener("click", scheduleCheck, true);
    document.addEventListener("keyup", scheduleCheck, true);
    scheduleCheck();

    return () => {
        if (disposed) return;
        disposed = true;
        window.clearInterval(fallbackTimer);
        document.removeEventListener("click", scheduleCheck, true);
        document.removeEventListener("keyup", scheduleCheck, true);
        if (animationFrameId !== null) {
            cancelAnimationFrame(animationFrameId);
            animationFrameId = null;
        }
        queued = false;
    };
}

export function setupGpuRelay(bridge) {
    let lastGpuPayload = null;
    let isExecuting = null;
    let disposed = false;
    const abortController = new AbortController();

    const computeRunningState = () => {
        const isAppRunning = app.running_node_id !== null && typeof app.running_node_id !== "undefined";
        if (typeof isExecuting === "boolean") {
            return isExecuting || isAppRunning;
        }

        return isAppRunning || !!lastGpuPayload?.is_running;
    };

    const relayGpuStats = (payload) => {
        if (disposed) return;

        try {
            lastGpuPayload = payload && typeof payload === "object" ? { ...payload } : payload;
            const mergedPayload = {
                ...(lastGpuPayload || {}),
                is_running: computeRunningState(),
            };
            const serialized = JSON.stringify(getGpuStateSignature(mergedPayload));
            if (serialized === bridge.lastGpuPayload) return;
            bridge.lastGpuPayload = serialized;
            bridge.send("GPU_STATS", mergedPayload);
        } catch (err) {
            bridge.log?.(`Nexus GPU relay error: ${err?.message || err}`);
        }
    };

    const relayCurrentGpuState = () => {
        if (disposed || !lastGpuPayload) return;
        relayGpuStats(lastGpuPayload);
    };

    // HUD emits a fresh timestamp every sample. It is transport metadata, not a
    // UI state change, so never use it to create a new native bridge message.
    const getGpuStateSignature = (payload) => {
        if (!payload || typeof payload !== "object") return payload;
        const { timestamp, time, updated_at, last_updated, ...state } = payload;
        return state;
    };

    const onHudGpuStats = (e) => relayGpuStats(e.detail);

    const onExecuting = (e) => {
        isExecuting = !!e.detail;
        relayCurrentGpuState();
    };

    const onStatus = (e) => {
        if (e.detail && e.detail.exec_info) {
            isExecuting = e.detail.exec_info.queue_remaining > 0;
            relayCurrentGpuState();
        }
    };

    api.addEventListener("hud_gpu_stats", onHudGpuStats);
    api.addEventListener("comfyui_hud_gpu_stats", onHudGpuStats);
    api.addEventListener("executing", onExecuting);
    api.addEventListener("status", onStatus);
    api.getQueue()
        .then((q) => {
            if (disposed) return;
            isExecuting = !!q && (q.Running?.length > 0 || q.Pending?.length > 0);
            relayCurrentGpuState();
        })
        .catch(() => {});
    fetch("/hud/gpu_stats", { signal: abortController.signal })
        .then((r) => r.json())
        .then((data) => relayGpuStats(data))
        .catch(() => {});

    return () => {
        if (disposed) return;
        disposed = true;
        abortController.abort();
        api.removeEventListener("hud_gpu_stats", onHudGpuStats);
        api.removeEventListener("comfyui_hud_gpu_stats", onHudGpuStats);
        api.removeEventListener("executing", onExecuting);
        api.removeEventListener("status", onStatus);
        lastGpuPayload = null;
    };
}
