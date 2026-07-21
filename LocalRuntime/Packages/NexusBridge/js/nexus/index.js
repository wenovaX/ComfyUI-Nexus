import { app } from "/scripts/app.js";
import { api } from "/scripts/api.js";
import { setupHudCompatibility, setupUiExile } from "./ui.js";
import {
	setupGpuRelay as setupGpuTelemetry,
	setupModeTracker as setupModeTelemetry,
	setupQueueTracker as setupQueueTelemetry,
} from "./telemetry.js";
import { setupCursorSync as setupCursorSynchronization } from "./cursor_sync.js";
import { setupFocusTracking } from "./tracking.js";
import { setupWorkflowSync as setupWorkflowSynchronization } from "./workflow_sync.js";
import { setupActions as setupBridgeActions } from "./actions.js";
import { sendToNative } from "./native_bridge.js";
import * as assetBridge from "./assets.js";
import * as nativeUi from "./native_ui.js";
import * as comfyInternals from "./comfy_internals.js";
import { logComfyInternalHealth } from "./comfy_internal/registry.js";
import { setupAppModeTrace } from "./app_mode.js";

const QUEUE_COUNT_INPUT_SELECTORS = [
	'.batch-count input',
	'input.comfy-queue-count',
	'input[data-testid="queue-count-input"]',
];

const NODES_20_SETTING_ID = "Comfy.VueNodes.Enabled";

/**
	Nexus Bridge v4 - Encapsulated & Conditional
*/
class NexusBridge {
	constructor() {
		this.lastPayloadStr = "";
		this.isInitialized = false;
		this.syncTimer = null;
		this.syncObserver = null;
		this.syncFallbackTimer = null;
		this.heartbeatTimer = null;
		this.lastFocusState = null;
		this.performSync = null;
		this.getFilteredTabs = null;
		this.getWorkflowStore = null;
		this.getOpenWorkflowByIndex = null;
		this.getOpenWorkflowByTarget = null;
		this.getActiveWorkflowIndex = null;
		this.lastGpuPayload = "";
		this.queueTelemetryCleanup = null;
		this.modeTelemetryCleanup = null;
		this.gpuTelemetryCleanup = null;
		this.workflowSyncCleanup = null;
		this.queueButtonStateSyncCleanup = null;
		this.trackingCleanup = null;
		this.cursorSyncCleanup = null;
		this.appModeTraceCleanup = null;
		this.uiExileCleanup = null;
		this.hudCompatibilityCleanup = null;
		this.actionsCleanup = null;
		this.autoRecoveryTimer = null;
		this.actionsSetupGeneration = 0;
		this.lastAssetIntent = null;
		this.modelDebugEnabled = false;
		this.processedAssetDragIds = new Set();
		this.discoveredPinia = null;
		this.featureConfig = {
			topbarHud: false,
		};
	}

	/**
	 * Start the Nexus services only when confirmed to be in Nexus Shell
	 */
	init() {
		if (this.isInitialized) return;
		this.isInitialized = true;

		this.ensureNodes20Enabled();
		this.setupUI();
		this.setupHudCompatibility();
		this.setupSync();
		this.setupGpuRelay();
		this.setupActions();
		this.setupTracking();
		this.setupAppModeTrace();
		this.setupModeTracker();
		this.setupQueueTracker();
		this.setupQueueButtonStateSync();
		this.setupCursorSync();
	}

	dispose() {
		const ownsGlobalBridge = window.__nexusBridge === this;
		this.isInitialized = false;
		this.actionsSetupGeneration++;

		const cleanupFields = [
			"cursorSyncCleanup",
			"queueButtonStateSyncCleanup",
			"queueTelemetryCleanup",
			"modeTelemetryCleanup",
			"trackingCleanup",
			"appModeTraceCleanup",
			"actionsCleanup",
			"gpuTelemetryCleanup",
			"workflowSyncCleanup",
			"hudCompatibilityCleanup",
			"uiExileCleanup",
		];

		for (const field of cleanupFields) {
			const cleanup = this[field];
			this[field] = null;
			try {
				cleanup?.();
			} catch (error) {
				console.warn(`[NexusBridge] Cleanup failed for ${field}:`, error);
			}
		}

		if (this.syncTimer !== null) {
			window.clearTimeout(this.syncTimer);
			this.syncTimer = null;
		}
		if (this.autoRecoveryTimer !== null) {
			window.clearTimeout(this.autoRecoveryTimer);
			this.autoRecoveryTimer = null;
		}

		this.performSync = null;
		this.getFilteredTabs = null;
		this.getWorkflowStore = null;
		this.getOpenWorkflowByIndex = null;
		this.getOpenWorkflowByTarget = null;
		this.getActiveWorkflowIndex = null;
		this.processedAssetDragIds.clear();
		this.lastAssetIntent = null;
		this.discoveredPinia = null;
		this.workflowStore = null;
		this.workspaceStore = null;
		this.modelToNodeStore = null;
		this.modelsStore = null;
		this.workflowService = null;
		this.managerAdapter = null;
		this.managerAdapterReady = null;

		if (ownsGlobalBridge) {
			[
				"NexusAction",
				"NexusBoot",
				"NexusDiscoverInternals",
				"NexusHandleAssetDrop",
				"NexusExileScan",
				"NexusExileStop",
				"NexusExileIsolation",
				"NexusExileStatus",
				"NexusExileDebug",
				"NexusExileRules",
				"NexusSources",
			].forEach((name) => delete window[name]);
			delete window.__nexusBridge;
		}
	}

	async ensureNodes20Enabled() {
		try {
			const settings = app?.ui?.settings;
			if (settings?.settingsValues && settings.settingsValues[NODES_20_SETTING_ID] !== true) {
				settings.settingsValues[NODES_20_SETTING_ID] = true;
			}

			if (typeof settings?.setSettingValue === "function") {
				await settings.setSettingValue(NODES_20_SETTING_ID, true);
			} else if (typeof settings?.set === "function") {
				await settings.set(NODES_20_SETTING_ID, true);
			}

			await api.fetchApi(`/settings/${encodeURIComponent(NODES_20_SETTING_ID)}`, {
				method: "POST",
				body: JSON.stringify(true),
				headers: { "Content-Type": "application/json" },
			});

		} catch (e) {
			this.log(`Nexus: Failed to enforce Nodes 2.0 setting: ${e.message}`);
		}
	}

	setupQueueTracker() {
		this.queueTelemetryCleanup?.();
		this.queueTelemetryCleanup = setupQueueTelemetry(this);
	}

	setupQueueButtonStateSync() {
		this.queueButtonStateSyncCleanup?.();
		this.queueButtonStateSyncCleanup = nativeUi.setupQueueButtonStateSync(this);
	}

	setupHudCompatibility() {
		this.hudCompatibilityCleanup?.();
		this.hudCompatibilityCleanup = setupHudCompatibility(this);
	}

	setupModeTracker() {
		this.modeTelemetryCleanup?.();
		this.modeTelemetryCleanup = setupModeTelemetry(this);
	}

	setupGpuRelay() {
		this.gpuTelemetryCleanup?.();
		this.gpuTelemetryCleanup = setupGpuTelemetry(this);
	}

	log(msg) {
		this.send("JS_LOG", msg);
	}

	debug(msg) {
		const enabled = window.NexusBridgeDebug === true ||
			window.localStorage?.getItem?.("nexus.bridge.debug") === "1";
		if (enabled) {
			this.log(msg);
		}
	}

	send(type, data) {
		sendToNative(type, data);
	}

	scheduleSync(delay = 120) {
		if (this.syncTimer !== null) return;

		this.syncTimer = window.setTimeout(() => {
			this.syncTimer = null;
			this.performSync?.();
		}, delay);
	}


	/**
	 * Module: UI & Exile Strategy
	 */
	setupUI() {
		this.uiExileCleanup?.();
		this.uiExileCleanup = setupUiExile(this);
	}

	/**
	 * Module: Data Synchronization (Workflow List + Paths)
	 */
	setupSync() {
		this.workflowSyncCleanup?.();
		this.workflowSyncCleanup = setupWorkflowSynchronization(this);
	}

	/**
	 * Module: Cursor Synchronization
	 * Bridging CSS cursor states to Native Win32 cursors to fix WebView2 cursor stuck issues.
	 */
	setupCursorSync() {
		this.cursorSyncCleanup?.();
		this.cursorSyncCleanup = setupCursorSynchronization(this);
	}

	/**
	 * Module: Interaction Controllers (C# -> JS Commands)
	 */
	async setupActions() {
		const generation = ++this.actionsSetupGeneration;
		this.actionsCleanup?.();
		this.actionsCleanup = null;
		const cleanup = await setupBridgeActions(this, app);
		if (!this.isInitialized || generation !== this.actionsSetupGeneration) {
			cleanup?.();
			return;
		}
		this.actionsCleanup = cleanup;
	}

	normalizeAssetPayload(payload) {
		return assetBridge.normalizeAssetPayload(this, payload);
	}

	markAssetDragProcessed(payload) {
		return assetBridge.markAssetDragProcessed(this, payload);
	}

	handleAssetBrowserDragStart(payload) {
		return assetBridge.handleAssetBrowserDragStart(this, payload);
	}

	handleAssetDropFeedback(payload) {
		return assetBridge.handleAssetDropFeedback(this, payload);
	}

	handleAssetDropFeedbackSource(payload) {
		return assetBridge.handleAssetDropFeedbackSource(this, payload);
	}

	handleAssetBrowserOpen(payload) {
		return assetBridge.handleAssetBrowserOpen(this, payload);
	}

	getCanvasCenterCanvasPosition() {
		return assetBridge.getCanvasCenterCanvasPosition(this);
	}

	getCanvasCenterClientPosition() {
		return assetBridge.getCanvasCenterClientPosition(this);
	}

	getRailSpawnOriginClientPosition(railWidthPx = 0) {
		return assetBridge.getRailSpawnOriginClientPosition(this, railWidthPx);
	}

	resolveAssetDropCanvasPosition(payload) {
		return assetBridge.resolveAssetDropCanvasPosition(this, payload);
	}

	resolveNodeSpawnCanvasPosition(payload) {
		return assetBridge.resolveNodeSpawnCanvasPosition(this, payload);
	}

	isClientPointInsideCanvas(clientX, clientY) {
		return assetBridge.isClientPointInsideCanvas(this, clientX, clientY);
	}

	getRandomCentralCanvasPosition(spreadRatio = 0.3) {
		return assetBridge.getRandomCentralCanvasPosition(this, spreadRatio);
	}

	jitterNodePosition(pos) {
		return assetBridge.jitterNodePosition(this, pos);
	}

	resolveModelCategory(payload) {
		return assetBridge.resolveModelCategory(this, payload);
	}

	applyModelSelection(payload) {
		return assetBridge.applyModelSelection(this, payload);
	}

	applyNodeSelection(payload) {
		return assetBridge.applyNodeSelection(this, payload);
	}

	/**
	 * Get the center of the WebView canvas area.
	 * Used for double-click node placement (center spawn with jitter).
	 * @param {number} railWidthPx - Legacy hint; placement intentionally ignores native rail overlay width.
	 * @returns {number[]} [x, y] in canvas coordinates
	 */
	getVisibleCenterCanvasPosition(railWidthPx = 0) {
		return assetBridge.getVisibleCenterCanvasPosition(this, railWidthPx);
	}

	/**
	 * Move a node to the top of the render/interaction stack.
	 */
	bringNodeToFront(node) {
		return assetBridge.bringNodeToFront(this, node);
	}

	applyModelToTarget(payload, modelDirectory, targetInfo) {
		return assetBridge.applyModelToTarget(this, payload, modelDirectory, targetInfo);
	}

	handleAssetIntentDrop(intent, dropInfo = {}) {
		return assetBridge.handleAssetIntentDrop(this, intent, dropInfo);
	}

	tryCreateModelProviderNode(modelDirectory, preferredPosition = null) {
		return assetBridge.tryCreateModelProviderNode(this, modelDirectory, preferredPosition);
	}

	bringGraphNodeToFront(graph, node) {
		return assetBridge.bringGraphNodeToFront(this, graph, node);
	}

	getModelNodeProvider(modelDirectory) {
		return assetBridge.getModelNodeProvider(this, modelDirectory);
	}

	resolveModelNodeSpec(modelDirectory, provider) {
		return assetBridge.resolveModelNodeSpec(this, modelDirectory, provider);
	}

	createGraphNodeFromSpec(nodeSpec) {
		return assetBridge.createGraphNodeFromSpec(this, nodeSpec);
	}

	getNextModelNodePosition() {
		return assetBridge.getNextModelNodePosition(this);
	}

	clampModelNodePositionToSafeArea(pos, safeAreaRatio = 0.5) {
		return assetBridge.clampModelNodePositionToSafeArea(this, pos, safeAreaRatio);
	}

	jitterModelNodePosition(pos, safeAreaRatio = null) {
		return assetBridge.jitterModelNodePosition(this, pos, safeAreaRatio);
	}

	describeProvider(provider) {
		return assetBridge.describeProvider(this, provider);
	}

	probeModelProvider(directory = "checkpoints") {
		return assetBridge.probeModelProvider(this, directory);
	}

	describeGraphNode(node) {
		return assetBridge.describeGraphNode(this, node);
	}

	getModelWidgetPredicates(modelDirectory) {
		return assetBridge.getModelWidgetPredicates(this, modelDirectory);
	}

	findMatchingWidget(node, predicates) {
		return assetBridge.findMatchingWidget(this, node, predicates);
	}

	debugModel(stage, payload) {
		return assetBridge.debugModel(this, stage, payload);
	}

	discoverComfyInternals(reason = "manual") {
		return comfyInternals.discoverComfyInternals(this, reason);
	}

	reportComfyInternalHealth(reason = "manual") {
		return logComfyInternalHealth(this, reason);
	}

	getObjectPreviewKeys(value) {
		return comfyInternals.getObjectPreviewKeys(this, value);
	}

	getFunctionKeys(value) {
		return comfyInternals.getFunctionKeys(this, value);
	}

	getCollectionHints(value) {
		return comfyInternals.getCollectionHints(this, value);
	}

	describeValue(value) {
		return comfyInternals.describeValue(this, value);
	}

	getVueProvidesSummary() {
		return comfyInternals.getVueProvidesSummary(this);
	}

	adjustQueueCount(type) {
		let input = null;
		for (const selector of QUEUE_COUNT_INPUT_SELECTORS) {
			input = document.querySelector(selector);
			if (input) break;
		}
		if (!input) return;

		let val = parseInt(input.value) || 1;
		if (type === 'increment') val += 1;
		else if (type === 'decrement') val = Math.max(1, val - 1);
		else if (type === 'mult') val *= 2;
		else if (type === 'div') val = Math.max(1, Math.floor(val / 2));
		this.setQueueCount(val);
	}

	setQueueCount(val) {
		let input = null;
		for (const selector of QUEUE_COUNT_INPUT_SELECTORS) {
			input = document.querySelector(selector);
			if (input) {
				break;
			}
		}
		if (!input) {
			this.log("ERROR: Batch count input NOT found!");
			return;
		}

		// 1. Direct app state sync if available (Legacy/Compatibility)
		if (window.app?.ui) {
			window.app.ui.batchCount = val;
		}

		// 2. Vue 3 Deep Sync (Internal state access)
		const vueInst = input.__vue_ || input.__vue_app__ || input._vnode?.component;
		if (vueInst) {
			const state = vueInst.setupState || vueInst.proxy;
			if (state) {
				if (state.batchCount !== undefined) {
					if (typeof state.batchCount === 'object' && 'value' in state.batchCount) {
						state.batchCount.value = val;
					} else {
						state.batchCount = val;
					}
				}
				if (state.batchCountInput !== undefined) {
					if (typeof state.batchCountInput === 'object' && 'value' in state.batchCountInput) {
						state.batchCountInput.value = String(val);
					} else {
						state.batchCountInput = String(val);
					}
				}
			}
		}

		// 3. User-simulated Input fallback (Ensures UI reflects change)
		input.focus();
		input.select();
		try {
			document.execCommand('insertText', false, val.toString());
		} catch (e) {}

		// 4. Finalize via events & blur
		input.dispatchEvent(new Event('input', { bubbles: true }));
		input.dispatchEvent(new Event('change', { bubbles: true }));
		input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

		setTimeout(() => {
			if (input) {
				input.blur();
			}
		}, 50);
	}

	setRunMode(mode) {
		return nativeUi.setRunMode(this, mode);
	}

	setCanvasMode(mode) {
		return nativeUi.setCanvasMode(this, mode);
	}

	ensureAppModeVisible() {
		return nativeUi.ensureAppModeVisible(this);
	}

	setUiIsolation(enabled) {
		if (typeof window.NexusExileIsolation === "function") {
			return window.NexusExileIsolation(enabled);
		}

		this.log("Nexus Exile: isolation API is not available.");
		return false;
	}

	executeWorkflowMenuAction(actionType) {
		return nativeUi.executeWorkflowMenuAction(this, actionType);
	}

	executeWorkflowCommandAction(actionType, metadata) {
		return nativeUi.executeWorkflowCommandAction(this, actionType, metadata);
	}

	async simulateDrop(payload) {
		return await nativeUi.simulateDrop(this, payload);
	}

	/**
	 * Visual Feedback
	 * Small local helpers that affect perceived responsiveness but not workflow state.
	 */
	animateCanvasPan(deltaX, durationMs) {
		return nativeUi.animateCanvasPan(this, deltaX, durationMs);
	}

	showToast(message, type = 'info') {
		return nativeUi.showToast(this, message, type);
	}

	/**
	 * Bookmark Sync
	 * Refresh bookmark state from ComfyUI stores without owning bookmark persistence here.
	 */
	async refreshBookmarks() {
		try {
			const pinia = window.app?.pinia || window.__pinia;
			if (!pinia) {
				this.log("Bookmark Refresh Error: Pinia instance not found.");
				return;
			}

			const store = pinia._s.get('workflowBookmark');
			const workflowStore = pinia._s.get('workflow');

			if (store) {
				// 1. Load data from file (server sync)
				await store.loadBookmarks();

				// 2. Also trigger workflow list sync (used by bookmark tab)
				if (workflowStore && typeof workflowStore.syncWorkflows === 'function') {
					await workflowStore.syncWorkflows();
				}

				this.log("Nexus: Bookmarks & Workflows sync triggered.");
			} else {
				const available = Array.from(pinia._s.keys()).join(", ");
				this.log(`Bookmark Store not found. Available: ${available}`);
			}
		} catch (e) {
			this.log(`Bookmark Refresh Exception: ${e.message}`);
		}
	}

	/**
	 * Native Input Reuse
	 * Reuse existing ComfyUI menus and shortcuts instead of duplicating their behavior.
	 */
	executeTabContextAction(targetTab, actionType) {
		return nativeUi.executeTabContextAction(this, targetTab, actionType);
	}

	executeTabCommandAction(actionType, metadata) {
		return nativeUi.executeTabCommandAction(this, actionType, metadata);
	}

	relayShortcut(key, ctrl, shift, alt) {
		return nativeUi.relayShortcut(this, key, ctrl, shift, alt);
	}

	createNewWorkflow() {
		return nativeUi.createNewWorkflow(this);
	}

	/**
	 * Focus + Shortcut Tracking
	 * Lightweight bridge events that keep the shell in sync with embedded app focus.
	 */
	setupTracking() {
		this.trackingCleanup?.();
		this.trackingCleanup = setupFocusTracking(this);
	}

	setupAppModeTrace() {
		this.appModeTraceCleanup?.();
		this.appModeTraceCleanup = setupAppModeTrace(this);
	}

	clickToolbarButton(selectors) {
		return nativeUi.clickToolbarButton(this, selectors);
	}
}

// Instantiate and Boot
window.__nexusBridge?.dispose?.();
const nexus = new NexusBridge();
window.__nexusBridge = nexus;
window.NexusDiscoverInternals = () => nexus.discoverComfyInternals("global");
window.NexusHandleAssetDrop = (intent, dropInfo) => nexus.handleAssetIntentDrop(intent, dropInfo);

app.registerExtension({
	name: "ComfyUI_Nexus.Bridge",
	async setup() {
		// Wait for the shell to explicitly request bridge boot before initializing.
		window.NexusBoot = (token) => {
			nexus.init();

			// Acknowledge readiness back to the shell.
			nexus.send("BOOT_READY", {
				agentId: "NEXUS",
				status: "SYNC_COMPLETE",
				timestamp: Date.now()
			});

			return true;
		};

		// Preserve late-load recovery when the shell identity already exists.
		if (window.isNexusShell && typeof window.NexusBoot === 'function') {
			// Give the global scope a moment to settle before auto-recovery.
			nexus.autoRecoveryTimer = window.setTimeout(() => {
				nexus.autoRecoveryTimer = null;
				window.NexusBoot?.("AUTO_RECOVERY");
			}, 100);
		}
	}
});
