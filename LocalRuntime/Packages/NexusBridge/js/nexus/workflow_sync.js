export async function refreshWorkflowAppData() {
	const workflowStore = window.comfyAPI?.app?.app?.extensionManager?.workflow;

	if (!workflowStore?.syncWorkflows) {
		throw new Error("workflowStore.syncWorkflows not found");
	}

	await workflowStore.syncWorkflows();
}

export function normalizeWorkflowTabName(text) {
	return String(text || "").replace(/[\s*•●×]+$/g, "").trim();
}

function normalizeWorkflowPath(path) {
	return String(path || "")
		.trim()
		.replace(/\\/g, "/")
		.replace(/^\/+/, "")
		.toLowerCase();
}

function getWorkflowPathAliases(path) {
	const normalized = normalizeWorkflowPath(path);
	if (!normalized) return [];

	const aliases = new Set([normalized]);
	if (normalized.startsWith("user/default/workflows/")) {
		aliases.add(normalized.slice("user/default/workflows/".length));
	}
	if (normalized.startsWith("workflows/")) {
		aliases.add(normalized.slice("workflows/".length));
	}
	return [...aliases];
}

function normalizeWorkflowName(name) {
	return normalizeWorkflowTabName(name)
		.replace(/\.json$/i, "")
		.trim()
		.toLowerCase();
}

function getElementPathValues(element) {
	return ["data-workflow-path", "data-file-path", "data-path"]
		.map((name) => element?.getAttribute?.(name))
		.filter((value) => typeof value === "string" && value.trim());
}

function getWorkflowName(workflow) {
	const filename = String(workflow?.filename || workflow?.fullFilename || "").trim();
	if (filename) return filename.replace(/\.json$/i, "");

	const path = String(workflow?.path || "").replace(/\\/g, "/");
	return path.split("/").pop()?.replace(/\.json$/i, "") || "Untitled";
}

function isWorkflowActive(store, workflow) {
	if (!workflow) return false;
	if (workflow === store?.activeWorkflow) return true;

	try {
		if (typeof store?.isActive === "function" && store.isActive(workflow)) return true;
	} catch {
		// Fall through to path identity.
	}

	const workflowPath = normalizeWorkflowPath(workflow.path);
	const activePath = normalizeWorkflowPath(store?.activeWorkflow?.path);
	return Boolean(workflowPath && workflowPath === activePath);
}

function getWorkflowId(workflow, index) {
	const candidate = workflow?.id ?? workflow?.key;
	if (typeof candidate === "string" || typeof candidate === "number") {
		return candidate;
	}

	return workflow?.path || index;
}

export function createWorkflowSyncPayload(store) {
	const openWorkflows = Array.from(store?.openWorkflows || []);
	return openWorkflows.map((workflow, index) => ({
		id: getWorkflowId(workflow, index),
		name: getWorkflowName(workflow),
		active: isWorkflowActive(store, workflow),
		modified: Boolean(workflow?._isModified),
		path: workflow?.path || "",
	}));
}

function getTabSurface(element) {
	return element?.closest?.('[role="tablist"], .p-tablist, .workflow-tabs, .comfyui-tabs') ||
		element?.parentElement || null;
}

function getElementPathMatchScore(element, workflow) {
	const workflowAliases = new Set(getWorkflowPathAliases(workflow?.path));
	if (workflowAliases.size === 0) return 0;

	return getElementPathValues(element).some((path) =>
		getWorkflowPathAliases(path).some((alias) => workflowAliases.has(alias))) ? 50 : 0;
}

function scoreTabSurface(elements, openWorkflows, isVisibleElement) {
	let score = elements.length === openWorkflows.length ? 30 : 0;
	const count = Math.min(elements.length, openWorkflows.length);
	for (let index = 0; index < count; index++) {
		const element = elements[index];
		const workflow = openWorkflows[index];
		score += getElementPathMatchScore(element, workflow);
		if (normalizeWorkflowName(element.textContent) === normalizeWorkflowName(getWorkflowName(workflow))) {
			score += 20;
		}
	}

	if (elements.some(isVisibleElement)) score += 10;
	return score;
}

function mapSurfaceToWorkflows(elements, openWorkflows) {
	const mapped = new Array(openWorkflows.length).fill(null);
	const consumed = new Set();

	for (let index = 0; index < openWorkflows.length; index++) {
		const workflow = openWorkflows[index];
		const positional = elements[index];
		if (positional && !consumed.has(positional) && (
			getElementPathMatchScore(positional, workflow) > 0 ||
			normalizeWorkflowName(positional.textContent) === normalizeWorkflowName(getWorkflowName(workflow)))) {
			mapped[index] = positional;
			consumed.add(positional);
			continue;
		}

		const pathMatches = elements.filter((element) =>
			!consumed.has(element) && getElementPathMatchScore(element, workflow) > 0);
		if (pathMatches.length === 1) {
			mapped[index] = pathMatches[0];
			consumed.add(pathMatches[0]);
			continue;
		}

		const expectedName = normalizeWorkflowName(getWorkflowName(workflow));
		const nameMatches = elements.filter((element) =>
			!consumed.has(element) && normalizeWorkflowName(element.textContent) === expectedName);
		if (nameMatches.length === 1) {
			mapped[index] = nameMatches[0];
			consumed.add(nameMatches[0]);
		}
	}

	return mapped;
}

export function setupWorkflowSync(bridge) {
	const systemTabNames = ["LOGS", "TERMINAL", "SHORTCUTS", "CONSOLE", "DEBUG"];
	const surfaceDefinitions = {
		settings: "global-settings",
		templates: "global-workflow-template-selector",
	};
	let lastExpensivePropertiesCheckAt = 0;
	let lastExpensivePropertiesState = false;
	const expensivePropertiesCheckIntervalMs = 1000;
	const getWorkflowStore = () =>
		window.comfyAPI?.app?.app?.extensionManager?.workflow || bridge.workflowStore || null;
	const getOpenWorkflows = () => Array.from(getWorkflowStore()?.openWorkflows || []);
	const getOpenWorkflowByIndex = (index) => getOpenWorkflows()[index] || null;
	const getActiveWorkflowIndex = () => {
		const store = getWorkflowStore();
		return getOpenWorkflows().findIndex((workflow) => isWorkflowActive(store, workflow));
	};

	const getFilteredTabs = () => {
		const openWorkflows = getOpenWorkflows();
		if (openWorkflows.length === 0) return [];

		const allTabs = document.querySelectorAll('.workflow-tab, .p-tablist .p-tab, .comfy-tab-item, .comfyui-tab-item');
		const candidates = Array.from(allTabs).filter(el => {
			// 1. Location-based exclusion: Ignore tabs inside system panels
			if (el.closest('.side-bar') ||
				el.closest('.comfy-sidebar') ||
				el.closest('.bottom-panel')) return false;

			// Clean name: remove icons (stars, dots) and close button characters
			const text = normalizeWorkflowTabName(el.textContent || "");

			// 2. Name-based exclusion: Ignore system-reserved names
			return Boolean(text && !systemTabNames.includes(text.toUpperCase()));
		});
		if (candidates.length === 0) return new Array(openWorkflows.length).fill(null);

		const surfaceGroups = new Map();
		for (const element of candidates) {
			const surface = getTabSurface(element);
			const group = surfaceGroups.get(surface) || [];
			group.push(element);
			surfaceGroups.set(surface, group);
		}

		let canonicalElements = null;
		let canonicalScore = Number.NEGATIVE_INFINITY;
		const candidateGroups = [...surfaceGroups.values(), candidates];
		for (const elements of candidateGroups) {
			const score = scoreTabSurface(elements, openWorkflows, isVisibleElement);
			if (score > canonicalScore) {
				canonicalScore = score;
				canonicalElements = elements;
			}
		}

		return canonicalElements
			? mapSurfaceToWorkflows(canonicalElements, openWorkflows)
			: new Array(openWorkflows.length).fill(null);
	};

	const isVisibleElement = (el) => {
		if (!(el instanceof HTMLElement)) return false;
		const style = window.getComputedStyle(el);
		if (style.display === "none" || style.visibility === "hidden" || Number(style.opacity || "1") === 0) {
			return false;
		}

		const rect = el.getBoundingClientRect();
		return rect.width > 0 && rect.height > 0;
	};

	const isSurfaceOpen = (name) => {
		const labelledBy = surfaceDefinitions[name];
		if (!labelledBy) return false;

		const marked = document.querySelector(`[data-nexus-surface="${name}"]`);
		if (isVisibleElement(marked)) return true;

		const surface = document.querySelector([
			`.p-dialog-mask .p-dialog.global-dialog[aria-labelledby="${labelledBy}"]`,
			`.p-dialog.global-dialog[aria-labelledby="${labelledBy}"]`,
			`[role="dialog"][aria-labelledby="${labelledBy}"]`,
		].join(", "));

		if (isVisibleElement(surface)) {
			surface.dataset.nexusSurface = name;
			return true;
		}

		return false;
	};

	const performSync = () => {
		try {
			const workflowStore = getWorkflowStore();
			if (workflowStore) {
				const workflowList = createWorkflowSyncPayload(workflowStore);
				const currentPayloadStr = JSON.stringify(workflowList);
				if (currentPayloadStr !== bridge.lastPayloadStr) {
					bridge.lastPayloadStr = currentPayloadStr;
					bridge.send("WORKFLOW_SYNC", workflowList);
				}
			}

			// Sync UI states (Terminal)
			const terminalBtn = document.querySelector('button[aria-label="Toggle Bottom Panel"]');
			const isTerminalOpen = !!terminalBtn?.classList.contains('side-bar-button-selected');

			if (bridge.lastTerminalState !== isTerminalOpen) {
				bridge.lastTerminalState = isTerminalOpen;
				bridge.send("UI_STATE_UPDATE", { terminalOpen: isTerminalOpen });
			}

			// Sync UI states (Shortcuts)
			const shortcutBtn = document.querySelector('button[aria-label^="Keyboard Shortcuts"]');
			const isShortcutsOpen = !!shortcutBtn?.classList.contains('side-bar-button-selected');

			if (bridge.lastShortcutsState !== isShortcutsOpen) {
				bridge.lastShortcutsState = isShortcutsOpen;
				bridge.send("UI_STATE_UPDATE", { shortcutsOpen: isShortcutsOpen });
			}

			// Sync UI states (Minimap)
			const minimapBtn = document.querySelector('button[aria-label*="Minimap"]');
			const isMinimapOpen = !!minimapBtn?.getAttribute('aria-label')?.includes('Hide');

			if (bridge.lastMinimapState !== isMinimapOpen) {
				bridge.lastMinimapState = isMinimapOpen;
				bridge.send("UI_STATE_UPDATE", { minimapOpen: isMinimapOpen });
			}

			// Sync UI states (Properties)
			const propertiesBtn = document.querySelector('button[aria-label="Properties"]') ||
								 document.querySelector('button[title="Properties"]');
			let isPropertiesOpen = !!propertiesBtn?.classList.contains('side-bar-button-selected');

			// Fallback: Check for visible splitter panels that likely contain properties
			if (!isPropertiesOpen) {
				const panel = document.querySelector('[data-testid="properties-panel"]');
				if (panel) {
					const container = panel.closest('.p-splitterpanel');
					isPropertiesOpen = !!(container && container.offsetWidth > 0);
				}
			}

			// Extra fallback is intentionally rate-limited because innerText over splitter panels is costly.
			if (!isPropertiesOpen) {
				const now = Date.now();
				if (now - lastExpensivePropertiesCheckAt >= expensivePropertiesCheckIntervalMs) {
					lastExpensivePropertiesCheckAt = now;
					const panels = Array.from(document.querySelectorAll('.p-splitterpanel.pointer-events-auto.bg-comfy-menu-bg'));
					lastExpensivePropertiesState = panels.some(p => {
						return p.offsetWidth > 0 && (
							p.innerText.includes("Properties") ||
							p.innerText.includes("Workflow Overview") ||
							p.querySelector('[aria-label="Toggle properties panel"]')
						);
					});
				}

				isPropertiesOpen = lastExpensivePropertiesState;
			}

			if (bridge.lastPropertiesState !== isPropertiesOpen) {
				bridge.lastPropertiesState = isPropertiesOpen;
				bridge.send("UI_STATE_UPDATE", { propertiesOpen: isPropertiesOpen });
			}

			// Sync UI states (Apps)
			const appsBtn = document.querySelector('button[aria-label="Apps"].apps-tab-button');
			const isAppsOpen = !!appsBtn?.classList.contains('side-bar-button-selected');

			if (bridge.lastAppsState !== isAppsOpen) {
				bridge.lastAppsState = isAppsOpen;
				bridge.send("UI_STATE_UPDATE", { appsOpen: isAppsOpen });
			}

			if (bridge.lastAssetsState !== false) {
				bridge.lastAssetsState = false;
				bridge.send("UI_STATE_UPDATE", { assetsOpen: false });
			}

			const isSettingsOpen = isSurfaceOpen("settings");
			if (bridge.lastSettingsState !== isSettingsOpen) {
				bridge.lastSettingsState = isSettingsOpen;
				bridge.send("UI_STATE_UPDATE", { settingsOpen: isSettingsOpen });
			}

			const isTemplatesOpen = isSurfaceOpen("templates");
			if (bridge.lastTemplatesState !== isTemplatesOpen) {
				bridge.lastTemplatesState = isTemplatesOpen;
				bridge.send("UI_STATE_UPDATE", { templatesOpen: isTemplatesOpen });
			}
		} catch (e) { bridge.log?.(`Nexus Sync Error: ${e?.message || e}`); }
	};
	bridge.performSync = performSync;
	bridge.getWorkflowStore = getWorkflowStore;
	bridge.getOpenWorkflowByIndex = getOpenWorkflowByIndex;
	bridge.getActiveWorkflowIndex = getActiveWorkflowIndex;

	if (bridge.syncObserver) {
		bridge.syncObserver.disconnect();
	}

	bridge.syncObserver = new MutationObserver(() => bridge.scheduleSync());
	bridge.syncObserver.observe(document.body || document.documentElement, {
		childList: true,
		subtree: true,
		attributes: true,
		attributeFilter: ['class', 'aria-label', 'aria-selected', 'data-state']
	});

	const onVisibilityChange = () => bridge.scheduleSync(50);
	const onInteraction = () => bridge.scheduleSync(50);
	document.addEventListener('visibilitychange', onVisibilityChange);
	document.addEventListener('click', onInteraction, true);
	document.addEventListener('keyup', onInteraction, true);

	if (bridge.syncFallbackTimer) {
		clearInterval(bridge.syncFallbackTimer);
	}
	bridge.syncFallbackTimer = setInterval(() => bridge.scheduleSync(0), 3500);

	if (bridge.heartbeatTimer) {
		clearInterval(bridge.heartbeatTimer);
	}
	bridge.heartbeatTimer = setInterval(() => bridge.send("HEARTBEAT", "alive"), 5000);

	let workflowStoreUnsubscribe = null;
	const workflowStore = getWorkflowStore();
	if (typeof workflowStore?.$subscribe === "function") {
		try {
			workflowStoreUnsubscribe = workflowStore.$subscribe(
				() => bridge.scheduleSync(0),
				{ detached: true });
		} catch (error) {
			bridge.log?.(`Workflow store subscription failed: ${error?.message || error}`);
		}
	}

	performSync();
	bridge.getFilteredTabs = getFilteredTabs; // Expose for actions

	return () => {
		if (bridge.syncTimer !== null) {
			window.clearTimeout(bridge.syncTimer);
			bridge.syncTimer = null;
		}

		document.removeEventListener('visibilitychange', onVisibilityChange);
		document.removeEventListener('click', onInteraction, true);
		document.removeEventListener('keyup', onInteraction, true);
		workflowStoreUnsubscribe?.();
		workflowStoreUnsubscribe = null;

		if (bridge.syncObserver) {
			bridge.syncObserver.disconnect();
			bridge.syncObserver = null;
		}

		if (bridge.syncFallbackTimer) {
			window.clearInterval(bridge.syncFallbackTimer);
			bridge.syncFallbackTimer = null;
		}

		if (bridge.heartbeatTimer) {
			window.clearInterval(bridge.heartbeatTimer);
			bridge.heartbeatTimer = null;
		}

		if (bridge.performSync === performSync) {
			bridge.performSync = null;
		}
		if (bridge.getFilteredTabs === getFilteredTabs) {
			bridge.getFilteredTabs = null;
		}
		if (bridge.getWorkflowStore === getWorkflowStore) {
			bridge.getWorkflowStore = null;
		}
		if (bridge.getOpenWorkflowByIndex === getOpenWorkflowByIndex) {
			bridge.getOpenWorkflowByIndex = null;
		}
		if (bridge.getActiveWorkflowIndex === getActiveWorkflowIndex) {
			bridge.getActiveWorkflowIndex = null;
		}
	};
}
