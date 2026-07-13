import { app } from "/scripts/app.js";

export const CANVAS_MODES = {
	select: "Select",
	hand: "Hand",
	selectUpper: "SELECT",
	handUpper: "HAND",
};

function getComfyApp() {
	return window.comfyAPI?.app?.app || window.app || app;
}

function getPiniaFromApp(comfyApp) {
	return comfyApp?.extensionManager?.command?._p ||
		comfyApp?.extensionManager?.workflow?._p ||
		document.querySelector("#vue-app")?.__vue_app__?._context?.provides?.pinia ||
		document.querySelector("#vue-app")?._vnode?.appContext?.provides?.pinia ||
		null;
}

function getCommandStore() {
	const comfyApp = getComfyApp();
	const pinia = getPiniaFromApp(comfyApp);
	return pinia?._s?.get?.("command") || null;
}

export function getCanvasMode() {
	const comfyApp = getComfyApp();
	const readOnly = comfyApp?.canvas?.state?.readOnly;
	if (readOnly === true) return CANVAS_MODES.hand;
	if (readOnly === false) return CANVAS_MODES.select;
	return null;
}

export async function setCanvasMode(mode) {
	const targetMode = String(mode || "").trim();
	const command = getCommandStore();
	if (!command || typeof command.execute !== "function") {
		throw new Error("command store execute not found");
	}

	if (targetMode === CANVAS_MODES.hand) {
		await command.execute("Comfy.Canvas.Lock");
		return CANVAS_MODES.hand;
	}

	if (targetMode === CANVAS_MODES.select) {
		await command.execute("Comfy.Canvas.Unlock");
		return CANVAS_MODES.select;
	}

	throw new Error(`unsupported canvas mode: ${targetMode}`);
}
