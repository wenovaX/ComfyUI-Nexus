import { findAssetModuleByExport } from "./asset_modules.js";

const RIGHT_PANEL_SETTING_ID = "Comfy.RightSidePanel.IsOpen";
const NEW_MENU_SETTING_ID = "Comfy.UseNewMenu";

let settingStorePromise = null;

function tryCreateStore(factory) {
	try {
		return factory();
	} catch {
		return null;
	}
}

function isSettingStore(value) {
	return value &&
		typeof value.get === "function" &&
		typeof value.set === "function";
}

function getPiniaSettingStore() {
	const app = window.comfyAPI?.app?.app || window.app || null;
	const pinia = app?.extensionManager?.command?._p ||
		app?.extensionManager?.workflow?._p ||
		document.querySelector("#vue-app")?.__vue_app__?._context?.provides?.pinia ||
		document.querySelector("#vue-app")?._vnode?.appContext?.provides?.pinia ||
		null;

	return pinia?._s?.get?.("setting") ||
		pinia?._s?.get?.("settings") ||
		null;
}

export async function getSettingStore() {
	const directStore = getPiniaSettingStore();
	if (isSettingStore(directStore)) {
		return directStore;
	}

	if (!settingStorePromise) {
		settingStorePromise = findAssetModuleByExport((value, key) => {
			if (typeof value !== "function") return false;
			return key === "useSettingStore" || value.$id === "setting" || value.$id === "settings";
		})
			.then(({ value: useSettingStore }) => {
				const store = tryCreateStore(useSettingStore);
				if (isSettingStore(store)) {
					return store;
				}

				throw new Error("settingStore export shape is invalid");
			})
			.catch((error) => {
				settingStorePromise = null;
				throw error;
			});
	}

	return settingStorePromise;
}

export async function setRightPanelOpen(isOpen) {
	const store = await getSettingStore();
	return store.set(RIGHT_PANEL_SETTING_ID, Boolean(isOpen));
}

export async function isRightPanelOpen() {
	const store = await getSettingStore();
	const useNewMenu = store.get(NEW_MENU_SETTING_ID);
	const isLegacyMenu = useNewMenu === "Disabled";
	return !isLegacyMenu && Boolean(store.get(RIGHT_PANEL_SETTING_ID));
}

export async function toggleRightPanel() {
	const isOpen = await isRightPanelOpen();
	return setRightPanelOpen(!isOpen);
}
