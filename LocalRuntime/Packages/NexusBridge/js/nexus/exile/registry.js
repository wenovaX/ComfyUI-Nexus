export const EXILE_DATASET = {
    policy: "nexusExilePolicy",
    rule: "nexusExileRule",
    reason: "nexusExileReason",
    source: "nexusExileSource",
    touchedAt: "nexusExileTouchedAt",
};

export const EXILE_POLICIES = {
    protect: "protect",
    hide: "hide",
    strip: "strip",
    reserve: "reserve",
    rehost: "rehost",
    relocate: "relocate",
};

export const exileRegistry = {
    protect: [],
    hide: [
        {
            id: "workflow-tab-chrome",
            optional: true,
            selectors: [
                ".workflow-tabs-container",
                '[role="toolbar"]:has([data-testid="zoom-controls-button"]):has([data-testid="toggle-minimap-button"])',
                '.p-buttongroup:has([data-testid="zoom-controls-button"]):has([data-testid="toggle-minimap-button"])',
                '.p-buttongroup:has([data-testid="zoom-controls-button"]):has([data-testid="toggle-link-visibility-button"])',
            ],
            reason: "Nexus owns workflow tabs and primary canvas chrome.",
        },
        {
            id: "manager-toolbar-buttons",
            optional: true,
            selectors: [
                'button:has([class*="icon-[lucide--share]"])',
                'button:has([class*="icon-[lucide--upload]"])',
                'button:has([class*="icon-[lucide--trash]"])',
                'button:has(.mdi-share)',
                'button:has(.mdi-vacuum)',
                'button:has(.mdi-vacuum-outline)',
                'button:has([class*="icon-[mdi--share]"])',
                'button:has([class*="icon-[mdi--vacuum]"])',
                'button:has([class*="icon-[mdi--vacuum-outline]"])',
            ],
            reason: "Nexus owns visible Manager toolbar actions, except Favorites which keeps its native click handler reserved.",
        },
        {
            id: "comfy-actionbar-shell",
            selectors: [
                ".actionbar-container",
                '[data-testid="legacy-topbar-container"]',
                ".p-panel.actionbar",
            ],
            reason: "Nexus owns the visible top action bar; native source controls remain reserved underneath.",
        },
        {
            id: "comfy-sidebar-utility-group",
            optional: true,
            selectors: [
                ".sidebar-item-group",
                ".comfy-help-center-btn",
                'button:has([class*="icon-[lucide--settings]"])',
            ],
            reason: "Nexus owns visible sidebar utility buttons while preserving their native click handlers.",
        },
        {
            id: "workflow-panel-header",
            optional: true,
            selectors: [
                'header:has(button[id^="reka-dropdown-menu-trigger"]):has([id^="reka-dropdown-menu-trigger"][aria-haspopup="menu"])',
                'header:has(.icon-\\[lucide--menu\\]):has(.icon-\\[lucide--panels-top-left\\])',
            ],
            reason: "Nexus owns workflow/menu header chrome; no native source is needed.",
        },
    ],
    strip: [],
    reserve: [
        {
            id: "toolbar-action-sources",
            reason: "Nexus owns visible toolbar actions while preserving native ComfyUI/Manager click handlers as hidden event sources.",
            sources: {
                manager: {
                    optional: true,
                    selector: [
                        'button:has(.mdi-puzzle)',
                        'button:has([class*="icon-[mdi--puzzle]"])',
                        'button:has(svg[data-icon="puzzle"])',
                    ],
                    mode: "click",
                },
                managerFavorites: {
                    optional: true,
                    selector: [
                        'button:has([class*="icon-[lucide--star]"])',
                        'button:has(.mdi-star)',
                        'button:has([class*="icon-[mdi--star]"])',
                        'button:has(svg[data-icon="star"])',
                    ],
                    mode: "click",
                },
                unloadModels: {
                    optional: true,
                    selector: [
                        'button:has([class*="icon-[lucide--trash]"])',
                        'button:has(.mdi-vacuum-outline)',
                        'button:has([class*="icon-[mdi--vacuum-outline]"])',
                        'button:has(svg[data-icon="vacuum-outline"])',
                    ],
                    mode: "click",
                },
                freeCache: {
                    optional: true,
                    selector: [
                        'button:has([class*="icon-[lucide--upload]"])',
                        'button:has(.mdi-vacuum)',
                        'button:has([class*="icon-[mdi--vacuum]"])',
                        'button:has(svg[data-icon="vacuum"])',
                    ],
                    mode: "click",
                },
                share: {
                    optional: true,
                    selector: [
                        'button:has([class*="icon-[lucide--share]"])',
                        'button:has(.mdi-share)',
                        'button:has([class*="icon-[mdi--share]"])',
                        'button:has(svg[data-icon="share"])',
                    ],
                    mode: "click",
                },
                runModeTrigger: {
                    selector: [
                        'button[data-testid="queue-mode-menu-trigger"]',
                        '[data-testid="queue-mode-menu-trigger"]',
                    ],
                    mode: "click",
                },
                helpCenter: {
                    optional: true,
                    selector: ".comfy-help-center-btn",
                    mode: "click",
                },
                settings: {
                    optional: true,
                    selector: [
                        'button:has([class*="icon-[lucide--settings]"])',
                        '.side-bar-button:has([class*="icon-[lucide--settings]"])',
                    ],
                    mode: "click",
                },
                templates: {
                    optional: true,
                    selector: "button.templates-tab-button",
                    mode: "click",
                },
                apps: {
                    optional: true,
                    selector: "button.apps-tab-button",
                    mode: "click",
                },
            },
        },
    ],
    rehost: [],
    relocate: [],
};

export function getConfiguredSelectorGroups() {
    const configured = window.__nexusExileSelectorGroups;
    if (!configured || typeof configured !== "object") {
        return null;
    }

    return {
        tabChrome: Array.isArray(configured.tabChrome) ? configured.tabChrome : null,
        executionControls: Array.isArray(configured.executionControls) ? configured.executionControls : null,
        toolbarButtons: Array.isArray(configured.toolbarButtons) ? configured.toolbarButtons : null,
    };
}

export function getDefaultExileSelectorGroups() {
    const configured = getConfiguredSelectorGroups();
    const tabChrome = exileRegistry.hide.find((rule) => rule.id === "workflow-tab-chrome");

    return {
        tabChrome: configured?.tabChrome || tabChrome?.selectors || [],
        executionControls: configured?.executionControls || [],
        toolbarButtons: configured?.toolbarButtons || [],
    };
}

export function getExileSelectorList(selectorGroups = getDefaultExileSelectorGroups()) {
    return Object.values(selectorGroups).flat().filter(Boolean);
}

export function getExileSelectorText(selectorGroups = getDefaultExileSelectorGroups()) {
    return getExileSelectorList(selectorGroups).join(",\n            ");
}

export function getRulesByPolicy(policy) {
    return exileRegistry[policy] || [];
}

export function getAllRules() {
    return Object.entries(exileRegistry).flatMap(([policy, rules]) => rules.map((rule) => ({ ...rule, policy })));
}

export function getSourceRules() {
    return [
        ...getRulesByPolicy(EXILE_POLICIES.reserve).map((rule) => ({ ...rule, policy: EXILE_POLICIES.reserve })),
        ...getRulesByPolicy(EXILE_POLICIES.rehost).map((rule) => ({ ...rule, policy: EXILE_POLICIES.rehost })),
    ];
}
