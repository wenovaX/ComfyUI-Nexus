import { findAssetModuleByExport } from "./asset_modules.js";

export const AUTO_QUEUE_MODE = {
	run: "disabled",
	onChange: "change",
	instantIdle: "instant-idle",
	instantRunning: "instant-running",
};

const RUN_MODE_TO_STORE_MODE = new Map([
	["Run", AUTO_QUEUE_MODE.run],
	["Run (On Change)", AUTO_QUEUE_MODE.onChange],
	["Run (Instant)", AUTO_QUEUE_MODE.instantIdle],
]);

let storePromise = null;

async function getStore() {
	if (storePromise) {
		return storePromise;
	}

	storePromise = findAssetModuleByExport((value) =>
		typeof value === "function" && value.$id === "queueSettingsStore"
	)
		.then(({ value: useQueueSettingsStore }) => {
			const store = useQueueSettingsStore();

			if (!store || !("mode" in store) || !("batchCount" in store)) {
				throw new Error("matched queueSettingsStore but shape is invalid");
			}

			return store;
		})
		.catch((error) => {
			storePromise = null;
			throw error;
		});

	return storePromise;
}

function normalizeStoreMode(mode) {
	const normalized = String(mode || "").trim();
	if (Object.values(AUTO_QUEUE_MODE).includes(normalized)) {
		return normalized;
	}

	const mapped = RUN_MODE_TO_STORE_MODE.get(normalized);
	if (mapped) {
		return mapped;
	}

	throw new Error(`Invalid auto queue mode: ${normalized}`);
}

export async function getAutoQueueMode() {
	const store = await getStore();
	return store.mode;
}

export async function setAutoQueueMode(mode) {
	const storeMode = normalizeStoreMode(mode);
	const store = await getStore();
	store.mode = storeMode;
	return store.mode;
}

export async function setRunMode(mode) {
	return setAutoQueueMode(mode);
}

export async function getAutoQueueState() {
	const store = await getStore();
	const mode = store.mode;

	return {
		mode,
		batchCount: store.batchCount,
		isRun: mode === AUTO_QUEUE_MODE.run,
		isOnChange: mode === AUTO_QUEUE_MODE.onChange,
		isInstant: mode === AUTO_QUEUE_MODE.instantIdle || mode === AUTO_QUEUE_MODE.instantRunning,
		isInstantRunning: mode === AUTO_QUEUE_MODE.instantRunning,
	};
}

// Debug helpers intentionally available from this module:
// - getAutoQueueState()
// - getAutoQueueMode()
// - setAutoQueueMode(AUTO_QUEUE_MODE.*)
// - setRunMode("Run" | "Run (On Change)" | "Run (Instant)")
