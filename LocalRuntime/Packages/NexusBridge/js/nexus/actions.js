import { createManagerAdapter, CUSTOM_NODE_SHOW_MODES } from "./manager_adapter.js";
import { api } from "/scripts/api.js";
import { refreshWorkflowAppData } from "./workflow_sync.js";
import { renameWorkflowByStore } from "./workflow_rename.js";
import { insertWorkflowFromJson, insertWorkflowFromUserData } from "./assets.js";

const MANAGER_COMMAND_IDS = {
	toggleManager: "Comfy.Manager.Menu.ToggleVisibility",
};

const SETTINGS_BUTTON_SELECTORS = [
	'button[aria-label^="Settings"]',
	'button[title^="Settings"]',
];

const SURFACE_DEFINITIONS = {
	settings: {
		labelledBy: "global-settings",
	},
	templates: {
		labelledBy: "global-workflow-template-selector",
	},
};

const MAIN_MENU_STATE = {
	isOpen: false,
	settleTimer: 0,
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
		'button[aria-label="Close"]',
		'button[aria-label="close"]',
		'button[title="Close"]',
		'button[title="close"]',
		'[data-pc-section="closebutton"]',
		'.p-dialog-close-button',
		'.p-dialog-header-close',
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

	const closeButton = Array.from(surface.querySelectorAll("button"))
		.find((button) => String(button.textContent || "").trim().toLowerCase() === "close");

	if (closeButton) {
		closeButton.click();
		return true;
	}

	surface.style.display = "none";
	return true;
}

function findWorkflowTabCloseButton(tab) {
	if (!tab) return null;

	const selectors = [
		'button[aria-label*="Close"]',
		'button[title*="Close"]',
		'[aria-label*="Close"]',
		'[title*="Close"]',
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

function getMainMenuButton() {
	return document.querySelector('.comfy-menu-button-wrapper');
}

function getMainMenu() {
	return document.querySelector('.p-tieredmenu.comfy-command-menu');
}

function isMainMenuVisible(menu = getMainMenu()) {
	return isVisibleElement(menu);
}

function alignMainMenu(payload = {}) {
	const menu = getMainMenu();
	if (!menu) return;

	if (menu.parentElement !== document.body) {
		document.body.appendChild(menu);
	}

	menu.style.setProperty("position", "fixed", "important");
	menu.style.setProperty("top", "56px", "important");
	menu.style.setProperty("left", `${(payload.railWidth || 0) + 8}px`, "important");
	menu.style.setProperty("transform", "none", "important");
	menu.style.setProperty("translate", "none", "important");
	menu.style.setProperty("margin", "0", "important");
	menu.style.setProperty("opacity", "1", "important");
	MAIN_MENU_STATE.isOpen = true;
}

function prepareMainMenuButton() {
	const btn = getMainMenuButton();
	if (!btn) return null;

	btn.style.setProperty("position", "fixed", "important");
	btn.style.setProperty("top", "8px", "important");
	btn.style.setProperty("left", "0px", "important");
	btn.style.setProperty("width", "48px", "important");
	btn.style.setProperty("height", "48px", "important");
	btn.style.setProperty("opacity", "0", "important");
	btn.style.setProperty("z-index", "-1", "important");

	if (btn.parentElement !== document.body) {
		document.body.appendChild(btn);
	}

	return btn;
}

function closeMainMenu() {
	if (!MAIN_MENU_STATE.isOpen && !isMainMenuVisible()) return false;

	if (MAIN_MENU_STATE.settleTimer) {
		clearTimeout(MAIN_MENU_STATE.settleTimer);
		MAIN_MENU_STATE.settleTimer = 0;
	}

	const btn = prepareMainMenuButton();
	if (btn) {
		btn.click();
		MAIN_MENU_STATE.isOpen = false;
		return true;
	}

	document.dispatchEvent(new KeyboardEvent("keydown", {
		key: "Escape",
		code: "Escape",
		keyCode: 27,
		which: 27,
		bubbles: true
	}));
	MAIN_MENU_STATE.isOpen = false;
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
		const managerCommon = await import("/extensions/ComfyUI-Manager/js/common.js");
		if (typeof managerCommon.free_models === "function") {
			await managerCommon.free_models(Boolean(body.free_memory));
			return true;
		}
	} catch (error) {
		bridge.log(`ComfyUI-Manager free_models unavailable: ${error?.message || error}`);
	}

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

function clickFirstButtonMatchingText(labels) {
	const normalizedLabels = labels.map((label) => label.toLowerCase());
	const buttons = Array.from(document.querySelectorAll("button"));
	const target = buttons.find((button) => {
		const text = [
			button.getAttribute("aria-label"),
			button.getAttribute("title"),
			button.textContent,
		].filter(Boolean).join(" ").trim().toLowerCase();

		return normalizedLabels.some((label) => text.includes(label));
	});

	target?.click();
	return Boolean(target);
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
				bridge.log(`ComfyUI-Manager adapter still initializing after ${timeoutMs}ms.`);
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

	bridge.log(`Media asset history delete requested: ${jobIds.length} job(s).`);
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
	bridge.log(`Media asset history deleted: ${jobIds.length} job(s).`);
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
			const store = bridge.getWorkflowStore?.();
			const workflow = bridge.getOpenWorkflowByIndex?.(payload.index);
			if (!store || !workflow || typeof store.openWorkflow !== "function") {
				bridge.log?.(`SWITCH_WORKFLOW failed: workflow ${payload.index} is unavailable.`);
				return false;
			}

			await store.openWorkflow(workflow);
			bridge.scheduleSync?.(0);
			return true;
		},
		TAB_ACTION: (payload) => {
			const tab = bridge.getFilteredTabs?.()[payload.index];
			if (!tab) {
				bridge.log?.(`TAB_ACTION failed: canonical tab not found for workflow ${payload.index}.`);
				return false;
			}

			bridge.executeTabContextAction(tab, payload.action);
			return true;
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
		PROBE_MODEL_PROVIDER: (payload) => bridge.probeModelProvider(payload?.directory || payload?.modelDirectory || "checkpoints"),
		SHOW_TOAST: (payload) => bridge.showToast(payload.message, payload.type),
		PAN_CANVAS_ANIMATE: (payload) => bridge.animateCanvasPan(payload.deltaX, payload.duration),
		SIMULATE_DROP: (payload) => bridge.simulateDrop(payload),
		INTERRUPT: () => {
			document.querySelector('button[aria-label="Cancel current run"], button[aria-label="Interrupt"], .comfyui-interrupt-button, [data-testid="interrupt-button"]')?.click();
		},
		ADJUST_QUEUE_COUNT: (payload) => bridge.adjustQueueCount(payload.type),
		SET_QUEUE_COUNT: (payload) => bridge.setQueueCount(payload.value),

		// Toolbar actions
		PANEL_TOGGLE: () => bridge.clickToolbarButton([
			'button[aria-label="Toggle properties panel"]',
			'button[title="Toggle properties panel"]'
		]),
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

			const clicked = bridge.clickToolbarButton([
				'button[aria-label="Share"]',
				'button[title="Share"]'
			]) || clickFirstButtonMatchingText(["share"]);

			if (clicked) {
				await markNewSurfaceAfterAction("share", beforeSurfaces);
			}

			if (!clicked) {
				bridge.log("Share action fallback failed: source button not found.");
			}
		},
		FREE_CACHE: async () => {
			if (window.NexusSources?.click?.("freeCache")) {
				return;
			}

			const ok = await postFreeRequest(
				bridge,
				{ unload_models: true, free_memory: true },
				"Models and execution cache cleared."
			);
			if (ok) return;

			bridge.clickToolbarButton([
				'button[aria-label="Free model and node cache"]',
				'button[title="Free model and node cache"]',
				'button[aria-label="Free cache"]',
				'button[title="Free cache"]'
			]);
		},
		UNLOAD_MODELS: async () => {
			if (window.NexusSources?.click?.("unloadModels")) {
				return;
			}

			const ok = await postFreeRequest(
				bridge,
				{ unload_models: true },
				"Models unloaded."
			);
			if (ok) return;

			bridge.clickToolbarButton([
				'button[aria-label="Unload Models"]',
				'button[title="Unload Models"]',
				'button[aria-label="Unload models"]',
				'button[title="Unload models"]'
			]);
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

			const clicked = bridge.clickToolbarButton([
				'button[aria-label="ComfyUI Manager"]',
				'button[title="ComfyUI Manager"]'
			]) || clickFirstButtonMatchingText(["comfyui manager", "manager"]);

			if (!clicked) {
				bridge.log("Manager action fallback failed: source button not found.");
			}
		},
		ENTER_APP_MODE: () => bridge.enterAppMode(),
		SET_UI_ISOLATION: (payload) => bridge.setUiIsolation(payload?.enabled !== false),

		WORKFLOW_ACTIONS: () => {
			const btn = window.NexusSources?.get?.("workflowActions") ||
				document.querySelector('button[aria-label="Workflow actions"]');
			if (!btn) return;
			// Temporarily restore the button so click opens dropdown at correct position
			const oldVisibility = btn.style.visibility;
			const oldPointer = btn.style.pointerEvents;
			btn.style.setProperty("visibility", "visible", "important");
			btn.style.setProperty("pointer-events", "auto", "important");
			btn.click();
			setTimeout(() => {
				btn.style.setProperty("visibility", oldVisibility, "important");
				btn.style.setProperty("pointer-events", oldPointer, "important");
			}, 100);
		},
		WORKFLOW_MENU_ACTION: (payload) => bridge.executeWorkflowMenuAction(payload?.action),
		HELP_CENTER: (payload) => {
			const leftOffset = Number(payload.railWidth || 0);
			const btn = window.NexusSources?.get?.("helpCenter") ||
						document.querySelector('.comfy-help-center-btn') ||
						document.querySelector('button[aria-label="Help Center"]');
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
		TOGGLE_BOTTOM_PANEL: () => {
			const btn = window.NexusSources?.get?.("bottomPanel") ||
						document.querySelector('button[aria-label="Toggle Bottom Panel"]') ||
						document.querySelector('button[title="Toggle Bottom Panel"]');
			if (btn) btn.click();
		},
		TOGGLE_SHORTCUTS: () => {
			const btn = window.NexusSources?.get?.("keyboardShortcuts") ||
						document.querySelector('button[aria-label^="Keyboard Shortcuts"]');
			if (btn) btn.click();
		},
		TOGGLE_SETTINGS: () => {
			const settingsSurface = findVisibleSettingsSurface();
			if (settingsSurface) {
				closeSurface(settingsSurface);
				sendSurfaceState(bridge, "settings", false);
				return;
			}

			const clicked = clickHiddenReservedSource("settings", SETTINGS_BUTTON_SELECTORS);

			if (!clicked) {
				bridge.log("Settings action failed: source button not found.");
				sendSurfaceState(bridge, "settings", false);
				return;
			}

			void sendSurfaceStateAfterAction(bridge, "settings");
		},
		TOGGLE_TEMPLATES: () => {
			if (closeVisibleSurfaceByName("templates")) {
				sendSurfaceState(bridge, "templates", false);
				return;
			}

			if (window.NexusSources?.click?.("templates")) {
				void sendSurfaceStateAfterAction(bridge, "templates");
				return;
			}

			const btn = document.querySelector('button.templates-tab-button') ||
						document.querySelector('button[aria-label="Templates"]');
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

			const btn = document.querySelector('button[aria-label="Apps"].apps-tab-button') ||
						document.querySelector('button.apps-tab-button') ||
						document.querySelector('button[aria-label="Apps"]');
			if (btn) {
				btn.click();
			} else {
				bridge.log("Apps action failed: source button not found.");
			}
		},
		TOGGLE_ASSETS: () => {},
		TOGGLE_MAIN_MENU: (payload = {}) => {
			if (payload.railWidth !== undefined) {
				document.documentElement.style.setProperty("--nexus-rail-width", `${payload.railWidth}px`);
			}

			if (MAIN_MENU_STATE.isOpen || isMainMenuVisible()) {
				closeMainMenu();
				return;
			}

			const btn = prepareMainMenuButton();
			if (!btn) return;

			btn.click();
			MAIN_MENU_STATE.isOpen = true;
			MAIN_MENU_STATE.settleTimer = setTimeout(() => {
				MAIN_MENU_STATE.settleTimer = 0;
				alignMainMenu(payload);
			}, 50);
		},
		CLOSE_MAIN_MENU: () => {
			closeMainMenu();
		},
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
			bridge.clickToolbarButton([
				'button[aria-label="Queue Prompt"]',
				'button[title="Queue Prompt"]',
				'button[data-testid="queue-button"]'
			]);
		},
		VIEW_QUEUE: () => {
			const btn = document.querySelector('button[data-testid="queue-overlay-toggle"]') ||
						document.querySelector('button[aria-label="View Queue"]') ||
						document.querySelector('button[title="View Queue"]');
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
		if (MAIN_MENU_STATE.settleTimer) {
			clearTimeout(MAIN_MENU_STATE.settleTimer);
			MAIN_MENU_STATE.settleTimer = 0;
		}
		MAIN_MENU_STATE.isOpen = false;
	};
}
