import { createManagerAdapter, CUSTOM_NODE_SHOW_MODES, getComfyManagerMemoryHelper } from "./manager_adapter.js";
import { api } from "/scripts/api.js";
import { refreshWorkflowAppData } from "./workflow_sync.js";
import { renameWorkflowByStore } from "./workflow_rename.js";
import { insertWorkflowFromJson, insertWorkflowFromUserData } from "./assets.js";
import { getComfySettingsHelper } from "./settings_dialog.js";
import { isRightPanelOpen, toggleRightPanel } from "./settings_store.js";
import { BOTTOM_PANELS, toggleBottomPanel } from "./bottom_panel.js";
import { isAppModeOpen, isNexusAppSurfaceOpen, toggleAppMode } from "./app_mode.js";
import { ComfyMainMenu } from "./comfy_main_menu.js";
import { WorkflowActionMenu } from "./workflow_action_menu.js";
import { cancelCurrentRun } from "./current_run_cancel.js";
import { switchWorkflowGraph } from "./workflow_tab_graph_switcher.js";
import { showWorkflowTemplatesDialog } from "./workflow_templates_dialog.js";

const MANAGER_COMMAND_IDS = {
	toggleManager: "Comfy.Manager.Menu.ToggleVisibility",
};

const SETTINGS_BUTTON_SELECTORS = [
	'button:has([class*="icon-[lucide--settings]"])',
	'.side-bar-button:has([class*="icon-[lucide--settings]"])',
];

const SURFACE_DEFINITIONS = {
	settings: {
		labelledBy: "global-settings",
	},
	templates: {
		labelledBy: "global-workflow-template-selector",
	},
};

function getCommandCandidates(app, commandId) {
	const managers = [
		app?.extensionManager,
		window.app?.extensionManager,
		app?.commands,
		window.app?.commands,
		window.comfyAPI?.commands,
	].filter(Boolean);

	return managers.flatMap((manager) => [
		[manager.commands, manager.commands?.execute],
		[manager.commands, manager.commands?.executeCommand],
		[manager.commands, manager.commands?.run],
		[manager.commandRegistry, manager.commandRegistry?.execute],
		[manager.commandRegistry, manager.commandRegistry?.executeCommand],
		[manager.commandRegistry, manager.commandRegistry?.run],
		[manager, manager.execute],
		[manager, manager.executeCommand],
		[manager, manager.runCommand],
		[manager, manager.invokeCommand],
	].filter(([, execute]) => typeof execute === "function").map(([owner, execute]) => () => execute.call(owner, commandId)));
}

function isElementVisible(selector) {
	const el = document.querySelector(selector);
	return Boolean(el && el.style.display !== "none" && el.offsetParent !== null);
}

function isVisibleElement(el) {
	if (!(el instanceof HTMLElement)) return false;
	const style = window.getComputedStyle(el);
	if (style.display === "none" || style.visibility === "hidden" || Number(style.opacity || "1") === 0) {
		return false;
	}

	const rect = el.getBoundingClientRect();
	return rect.width > 0 && rect.height > 0;
}

function markSurface(name, surface) {
	if (surface) {
		surface.dataset.nexusSurface = name;
	}

	return surface;
}

function setNexusRailWidth(value) {
	const railWidth = Number(value);
	if (!Number.isFinite(railWidth)) return;

	document.documentElement.style.setProperty("--nexus-rail-width", `${Math.max(0, railWidth)}px`);
}

function findVisibleSurface(name) {
	const definition = SURFACE_DEFINITIONS[name];
	const marked = document.querySelector(`[data-nexus-surface="${name}"]`);
	if (isVisibleElement(marked)) {
		return marked;
	}

	if (!definition?.labelledBy) {
		return null;
	}

	const found = document.querySelector([
		`.p-dialog-mask .p-dialog.global-dialog[aria-labelledby="${definition.labelledBy}"]`,
		`.p-dialog.global-dialog[aria-labelledby="${definition.labelledBy}"]`,
		`[role="dialog"][aria-labelledby="${definition.labelledBy}"]`,
	].join(", "));

	return isVisibleElement(found) ? markSurface(name, found) : null;
}

function findVisibleSettingsSurface() {
	return findVisibleSurface("settings");
}

function findVisibleNamedSurface(name) {
	return findVisibleSurface(String(name || "").toLowerCase());
}

function getVisibleOverlaySurfaces() {
	return Array.from(document.querySelectorAll([
		".p-dialog",
		".p-popover",
		'[role="dialog"]',
		'[data-pc-name="popover"]',
	].join(", "))).filter(isVisibleElement);
}

async function markNewSurfaceAfterAction(name, beforeSurfaces) {
	await new Promise((resolve) => requestAnimationFrame(() => resolve()));
	await new Promise((resolve) => requestAnimationFrame(() => resolve()));

	const surface = getVisibleOverlaySurfaces()
		.find((candidate) => !beforeSurfaces.has(candidate));

	return markSurface(name, surface);
}

function closeSurface(surface, useEscapeFallback = true) {
	const closeButton = surface.querySelector([
		'[data-pc-section="closebutton"]',
		'.p-dialog-close-button',
		'.p-dialog-header-close',
		'.icon-[lucide--x]',
		'.pi-times',
	].join(", "));

	if (closeButton) {
		closeButton.click();
		return true;
	}

	if (!useEscapeFallback) {
		return false;
	}

	const escapeOptions = { key: "Escape", code: "Escape", bubbles: true, cancelable: true };
	document.dispatchEvent(new KeyboardEvent("keydown", escapeOptions));
	window.dispatchEvent(new KeyboardEvent("keydown", escapeOptions));
	document.dispatchEvent(new KeyboardEvent("keyup", escapeOptions));
	return true;
}

function closeVisibleSurfaceByName(name) {
	const surface = findVisibleNamedSurface(name);
	if (!surface) return false;

	return closeSurface(surface);
}

function sendSurfaceState(bridge, name, isOpen) {
	const payloadKey = `${name}Open`;
	bridge.send("UI_STATE_UPDATE", { [payloadKey]: isOpen });
}

async function sendSurfaceStateAfterAction(bridge, name) {
	await new Promise((resolve) => requestAnimationFrame(() => resolve()));
	await new Promise((resolve) => requestAnimationFrame(() => resolve()));
	sendSurfaceState(bridge, name, Boolean(findVisibleNamedSurface(name)));
}

function findVisibleComfyShareSurface() {
	const containers = Array.from(document.querySelectorAll("#comfyui-share-container"));
	const surface = containers
		.map((container) => container.closest(".comfy-modal"))
		.find(isVisibleElement);

	return markSurface("share", surface);
}

function closeComfyShareSurface() {
	const surface = findVisibleComfyShareSurface();
	if (!surface) return false;

	surface.style.display = "none";
	return true;
}

function findWorkflowTabCloseButton(tab) {
	if (!tab) return null;

	const selectors = [
		'[class*="close"]',
		'.pi-times',
		'.icon-[lucide--x]',
	];

	for (const selector of selectors) {
		const candidate = tab.querySelector?.(selector);
		if (candidate && candidate !== tab) {
			return candidate;
		}
	}

	return null;
}

async function closeWorkflowTabByIndex(bridge, index) {
	const tabs = bridge.getFilteredTabs?.() || [];
	const tab = tabs[index];
	if (!tab) {
		bridge.log?.(`CLOSE_WORKFLOW failed: canonical tab not found for workflow ${index}.`);
		return false;
	}

	let closeButton = findWorkflowTabCloseButton(tab);
	if (!closeButton) {
		tab.dispatchEvent(new PointerEvent("pointerenter", { bubbles: true, pointerType: "mouse" }));
		tab.dispatchEvent(new MouseEvent("mouseenter", { bubbles: true }));
		await new Promise((resolve) => requestAnimationFrame(resolve));
		closeButton = findWorkflowTabCloseButton(tab);
	}

	if (!closeButton) {
		bridge.log?.(`CLOSE_WORKFLOW failed: close button not found for tab ${index}.`);
		return false;
	}

	closeButton.click();
	return true;
}

function closeElementSurface(selector) {
	const dialog = document.querySelector(selector);
	const surface = dialog?.closest(".p-dialog-mask") || dialog;
	if (!surface) return false;

	surface.style.display = "none";
	return true;
}

function workflowTargetsMatch(left, right) {
	return Boolean(left && right) && (
		left === right ||
		(left.key && left.key === right.key) ||
		(left.path && left.path === right.path) ||
		(left.id != null && right.id != null && String(left.id) === String(right.id))
	);
}

async function waitForActiveWorkflowTarget(bridge, workflow, maxFrames = 12) {
	for (let attempt = 0; attempt < maxFrames; attempt++) {
		const activeWorkflow = bridge.getWorkflowStore?.()?.activeWorkflow;
		if (workflowTargetsMatch(activeWorkflow, workflow)) {
			return true;
		}

		await new Promise((resolve) => requestAnimationFrame(resolve));
	}

	return workflowTargetsMatch(bridge.getWorkflowStore?.()?.activeWorkflow, workflow);
}

async function showMainMenu(payload = {}) {
	if (WorkflowActionMenu.visible()) {
		await WorkflowActionMenu.close();
		await waitForWorkflowActionMenuClosed();
	}

	let menu = await ComfyMainMenu.show();
	if (!menu && !WorkflowActionMenu.visible()) {
		await nextAnimationFrame();
		menu = await ComfyMainMenu.show();
	}

	if (!menu) return false;

	const railWidth = Number(payload?.railWidth || 0);
	const rect = ComfyMainMenu.getRect();
	ComfyMainMenu.setPosition(railWidth + 8, rect?.y ?? 0);
	return true;
}

function nextAnimationFrame() {
	return new Promise((resolve) => requestAnimationFrame(resolve));
}

async function waitForWorkflowActionMenuClosed() {
	for (let attempt = 0; attempt < 5; attempt++) {
		await nextAnimationFrame();
		if (!WorkflowActionMenu.visible()) {
			return true;
		}
	}

	return false;
}

async function closeMainMenu() {
	if (!ComfyMainMenu.visible()) return false;

	await ComfyMainMenu.hide();
	return true;
}

function clickHiddenReservedSource(key, selectors, left = 0, top = 0) {
	const source = window.NexusSources?.get?.(key) ||
		(Array.isArray(selectors) ? selectors : [selectors])
			.filter(Boolean)
			.map((selector) => document.querySelector(selector))
			.find(Boolean);

	if (!source) {
		return false;
	}

	const original = {
		parent: source.parentElement,
		nextSibling: source.nextSibling,
		cssText: source.style.cssText,
	};

	if (source.parentElement !== document.body) {
		document.body.appendChild(source);
	}

	source.style.setProperty("position", "fixed", "important");
	source.style.setProperty("left", `${left}px`, "important");
	source.style.setProperty("top", `${top}px`, "important");
	source.style.setProperty("width", "40px", "important");
	source.style.setProperty("height", "40px", "important");
	source.style.setProperty("opacity", "0", "important");
	source.style.setProperty("visibility", "visible", "important");
	source.style.setProperty("pointer-events", "auto", "important");
	source.style.setProperty("z-index", "999999", "important");

	source.click();

	setTimeout(() => {
		source.style.cssText = original.cssText;
		if (original.parent && source.parentElement === document.body) {
			original.parent.insertBefore(source, original.nextSibling);
		}
	}, 100);

	return true;
}

async function tryExecuteComfyCommand(bridge, app, commandId, verify = null) {
	for (const execute of getCommandCandidates(app, commandId)) {
		try {
			await execute();
			if (verify) {
				await new Promise((resolve) => setTimeout(resolve, 80));
				if (!verify()) {
					bridge.log(`Comfy command did not pass verification: ${commandId}`);
					continue;
				}
			}
			return true;
		} catch (error) {
			bridge.log(`Comfy command candidate failed (${commandId}): ${error?.message || error}`);
		}
	}

	return false;
}

async function postFreeRequest(bridge, body, successMessage) {
	try {
		const response = await api.fetchApi("/free", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify(body),
		});

		if (!response.ok) {
			throw new Error(`HTTP ${response.status}`);
		}

		bridge.showToast?.(successMessage, "success");
		bridge.debug?.(`Manager API /free accepted: ${JSON.stringify(body)}`);
		return true;
	} catch (error) {
		bridge.showToast?.("ComfyUI Manager cache action failed.", "error");
		bridge.log(`Manager API /free failed: ${error?.message || error}`);
		return false;
	}
}

async function ensureNoFakeManagerInstance(bridge) {
	const adapter = await getManagerAdapter(bridge, 800);
	await adapter?.clearFakeManagerInstance?.();
}

function isCustomNodesManagerModeVisible(mode) {
	const dialog = document.querySelector("#cn-manager-dialog");
	const filter = dialog?.querySelector?.(".cn-manager-filter");
	return isElementVisible("#cn-manager-dialog") && String(filter?.value || "") === mode;
}

function closeCustomNodesManagerSurface() {
	return closeElementSurface("#cn-manager-dialog");
}

function closeComfyManagerSurface() {
	return closeElementSurface("#cm-manager-dialog");
}

function closeManagedWebPopupGroup(except = "") {
	if (except !== "share") {
		closeComfyShareSurface() || closeVisibleSurfaceByName("share");
	}

	if (except !== "favorites") {
		closeCustomNodesManagerSurface();
	}

	if (except !== "manager") {
		closeComfyManagerSurface();
	}
}

async function toggleVisibleComfyManagerDialog(bridge, app, selector, commandId) {
	if (!isElementVisible(selector)) return false;

	if (await tryExecuteComfyCommand(
		bridge,
		app,
		commandId,
		() => !isElementVisible(selector)
	)) {
		return true;
	}

	const dialog = document.querySelector(selector);
	if (dialog && closeSurface(dialog, false)) {
		return true;
	}

	return false;
}

function createUnavailableManagerAdapter() {
	return {
		available: false,
		async clearFakeManagerInstance() {},
		async showCustomNodesMode() { return false; },
		async showFavorites() { return false; },
	};
}

async function getManagerAdapter(bridge, timeoutMs = 0) {
	if (bridge.managerAdapter) return bridge.managerAdapter;
	if (!bridge.managerAdapterReady) return null;

	if (timeoutMs <= 0) {
		return bridge.managerAdapterReady;
	}

	return Promise.race([
		bridge.managerAdapterReady,
		new Promise((resolve) => {
			setTimeout(() => {
				bridge.debug?.(`ComfyUI-Manager adapter still initializing after ${timeoutMs}ms.`);
				resolve(null);
			}, timeoutMs);
		}),
	]);
}

function resolvePinia(bridge, app) {
	if (bridge.discoveredPinia) return bridge.discoveredPinia;
	let pinia = app?.pinia || window.app?.pinia || window.__pinia;
	if (!pinia) {
		const rootEl = document.querySelector('#vue-app');
		if (rootEl?.__vue_app__) {
			pinia = rootEl.__vue_app__.config?.globalProperties?.$pinia || rootEl.__vue_app__._context?.provides?.pinia;
		}
	}
	if (!pinia) {
		const vueApp = window.vueApp || globalThis.vueApp;
		const provides = vueApp?._context?.provides || {};
		pinia = Reflect.ownKeys(provides)
			.map(key => provides[key])
			.find(value => value && value._s instanceof Map);
	}
	if (pinia) {
		bridge.discoveredPinia = pinia;
	}
	return pinia;
}

async function deleteMediaAssetHistory(bridge, app, payload) {
	const items = Array.isArray(payload?.items) ? payload.items : [];
	const jobIds = [...new Set(items
		.map(item => item?.jobId)
		.filter(jobId => typeof jobId === 'string' && jobId.length > 0))];

	if (jobIds.length === 0) {
		bridge.log("Media asset history delete skipped: no job id.");
		return;
	}

	bridge.debug?.(`Media asset history delete requested: ${jobIds.length} job(s).`);
	const response = await fetch('/api/history', {
		cache: 'no-cache',
		method: 'POST',
		headers: {
			'Content-Type': 'application/json'
		},
		body: JSON.stringify({
			delete: jobIds
		})
	});

	if (!response.ok) {
		throw new Error(`history delete failed: ${response.status}`);
	}

	const pinia = resolvePinia(bridge, app);
	const assetsStore = pinia?._s?.get('assets');
	const queueStore = pinia?._s?.get('queue');
	if (!assetsStore || !queueStore) {
		const available = pinia?._s ? Array.from(pinia._s.keys()).join(", ") : "none";
		throw new Error(`history delete stores missing. available=${available}`);
	}

	await assetsStore.updateHistory?.();
	await queueStore.update?.({ detail: {} });
	await window.NexusMediaAssetJobSync?.snapshot?.('history-delete');
	bridge.debug?.(`Media asset history deleted: ${jobIds.length} job(s).`);
}

export async function setupActions(bridge, app) {
	let disposed = false;

	// Resolve ComfyUI stores and services lazily from the live app context.
	try {
		// Prefer the active Pinia instance from app, window, or Vue context.
		let pinia = app?.pinia || window.app?.pinia || window.__pinia;
		if (!pinia) {
			const rootEl = document.querySelector('#vue-app');
			if (rootEl?.__vue_app__) {
				pinia = rootEl.__vue_app__.config?.globalProperties?.$pinia || rootEl.__vue_app__._context?.provides?.pinia;
			}
		}

		if (pinia) {
			bridge.discoveredPinia = pinia;
			bridge.workflowStore = pinia._s.get('workflow');
			bridge.workspaceStore = pinia._s.get('workspace');
			bridge.modelToNodeStore = pinia._s.get('modelToNode');
			bridge.modelsStore = pinia._s.get('models');
			// Resolve workflow services from whichever host surface exposes them.
			bridge.workflowService = app.workflowService || window.workflowService || app.vue?._context.provides?.workflowService || document.querySelector('#vue-app')?.__vue_app__?._context.provides?.workflowService;
		}
	} catch (e) {
		bridge.log(`Service Discovery Failed: ${e.message}`);
	}

	bridge.managerAdapter = null;
	bridge.managerAdapterReady = createManagerAdapter(bridge, app)
		.then((adapter) => {
			if (!disposed) {
				bridge.managerAdapter = adapter;
			}
			return adapter;
		})
		.catch((error) => {
			if (!disposed) {
				bridge.log(`ComfyUI-Manager adapter setup failed: ${error?.message || error}`);
			}
			const adapter = createUnavailableManagerAdapter();
			if (!disposed) {
				bridge.managerAdapter = adapter;
			}
			return adapter;
		});

	const actionHandlers = {
		SWITCH_WORKFLOW: async (payload) => {
			try {
				await switchWorkflowGraph(Number(payload.index), bridge);
				bridge.scheduleSync?.(0);
				return true;
			} catch (error) {
				bridge.log?.(`SWITCH_WORKFLOW failed: workflow ${payload.index} is unavailable. ${error?.message || error}`);
				return false;
			}
		},
		TAB_ACTION: async (payload) => {
			const action = String(payload?.action || "");
			const tab = bridge.getFilteredTabs?.()[payload.index];
			if (!tab) {
				bridge.log?.(`TAB_ACTION failed: canonical tab not found for workflow ${payload.index}.`);
				return false;
			}

			const workflow = bridge.getOpenWorkflowByIndex?.(payload.index);
			if (workflow) {
				await switchWorkflowGraph(workflow, bridge);
				bridge.scheduleSync?.(0);
				await waitForActiveWorkflowTarget(bridge, workflow);
			}

			const result = await bridge.executeTabContextAction(tab, payload.action);
			if (!result) {
				bridge.log?.(`TAB_ACTION failed: action=${action}, index=${payload.index}.`);
			}
			return result;
		},
		TAB_COMMAND_ACTION: async (payload) => {
			const action = String(payload?.action || "");
			const workflow = bridge.getOpenWorkflowByTarget?.(payload) ||
				bridge.getOpenWorkflowByIndex?.(payload.index);
			if (!workflow) {
				bridge.log?.(`TAB_COMMAND_ACTION failed: workflow target unavailable. index=${payload.index}, id=${payload.id || ""}, path=${payload.path || ""}`);
				return false;
			}

			await switchWorkflowGraph(workflow, bridge);
			bridge.scheduleSync?.(0);
			const activeReady = await waitForActiveWorkflowTarget(bridge, workflow);
			if (!activeReady) {
				bridge.log?.(`TAB_COMMAND_ACTION skipped: action=${action}, index=${payload.index}, target workflow did not become active.`);
				return false;
			}

			const result = await bridge.executeTabCommandAction(action, {
				index: payload.index,
				path: workflow?.path || "",
				filename: workflow?.filename || "",
			});
			if (!result) {
				bridge.log?.(`TAB_COMMAND_ACTION failed: action=${action}, index=${payload.index}, path=${workflow?.path || ""}.`);
			}
			return result;
		},
		RELAY_SHORTCUT: (payload) => bridge.relayShortcut(payload.key, payload.ctrl, payload.shift, payload.alt),
		CLOSE_WORKFLOW: (payload) => closeWorkflowTabByIndex(bridge, payload.index),
		NEW_WORKFLOW: () => bridge.createNewWorkflow(),
		REFRESH_BOOKMARKS: () => bridge.refreshBookmarks(),
		REFRESH_WORKFLOW_APP_DATA: () => refreshWorkflowAppData(),
		RENAME_WORKFLOW_BY_STORE: async (payload) => {
			const result = await renameWorkflowByStore(payload);
			bridge.performSync?.();
			bridge.scheduleSync?.(50);
			return result;
		},
		INSERT_WORKFLOW_FROM_USERDATA: async (payload) => {
			const result = await insertWorkflowFromUserData(payload?.path, payload?.options || {});
			bridge.performSync?.();
			bridge.scheduleSync?.(50);
			return result;
		},
		INSERT_WORKFLOW_FROM_JSON: async (payload) => {
			const result = insertWorkflowFromJson(payload?.workflowJson, payload?.options || {});
			bridge.performSync?.();
			bridge.scheduleSync?.(50);
			return result;
		},
		ASSET_BROWSER_OPEN: (payload) => bridge.handleAssetBrowserOpen(payload),
		ASSET_BROWSER_DRAG_START: (payload) => bridge.handleAssetBrowserDragStart(payload),
		ASSET_DROP_FEEDBACK: (payload) => bridge.handleAssetDropFeedback(payload),
		ASSET_DROP_FEEDBACK_SOURCE: (payload) => bridge.handleAssetDropFeedbackSource(payload),
		MEDIA_ASSET_DELETE_HISTORY: (payload) => deleteMediaAssetHistory(bridge, app, payload),
		DISCOVER_INTERNALS: () => bridge.discoverComfyInternals("manual"),
		COMFY_INTERNAL_HEALTH: () => bridge.reportComfyInternalHealth("manual"),
		PROBE_MODEL_PROVIDER: (payload) => bridge.probeModelProvider(payload?.directory || payload?.modelDirectory || "checkpoints"),
		SHOW_TOAST: (payload) => bridge.showToast(payload.message, payload.type),
		PAN_CANVAS_ANIMATE: (payload) => bridge.animateCanvasPan(payload.deltaX, payload.duration),
		SIMULATE_DROP: (payload) => bridge.simulateDrop(payload),
		INTERRUPT: async () => {
			try {
				return await cancelCurrentRun();
			} catch (error) {
				bridge.log(`Current run API cancel failed: ${error?.message || error}`);
				return false;
			}
		},
		ADJUST_QUEUE_COUNT: (payload) => bridge.adjustQueueCount(payload.type),
		SET_QUEUE_COUNT: (payload) => bridge.setQueueCount(payload.value),

		// Toolbar actions
		PANEL_TOGGLE: async () => {
			try {
				await toggleRightPanel();
				const propertiesOpen = await isRightPanelOpen();
				bridge.lastPropertiesState = propertiesOpen;
				bridge.send("UI_STATE_UPDATE", { propertiesOpen });
				return;
			} catch (error) {
				bridge.log(`Right panel setting toggle failed: ${error?.message || error}`);
			}

			bridge.clickToolbarButton([
				'button:has(.icon-\\[lucide--panel-right\\])',
			]);
		},
		SHARE: async () => {
			if (closeComfyShareSurface() || closeVisibleSurfaceByName("share")) {
				return;
			}

			closeManagedWebPopupGroup("share");

			const beforeSurfaces = new Set(getVisibleOverlaySurfaces());

			if (window.NexusSources?.click?.("share")) {
				await markNewSurfaceAfterAction("share", beforeSurfaces);
				return;
			}

			bridge.log("Share action failed: source button not found.");
		},
		FREE_CACHE: async () => {
			try {
				if (await getComfyManagerMemoryHelper(bridge).freeModelAndNodeCache()) {
					return;
				}
			} catch (error) {
				bridge.log(`ComfyManagerMemory free cache failed: ${error?.message || error}`);
			}

			const ok = await postFreeRequest(
				bridge,
				{ unload_models: true, free_memory: true },
				"Models and execution cache cleared."
			);
			if (ok) return;

			if (window.NexusSources?.click?.("freeCache")) {
				return;
			}

			bridge.log("Free cache action failed: API unavailable.");
		},
		UNLOAD_MODELS: async () => {
			try {
				if (await getComfyManagerMemoryHelper(bridge).unloadModels()) {
					return;
				}
			} catch (error) {
				bridge.log(`ComfyManagerMemory unload models failed: ${error?.message || error}`);
			}

			const ok = await postFreeRequest(
				bridge,
				{ unload_models: true },
				"Models unloaded."
			);
			if (ok) return;

			if (window.NexusSources?.click?.("unloadModels")) {
				return;
			}

			bridge.log("Unload models action failed: API unavailable.");
		},
		SHOW_FAVORITES: async () => {
			if (isCustomNodesManagerModeVisible(CUSTOM_NODE_SHOW_MODES.favorites)) {
				const adapter = await getManagerAdapter(bridge, 1200);
				if (await adapter?.closeCustomNodesMode?.(CUSTOM_NODE_SHOW_MODES.favorites)) {
					return;
				}

				closeCustomNodesManagerSurface();
				return;
			}

			const adapter = await getManagerAdapter(bridge, 1200);
			closeManagedWebPopupGroup("favorites");

			if (await adapter?.showFavorites?.()) {
				return;
			}

			if (window.NexusSources?.click?.("managerFavorites")) {
				await new Promise((resolve) => requestAnimationFrame(() => resolve()));
				await new Promise((resolve) => requestAnimationFrame(() => resolve()));
				closeComfyManagerSurface();

				if (isElementVisible("#cn-manager-dialog")) {
					return;
				}
			}

			bridge.log("Favorites action failed: Manager adapter did not open Favorites.");
		},
		SHOW_MANAGER: async () => {
			if (await toggleVisibleComfyManagerDialog(
				bridge,
				app,
				"#cm-manager-dialog",
				MANAGER_COMMAND_IDS.toggleManager
			)) {
				return;
			}

			closeManagedWebPopupGroup("manager");

			await ensureNoFakeManagerInstance(bridge);
			if (await tryExecuteComfyCommand(
				bridge,
				app,
				MANAGER_COMMAND_IDS.toggleManager,
				() => isElementVisible("#cm-manager-dialog")
			)) return;

			if (window.NexusSources?.click?.("manager")) {
				return;
			}

			bridge.log("Manager action failed: Manager command and source button unavailable.");
		},
		ENTER_APP_MODE: async () => {
			try {
				const wasAppModeOpen = await isAppModeOpen();
				if (!wasAppModeOpen && ComfyMainMenu.visible()) {
					await closeMainMenu();
					await new Promise((resolve) => requestAnimationFrame(resolve));
				}

				const state = await toggleAppMode();
				const appModeOpen = isNexusAppSurfaceOpen(state);
				bridge.lastAppModeState = appModeOpen;
				bridge.send("UI_STATE_UPDATE", { appModeOpen });
				return true;
			} catch (error) {
				bridge.log(`App mode toggle failed: ${error?.message || error}`);
				return false;
			}
		},
		SET_UI_ISOLATION: (payload) => bridge.setUiIsolation(payload?.enabled !== false),

		WORKFLOW_ACTIONS: async () => {
			await WorkflowActionMenu.show();
		},
		WORKFLOW_MENU_ACTION: (payload) => {
			return bridge.executeWorkflowMenuAction(payload?.action);
		},
		WORKFLOW_COMMAND_ACTION: (payload) => {
			return bridge.executeWorkflowCommandAction(payload?.action);
		},
		HELP_CENTER: (payload) => {
			const leftOffset = Number(payload.railWidth || 0);
			const btn = window.NexusSources?.get?.("helpCenter") ||
						document.querySelector('.comfy-help-center-btn');
			if (!btn) return;

			btn.click();

			let attempts = 0;
			const interval = setInterval(() => {
				const popup = document.querySelector('.help-center-popup');
				if (popup) {
					clearInterval(interval);
					// Adjust position to fit Nexus Shell layout
					popup.style.setProperty("position", "fixed", "important");
					popup.style.setProperty("left", `${leftOffset}px`, "important");
					popup.style.setProperty("bottom", "0px", "important");
					popup.style.setProperty("top", "auto", "important");
					popup.style.setProperty("z-index", "999999", "important");
				}
				if (++attempts > 30) clearInterval(interval);
			}, 10);
		},
		TOGGLE_BOTTOM_PANEL: async () => {
			try {
				const state = await toggleBottomPanel(BOTTOM_PANELS.terminal);
				bridge.lastTerminalState = state.activePanel === BOTTOM_PANELS.terminal;
				bridge.lastShortcutsState = state.activePanel === BOTTOM_PANELS.shortcuts;
				bridge.send("UI_STATE_UPDATE", {
					terminalOpen: bridge.lastTerminalState,
					shortcutsOpen: bridge.lastShortcutsState,
				});
			} catch (error) {
				bridge.log(`Bottom panel terminal action failed: ${error?.message || error}`);
			}
		},
		TOGGLE_SHORTCUTS: async () => {
			try {
				const state = await toggleBottomPanel(BOTTOM_PANELS.shortcuts);
				bridge.lastTerminalState = state.activePanel === BOTTOM_PANELS.terminal;
				bridge.lastShortcutsState = state.activePanel === BOTTOM_PANELS.shortcuts;
				bridge.send("UI_STATE_UPDATE", {
					terminalOpen: bridge.lastTerminalState,
					shortcutsOpen: bridge.lastShortcutsState,
				});
			} catch (error) {
				bridge.log(`Bottom panel shortcuts action failed: ${error?.message || error}`);
			}
		},
		TOGGLE_SETTINGS: async () => {
			const comfySettings = getComfySettingsHelper(bridge);
			let settingsDialogOpen = false;
			try {
				settingsDialogOpen = await comfySettings.isOpen();
			} catch (error) {
				bridge.log(`Settings dialog store check failed: ${error?.message || error}`);
				settingsDialogOpen = Boolean(findVisibleSettingsSurface());
			}

			if (settingsDialogOpen) {
				try {
					if (!await comfySettings.close()) {
						throw new Error("settings dialog API close did not close the dialog");
					}
				} catch (error) {
					bridge.log(`Settings dialog API hide failed: ${error?.message || error}`);
					const settingsSurface = findVisibleSettingsSurface();
					if (settingsSurface) {
						closeSurface(settingsSurface);
					}
				}
				sendSurfaceState(bridge, "settings", false);
				return;
			}

			try {
				if (!await comfySettings.open()) {
					throw new Error("settings dialog API show did not open the dialog");
				}
				void sendSurfaceStateAfterAction(bridge, "settings");
				return;
			} catch (error) {
				bridge.log(`Settings dialog API show failed: ${error?.message || error}`);
			}

			const clicked = clickHiddenReservedSource("settings", SETTINGS_BUTTON_SELECTORS);

			if (!clicked) {
				bridge.log("Settings action failed: source button not found.");
				sendSurfaceState(bridge, "settings", false);
				return;
			}

			void sendSurfaceStateAfterAction(bridge, "settings");
		},
		TOGGLE_TEMPLATES: async (payload = {}) => {
			if (closeVisibleSurfaceByName("templates")) {
				sendSurfaceState(bridge, "templates", false);
				return;
			}

			try {
				await showWorkflowTemplatesDialog({ initialCategory: payload.initialCategory });
				void sendSurfaceStateAfterAction(bridge, "templates");
				return;
			} catch (error) {
				bridge.log(`Templates dialog API show failed: ${error?.message || error}`);
			}

			if (window.NexusSources?.click?.("templates")) {
				void sendSurfaceStateAfterAction(bridge, "templates");
				return;
			}

			const btn = document.querySelector('button.templates-tab-button');
			if (btn) {
				btn.click();
				void sendSurfaceStateAfterAction(bridge, "templates");
			} else {
				bridge.log("Templates action failed: source button not found.");
				sendSurfaceState(bridge, "templates", false);
			}
		},
		TOGGLE_APPS: () => {
			if (window.NexusSources?.click?.("apps")) {
				return;
			}

			const btn = document.querySelector('button.apps-tab-button');
			if (btn) {
				btn.click();
			} else {
				bridge.log("Apps action failed: source button not found.");
			}
		},
		TOGGLE_ASSETS: () => {},
		TOGGLE_MAIN_MENU: async (payload = {}) => {
			if (payload.railWidth !== undefined) {
				setNexusRailWidth(payload.railWidth);
			}

			if (await isAppModeOpen()) {
				await closeMainMenu();
				return false;
			}

			if (ComfyMainMenu.visible()) {
				await closeMainMenu();
				return true;
			}

			return showMainMenu(payload);
		},
		CLOSE_MAIN_MENU: () => closeMainMenu(),
		SET_RAIL_WIDTH: (payload = {}) => setNexusRailWidth(payload.railWidth),
		QUEUE_PROMPT: async (payload) => {
			const count = payload.batchCount || 1;

			// 1. Update UI for visual feedback
			bridge.setQueueCount(count);

			try {
				// 2. Direct API Call (Bypasses UI timing issues)
				if (window.app && typeof window.app.queuePrompt === 'function') {
					await window.app.queuePrompt(0, count);
					return;
				}
			} catch (e) {
				bridge.log("Direct API call failed, falling back to button click");
			}

			// 3. Fallback: Classic button click if API is not available
			await new Promise(r => setTimeout(r, 150));
			bridge.clickToolbarButton('button[data-testid="queue-button"]');
		},
		VIEW_QUEUE: () => {
			const btn = document.querySelector('button[data-testid="queue-overlay-toggle"]');
			if (btn) btn.click();
		},
		SET_RUN_MODE: (payload) => bridge.setRunMode(payload?.mode),
		SET_CANVAS_MODE: (payload) => bridge.setCanvasMode(payload?.mode),
	};

	const nexusAction = (action, payload) => {
		const handler = actionHandlers[action];
		if (!handler) {
			bridge.log(`Missing NexusAction handler: ${action}`);
			return Promise.resolve(false);
		}

		try {
			const result = handler(payload ?? {});
			if (result && typeof result.catch === "function") {
				return result.catch((error) => {
					bridge.log(`NexusAction async error [${action}]: ${error?.stack || error?.message || error}`);
					throw error;
				});
			}
			return Promise.resolve(result);
		} catch (error) {
			bridge.log(`NexusAction error [${action}]: ${error?.stack || error?.message || error}`);
			return Promise.reject(error);
		}
	};
	window.NexusAction = nexusAction;

	return () => {
		disposed = true;
		if (window.NexusAction === nexusAction) {
			delete window.NexusAction;
		}
	};
}
