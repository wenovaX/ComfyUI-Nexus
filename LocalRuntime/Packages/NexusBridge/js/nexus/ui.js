export {
    applyExile,
    createSourceApi,
    ensureExileStyle,
    exileTargets,
    getDefaultExileSelectorGroups,
    getExileSelectorList,
    getExileSelectorText,
    getSourceSnapshot,
    installSourceApi,
    resolveSource,
    scanSources,
    setupExileSystem,
    stopExileSystem,
} from "./exile/index.js";

import { setupExileSystem, stopExileSystem } from "./exile/index.js";

const COMFY_PORTAL_OVERLAY_MIN_Z_INDEX = 30000;
const COMFY_PORTAL_OVERLAY_SELECTOR = [
    '.p-select-overlay.p-component',
    '.p-dropdown-panel.p-component',
    '.p-multiselect-panel.p-component',
    '.p-menu-overlay.p-component',
    '.p-tieredmenu-overlay.p-component',
    '.p-popover.p-component',
    '.p-dialog-mask.p-overlay-mask',
    '[data-pc-section="overlay"].p-component',
    '[data-reka-popper-content-wrapper]',
    '[data-reka-menu-content][role="menu"]',
].join(", ");

function parseZIndexValue(value) {
    const parsed = Number.parseInt(value, 10);
    return Number.isFinite(parsed) ? parsed : null;
}

function ensureComfyPortalOverlayStyle() {
    if (document.getElementById("nexus-comfy-portal-overlay-style")) {
        return;
    }

    const style = document.createElement("style");
    style.id = "nexus-comfy-portal-overlay-style";
    style.textContent = `
        body > .p-select-overlay.p-component,
        body > .p-dropdown-panel.p-component,
        body > .p-multiselect-panel.p-component,
        body > .p-menu-overlay.p-component,
        body > .p-tieredmenu-overlay.p-component,
        body > .p-popover.p-component,
        body > .p-dialog-mask.p-overlay-mask,
        body > [data-pc-section="overlay"].p-component,
        body > [data-reka-popper-content-wrapper],
        body > [data-reka-menu-content][role="menu"] {
            z-index: ${COMFY_PORTAL_OVERLAY_MIN_Z_INDEX} !important;
        }
    `;
    document.head.appendChild(style);
}

function getPortalOverlayCandidates(root) {
    const candidates = new Set();

    if (root instanceof Element && root.matches(COMFY_PORTAL_OVERLAY_SELECTOR)) {
        candidates.add(root);
    }

    if (root?.querySelectorAll) {
        root.querySelectorAll(COMFY_PORTAL_OVERLAY_SELECTOR).forEach((overlay) => candidates.add(overlay));
    }

    return candidates;
}

function elevateComfyPortalOverlays(root = document) {
    for (const overlay of getPortalOverlayCandidates(root)) {
        const inlineZIndex = parseZIndexValue(overlay.style.zIndex);
        const computedZIndex = parseZIndexValue(window.getComputedStyle(overlay).zIndex);
        const targetZIndex = Math.max(
            inlineZIndex ?? COMFY_PORTAL_OVERLAY_MIN_Z_INDEX,
            computedZIndex ?? COMFY_PORTAL_OVERLAY_MIN_Z_INDEX,
            COMFY_PORTAL_OVERLAY_MIN_Z_INDEX);

        if (computedZIndex !== targetZIndex || overlay.dataset.nexusZIndexGuard !== String(targetZIndex)) {
            overlay.style.setProperty("z-index", String(targetZIndex), "important");
            overlay.dataset.nexusZIndexGuard = String(targetZIndex);
        }
    }
}

function setupComfyPortalOverlayZIndexGuard() {
    window._nexusComfyPortalOverlayZIndexCleanup?.();

    ensureComfyPortalOverlayStyle();
    elevateComfyPortalOverlays();

    let frame = null;
    const pendingRoots = new Set();
    const scheduleElevate = (root = document) => {
        ensureComfyPortalOverlayStyle();
        pendingRoots.add(root || document);

        if (frame !== null) {
            return;
        }

        frame = window.requestAnimationFrame(() => {
            frame = null;
            const roots = Array.from(pendingRoots);
            pendingRoots.clear();

            for (const pendingRoot of roots) {
                elevateComfyPortalOverlays(pendingRoot);
            }
        });
    };

    const observer = new MutationObserver((mutations) => {
        for (const mutation of mutations) {
            if (mutation.type === "childList") {
                mutation.addedNodes.forEach((node) => {
                    if (node instanceof Element) {
                        scheduleElevate(node);
                    }
                });
                continue;
            }

            if (mutation.target instanceof Element) {
                scheduleElevate(mutation.target);
            }
        }
    });

    observer.observe(document.documentElement || document.body, {
        childList: true,
        subtree: true,
        attributes: true,
        attributeFilter: ["class", "style", "data-state", "aria-expanded"],
    });

    const scheduleDocumentElevate = () => scheduleElevate(document);
    const eventOptions = { capture: true, passive: true };
    document.addEventListener("pointerdown", scheduleDocumentElevate, eventOptions);
    document.addEventListener("focusin", scheduleDocumentElevate, eventOptions);
    document.addEventListener("keydown", scheduleDocumentElevate, eventOptions);

    const cleanup = () => {
        observer.disconnect();
        document.removeEventListener("pointerdown", scheduleDocumentElevate, eventOptions);
        document.removeEventListener("focusin", scheduleDocumentElevate, eventOptions);
        document.removeEventListener("keydown", scheduleDocumentElevate, eventOptions);
        pendingRoots.clear();
        if (frame !== null) {
            window.cancelAnimationFrame(frame);
            frame = null;
        }
        if (window._nexusComfyPortalOverlayZIndexCleanup === cleanup) {
            delete window._nexusComfyPortalOverlayZIndexCleanup;
        }
    };

    window._nexusComfyPortalOverlayZIndexCleanup = cleanup;
    return cleanup;
}

function ensureSplitterPolish() {
    if (document.getElementById("nexus-splitter-polish-style")) {
        return;
    }

    const style = document.createElement("style");
    style.id = "nexus-splitter-polish-style";
    style.textContent = `
        :root {
            --nexus-rail-width: 48px;
        }

        .p-splitterpanel.bottom-panel {
            box-sizing: border-box !important;
            padding-left: var(--nexus-rail-width) !important;
        }

        .p-splitter-gutter[data-pc-section="gutter"] {
            position: relative !important;
            z-index: 1800 !important;
            overflow: visible !important;
            border-radius: 10px !important;
            background: transparent !important;
            min-height: 12px !important;
            transition: background 140ms ease, box-shadow 140ms ease, opacity 140ms ease !important;
        }

        .p-splitter-gutter[data-pc-section="gutter"] {
            cursor: row-resize !important;
        }

        .p-splitter-gutter[data-pc-section="gutter"][aria-orientation="vertical"] {
            min-height: 14px !important;
            height: 14px !important;
            margin-top: -5px !important;
            margin-bottom: -5px !important;
            cursor: row-resize !important;
        }

        .p-splitter-gutter[data-pc-section="gutter"][aria-orientation="horizontal"] {
            min-width: 14px !important;
            width: 14px !important;
            margin-left: -5px !important;
            margin-right: -5px !important;
            cursor: col-resize !important;
        }

        .p-splitter-gutter[data-pc-section="gutter"]:has(.p-splitter-gutter-handle[aria-orientation="vertical"]) {
            min-height: 14px !important;
            height: 14px !important;
            margin-top: -5px !important;
            margin-bottom: -5px !important;
            cursor: row-resize !important;
        }

        .p-splitter-gutter[data-pc-section="gutter"]:has(.p-splitter-gutter-handle[aria-orientation="horizontal"]) {
            min-width: 14px !important;
            width: 14px !important;
            margin-left: -5px !important;
            margin-right: -5px !important;
            cursor: col-resize !important;
        }

        .p-splitter-gutter[data-pc-section="gutter"]::after {
            content: "";
            position: absolute;
            left: 18px;
            right: 18px;
            top: 5px;
            bottom: 5px;
            border-radius: 999px;
            background: rgba(49, 216, 255, 0.18);
            box-shadow: 0 0 8px rgba(49, 216, 255, 0.14);
            opacity: 0.72;
            pointer-events: none;
            transition: opacity 130ms ease, background 130ms ease, box-shadow 130ms ease, transform 130ms ease;
        }

        .p-splitter-gutter[data-pc-section="gutter"][aria-orientation="vertical"]::after {
            left: 18px;
            right: 18px;
            top: 5px;
            bottom: 5px;
        }

        .p-splitter-gutter[data-pc-section="gutter"][aria-orientation="horizontal"]::after {
            top: 18px;
            bottom: 18px;
            left: 5px;
            right: 5px;
        }

        .p-splitter-gutter[data-pc-section="gutter"]:has(.p-splitter-gutter-handle[aria-orientation="vertical"])::after {
            left: 18px;
            right: 18px;
            top: 5px;
            bottom: 5px;
        }

        .p-splitter-gutter[data-pc-section="gutter"]:has(.p-splitter-gutter-handle[aria-orientation="horizontal"])::after {
            top: 18px;
            bottom: 18px;
            left: 5px;
            right: 5px;
        }

        .p-splitter-gutter[data-pc-section="gutter"]:hover::after,
        .p-splitter-gutter[data-pc-section="gutter"].nexus-splitter-hot::after,
        .p-splitter-gutter[data-pc-section="gutter"][data-p-gutter-resizing="true"]::after {
            opacity: 1;
            background: rgba(49, 216, 255, 0.28);
            box-shadow: 0 0 12px rgba(49, 216, 255, 0.52), 0 0 28px rgba(49, 216, 255, 0.22);
        }

        .p-splitter-gutter[data-pc-section="gutter"][data-p-gutter-resizing="true"]::after {
            background: rgba(255, 77, 109, 0.34);
            box-shadow: 0 0 14px rgba(255, 77, 109, 0.58), 0 0 34px rgba(255, 77, 109, 0.26);
        }

        .p-splitter-gutter-handle[data-pc-section="gutterhandle"] {
            border-radius: 999px !important;
            background: rgba(210, 244, 255, 0.46) !important;
            box-shadow: 0 0 8px rgba(49, 216, 255, 0.28) !important;
            opacity: 1 !important;
            transition: background 130ms ease, box-shadow 130ms ease, opacity 130ms ease, transform 130ms ease !important;
        }

        .p-splitter-gutter[data-pc-section="gutter"][aria-orientation="vertical"] .p-splitter-gutter-handle {
            height: 6px !important;
            min-height: 6px !important;
        }

        .p-splitter-gutter[data-pc-section="gutter"]:has(.p-splitter-gutter-handle[aria-orientation="vertical"]) .p-splitter-gutter-handle {
            height: 6px !important;
            min-height: 6px !important;
        }

        .p-splitter-gutter[data-pc-section="gutter"][aria-orientation="horizontal"] .p-splitter-gutter-handle {
            width: 6px !important;
            min-width: 6px !important;
        }

        .p-splitter-gutter[data-pc-section="gutter"]:has(.p-splitter-gutter-handle[aria-orientation="horizontal"]) .p-splitter-gutter-handle {
            width: 6px !important;
            min-width: 6px !important;
        }

        .p-splitter-gutter[data-pc-section="gutter"]:hover .p-splitter-gutter-handle,
        .p-splitter-gutter[data-pc-section="gutter"].nexus-splitter-hot .p-splitter-gutter-handle,
        .p-splitter-gutter[data-pc-section="gutter"][data-p-gutter-resizing="true"] .p-splitter-gutter-handle {
            background: rgba(49, 216, 255, 0.82) !important;
            box-shadow: 0 0 12px rgba(49, 216, 255, 0.72) !important;
            opacity: 1 !important;
        }

        html.nexus-splitter-row-resizing,
        html.nexus-splitter-row-resizing *,
        body.nexus-splitter-row-resizing,
        body.nexus-splitter-row-resizing * {
            cursor: row-resize !important;
            user-select: none !important;
        }

        html.nexus-splitter-col-resizing,
        html.nexus-splitter-col-resizing *,
        body.nexus-splitter-col-resizing,
        body.nexus-splitter-col-resizing * {
            cursor: col-resize !important;
            user-select: none !important;
        }
    `;

    document.head.appendChild(style);
}

function setupSplitterFeedback() {
    window._nexusSplitterFeedbackCleanup?.();

    const getGutter = (target) => target?.closest?.('.p-splitter-gutter[data-pc-section="gutter"], .p-splitter-gutter');
    let activeGutter = null;

    const clearHot = () => {
        activeGutter?.classList.remove("nexus-splitter-hot");
        activeGutter = null;
    };

    const onPointerOver = (event) => {
        const gutter = getGutter(event.target);
        if (!gutter || gutter === activeGutter) return;

        clearHot();
        activeGutter = gutter;
        gutter.classList.add("nexus-splitter-hot");
    };

    const onPointerOut = (event) => {
        const gutter = getGutter(event.target);
        if (!gutter || gutter.getAttribute("data-p-gutter-resizing") === "true") return;

        gutter.classList.remove("nexus-splitter-hot");
        if (gutter === activeGutter) {
            activeGutter = null;
        }
    };

    document.addEventListener("pointerover", onPointerOver, true);
    document.addEventListener("pointerout", onPointerOut, true);
    document.addEventListener("pointerup", clearHot, true);
    document.addEventListener("pointercancel", clearHot, true);

    const cleanup = () => {
        clearHot();
        document.removeEventListener("pointerover", onPointerOver, true);
        document.removeEventListener("pointerout", onPointerOut, true);
        document.removeEventListener("pointerup", clearHot, true);
        document.removeEventListener("pointercancel", clearHot, true);
        if (window._nexusSplitterFeedbackCleanup === cleanup) {
            delete window._nexusSplitterFeedbackCleanup;
        }
    };

    window._nexusSplitterFeedbackCleanup = cleanup;
    return cleanup;
}

function collectComfyMediaAssetButtons() {
	const buttons = new Set();

	document.querySelectorAll('.icon-\\[comfy--image-ai-edit\\]').forEach((icon) => {
		const button = icon.closest("button");
		if (button) {
			buttons.add(button);
		}
	});

	return buttons;
}

function collectComfyMediaAssetToggleButtons() {
	const buttons = new Set();

	document.querySelectorAll('.assets-tab-button .icon-\\[comfy--image-ai-edit\\]').forEach((icon) => {
		const button = icon.closest("button");
		if (button) {
			buttons.add(button);
		}
	});

	return buttons;
}

function isComfyMediaAssetsPanelVisible() {
	return Boolean(document.querySelector([
		'[data-testid="assets-delete-selected"]',
		'[data-testid="assets-download-selected"]',
		'#tab-output[aria-controls="tabpanel-output"]',
		'#tab-input[aria-controls="tabpanel-input"]',
	].join(", ")));
}

function disableComfyMediaAssetsPanel() {
    window._nexusMediaAssetsPanelCleanup?.();

    if (!document.getElementById("nexus-disable-comfy-media-assets-style")) {
        const style = document.createElement("style");
        style.id = "nexus-disable-comfy-media-assets-style";
        style.textContent = `
            button:has(.icon-\\[comfy--image-ai-edit\\]) {
                display: none !important;
                visibility: hidden !important;
                pointer-events: none !important;
            }
        `;
        document.head.appendChild(style);
	}

	let lastCloseRequestAt = 0;
	const pendingTimeouts = new Set();
	const scheduleCloseCheck = (delay) => {
		const timeoutId = window.setTimeout(() => {
			pendingTimeouts.delete(timeoutId);
			closeIfMediaAssetsVisible();
		}, delay);
		pendingTimeouts.add(timeoutId);
	};
	const closeIfMediaAssetsVisible = () => {
		const entryButtons = collectComfyMediaAssetButtons();
		const toggleButtons = collectComfyMediaAssetToggleButtons();
		const panelVisible = isComfyMediaAssetsPanelVisible();
		const buttons = panelVisible && toggleButtons.size > 0 ? toggleButtons : entryButtons;

		buttons.forEach((button) => {
			const isOpen =
				panelVisible ||
				button.classList.contains("side-bar-button-selected") ||
				button.getAttribute("aria-pressed") === "true" ||
				button.getAttribute("aria-expanded") === "true" ||
				button.dataset.state === "open";

			if (isOpen && button.dataset.nexusClosingMediaAssets !== "true") {
				const now = Date.now();
				if (now - lastCloseRequestAt < 300) {
					return;
				}

				lastCloseRequestAt = now;
				button.dataset.nexusClosingMediaAssets = "true";
				button.click();
				const timeoutId = window.setTimeout(() => {
					pendingTimeouts.delete(timeoutId);
					button.dataset.nexusClosingMediaAssets = "false";
				}, 350);
				pendingTimeouts.add(timeoutId);
				return;
			}
		});
	};

	closeIfMediaAssetsVisible();
	const onDoubleClick = () => {
		[0, 50, 150, 350].forEach(scheduleCloseCheck);
	};
	document.addEventListener("dblclick", onDoubleClick, true);

	const observer = new MutationObserver(closeIfMediaAssetsVisible);
	observer.observe(document.body || document.documentElement, {
		childList: true,
		subtree: true,
		attributes: true,
		attributeFilter: ["class", "aria-pressed", "aria-expanded"],
	});

	const cleanup = () => {
		document.removeEventListener("dblclick", onDoubleClick, true);
		observer.disconnect();
		pendingTimeouts.forEach((timeoutId) => window.clearTimeout(timeoutId));
		pendingTimeouts.clear();
		collectComfyMediaAssetButtons().forEach((button) => delete button.dataset.nexusClosingMediaAssets);
		if (window._nexusMediaAssetsPanelCleanup === cleanup) {
			delete window._nexusMediaAssetsPanelCleanup;
		}
	};

	window._nexusMediaAssetsPanelCleanup = cleanup;
	return cleanup;
}

function setupRefreshShortcut(bridge) {
    window._nexusRefreshShortcutCleanup?.();
    let lastRefreshRequestAt = 0;

    const onKeyDown = (e) => {
        if (e.key !== "F5" && !(e.ctrlKey && e.key.toLowerCase() === "r")) {
            return;
        }

        e.preventDefault();
        e.stopPropagation();

        const now = Date.now();
        if (now - lastRefreshRequestAt < 1000) {
            return;
        }

        lastRefreshRequestAt = now;
        bridge.send("REFRESH_REQUEST", {
            shortcut: e.key === "F5" ? "F5" : "CTRL_R",
            timestamp: now,
        });
    };

    window.addEventListener("keydown", onKeyDown, true);
    const cleanup = () => {
        window.removeEventListener("keydown", onKeyDown, true);
        if (window._nexusRefreshShortcutCleanup === cleanup) {
            delete window._nexusRefreshShortcutCleanup;
        }
    };

    window._nexusRefreshShortcutCleanup = cleanup;
    return cleanup;
}

export function setupUiExile(bridge) {
    // Block browser-level refresh shortcuts and hand them to the shell.
    const refreshShortcutCleanup = setupRefreshShortcut(bridge);
    const portalOverlayZIndexCleanup = setupComfyPortalOverlayZIndexGuard();

    ensureSplitterPolish();
    const splitterFeedbackCleanup = setupSplitterFeedback();
    const mediaAssetsCleanup = disableComfyMediaAssetsPanel();

    setupExileSystem(bridge);

    return () => {
        refreshShortcutCleanup?.();
        portalOverlayZIndexCleanup?.();
        splitterFeedbackCleanup?.();
        mediaAssetsCleanup?.();
        stopExileSystem(bridge);
    };
}

export function setupHudCompatibility(bridge) {
    const removeTopbarHud = () => {
        document.getElementById("hud-global-hud")?.remove();
        document.getElementById("hud-asset-hub-btn")?.remove();
    };

    if (bridge.featureConfig.topbarHud === false) {
        removeTopbarHud();
        const observer = new MutationObserver(() => removeTopbarHud());
        observer.observe(document.body || document.documentElement, { childList: true, subtree: true });
		return () => observer.disconnect();
    }

	return () => {};
}
