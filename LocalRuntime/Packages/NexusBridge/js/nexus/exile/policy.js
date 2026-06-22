import { EXILE_DATASET, getRulesByPolicy } from "./registry.js";

export function markElement(el, policy, rule) {
    if (!el || !rule) return;
    if (el.dataset[EXILE_DATASET.policy] === policy && el.dataset[EXILE_DATASET.rule] === rule.id) {
        return;
    }

    el.dataset[EXILE_DATASET.policy] = policy;
    el.dataset[EXILE_DATASET.rule] = rule.id;
    el.dataset[EXILE_DATASET.reason] = rule.reason || "";
    el.dataset[EXILE_DATASET.touchedAt] = String(Date.now());
}

export function isProtectedElement(el) {
    if (!el) return true;
    return getRulesByPolicy("protect").some((rule) => rule.selectors.some((selector) => el.matches?.(selector) || el.closest?.(selector)));
}

export function applyHidePolicy(el, rule) {
    if (!el || isProtectedElement(el)) return false;
    if (document.documentElement.classList.contains("nexus-exile-disabled")) return false;
    if (el.dataset[EXILE_DATASET.policy] === "hide" && el.dataset[EXILE_DATASET.rule] === rule.id) {
        return false;
    }

    markElement(el, "hide", rule);
    el.style.setProperty("position", "fixed", "important");
    el.style.setProperty("top", "-200vh", "important");
    el.style.setProperty("left", "-200vw", "important");
    el.style.setProperty("width", "1px", "important");
    el.style.setProperty("height", "1px", "important");
    el.style.setProperty("overflow", "hidden", "important");
    el.style.setProperty("opacity", "0", "important");
    el.style.setProperty("pointer-events", "none", "important");
    el.style.setProperty("z-index", "-1000", "important");
    return true;
}

export function applyStripPolicy(el, rule) {
    if (!el || isProtectedElement(el)) return false;
    if (document.documentElement.classList.contains("nexus-exile-disabled")) return false;
    if (el.dataset[EXILE_DATASET.policy] === "strip" && el.dataset[EXILE_DATASET.rule] === rule.id) {
        return false;
    }

    markElement(el, "strip", rule);
    return true;
}

export function applyRelocatePolicy(el, rule) {
    if (!el) return false;
    if (document.documentElement.classList.contains("nexus-exile-disabled")) return false;
    if (el.dataset[EXILE_DATASET.policy] === "relocate" && el.dataset[EXILE_DATASET.rule] === rule.id) {
        return false;
    }

    markElement(el, "relocate", rule);
    return true;
}

export function protectElementPath(el, ruleId = "manual-protect") {
    if (!el) return;
    let curr = el;
    while (curr && curr !== document.body) {
        if (curr.dataset[EXILE_DATASET.policy] === "protect" && curr.dataset[EXILE_DATASET.rule] === ruleId) {
            curr = curr.parentElement;
            continue;
        }

        curr.dataset[EXILE_DATASET.policy] = "protect";
        curr.dataset[EXILE_DATASET.rule] = ruleId;
        curr = curr.parentElement;
    }
}
