import { EXILE_DATASET, getAllRules } from "./registry.js";
import { getSourceSnapshot } from "./sources.js";

export function describeExiledElement(el) {
    if (!el) return null;
    return {
        tag: el.tagName?.toLowerCase() || "unknown",
        id: el.id || "",
        className: typeof el.className === "string" ? el.className : "",
        policy: el.dataset?.[EXILE_DATASET.policy] || "",
        rule: el.dataset?.[EXILE_DATASET.rule] || "",
        source: el.dataset?.[EXILE_DATASET.source] || "",
        reason: el.dataset?.[EXILE_DATASET.reason] || "",
        touchedAt: el.dataset?.[EXILE_DATASET.touchedAt] || "",
        text: (el.textContent || "").trim().slice(0, 120),
    };
}

export function getExileDebugSnapshot() {
    const touched = Array.from(document.querySelectorAll("[data-nexus-exile-policy]")).map(describeExiledElement);
    const sourceSnapshot = getSourceSnapshot();
    const lastScan = window.__nexusBridge?.lastExileScan || null;
    return {
        rules: getAllRules().map((rule) => ({
            id: rule.id,
            policy: rule.policy,
            optional: Boolean(rule.optional),
            selectors: rule.selectors || [],
            sources: rule.sources || {},
            reason: rule.reason,
        })),
        sources: sourceSnapshot.sources,
        sourceCounts: sourceSnapshot.counts,
        scanner: lastScan ? {
            complete: Boolean(lastScan.complete),
            missing: lastScan.missing || [],
            counts: lastScan.counts || {},
        } : null,
        touched,
        counts: touched.reduce((acc, item) => {
            acc[item.policy] = (acc[item.policy] || 0) + 1;
            return acc;
        }, {}),
    };
}

export function installExileDebugApi(bridge) {
    window.NexusExileDebug = () => {
        const snapshot = getExileDebugSnapshot();
        console.table(snapshot.touched);
        console.table(snapshot.sources);
        return snapshot;
    };
    window.NexusExileRules = () => getAllRules();
    bridge.getExileDebugSnapshot = getExileDebugSnapshot;
}
