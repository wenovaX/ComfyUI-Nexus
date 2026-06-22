import { getRulesByPolicy } from "./registry.js";

const STYLE_IDS = {
    base: "nexus-exile-style",
    relocate: "nexus-exile-relocate-style",
    polish: "nexus-exile-polish-style",
};

function ensureStyle(id, text) {
    const style = document.getElementById(id) || document.createElement("style");
    style.id = id;
    style.textContent = text;
    (document.head || document.documentElement).appendChild(style);
    return style;
}

function joinSelectors(rules) {
    return rules.flatMap((rule) => rule.selectors).filter(Boolean).join(",\n            ");
}

function buildRule(selectors, declarations) {
    if (!selectors) return "";
    const guardedSelectors = selectors
        .split(",")
        .map((selector) => selector.trim())
        .filter(Boolean)
        .map((selector) => `html:not(.nexus-exile-disabled) ${selector}`)
        .join(",\n            ");
    return `
            ${guardedSelectors} {
${declarations}
            }
        `;
}

export function mountExileStyles() {
    const hideSelectors = joinSelectors(getRulesByPolicy("hide"));
    const stripSelectors = joinSelectors(getRulesByPolicy("strip"));
    const reservedSourceSelectors = '[data-nexus-exile-source]';
    const bottomDockSelectors = [
        '.absolute.right-0[class*="bottom-[62px]"][class*="z-1300"]',
        '.minimap-main-container.absolute.right-0[class*="bottom-[54px]"]',
    ].join(",\n            ");
    const topLeftAppModeSelectors = [
        '.absolute.top-2[class*="left-4.5"]:has(button[aria-label="App builder"])',
        '.absolute.top-2[class*="left-4.5"]:has(button[aria-label="Apps"])',
    ].join(",\n            ");
    const standaloneAppBuilderSelector = 'button.absolute.left-4[aria-label="App builder"]';
    const hideRule = buildRule(hideSelectors, `                position: fixed !important;
                top: -200vh !important;
                left: -200vw !important;
                height: 1px !important;
                width: 1px !important;
                overflow: hidden !important;
                z-index: -1000 !important;
                opacity: 0 !important;
                pointer-events: none !important;`);
    const stripRule = buildRule(stripSelectors, `                background: transparent !important;
                border: none !important;
                box-shadow: none !important;
                pointer-events: none !important;
                visibility: hidden !important;`);
    const reservedSourceRule = buildRule(reservedSourceSelectors, `                position: fixed !important;
                top: -200vh !important;
                left: -200vw !important;
                height: 1px !important;
                width: 1px !important;
                overflow: hidden !important;
                opacity: 0 !important;
                pointer-events: none !important;
                z-index: -1000 !important;`);

    ensureStyle(STYLE_IDS.base, `
            html:not(.nexus-exile-disabled) { --workflow-tabs-height: 0px !important; }
${hideRule}${stripRule}${reservedSourceRule}
        `);

    ensureStyle(STYLE_IDS.relocate, "");
    ensureStyle(STYLE_IDS.polish, `
            ${bottomDockSelectors} {
                bottom: 0px !important;
            }

            ${topLeftAppModeSelectors} {
                top: 72px !important;
                left: 18px !important;
            }

            ${standaloneAppBuilderSelector} {
                top: calc(var(--workflow-tabs-height) + 76px) !important;
            }
        `);
}
