import { findAssetExportByKey, findAssetExportCandidates } from "./asset_modules.js";

export const APP_MODES = {
	app: "app",
	graph: "graph",
	builderInputs: "builder:inputs",
	builderOutputs: "builder:outputs",
	builderArrange: "builder:arrange",
};

let appModePromise = null;

const APP_MODE_EXPORT_KEYS = ["ut"];
const APP_MODE_MODULE_REGEXES = [
	/\/assets\/GraphView-[^/]+\.js(?:\?|$)/,
	/\/assets\/dialogService-[^/]+\.js(?:\?|$)/,
];

function readMaybeRef(value) {
	return value && typeof value === "object" && "value" in value
		? value.value
		: value;
}

function createStateSnapshot(state) {
	return {
		mode: state?.mode || "",
		isAppMode: Boolean(state?.isAppMode),
		isGraphMode: Boolean(state?.isGraphMode),
		isBuilderMode: Boolean(state?.isBuilderMode),
		isSelectMode: Boolean(state?.isSelectMode),
		isArrangeMode: Boolean(state?.isArrangeMode),
		isNexusAppSurfaceOpen: isNexusAppSurfaceOpen(state),
	};
}

function formatStateSnapshot(state) {
	return [
		`mode=${state.mode || "unknown"}`,
		`app=${state.isAppMode ? "1" : "0"}`,
		`graph=${state.isGraphMode ? "1" : "0"}`,
		`builder=${state.isBuilderMode ? "1" : "0"}`,
		`select=${state.isSelectMode ? "1" : "0"}`,
		`arrange=${state.isArrangeMode ? "1" : "0"}`,
		`surface=${state.isNexusAppSurfaceOpen ? "1" : "0"}`,
	].join(" ");
}

function isValidAppMode(appMode) {
	return Boolean(
		appMode &&
		typeof appMode.setMode === "function" &&
		"mode" in appMode &&
		"isAppMode" in appMode
	);
}

function looksLikeAppModeFactory(value) {
	if (typeof value !== "function") {
		return false;
	}

	const source = Function.prototype.toString.call(value);
	return (
		source.includes("isAppMode") &&
		source.includes("isGraphMode") &&
		source.includes("setMode")
	);
}

async function tryCreateAppModeFromExport(exportInfo) {
	const { key, value: useAppMode } = exportInfo;
	if (typeof useAppMode !== "function") {
		return null;
	}

	const appMode = useAppMode();
	if (!isValidAppMode(appMode)) {
		console.warn(`[NexusBridge] Ignoring invalid app mode export: ${key}`);
		return null;
	}

	return appMode;
}

async function findAppModeByKnownKeys() {
	for (const regex of APP_MODE_MODULE_REGEXES) {
		try {
			for (const key of APP_MODE_EXPORT_KEYS) {
				const exportInfo = await findAssetExportByKey(regex, [key]);
				const appMode = await tryCreateAppModeFromExport(exportInfo);
				if (appMode) {
					return appMode;
				}
			}
		} catch {
			// Keep probing the next known module.
		}
	}

	return null;
}

async function findAppModeBySafeShapeScan() {
	for (const regex of APP_MODE_MODULE_REGEXES) {
		try {
			const candidates = await findAssetExportCandidates(regex, looksLikeAppModeFactory);
			for (const candidate of candidates) {
				const appMode = await tryCreateAppModeFromExport(candidate);
				if (appMode) {
					return appMode;
				}
			}
		} catch {
			// Keep probing the next known module.
		}
	}

	return null;
}

async function getAppMode() {
	if (appModePromise) {
		return appModePromise;
	}

	appModePromise = findAppModeByKnownKeys()
		.then((appMode) => appMode || findAppModeBySafeShapeScan())
		.then((appMode) => {
			if (!appMode) {
				throw new Error("app mode export not found");
			}
			return appMode;
		})
		.catch((error) => {
			appModePromise = null;
			throw error;
		});

	return appModePromise;
}

export async function getAppModeState() {
	const appMode = await getAppMode();
	const mode = readMaybeRef(appMode.mode);

	return {
		mode,
		isAppMode: Boolean(readMaybeRef(appMode.isAppMode)),
		isGraphMode: Boolean(readMaybeRef(appMode.isGraphMode)),
		isBuilderMode: Boolean(readMaybeRef(appMode.isBuilderMode)),
		isSelectMode: Boolean(readMaybeRef(appMode.isSelectMode)),
		isArrangeMode: Boolean(readMaybeRef(appMode.isArrangeMode)),
	};
}

export function isNexusAppSurfaceOpen(state) {
	const mode = String(state?.mode || "");
	return Boolean(
		state?.isAppMode ||
		state?.isBuilderMode ||
		mode === APP_MODES.app ||
		mode.startsWith("builder:")
	);
}

export async function openAppMode() {
	const appMode = await getAppMode();
	appMode.setMode(APP_MODES.app);
	return getAppModeState();
}

export async function closeAppMode() {
	const appMode = await getAppMode();
	appMode.setMode(APP_MODES.graph);
	return getAppModeState();
}

export async function toggleAppMode() {
	const appMode = await getAppMode();
	const state = await getAppModeState();

	if (isNexusAppSurfaceOpen(state)) {
		appMode.setMode(APP_MODES.graph);
	} else {
		appMode.setMode(APP_MODES.app);
	}

	return getAppModeState();
}

export async function isAppModeOpen() {
	try {
		const state = await getAppModeState();
		return isNexusAppSurfaceOpen(state);
	} catch (error) {
		console.warn(`[NexusBridge] App mode state unavailable: ${error?.message || error}`);
		return false;
	}
}

export function setupAppModeTrace(bridge) {
	let disposed = false;
	let unsubscribe = null;
	let fallbackTimer = null;
	let lastSnapshot = "";

	const emitState = async (reason) => {
		if (disposed) {
			return;
		}

		try {
			const state = createStateSnapshot(await getAppModeState());
			const snapshot = JSON.stringify(state);
			if (snapshot === lastSnapshot) {
				return;
			}

			lastSnapshot = snapshot;
			bridge.log?.(`[APP_MODE] ${reason}: ${formatStateSnapshot(state)}`);
		} catch (error) {
			bridge.log?.(`[APP_MODE] trace failed: ${error?.message || error}`);
		}
	};

	getAppMode()
		.then((appMode) => {
			if (disposed) {
				return;
			}

			void emitState("initial");

			if (typeof appMode.$subscribe === "function") {
				unsubscribe = appMode.$subscribe(() => {
					void emitState("store");
				});
				return;
			}

			fallbackTimer = window.setInterval(() => {
				void emitState("poll");
			}, 500);
		})
		.catch((error) => {
			bridge.log?.(`[APP_MODE] trace setup failed: ${error?.message || error}`);
		});

	return () => {
		disposed = true;
		try {
			unsubscribe?.();
		} catch {
		}
		if (fallbackTimer !== null) {
			window.clearInterval(fallbackTimer);
			fallbackTimer = null;
		}
	};
}

// Direct mode selection is available through appMode.setMode(APP_MODES.builderInputs)
// and related APP_MODES values, but Nexus currently only needs app/graph toggle.
