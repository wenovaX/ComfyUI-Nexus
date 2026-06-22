const postNexusBridgeLoadError = (error) => {
	const payload = {
		kind: "module-import",
		message: error?.message || String(error),
		stack: error?.stack || "",
		source: "js/nexus_bridge.js",
	};

	try {
		window.__nexusNative?.post?.("WEB_ERROR", payload);
	} catch {
	}

	console.error("[NexusBridge] Failed to load Nexus module", error);
};

import("./nexus/index.js").catch(postNexusBridgeLoadError);
