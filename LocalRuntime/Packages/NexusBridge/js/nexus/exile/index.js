import { installExileDebugApi } from "./debug.js";
import { applyHidePolicy } from "./policy.js";
import { getDefaultExileSelectorGroups, getExileSelectorList, getExileSelectorText, getRulesByPolicy } from "./registry.js";
import { scanExileTargets, startExileScanner, stopExileScanner } from "./scanner.js";
import { installSourceApi } from "./sources.js";
import { mountExileStyles } from "./styles.js";

export { getDefaultExileSelectorGroups, getExileSelectorList, getExileSelectorText } from "./registry.js";
export { createSourceApi, getSourceSnapshot, installSourceApi, resolveSource, scanSources } from "./sources.js";

export function applyExile(el) {
    const fallbackRule = getRulesByPolicy("hide")[0] || { id: "manual-hide", reason: "Manual Nexus exile request." };
    return applyHidePolicy(el, fallbackRule);
}

export function exileTargets() {
    return scanExileTargets();
}

export function ensureExileStyle() {
    mountExileStyles();
    return document.getElementById("nexus-exile-style");
}

function releaseExileInlineStyles() {
    const inlineProperties = [
        "position",
        "top",
        "left",
        "width",
        "height",
        "min-width",
        "min-height",
        "max-width",
        "max-height",
        "padding",
        "margin",
        "border",
        "overflow",
        "opacity",
        "pointer-events",
        "visibility",
        "z-index",
    ];

    document.querySelectorAll("[data-nexus-exile-policy], [data-nexus-exile-source-hidden]").forEach((el) => {
        inlineProperties.forEach((property) => el.style.removeProperty(property));
        delete el.dataset.nexusExilePolicy;
        delete el.dataset.nexusExileRule;
        delete el.dataset.nexusExileReason;
        delete el.dataset.nexusExileSource;
        delete el.dataset.nexusExileSourceHidden;
        delete el.dataset.nexusExileTouchedAt;
    });
}

export function setExileIsolationEnabled(bridge, enabled) {
    const isEnabled = Boolean(enabled);
    document.documentElement.classList.toggle("nexus-exile-disabled", !isEnabled);

    if (!isEnabled) {
        bridge.uiIsolationDisabled = true;
        stopExileScanner(bridge);
        releaseExileInlineStyles();
        bridge.exileScannerState = "disabled";
        bridge.exileScannerMessage = "UI isolation disabled for debugging.";
        bridge.log?.("[Exile] UI isolation disabled for debugging.");
        return false;
    }

    bridge.uiIsolationDisabled = false;
    mountExileStyles();
    const run = startExileScanner(bridge);
    bridge.exileScannerState = "watching";
    bridge.exileScannerMessage = "UI isolation enabled.";
    bridge.log?.("[Exile] UI isolation enabled.");
    return run;
}

export function setupExileSystem(bridge) {
    mountExileStyles();
    installSourceApi(bridge);
    installExileDebugApi(bridge);
    const run = startExileScanner(bridge);
    window.NexusExileScan = run;
    window.NexusExileStop = () => stopExileScanner(bridge);
    window.NexusExileIsolation = (enabled) => setExileIsolationEnabled(bridge, enabled);
    window.NexusExileStatus = () => ({
        state: bridge.exileScannerState || "unknown",
        message: bridge.exileScannerMessage || "",
        scan: bridge.lastExileScan || null,
        observerActive: Boolean(bridge.exileObserver),
        timerActive: Boolean(bridge.exileScanTimer),
    });
    return run;
}

export function stopExileSystem(bridge) {
    stopExileScanner(bridge);
}
