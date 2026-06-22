export function discoverComfyInternals(bridge, reason = "manual") {
	try {
		const pinia = bridge.discoveredPinia || app?.pinia || window.app?.pinia || window.__pinia || document.querySelector('#vue-app')?.__vue_app__?.config?.globalProperties?.$pinia || document.querySelector('#vue-app')?.__vue_app__?._context?.provides?.pinia;
		const storeKeys = pinia?._s ? Array.from(pinia._s.keys()) : [];
		const storeSummary = storeKeys.map((key) => {
			const store = pinia._s.get(key);
			return {
				key,
				props: getObjectPreviewKeys(bridge, store),
				functions: getFunctionKeys(bridge, store),
				collectionHints: getCollectionHints(bridge, store),
			};
		});

		const appSummary = {
			appKeys: getObjectPreviewKeys(bridge, app),
			appFunctions: getFunctionKeys(bridge, app),
			canvasKeys: getObjectPreviewKeys(bridge, app?.canvas),
			graphNodeCount: app?.canvas?.graph?._nodes?.length ?? null,
		};

		const vueProvides = getVueProvidesSummary(bridge);

		const summary = {
			reason,
			storeKeys,
			storeSummary,
			appSummary,
			vueProvides,
		};

		window.__nexusInternalDiscovery = summary;
		bridge.log(`[INTERNAL_DISCOVERY] ${JSON.stringify(summary)}`);
		return summary;
	} catch (error) {
		const message = error?.message || String(error);
		bridge.log(`[INTERNAL_DISCOVERY_ERROR] ${message}`);
		return null;
	}
}

export function getObjectPreviewKeys(bridge, value) {
	if (!value || (typeof value !== "object" && typeof value !== "function")) {
		return [];
	}

	try {
		return Reflect.ownKeys(value)
			.filter((key) => typeof key === "string")
			.slice(0, 80);
	} catch (error) {
		return [];
	}
}

export function getFunctionKeys(bridge, value) {
	if (!value || (typeof value !== "object" && typeof value !== "function")) {
		return [];
	}

	const keys = new Set();
	let current = value;
	let depth = 0;
	while (current && depth < 4) {
		try {
			for (const key of Reflect.ownKeys(current)) {
				if (typeof key !== "string" || key === "constructor") continue;
				if (typeof value[key] === "function" || typeof current[key] === "function") {
					keys.add(key);
				}
			}
		} catch (error) {}
		current = Object.getPrototypeOf(current);
		depth += 1;
	}

	return Array.from(keys).slice(0, 80);
}

export function getCollectionHints(bridge, value) {
	if (!value || typeof value !== "object") {
		return {};
	}

	const hints = {};
	for (const key of getObjectPreviewKeys(bridge, value)) {
		try {
			const item = value[key];
			if (item instanceof Map) {
				hints[key] = { type: "Map", size: item.size, keys: Array.from(item.keys()).slice(0, 20) };
			} else if (Array.isArray(item)) {
				hints[key] = { type: "Array", length: item.length, sample: item.slice(0, 5).map((entry) => describeValue(bridge, entry)) };
			} else if (item && typeof item === "object" && typeof item.size === "number") {
				hints[key] = { type: item.constructor?.name || "Object", size: item.size };
			}
		} catch (error) {}
	}
	return hints;
}

export function describeValue(bridge, value) {
	if (value == null) return value;
	if (typeof value !== "object") return value;
	return {
		type: value.constructor?.name || typeof value,
		keys: getObjectPreviewKeys(bridge, value).slice(0, 20),
	};
}

export function getVueProvidesSummary(bridge) {
	try {
		const provides = document.querySelector('#vue-app')?.__vue_app__?._context?.provides;
		if (!provides) return {};
		return {
			keys: getObjectPreviewKeys(bridge, provides),
			functions: getFunctionKeys(bridge, provides),
		};
	} catch (error) {
		return {};
	}
}
