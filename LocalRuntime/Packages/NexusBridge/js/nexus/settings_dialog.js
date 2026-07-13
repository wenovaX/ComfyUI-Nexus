import { findAssetExportByKey } from "./asset_modules.js";
import { registerComfyInternalHelper } from "./comfy_internal/registry.js";

let dialogStorePromise = null;
let settingsDialogPromise = null;

const DIALOG_STORE_EXPORT_KEYS = ["t"];
const SETTINGS_DIALOG_EXPORT_KEYS = ["$n"];

function resetSettingsDialogCache() {
	dialogStorePromise = null;
	settingsDialogPromise = null;
}

function nextAnimationFrame() {
	return new Promise((resolve) => requestAnimationFrame(resolve));
}

function isDialogStore(value) {
	return value &&
		typeof value.isDialogOpen === "function" &&
		typeof value.closeDialog === "function" &&
		typeof value.showDialog === "function";
}

function isSettingsDialog(value) {
	return value &&
		typeof value.show === "function" &&
		typeof value.hide === "function" &&
		typeof value.showAbout === "function";
}

async function getDialogStore() {
	if (!dialogStorePromise) {
		dialogStorePromise = findAssetExportByKey(
			/\/assets\/dialogStore-[^/]+\.js(?:\?|$)/,
			DIALOG_STORE_EXPORT_KEYS
		)
			.then(({ value: useDialogStore }) => {
				if (typeof useDialogStore !== "function") {
					throw new Error("dialogStore export is not callable");
				}

				const store = useDialogStore();
				if (!isDialogStore(store)) {
					throw new Error("dialogStore shape is invalid");
				}

				return store;
			})
			.catch((error) => {
				dialogStorePromise = null;
				throw error;
			});
	}

	return dialogStorePromise;
}

async function getSettingsDialog() {
	if (!settingsDialogPromise) {
		settingsDialogPromise = findAssetExportByKey(
			/\/assets\/dialogService-[^/]+\.js(?:\?|$)/,
			SETTINGS_DIALOG_EXPORT_KEYS
		)
			.then(({ value: useSettingsDialog }) => {
				if (typeof useSettingsDialog !== "function") {
					throw new Error("useSettingsDialog export is not callable");
				}

				const dialog = useSettingsDialog();
				if (!isSettingsDialog(dialog)) {
					throw new Error("useSettingsDialog shape is invalid");
				}

				return dialog;
			})
			.catch((error) => {
				settingsDialogPromise = null;
				throw error;
			});
	}

	return settingsDialogPromise;
}

export function getComfySettingsHelper(bridge = null) {
	return registerComfyInternalHelper("ComfySettings", () => ({
		getSettingsDialog,
		getDialogStore,
		async click(panel, settingId) {
			const dialog = await getSettingsDialog();
			dialog.show(panel, settingId);
			await nextAnimationFrame();

			if (await this.isOpen()) {
				return true;
			}

			resetSettingsDialogCache();
			const refreshedDialog = await getSettingsDialog();
			refreshedDialog.show(panel, settingId);
			await nextAnimationFrame();
			return this.isOpen();
		},
		async close() {
			const dialog = await getSettingsDialog();
			dialog.hide();
			await nextAnimationFrame();

			if (!(await this.isOpen())) {
				return true;
			}

			resetSettingsDialogCache();
			const refreshedDialog = await getSettingsDialog();
			refreshedDialog.hide();
			await nextAnimationFrame();
			return !(await this.isOpen());
		},
		async isOpen() {
			const store = await getDialogStore();
			return store.isDialogOpen("global-settings");
		},
		async toggle(panel, settingId) {
			if (await this.isOpen()) {
				return this.close();
			}

			return this.click(panel, settingId);
		},
		reset: resetSettingsDialogCache,
		get open() {
			return this.click;
		},
	}), bridge);
}

export async function showSettingsDialog(panel, settingId) {
	await getComfySettingsHelper().click(panel, settingId);
	return true;
}

export async function hideSettingsDialog() {
	await getComfySettingsHelper().close();
	return true;
}

export async function showSettingsAboutDialog() {
	const dialog = await getSettingsDialog();
	dialog.showAbout();
	return true;
}

export async function isSettingsDialogOpen() {
	const store = await getDialogStore();
	return store.isDialogOpen("global-settings");
}

export async function closeSettingsDialogByStore() {
	const store = await getDialogStore();
	store.closeDialog({ key: "global-settings" });
	return true;
}
