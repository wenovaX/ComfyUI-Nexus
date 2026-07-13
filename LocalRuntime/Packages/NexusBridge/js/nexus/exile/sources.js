import { EXILE_DATASET, getSourceRules } from "./registry.js";

function getSourceSelector(source) {
    if (!source) return "";
    return typeof source === "string" ? source : source.selector || "";
}

function getSourceMode(source) {
    if (!source || typeof source === "string") return "element";
    return source.mode || "element";
}

function queryFirst(selector) {
    if (!selector) return null;
    if (Array.isArray(selector)) {
        for (const item of selector) {
            const found = queryFirst(item);
            if (found) return found;
        }
        return null;
    }

    try {
        return document.querySelector(selector);
    } catch (error) {
        return null;
    }
}

function queryAll(selector) {
    if (!selector) return [];
    if (Array.isArray(selector)) {
        return [...new Set(selector.flatMap((item) => queryAll(item)))];
    }

    try {
        return Array.from(document.querySelectorAll(selector));
    } catch (error) {
        return [];
    }
}

function hideReservedSource(el) {
    if (!el) return;
    if (document.documentElement.classList.contains("nexus-exile-disabled")) return;
    if (el.dataset.nexusExileSourceHidden === "true") return;

    el.style.setProperty("position", "fixed", "important");
    el.style.setProperty("top", "-200vh", "important");
    el.style.setProperty("left", "-200vw", "important");
    el.style.setProperty("width", "1px", "important");
    el.style.setProperty("height", "1px", "important");
    el.style.setProperty("min-width", "1px", "important");
    el.style.setProperty("min-height", "1px", "important");
    el.style.setProperty("max-width", "1px", "important");
    el.style.setProperty("max-height", "1px", "important");
    el.style.setProperty("padding", "0", "important");
    el.style.setProperty("margin", "0", "important");
    el.style.setProperty("border", "0", "important");
    el.style.setProperty("overflow", "hidden", "important");
    el.style.setProperty("opacity", "0", "important");
    el.style.setProperty("pointer-events", "none", "important");
    el.style.setProperty("visibility", "hidden", "important");
    el.style.setProperty("z-index", "-1000", "important");
    el.dataset.nexusExileSourceHidden = "true";
}

function markSource(el, rule, key) {
    if (!el) return;
    if (document.documentElement.classList.contains("nexus-exile-disabled")) return;
    const policy = rule.policy || "reserve";
    const alreadyMarked =
        el.dataset[EXILE_DATASET.policy] === policy &&
        el.dataset[EXILE_DATASET.rule] === rule.id &&
        el.dataset[EXILE_DATASET.source] === key;

    if (alreadyMarked && el.dataset.nexusExileSourceHidden === "true") {
        return;
    }

    el.dataset[EXILE_DATASET.policy] = policy;
    el.dataset[EXILE_DATASET.rule] = rule.id;
    el.dataset[EXILE_DATASET.source] = key;
    el.dataset[EXILE_DATASET.reason] = rule.reason || "";
    if (!alreadyMarked) {
        el.dataset[EXILE_DATASET.touchedAt] = String(Date.now());
    }
    hideReservedSource(el);
}

function getRuleSources(rule) {
    return Object.entries(rule.sources || {}).map(([key, source]) => ({
        key,
        mode: getSourceMode(source),
        selector: getSourceSelector(source),
        optional: Boolean(source?.optional || rule.optional),
    }));
}

export function resolveSource(key) {
    for (const rule of getSourceRules()) {
        for (const source of getRuleSources(rule)) {
            if (source.key !== key) continue;
            const element = queryFirst(source.selector);
            if (element) return { ...source, rule, element };
        }
    }
    return null;
}

export function scanSources() {
    const sources = [];
    for (const rule of getSourceRules()) {
        for (const source of getRuleSources(rule)) {
            const elements = queryAll(source.selector);
            for (const element of elements) {
                markSource(element, rule, source.key);
            }
            sources.push({
                ruleId: rule.id,
                policy: rule.policy || "reserve",
                key: source.key,
                mode: source.mode,
                optional: source.optional,
                selector: Array.isArray(source.selector) ? source.selector.join(", ") : source.selector,
                found: elements.length > 0,
                count: elements.length,
            });
        }
    }
    return sources;
}

export function getSourceSnapshot() {
    const sources = scanSources();
    return {
        sources,
        counts: sources.reduce((acc, source) => {
            const bucket = source.found ? "found" : "missing";
            acc[bucket] = (acc[bucket] || 0) + 1;
            return acc;
        }, { found: 0, missing: 0 }),
    };
}

export function createSourceApi() {
    return {
        get(key) {
            return resolveSource(key)?.element || null;
        },
        click(key) {
            const source = resolveSource(key);
            source?.element?.click?.();
            return Boolean(source?.element);
        },
        focus(key) {
            const source = resolveSource(key);
            source?.element?.focus?.();
            return Boolean(source?.element);
        },
        read(key) {
            const element = resolveSource(key)?.element;
            if (!element) return undefined;
            if ("checked" in element && element.type === "checkbox") return element.checked;
            if ("value" in element) return element.value;
            return element.textContent;
        },
        write(key, value) {
            const element = resolveSource(key)?.element;
            if (!element) return false;
            if ("checked" in element && element.type === "checkbox") {
                element.checked = Boolean(value);
                element.dispatchEvent(new Event("change", { bubbles: true }));
                return true;
            }
            if ("value" in element) {
                element.value = value;
                element.dispatchEvent(new Event("input", { bubbles: true }));
                element.dispatchEvent(new Event("change", { bubbles: true }));
                return true;
            }
            return false;
        },
        snapshot: getSourceSnapshot,
    };
}

export function installSourceApi(bridge) {
    const api = createSourceApi();
    window.NexusSources = api;
    bridge.sources = api;
    bridge.getSourceSnapshot = getSourceSnapshot;
    return api;
}
