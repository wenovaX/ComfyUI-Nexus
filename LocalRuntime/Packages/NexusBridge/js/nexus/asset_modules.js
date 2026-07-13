function getLoadedAssetModuleUrls() {
	return [
		...new Set([
			...Array.from(document.scripts).map((script) => script.src).filter(Boolean),
			...performance
				.getEntriesByType("resource")
				.map((entry) => entry.name)
				.filter((name) => name.includes("/assets/") && /\.js(?:\?|$)/.test(name)),
		]),
	];
}

function getPreferredAssetModuleUrls() {
	const urls = getLoadedAssetModuleUrls();
	const preferredUrls = [
		...urls.filter((url) => /\/assets\/dialogService-[^/]+\.js(?:\?|$)/.test(url)),
		...urls.filter((url) => /\/assets\/GraphView-[^/]+\.js(?:\?|$)/.test(url)),
		...urls.filter((url) => /\/assets\/main-[^/]+\.js(?:\?|$)/.test(url)),
		...urls,
	];

	return [...new Set(preferredUrls)];
}

export async function findAssetModule(regex) {
	const url = getLoadedAssetModuleUrls().find((candidate) => regex.test(candidate));
	if (!url) {
		throw new Error(`asset module not found: ${regex}`);
	}

	return import(url);
}

export async function findAssetModuleByExport(predicate) {
	for (const url of getPreferredAssetModuleUrls()) {
		try {
			const module = await import(url);
			for (const [key, value] of Object.entries(module)) {
				// Predicates must inspect exports only. Do not call arbitrary bundled functions here;
				// dialogService/GraphView/main chunks also export functions with UI side effects.
				if (predicate(value, key, url, module)) {
					return { module, key, value, url };
				}
			}
		} catch {
			// Ignore stale or incompatible chunks and keep scanning loaded assets.
		}
	}

	throw new Error("target asset export not found");
}

export async function findPiniaStoreFactoryById(storeId) {
	return findAssetModuleByExport((value) =>
		typeof value === "function" && value.$id === storeId
	);
}

export async function findAssetExportByKey(regex, exportKeys) {
	const module = await findAssetModule(regex);
	for (const key of exportKeys) {
		if (Object.prototype.hasOwnProperty.call(module, key)) {
			return { module, key, value: module[key] };
		}
	}

	throw new Error(`asset export not found: ${regex} [${exportKeys.join(", ")}]`);
}

export async function findAssetExportCandidates(regex, predicate) {
	const module = await findAssetModule(regex);
	const matches = [];

	for (const [key, value] of Object.entries(module)) {
		if (predicate(value, key, module)) {
			matches.push({ module, key, value });
		}
	}

	return matches;
}
