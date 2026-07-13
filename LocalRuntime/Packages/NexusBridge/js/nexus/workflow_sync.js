import { BOTTOM_PANELS, getBottomPanelState } from "./bottom_panel.js";
import { isAppModeOpen } from "./app_mode.js";
import { isRightPanelOpen } from "./settings_store.js";
import { ensureWorkflowTemplatesDefaultCategory } from "./workflow_templates_dialog.js";

export async function refreshWorkflowAppData() {
	const workflowStore = window.comfyAPI?.app?.app?.extensionManager?.workflow;

	if (!workflowStore?.syncWorkflows) {
		throw new Error("workflowStore.syncWorkflows not found");
	}

	await workflowStore.syncWorkflows();
}

export function normalizeWorkflowTabName(text) {
	return String(text || "").replace(/[\s*•●×]+$/g, "").trim();
}

function normalizeWorkflowPath(path) {
	return String(path || "")
		.trim()
		.replace(/\\/g, "/")
		.replace(/^\/+/, "")
		.toLowerCase();
}

function getWorkflowPathAliases(path) {
	const normalized = normalizeWorkflowPath(path);
	if (!normalized) return [];

	const aliases = new Set([normalized]);
	if (normalized.startsWith("user/default/workflows/")) {
		aliases.add(normalized.slice("user/default/workflows/".length));
	}
	if (normalized.startsWith("workflows/")) {
		aliases.add(normalized.slice("workflows/".length));
	}
	return [...aliases];
}

function normalizeWorkflowName(name) {
	return normalizeWorkflowTabName(name)
		.replace(/\.json$/i, "")
		.trim()
		.toLowerCase();
}

function getElementPathValues(element) {
	return ["data-workflow-path", "data-file-path", "data-path"]
		.map((name) => element?.getAttribute?.(name))
		.filter((value) => typeof value === "string" && value.trim());
}

function getAppsButton() {
	return window.NexusSources?.get?.("apps") ||
		document.querySelector("button.apps-tab-button") ||
		document.querySelector(".side-bar-button.apps-tab-button");
}

function isAppsButtonSelected(button) {
	return Boolean(
		button?.classList?.contains("side-bar-button-selected") ||
		button?.getAttribute?.("aria-selected") === "true" ||
		button?.getAttribute?.("data-state") === "active"
	);
}

function isToggleButtonActive(button) {
	const state = String(button?.getAttribute?.("data-state") || "").toLowerCase();
	return Boolean(
		state === "open" ||
		state === "active" ||
		state === "on" ||
		state === "checked" ||
		button?.getAttribute?.("aria-pressed") === "true" ||
		button?.getAttribute?.("aria-checked") === "true" ||
		button?.classList?.contains("p-togglebutton-checked") ||
		button?.classList?.contains("bg-primary-background")
	);
}

function isVisibleDomElement(element) {
	if (!(element instanceof HTMLElement)) return false;

	const style = window.getComputedStyle(element);
	if (style.display === "none" || style.visibility === "hidden" || Number(style.opacity || "1") === 0) {
		return false;
	}

	const rect = element.getBoundingClientRect();
	return rect.width > 0 && rect.height > 0;
}

function readMinimapOpen() {
	const minimapSurface = document.querySelector(".minimap-main-container, [data-testid=\"minimap-container\"]");
	if (isVisibleDomElement(minimapSurface)) {
		return true;
	}

	const minimapBtn = document.querySelector('button[data-testid="toggle-minimap-button"]');
	return isToggleButtonActive(minimapBtn);
}

function readLinksHidden() {
	const canvas = window.app?.canvas ?? window.comfyAPI?.app?.app?.canvas;
	const mode = canvas?.links_render_mode ?? canvas?.linkRenderMode;
	const hiddenCandidates = [
		window.LiteGraph?.HIDDEN_LINK,
		window.LGraphCanvas?.HIDDEN_LINK,
		4,
		"hidden",
		"HIDDEN_LINK",
	].filter((value) => value !== undefined && value !== null);

	if (mode !== undefined && mode !== null) {
		return hiddenCandidates.some((candidate) =>
			String(candidate).toLowerCase() === String(mode).toLowerCase());
	}

	const linksBtn = document.querySelector('button[data-testid="toggle-link-visibility-button"]');
	return isToggleButtonActive(linksBtn);
}

function readCanvasZoomPercent() {
	const scale =
		window.app?.canvas?.ds?.scale ??
		window.comfyAPI?.app?.app?.canvas?.ds?.scale;

	if (!Number.isFinite(scale) || scale <= 0) {
		return null;
	}

	return `${Math.round(scale * 100)}%`;
}

function getWorkflowName(workflow) {
	const filename = String(workflow?.filename || workflow?.fullFilename || "").trim();
	if (filename) return filename.replace(/\.json$/i, "");

	const path = String(workflow?.path || "").replace(/\\/g, "/");
	return path.split("/").pop()?.replace(/\.json$/i, "") || "Untitled";
}

function isWorkflowActive(store, workflow) {
	if (!workflow) return false;
	if (workflow === store?.activeWorkflow) return true;

	try {
		if (typeof store?.isActive === "function" && store.isActive(workflow)) return true;
	} catch {
		// Fall through to path identity.
	}

	const workflowPath = normalizeWorkflowPath(workflow.path);
	const activePath = normalizeWorkflowPath(store?.activeWorkflow?.path);
	return Boolean(workflowPath && workflowPath === activePath);
}

function getWorkflowId(workflow, index) {
	const candidate = workflow?.id ?? workflow?.key;
	if (typeof candidate === "string" || typeof candidate === "number") {
		return candidate;
	}

	return workflow?.path || index;
}

export function createWorkflowSyncPayload(store) {
	const openWorkflows = Array.from(store?.openWorkflows || []);
	return openWorkflows.map((workflow, index) => ({
		id: getWorkflowId(workflow, index),
		name: getWorkflowName(workflow),
		active: isWorkflowActive(store, workflow),
		modified: Boolean(workflow?._isModified),
		path: workflow?.path || "",
	}));
}

function getTabSurface(element) {
	return element?.closest?.('[role="tablist"], .p-tablist, .workflow-tabs, .comfyui-tabs') ||
		element?.parentElement || null;
}

function getElementPathMatchScore(element, workflow) {
	const workflowAliases = new Set(getWorkflowPathAliases(workflow?.path));
	if (workflowAliases.size === 0) return 0;

	return getElementPathValues(element).some((path) =>
		getWorkflowPathAliases(path).some((alias) => workflowAliases.has(alias))) ? 50 : 0;
}

function scoreTabSurface(elements, openWorkflows, isVisibleElement) {
	let score = elements.length === openWorkflows.length ? 30 : 0;
	const count = Math.min(elements.length, openWorkflows.length);
	for (let index = 0; index < count; index++) {
		const element = elements[index];
		const workflow = openWorkflows[index];
		score += getElementPathMatchScore(element, workflow);
		if (normalizeWorkflowName(element.textContent) === normalizeWorkflowName(getWorkflowName(workflow))) {
			score += 20;
		}
	}

	if (elements.some(isVisibleElement)) score += 10;
	return score;
}

function mapSurfaceToWorkflows(elements, openWorkflows) {
	const mapped = new Array(openWorkflows.length).fill(null);
	const consumed = new Set();

	for (let index = 0; index < openWorkflows.length; index++) {
		const workflow = openWorkflows[index];
		const positional = elements[index];
		if (positional && !consumed.has(positional) && (
			getElementPathMatchScore(positional, workflow) > 0 ||
			normalizeWorkflowName(positional.textContent) === normalizeWorkflowName(getWorkflowName(workflow)))) {
			mapped[index] = positional;
			consumed.add(positional);
			continue;
		}

		const pathMatches = elements.filter((element) =>
			!consumed.has(element) && getElementPathMatchScore(element, workflow) > 0);
		if (pathMatches.length === 1) {
			mapped[index] = pathMatches[0];
			consumed.add(pathMatches[0]);
			continue;
		}

		const expectedName = normalizeWorkflowName(getWorkflowName(workflow));
		const nameMatches = elements.filter((element) =>
			!consumed.has(element) && normalizeWorkflowName(element.textContent) === expectedName);
		if (nameMatches.length === 1) {
			mapped[index] = nameMatches[0];
			consumed.add(nameMatches[0]);
		}
	}

	return mapped;
}

export function setupWorkflowSync(bridge) {
	const systemTabNames = ["LOGS", "TERMINAL", "SHORTCUTS", "CONSOLE", "DEBUG"];
	const surfaceDefinitions = {
		settings: "global-settings",
		templates: "global-workflow-template-selector",
	};
	let lastExpensivePropertiesCheckAt = 0;
	let lastExpensivePropertiesState = false;
	const expensivePropertiesCheckIntervalMs = 1000;
	const getWorkflowStore = () =>
		bridge.workflowStore || window.comfyAPI?.app?.app?.extensionManager?.workflow || null;
	const getOpenWorkflows = () => Array.from(getWorkflowStore()?.openWorkflows || []);
	const getOpenWorkflowByIndex = (index) => getOpenWorkflows()[index] || null;
	const getOpenWorkflowByTarget = (target = {}) => {
		const openWorkflows = getOpenWorkflows();
		const targetId = target?.id === undefined || target?.id === null ? "" : String(target.id);
		const targetPath = normalizeWorkflowPath(target?.path);
		const targetRelativePath = normalizeWorkflowPath(target?.relativePath);

		if (targetId) {
			const idMatch = openWorkflows.find((workflow, index) => {
				const workflowId = getWorkflowId(workflow, index);
				return String(workflowId) === targetId || String(workflow?.key ?? "") === targetId;
			});
			if (idMatch) return idMatch;
		}

		if (targetPath || targetRelativePath) {
			const pathMatch = openWorkflows.find((workflow) => {
				const aliases = new Set(getWorkflowPathAliases(workflow?.path));
				return (targetPath && getWorkflowPathAliases(targetPath).some((alias) => aliases.has(alias))) ||
					(targetRelativePath && getWorkflowPathAliases(targetRelativePath).some((alias) => aliases.has(alias)));
			});
			if (pathMatch) return pathMatch;
		}

		return getOpenWorkflowByIndex(target?.index);
	};
	const getActiveWorkflowIndex = () => {
		const store = getWorkflowStore();
		return getOpenWorkflows().findIndex((workflow) => isWorkflowActive(store, workflow));
	};

	const getFilteredTabs = () => {
		const openWorkflows = getOpenWorkflows();
		if (openWorkflows.length === 0) return [];

		const allTabs = document.querySelectorAll('.workflow-tab, .p-tablist .p-tab, .comfy-tab-item, .comfyui-tab-item');
		const candidates = Array.from(allTabs).filter(el => {
			// 1. Location-based exclusion: Ignore tabs inside system panels
			if (el.closest('.side-bar') ||
				el.closest('.comfy-sidebar') ||
				el.closest('.bottom-panel')) return false;

			// Clean name: remove icons (stars, dots) and close button characters
			const text = normalizeWorkflowTabName(el.textContent || "");

			// 2. Name-based exclusion: Ignore system-reserved names
			return Boolean(text && !systemTabNames.includes(text.toUpperCase()));
		});
		if (candidates.length === 0) return new Array(openWorkflows.length).fill(null);

		const surfaceGroups = new Map();
		for (const element of candidates) {
			const surface = getTabSurface(element);
			const group = surfaceGroups.get(surface) || [];
			group.push(element);
			surfaceGroups.set(surface, group);
		}

		let canonicalElements = null;
		let canonicalScore = Number.NEGATIVE_INFINITY;
		const candidateGroups = [...surfaceGroups.values(), candidates];
		for (const elements of candidateGroups) {
			const score = scoreTabSurface(elements, openWorkflows, isVisibleElement);
			if (score > canonicalScore) {
				canonicalScore = score;
				canonicalElements = elements;
			}
		}

		return canonicalElements
			? mapSurfaceToWorkflows(canonicalElements, openWorkflows)
			: new Array(openWorkflows.length).fill(null);
	};

	const isVisibleElement = (el) => {
		if (!(el instanceof HTMLElement)) return false;
		const style = window.getComputedStyle(el);
		if (style.display === "none" || style.visibility === "hidden" || Number(style.opacity || "1") === 0) {
			return false;
		}

		const rect = el.getBoundingClientRect();
		return rect.width > 0 && rect.height > 0;
	};

	const isSurfaceOpen = (name) => {
		const labelledBy = surfaceDefinitions[name];
		if (!labelledBy) return false;

		const marked = document.querySelector(`[data-nexus-surface="${name}"]`);
		if (isVisibleElement(marked)) return true;

		const surface = document.querySelector([
			`.p-dialog-mask .p-dialog.global-dialog[aria-labelledby="${labelledBy}"]`,
			`.p-dialog.global-dialog[aria-labelledby="${labelledBy}"]`,
			`[role="dialog"][aria-labelledby="${labelledBy}"]`,
		].join(", "));

		if (isVisibleElement(surface)) {
			surface.dataset.nexusSurface = name;
			return true;
		}

		return false;
	};

	const readPropertiesOpenFallback = () => {
		const panel = document.querySelector('[data-testid="properties-panel"]');
		if (panel) {
			const container = panel.closest('.p-splitterpanel');
			return !!(container && container.offsetWidth > 0);
		}

		return false;
	};

	const performSync = async () => {
		try {
			const workflowStore = getWorkflowStore();
			if (workflowStore) {
				const workflowList = createWorkflowSyncPayload(workflowStore);
				const currentPayloadStr = JSON.stringify(workflowList);
				if (currentPayloadStr !== bridge.lastPayloadStr) {
					bridge.lastPayloadStr = currentPayloadStr;
					bridge.send("WORKFLOW_SYNC", workflowList);
				}
			}

			try {
				const bottomPanelState = await getBottomPanelState();
				const isTerminalOpen = bottomPanelState.visible && bottomPanelState.activePanel === BOTTOM_PANELS.terminal;
				const isShortcutsOpen = bottomPanelState.visible && bottomPanelState.activePanel === BOTTOM_PANELS.shortcuts;

				if (bridge.lastTerminalState !== isTerminalOpen) {
					bridge.lastTerminalState = isTerminalOpen;
					bridge.send("UI_STATE_UPDATE", { terminalOpen: isTerminalOpen });
				}

				if (bridge.lastShortcutsState !== isShortcutsOpen) {
					bridge.lastShortcutsState = isShortcutsOpen;
					bridge.send("UI_STATE_UPDATE", { shortcutsOpen: isShortcutsOpen });
				}
			} catch (error) {
				bridge.log?.(`Bottom panel state sync failed: ${error?.message || error}`);
			}

			const zoomPercent = readCanvasZoomPercent();
			if (zoomPercent && bridge.lastZoomPercent !== zoomPercent) {
				bridge.lastZoomPercent = zoomPercent;
				bridge.send("UI_STATE_UPDATE", { zoomPercent });
			}

			const isMinimapOpen = readMinimapOpen();

			if (bridge.lastMinimapState !== isMinimapOpen) {
				bridge.lastMinimapState = isMinimapOpen;
				bridge.send("UI_STATE_UPDATE", { minimapOpen: isMinimapOpen });
			}

			const areLinksHidden = readLinksHidden();

			if (bridge.lastLinksHiddenState !== areLinksHidden) {
				bridge.lastLinksHiddenState = areLinksHidden;
				bridge.send("UI_STATE_UPDATE", { linksHidden: areLinksHidden });
			}

			let isPropertiesOpen = false;
			try {
				isPropertiesOpen = await isRightPanelOpen();
				lastExpensivePropertiesState = isPropertiesOpen;
			} catch (error) {
				if (Date.now() - lastExpensivePropertiesCheckAt >= expensivePropertiesCheckIntervalMs) {
					lastExpensivePropertiesCheckAt = Date.now();
					lastExpensivePropertiesState = readPropertiesOpenFallback();
					bridge.log?.(`Right panel setting state failed: ${error?.message || error}`);
				}

				isPropertiesOpen = lastExpensivePropertiesState;
			}

			if (bridge.lastPropertiesState !== isPropertiesOpen) {
				bridge.lastPropertiesState = isPropertiesOpen;
				bridge.send("UI_STATE_UPDATE", { propertiesOpen: isPropertiesOpen });
			}

			// Sync UI states (Apps)
			const appsBtn = getAppsButton();
			const isAppsOpen = isAppsButtonSelected(appsBtn);

			if (bridge.lastAppsState !== isAppsOpen) {
				bridge.lastAppsState = isAppsOpen;
				bridge.send("UI_STATE_UPDATE", { appsOpen: isAppsOpen });
			}

			if (bridge.lastAssetsState !== false) {
				bridge.lastAssetsState = false;
				bridge.send("UI_STATE_UPDATE", { assetsOpen: false });
			}

			const isSettingsOpen = isSurfaceOpen("settings");
			if (bridge.lastSettingsState !== isSettingsOpen) {
				bridge.lastSettingsState = isSettingsOpen;
				bridge.send("UI_STATE_UPDATE", { settingsOpen: isSettingsOpen });
			}

			const isTemplatesOpen = isSurfaceOpen("templates");
			if (isTemplatesOpen) {
				void ensureWorkflowTemplatesDefaultCategory()
					.catch((error) => bridge.log?.(`Workflow template category correction failed: ${error?.message || error}`));
			}

			if (bridge.lastTemplatesState !== isTemplatesOpen) {
				bridge.lastTemplatesState = isTemplatesOpen;
				bridge.send("UI_STATE_UPDATE", { templatesOpen: isTemplatesOpen });
			}

			try {
				const appModeOpen = await isAppModeOpen();
				if (bridge.lastAppModeState !== appModeOpen) {
					bridge.lastAppModeState = appModeOpen;
					bridge.lastAppsState = isAppsOpen;
					bridge.send("UI_STATE_UPDATE", { appModeOpen, appsOpen: isAppsOpen });
				}
			} catch (error) {
				bridge.log?.(`App mode state sync failed: ${error?.message || error}`);
			}
		} catch (e) { bridge.log?.(`Nexus Sync Error: ${e?.message || e}`); }
	};
	bridge.performSync = performSync;
	bridge.getWorkflowStore = getWorkflowStore;
	bridge.getOpenWorkflowByIndex = getOpenWorkflowByIndex;
	bridge.getOpenWorkflowByTarget = getOpenWorkflowByTarget;
	bridge.getActiveWorkflowIndex = getActiveWorkflowIndex;

	if (bridge.syncObserver) {
		bridge.syncObserver.disconnect();
	}

	bridge.syncObserver = new MutationObserver(() => bridge.scheduleSync());
	bridge.syncObserver.observe(document.body || document.documentElement, {
		childList: true,
		subtree: true,
		attributes: true,
		attributeFilter: ['class', 'aria-label', 'aria-selected', 'data-state']
	});

	const onVisibilityChange = () => bridge.scheduleSync(50);
	const onInteraction = () => bridge.scheduleSync(50);
	document.addEventListener('visibilitychange', onVisibilityChange);
	document.addEventListener('click', onInteraction, true);
	document.addEventListener('keyup', onInteraction, true);

	if (bridge.syncFallbackTimer) {
		clearInterval(bridge.syncFallbackTimer);
	}
	bridge.syncFallbackTimer = setInterval(() => bridge.scheduleSync(0), 3500);

	if (bridge.heartbeatTimer) {
		clearInterval(bridge.heartbeatTimer);
	}
	bridge.heartbeatTimer = setInterval(() => bridge.send("HEARTBEAT", "alive"), 5000);

	let workflowStoreUnsubscribe = null;
	const workflowStore = getWorkflowStore();
	if (typeof workflowStore?.$subscribe === "function") {
		try {
			workflowStoreUnsubscribe = workflowStore.$subscribe(
				() => bridge.scheduleSync(0),
				{ detached: true });
		} catch (error) {
			bridge.log?.(`Workflow store subscription failed: ${error?.message || error}`);
		}
	}

	performSync();
	bridge.getFilteredTabs = getFilteredTabs; // Expose for actions

	return () => {
		if (bridge.syncTimer !== null) {
			window.clearTimeout(bridge.syncTimer);
			bridge.syncTimer = null;
		}

		document.removeEventListener('visibilitychange', onVisibilityChange);
		document.removeEventListener('click', onInteraction, true);
		document.removeEventListener('keyup', onInteraction, true);
		workflowStoreUnsubscribe?.();
		workflowStoreUnsubscribe = null;

		if (bridge.syncObserver) {
			bridge.syncObserver.disconnect();
			bridge.syncObserver = null;
		}

		if (bridge.syncFallbackTimer) {
			window.clearInterval(bridge.syncFallbackTimer);
			bridge.syncFallbackTimer = null;
		}

		if (bridge.heartbeatTimer) {
			window.clearInterval(bridge.heartbeatTimer);
			bridge.heartbeatTimer = null;
		}

		if (bridge.performSync === performSync) {
			bridge.performSync = null;
		}
		if (bridge.getFilteredTabs === getFilteredTabs) {
			bridge.getFilteredTabs = null;
		}
		if (bridge.getWorkflowStore === getWorkflowStore) {
			bridge.getWorkflowStore = null;
		}
		if (bridge.getOpenWorkflowByIndex === getOpenWorkflowByIndex) {
			bridge.getOpenWorkflowByIndex = null;
		}
		if (bridge.getOpenWorkflowByTarget === getOpenWorkflowByTarget) {
			bridge.getOpenWorkflowByTarget = null;
		}
		if (bridge.getActiveWorkflowIndex === getActiveWorkflowIndex) {
			bridge.getActiveWorkflowIndex = null;
		}
	};
}
