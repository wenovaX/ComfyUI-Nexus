const importCache = new Map();
const helperCache = new Map();

function getRegistry() {
	const registry = globalThis.NexusComfyInternal ||= {
		helpers: {},
		reset() {
			importCache.clear();
			helperCache.clear();
			this.helpers = {};
		},
	};

	return registry;
}

export async function importFirstCached(cacheKey, paths) {
	if (importCache.has(cacheKey)) {
		return importCache.get(cacheKey);
	}

	const promise = importFirst(paths).catch((error) => {
		importCache.delete(cacheKey);
		throw error;
	});
	importCache.set(cacheKey, promise);
	return promise;
}

export async function importFirst(paths) {
	const errors = [];
	for (const path of paths) {
		try {
			return await import(path);
		} catch (error) {
			errors.push(`${path}: ${error?.message || error}`);
		}
	}

	throw new Error(errors.join("; "));
}

export function registerComfyInternalHelper(name, factory, bridge = null) {
	if (helperCache.has(name)) {
		return helperCache.get(name);
	}

	const helper = factory();
	helperCache.set(name, helper);

	const registry = getRegistry();
	registry.helpers[name] = helper;
	globalThis[name] = helper;
	bridge?.debug?.(`[ComfyInternal] helper registered: ${name}`);
	return helper;
}

export function getComfyInternalHealthSnapshot() {
	const registry = getRegistry();
	return {
		helpers: Object.keys(registry.helpers).sort(),
		imports: Array.from(importCache.keys()).sort(),
	};
}

export function logComfyInternalHealth(bridge, reason = "manual") {
	const snapshot = getComfyInternalHealthSnapshot();
	bridge?.log?.(`[ComfyInternal] ${reason}: helpers=${snapshot.helpers.join(",") || "none"} imports=${snapshot.imports.join(",") || "none"}`);
	return snapshot;
}

export function resetComfyInternalCaches() {
	getRegistry().reset();
}
