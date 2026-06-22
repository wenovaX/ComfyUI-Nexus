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
                'span[aria-label="Canvas Toolbar"]',
                '.p-buttongroup[aria-label="Canvas Toolbar"]',
            ],
            reason: "Nexus owns workflow tabs and primary canvas chrome.",
        },
        {
            id: "manager-toolbar-buttons",
            optional: true,
            selectors: [
                'button[aria-label="Unload Models"]',
                'button[title="Unload Models"]',
                'button[aria-label="Unload models"]',
                'button[title="Unload models"]',
                'button[aria-label="Free model and node cache"]',
                'button[title="Free model and node cache"]',
                'button[aria-label="Free cache"]',
                'button[title="Free cache"]',
                'button[aria-label="Share"]',
                'button[title="Share"]',
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
                'button[aria-label="Toggle Bottom Panel"]',
                'button[aria-label^="Keyboard Shortcuts"]',
                'button[aria-label^="Settings"]',
            ],
            reason: "Nexus owns visible sidebar utility buttons while preserving their native click handlers.",
        },
        {
            id: "subgraph-breadcrumb-shell",
            selectors: [
                '[data-testid="subgraph-breadcrumb"]',
                ".subgraph-breadcrumb",
            ],
            reason: "Nexus owns app/workflow breadcrumb chrome while preserving native action triggers.",
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
                        'button[aria-label="ComfyUI Manager"]',
                        'button[title="ComfyUI Manager"]',
                    ],
                    mode: "click",
                },
                managerFavorites: {
                    optional: true,
                    selector: [
                        'button[aria-label="Show favorite custom node list"]',
                        'button[title="Show favorite custom node list"]',
                        'button[aria-label="Show favorites"]',
                        'button[title="Show favorites"]',
                    ],
                    mode: "click",
                },
                unloadModels: {
                    optional: true,
                    selector: [
                        'button[aria-label="Unload Models"]',
                        'button[title="Unload Models"]',
                        'button[aria-label="Unload models"]',
                        'button[title="Unload models"]',
                    ],
                    mode: "click",
                },
                freeCache: {
                    optional: true,
                    selector: [
                        'button[aria-label="Free model and node cache"]',
                        'button[title="Free model and node cache"]',
                        'button[aria-label="Free cache"]',
                        'button[title="Free cache"]',
                    ],
                    mode: "click",
                },
                share: {
                    optional: true,
                    selector: [
                        'button[aria-label="Share"]',
                        'button[title="Share"]',
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
                    selector: [
                        ".comfy-help-center-btn",
                        'button[aria-label="Help Center"]',
                    ],
                    mode: "click",
                },
                bottomPanel: {
                    optional: true,
                    selector: [
                        'button[aria-label="Toggle Bottom Panel"]',
                        'button[title="Toggle Bottom Panel"]',
                    ],
                    mode: "click",
                },
                keyboardShortcuts: {
                    optional: true,
                    selector: [
                        'button[aria-label^="Keyboard Shortcuts"]',
                        'button[title^="Keyboard Shortcuts"]',
                    ],
                    mode: "click",
                },
                settings: {
                    optional: true,
                    selector: [
                        'button[aria-label^="Settings"]',
                        'button[title^="Settings"]',
                    ],
                    mode: "click",
                },
                templates: {
                    optional: true,
                    selector: [
                        "button.templates-tab-button",
                        'button[aria-label="Templates"]',
                        'button[title="Templates"]',
                    ],
                    mode: "click",
                },
                apps: {
                    optional: true,
                    selector: [
                        "button.apps-tab-button",
                        'button[aria-label="Apps"]',
                        'button[title="Apps"]',
                    ],
                    mode: "click",
                },
                enterAppMode: {
                    selector: [
                        '[data-testid="subgraph-breadcrumb"] button[aria-label="Enter app mode"]',
                        '.subgraph-breadcrumb button[aria-label="Enter app mode"]',
                        'button[aria-label="Enter app mode"]',
                    ],
                    mode: "click",
                },
                workflowActions: {
                    selector: [
                        '[data-testid="subgraph-breadcrumb"] button[aria-label="Workflow actions"]',
                        '.subgraph-breadcrumb button[aria-label="Workflow actions"]',
                        'button[aria-label="Workflow actions"]',
                    ],
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
