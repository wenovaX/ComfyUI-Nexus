import { findAssetModuleByExport } from "./asset_modules.js";

export const BOTTOM_PANELS = {
	terminal: "terminal",
	shortcuts: "shortcuts",
};

export const BOTTOM_PANEL_TABS = {
	commandTerminal: "command-terminal",
	logsTerminal: "logs-terminal",
	shortcutsEssentials: "shortcuts-essentials",
	shortcutsViewControls: "shortcuts-view-controls",
};

let bottomPanelStorePromise = null;

function readMaybeRef(value) {
	return value && typeof value === "object" && "value" in value
		? value.value
		: value;
}

async function getBottomPanelStore() {
	if (!bottomPanelStorePromise) {
		bottomPanelStorePromise = findAssetModuleByExport((value) =>
			typeof value === "function" && value.$id === "bottomPanel")
			.then(({ value: useBottomPanelStore }) => {
				const store = useBottomPanelStore();
				if (
					!store ||
					typeof store.togglePanel !== "function" ||
					typeof store.toggleBottomPanel !== "function" ||
					typeof store.toggleBottomPanelTab !== "function" ||
					!("bottomPanelVisible" in store) ||
					!("activePanel" in store) ||
					!("activeBottomPanelTabId" in store)
				) {
					throw new Error("matched bottomPanel export but store shape is invalid");
				}

				return store;
			})
			.catch((error) => {
				bottomPanelStorePromise = null;
				throw error;
			});
	}

	return bottomPanelStorePromise;
}

export async function getBottomPanelState() {
	const store = await getBottomPanelStore();
	return {
		visible: Boolean(readMaybeRef(store.bottomPanelVisible)),
		activePanel: readMaybeRef(store.activePanel) || null,
		activeTabId: readMaybeRef(store.activeBottomPanelTabId) || null,
	};
}

export async function openBottomPanel(panelType = BOTTOM_PANELS.terminal) {
	const store = await getBottomPanelStore();
	const state = await getBottomPanelState();
	if (state.activePanel !== panelType) {
		store.togglePanel(panelType);
	}

	return getBottomPanelState();
}

export async function toggleBottomPanel(panelType = BOTTOM_PANELS.terminal) {
	const store = await getBottomPanelStore();
	store.togglePanel(panelType);
	return getBottomPanelState();
}

export async function closeBottomPanelIf(panelType) {
	const store = await getBottomPanelStore();
	const state = await getBottomPanelState();
	if (state.activePanel === panelType) {
		store.bottomPanelVisible = false;
	}

	return getBottomPanelState();
}

// Available for precise future routing:
// await openBottomPanelTab(BOTTOM_PANEL_TABS.logsTerminal);
export async function openBottomPanelTab(tabId) {
	const store = await getBottomPanelStore();
	const state = await getBottomPanelState();
	if (state.activeTabId !== tabId) {
		store.toggleBottomPanelTab(tabId);
	}

	return getBottomPanelState();
}
