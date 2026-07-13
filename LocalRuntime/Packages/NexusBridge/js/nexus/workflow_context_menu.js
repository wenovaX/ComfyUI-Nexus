const WORKFLOW_CONTEXT_ACTIONS = {
	RENAME: {
		index: 0,
		icon: ["pi-pencil"],
		commandId: "Comfy_RenameWorkflow",
	},
	DUPLICATE: {
		index: 1,
		icon: ["pi-copy"],
		commandId: "Comfy_DuplicateWorkflow",
	},
	BOOKMARK: {
		index: 2,
		icon: ["pi-bookmark"],
		commandId: null,
	},
	SAVE: {
		index: 3,
		icon: ["pi-save"],
		commandId: "Comfy_SaveWorkflow",
	},
	SAVE_AS: {
		index: 4,
		icon: ["pi-save"],
		commandId: "Comfy_SaveWorkflowAs",
	},
	EXPORT: {
		index: 5,
		icon: ["pi-download"],
		commandId: "Comfy_ExportWorkflow",
	},
	EXPORT_API: {
		index: 6,
		icon: ["pi-download"],
		commandId: "Comfy_ExportWorkflowAPI",
	},
	CLEAR_WORKFLOW: {
		index: 7,
		icon: ["pi-trash"],
		commandId: "Comfy_ClearWorkflow",
	},
	CLOSE_TAB: {
		index: 8,
		icon: ["pi-times"],
		commandId: "Workspace_CloseWorkflow",
	},
	CLOSE_LEFT_TABS: {
		index: 9,
		icon: ["pi-times", "pi-arrow-left"],
		commandId: null,
	},
	CLOSE_RIGHT_TABS: {
		index: 10,
		icon: ["pi-times", "pi-arrow-right"],
		commandId: null,
	},
	CLOSE_OTHER_TABS: {
		index: 11,
		icon: ["pi-times", "pi-arrows-h"],
		commandId: null,
	},
};

const WORKFLOW_COMMAND_ACTIONS = {
	duplicate: {
		commandIds: ["Comfy.DuplicateWorkflow", "Comfy_DuplicateWorkflow"],
	},
	save: {
		commandIds: ["Comfy.SaveWorkflow", "Comfy_SaveWorkflow"],
	},
	save_as: {
		commandIds: ["Comfy.SaveWorkflowAs", "Comfy_SaveWorkflowAs"],
	},
	export: {
		commandIds: ["Comfy.ExportWorkflow", "Comfy_ExportWorkflow"],
	},
	export_api: {
		commandIds: ["Comfy.ExportWorkflowAPI", "Comfy_ExportWorkflowAPI"],
	},
	clear: {
		commandIds: ["Comfy.ClearWorkflow", "Comfy_ClearWorkflow"],
	},
	close_tab: {
		commandIds: ["Workspace.CloseWorkflow", "Workspace_CloseWorkflow"],
	},
	close: {
		commandIds: ["Workspace.CloseWorkflow", "Workspace_CloseWorkflow"],
	},
};

let commandStorePromise = null;

const NEXUS_ACTION_TO_CONTEXT_ACTION = {
	rename: "RENAME",
	duplicate: "DUPLICATE",
	bookmark: "BOOKMARK",
	save: "SAVE",
	save_as: "SAVE_AS",
	export: "EXPORT",
	export_api: "EXPORT_API",
	clear: "CLEAR_WORKFLOW",
	close: "CLOSE_TAB",
	close_tab: "CLOSE_TAB",
	close_left: "CLOSE_LEFT_TABS",
	close_right: "CLOSE_RIGHT_TABS",
	close_other: "CLOSE_OTHER_TABS",
};

export const WorkflowContextMenuState = {
	ACTIONS: WORKFLOW_CONTEXT_ACTIONS,
	getOpenMenu,
	getItems,
	getState,
	getActionState,
	isEnabled,
	isDisabled,
	clickIfEnabled,
	buildCommandState,
	runCommandAction,
};

export function mapNexusWorkflowAction(actionType) {
	return NEXUS_ACTION_TO_CONTEXT_ACTION[String(actionType || "").trim()] || null;
}

export function getWorkflowContextMenuState(actionName) {
	return getActionState(actionName);
}

export function getWorkflowContextMenuSnapshot() {
	return getState();
}

export function clickWorkflowContextMenuAction(actionName) {
	return clickIfEnabled(actionName);
}

export async function executeWorkflowContextCommand(actionName) {
	const action = WORKFLOW_CONTEXT_ACTIONS[actionName];
	if (!action?.commandId) return false;

	return (await executeComfyCommandById(action.commandId)).ok;
}

export async function buildWorkflowCommandState() {
	return buildCommandState();
}

export async function runWorkflowCommandAction(actionKey, metadata = undefined) {
	return runCommandAction(actionKey, metadata);
}

export function getOpenWorkflowContextMenu() {
	return getOpenMenu();
}

function getComfyApp() {
	return window.comfyAPI?.app?.app || window.app || null;
}

function getPiniaFromApp(comfyApp) {
	return comfyApp?.extensionManager?.command?._p ||
		comfyApp?.extensionManager?.workflow?._p ||
		document.querySelector("#vue-app")?.__vue_app__?._context?.provides?.pinia ||
		document.querySelector("#vue-app")?._vnode?.appContext?.provides?.pinia ||
		null;
}

function getCommandStore() {
	const pinia = getPiniaFromApp(getComfyApp());
	return pinia?._s?.get?.("command") || null;
}

function getAssetModuleUrls() {
	return [
		...new Set([
			...[...document.scripts].map((script) => script.src).filter(Boolean),
			...performance
				.getEntriesByType("resource")
				.map((entry) => entry.name)
				.filter((name) => name.includes("/assets/") && name.endsWith(".js")),
		]),
	];
}

async function findAssetModuleByExport(predicate) {
	const urls = getAssetModuleUrls();
	const preferredUrls = [
		...urls.filter((url) => /\/assets\/commands-[^/]+\.js$/.test(url)),
		...urls.filter((url) => /\/assets\/GraphView-[^/]+\.js$/.test(url)),
		...urls.filter((url) => /\/assets\/dialogService-[^/]+\.js$/.test(url)),
		...urls.filter((url) => /\/assets\/main-[^/]+\.js$/.test(url)),
		...urls,
	];

	for (const url of [...new Set(preferredUrls)]) {
		try {
			const mod = await import(url);

			for (const [key, value] of Object.entries(mod)) {
				if (predicate(value, key, url, mod)) {
					return { mod, key, value, url };
				}
			}
		} catch {
			// Some chunks are not import-safe from this context. Keep scanning.
		}
	}

	throw new Error("target asset export not found");
}

async function getStoreById(id) {
	const { value: useStore } = await findAssetModuleByExport((value) =>
		typeof value === "function" && value.$id === id
	);

	const store = useStore();
	if (!store) {
		throw new Error(`${id} store export returned empty store`);
	}

	return store;
}

async function getRequiredCommandStore() {
	const command = getCommandStore();
	if (command && typeof command.execute === "function") {
		return command;
	}

	if (!commandStorePromise) {
		commandStorePromise = getStoreById("command")
			.catch((error) => {
				commandStorePromise = null;
				throw error;
			});
	}

	const discovered = await commandStorePromise;
	if (!discovered || typeof discovered.execute !== "function") {
		throw new Error("command store execute not found");
	}

	return discovered;
}

function getCommand(commandStore, commandId) {
	if (typeof commandStore.getCommand === "function") {
		return commandStore.getCommand(commandId);
	}

	return commandStore.commands?.[commandId] ||
		commandStore.commands?.get?.(commandId) ||
		null;
}

function isCommandRegistered(commandStore, commandId) {
	if (typeof commandStore.isRegistered === "function") {
		return Boolean(commandStore.isRegistered(commandId));
	}

	return Boolean(getCommand(commandStore, commandId));
}

async function resolveCommandId(commandStore, action) {
	for (const commandId of action.commandIds || []) {
		if (isCommandRegistered(commandStore, commandId)) {
			return commandId;
		}
	}

	return null;
}

function isCommandEnabled(command) {
	if (!command) return true;

	if (typeof command.active === "function") {
		try {
			return Boolean(command.active());
		} catch {
			return true;
		}
	}

	if (typeof command.active === "boolean") {
		return command.active;
	}

	if (typeof command.disabled === "function") {
		try {
			return !command.disabled();
		} catch {
			return true;
		}
	}

	if (typeof command.disabled === "boolean") {
		return !command.disabled;
	}

	return true;
}

async function getComfyCommandState(actionKey) {
	const action = WORKFLOW_COMMAND_ACTIONS[String(actionKey || "")];
	if (!action) {
		throw new Error(`Unknown workflow command action: ${actionKey}`);
	}

	const commandStore = await getRequiredCommandStore();
	const commandId = await resolveCommandId(commandStore, action);
	if (!commandId) {
		return {
			actionKey,
			commandId: null,
			commandIds: action.commandIds || [],
			enabled: false,
			disabled: true,
			reason: "command-not-registered",
		};
	}

	const command = getCommand(commandStore, commandId);
	const enabled = isCommandEnabled(command);

	return {
		actionKey,
		commandId,
		commandIds: action.commandIds || [],
		enabled,
		disabled: !enabled,
		reason: enabled ? null : "command-disabled",
	};
}

async function isComfyCommandAvailable(actionKey) {
	return (await getComfyCommandState(actionKey)).enabled;
}

async function executeComfyCommand(actionKey, metadata = undefined) {
	const commandStore = await getRequiredCommandStore();
	const state = await getComfyCommandState(actionKey);
	if (!state.commandId) {
		return {
			ok: false,
			reason: state.reason,
			actionKey,
			commandIds: state.commandIds,
		};
	}

	if (!state.enabled) {
		return {
			ok: false,
			reason: state.reason,
			actionKey,
			commandId: state.commandId,
		};
	}

	await commandStore.execute(state.commandId, metadata ? { metadata } : undefined);
	return {
		ok: true,
		actionKey,
		commandId: state.commandId,
	};
}

async function executeComfyCommandById(commandId, metadata = undefined) {
	const commandStore = await getRequiredCommandStore();
	if (!isCommandRegistered(commandStore, commandId)) {
		return {
			ok: false,
			reason: "command-not-registered",
			commandId,
		};
	}

	const command = getCommand(commandStore, commandId);
	if (!isCommandEnabled(command)) {
		return {
			ok: false,
			reason: "command-disabled",
			commandId,
		};
	}

	await commandStore.execute(commandId, metadata ? { metadata } : undefined);
	return {
		ok: true,
		commandId,
	};
}

async function buildCommandState() {
	const items = [];

	for (const key of Object.keys(WORKFLOW_COMMAND_ACTIONS)) {
		const state = await getComfyCommandState(key);
		items.push({
			key,
			commandId: state.commandId,
			commandIds: state.commandIds,
			enabled: state.enabled,
			disabled: state.disabled,
			reason: state.reason,
		});
	}

	return items;
}

async function runCommandAction(actionKey, metadata = undefined) {
	const action = WORKFLOW_COMMAND_ACTIONS[String(actionKey || "")];
	if (!action) {
		throw new Error(`Unknown workflow command action: ${actionKey}`);
	}

	return executeComfyCommand(actionKey, metadata);
}

function getOpenMenu() {
	const menus = [
		...document.querySelectorAll('[data-reka-menu-content][role="menu"][data-state="open"]'),
	];

	return menus.find((menu) => {
		const items = getItems(menu);
		const icons = items.map(getIconClasses).join("|");

		return menu.getAttribute("data-side") === "right" &&
			icons.includes("pi-copy") &&
			icons.includes("pi-save") &&
			icons.includes("pi-download") &&
			icons.includes("pi-trash");
	}) ?? null;
}

function getItems(menu = getOpenMenu()) {
	if (!menu) return [];

	return [
		...menu.querySelectorAll('[data-reka-collection-item][role="menuitem"]'),
	];
}

function getIconClasses(item) {
	return [...item.querySelectorAll("i")]
		.flatMap((icon) => [...icon.classList])
		.filter((name) => name.startsWith("pi-") || name.startsWith("icon-"));
}

function isItemDisabled(item) {
	if (!item) return true;

	return item.getAttribute("aria-disabled") === "true" ||
		item.hasAttribute("data-disabled") ||
		item.getAttribute("data-disabled") === "" ||
		item.getAttribute("disabled") !== null;
}

function getItemByAction(actionName) {
	const action = WORKFLOW_CONTEXT_ACTIONS[actionName];
	if (!action) {
		throw new Error(`Unknown workflow context action: ${actionName}`);
	}

	const items = getItems();
	const item = items[action.index];
	if (!item) return null;

	const iconClasses = getIconClasses(item);
	const iconMatched = action.icon.every((className) => iconClasses.includes(className));

	return {
		item,
		action,
		iconMatched,
		iconClasses,
	};
}

function getActionState(actionName) {
	const found = getItemByAction(actionName);
	if (!found) {
		return {
			actionName,
			found: false,
			enabled: false,
			disabled: true,
			iconMatched: false,
			reason: "menu item not found",
		};
	}

	const { item, action, iconMatched, iconClasses } = found;
	const disabled = isItemDisabled(item);

	return {
		actionName,
		found: true,
		enabled: iconMatched && !disabled,
		disabled,
		iconMatched,
		iconClasses,
		commandId: action.commandId,
		index: action.index,
	};
}

function isEnabled(actionName) {
	return getActionState(actionName).enabled;
}

function isDisabled(actionName) {
	return getActionState(actionName).disabled;
}

function getState() {
	const menu = getOpenMenu();
	const items = getItems(menu);

	return {
		visible: Boolean(menu),
		itemCount: items.length,
		actions: Object.fromEntries(
			Object.keys(WORKFLOW_CONTEXT_ACTIONS).map((name) => [
				name,
				getActionState(name),
			])
		),
	};
}

function clickIfEnabled(actionName) {
	const found = getItemByAction(actionName);
	if (!found) return false;

	const state = getActionState(actionName);
	if (!state.enabled) return false;

	found.item.click();
	return true;
}

globalThis.WorkflowContextMenuState = WorkflowContextMenuState;
