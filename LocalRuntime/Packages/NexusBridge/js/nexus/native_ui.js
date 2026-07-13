import { app } from "/scripts/app.js";
import { CANVAS_MODES, setCanvasMode as setCanvasModeByCommand } from "./canvas_mode.js";
import { getAutoQueueState, setRunMode as setAutoQueueRunMode } from "./auto_queue_mode.js";
import {
	clickWorkflowContextMenuAction,
	executeWorkflowContextCommand,
	getWorkflowContextMenuSnapshot,
	getWorkflowContextMenuState,
	mapNexusWorkflowAction,
	runWorkflowCommandAction,
} from "./workflow_context_menu.js";

export function setupQueueButtonStateSync(bridge) {
	if (bridge.queueButtonStateSyncStarted) {
		return;
	}

	bridge.queueButtonStateSyncStarted = true;

	let lastSignature = "";
	let scheduled = false;
	let animationFrameId = null;
	let disposed = false;
	let observedButton = null;
	let buttonObserver = null;
	let rootObserver = null;

	const readState = async () => {
		const button = document.querySelector('[data-testid="queue-button"]');
		const queueState = await getAutoQueueState();
		const variant = button?.dataset?.variant || button?.getAttribute("data-variant") || "";
		const mode = String(queueState?.mode || "");
		const isStop = mode === "instant-running";

		return {
			button,
			variant,
			mode,
			batchCount: queueState?.batchCount ?? null,
			isStop,
		};
	};

	const stopObservingButton = () => {
		buttonObserver?.disconnect();
		buttonObserver = null;
		observedButton = null;
	};

	const sendStateIfChanged = (state) => {
		const signature = state ? `${state.mode}|${state.batchCount}|${state.variant}|${state.isStop}` : "missing";
		if (signature === lastSignature) {
			return;
		}

		lastSignature = signature;
		bridge.send("QUEUE_BUTTON_STATE_SYNC", state
			? { variant: state.variant, mode: state.mode, batchCount: state.batchCount, isStop: state.isStop }
			: { text: "", variant: "", isStop: false, missing: true });
	};

	const updateButtonObserver = (state) => {
		const button = state?.button ?? null;

		if (!button || button === observedButton) {
			return;
		}

		stopObservingButton();
		observedButton = button;
		buttonObserver = new MutationObserver(scheduleSync);
		buttonObserver.observe(button, {
			childList: true,
			subtree: true,
			characterData: true,
			attributes: true,
			attributeFilter: ["data-variant", "disabled", "class"],
		});
	};

	function scheduleSync() {
		if (disposed || scheduled) {
			return;
		}

		scheduled = true;
		animationFrameId = requestAnimationFrame(async () => {
			animationFrameId = null;
			scheduled = false;
			if (disposed) {
				return;
			}

			try {
				const state = await readState();
				if (disposed) {
					return;
				}

				updateButtonObserver(state);
				sendStateIfChanged(state);
			} catch (error) {
				bridge.log(`QUEUE_BUTTON_STATE_SYNC failed: ${error?.message || error}`);
			}
		});
	}

	const root = document.body || document.documentElement;
	if (root) {
		rootObserver = new MutationObserver((mutations) => {
			const hasQueueButtonChange = mutations.some((mutation) => {
				return Array.from(mutation.addedNodes || []).some((node) => {
					if (!(node instanceof Element)) return false;
					return node.matches?.('[data-testid="queue-button"]') ||
						node.querySelector?.('[data-testid="queue-button"]');
				});
			});

			if (hasQueueButtonChange || !observedButton?.isConnected) {
				scheduleSync();
			}
		});
		rootObserver.observe(root, { childList: true, subtree: true });
		bridge.queueButtonStateRootObserver = rootObserver;
	}

	const fallbackTimer = window.setInterval(scheduleSync, 1500);
	scheduleSync();

	return () => {
		if (disposed) {
			return;
		}

		disposed = true;
		window.clearInterval(fallbackTimer);
		if (animationFrameId !== null) {
			cancelAnimationFrame(animationFrameId);
			animationFrameId = null;
		}
		scheduled = false;
		stopObservingButton();
		const ownedRootObserver = rootObserver;
		ownedRootObserver?.disconnect();
		rootObserver = null;
		if (bridge.queueButtonStateRootObserver === ownedRootObserver) {
			bridge.queueButtonStateRootObserver = null;
		}
		bridge.queueButtonStateSyncStarted = false;
	};
}

export async function setRunMode(bridge, mode) {
	const targetMode = String(mode || "").trim();
	const validModes = new Set(["Run", "Run (On Change)", "Run (Instant)"]);
	if (!validModes.has(targetMode)) {
		bridge.log(`SET_RUN_MODE ignored unknown mode: ${targetMode}`);
		return false;
	}

	try {
		await setAutoQueueRunMode(targetMode);
		return true;
	} catch (error) {
		bridge.log(`SET_RUN_MODE failed: ${error?.message || error}`);
		return false;
	}
}

export async function setCanvasMode(bridge, mode) {
	const targetMode = String(mode || "").trim();
	const validModes = new Set([CANVAS_MODES.select, CANVAS_MODES.hand]);
	if (!validModes.has(targetMode)) {
		bridge.log(`SET_CANVAS_MODE ignored unknown mode: ${targetMode}`);
		return false;
	}

	try {
		const appliedMode = await setCanvasModeByCommand(targetMode);
		const mode = appliedMode === CANVAS_MODES.hand ? CANVAS_MODES.handUpper : CANVAS_MODES.selectUpper;
		if (bridge.lastMode !== mode) {
			bridge.lastMode = mode;
			bridge.send("MODE_UPDATE", { mode });
		}
		return true;
	} catch (error) {
		bridge.log(`SET_CANVAS_MODE failed: ${error?.message || error}`);
		return false;
	}
}

export function ensureAppModeVisible(bridge) {
	const welcome = document.querySelector('[data-testid="linear-welcome"]');
	const centerPanel = document.getElementById("linearCenterPanel");
	if (!welcome && !centerPanel) return;

	[centerPanel, welcome].filter(Boolean).forEach((el) => {
		el.style.removeProperty("position");
		el.style.removeProperty("top");
		el.style.removeProperty("left");
		el.style.removeProperty("width");
		el.style.removeProperty("height");
		el.style.removeProperty("overflow");
		el.style.removeProperty("z-index");
		el.style.removeProperty("opacity");
		el.style.removeProperty("pointer-events");
		el.style.removeProperty("visibility");
	});
}

export function executeWorkflowMenuAction(bridge, actionType) {
	if (!actionType) return;

	const contextAction = mapNexusWorkflowAction(actionType);
	if (!contextAction) {
		bridge.log?.(`Nexus Error: Unsupported workflow action [${actionType}]`);
		return;
	}

	const trigger = window.NexusSources?.get?.("workflowActions");
	if (!trigger) {
		bridge.log(`Nexus Error: Workflow actions trigger not found for [${actionType}]`);
		return;
	}

	trigger.click();
	return clickWorkflowContextActionWhenReady(bridge, contextAction, actionType);
}

export async function executeWorkflowCommandAction(bridge, actionType, metadata = undefined) {
	const action = String(actionType || "");
	if (!action) return false;

	try {
		const result = await runWorkflowCommandAction(action, metadata);
		if (!result?.ok) {
			bridge.log?.(`Nexus: Workflow command action [${action}] skipped: ${result?.reason || "unknown"}.`);
		}
		return Boolean(result?.ok);
	} catch (error) {
		bridge.log?.(`Nexus Error: Workflow command action [${action}] failed: ${error?.message || error}`);
		return false;
	}
}

export async function simulateDrop(bridge, payload) {
	try {
		const workflowRelativePath = String(payload.workflowRelativePath || "")
			.replace(/\\/g, "/")
			.replace(/^workflows\//i, "")
			.replace(/^\/+/, "");

		// 1. Base64 -> Blob -> File conversion
		const byteCharacters = atob(payload.data);
		const byteNumbers = new Array(byteCharacters.length);
		for (let i = 0; i < byteCharacters.length; i++) {
			byteNumbers[i] = byteCharacters.charCodeAt(i);
		}
		const byteArray = new Uint8Array(byteNumbers);
		const blob = new Blob([byteArray], { type: payload.type });
		const file = new File([blob], workflowRelativePath || payload.name, { type: payload.type });

		if (workflowRelativePath) {
			try {
				Object.defineProperty(file, "webkitRelativePath", {
					value: workflowRelativePath,
					configurable: true
				});
				Object.defineProperty(file, "nexusWorkflowRelativePath", {
					value: workflowRelativePath,
					configurable: true
				});
			} catch {
			}
		}

		// 2. Mock DataTransfer
		const dataTransfer = new DataTransfer();
		dataTransfer.items.add(file);
		if (workflowRelativePath) {
			try {
				dataTransfer.setData("application/x-nexus-workflow-path", workflowRelativePath);
			} catch {
			}
		}

		// 3. Dispatch Drop Event (Target canvas to avoid node interference)
		const dropEvent = new DragEvent("drop", {
			dataTransfer: dataTransfer,
			bubbles: true,
			cancelable: true,
			clientX: 0,
			clientY: 0
		});

		const target = document.querySelector('canvas') || window;
		target.dispatchEvent(dropEvent);
	} catch (e) {
		bridge.log(`Drop Simulation Error: ${e.message}`);
	}
}

export function animateCanvasPan(bridge, deltaX, durationMs) {
	if (!window.app?.canvas?.ds) return;
	const startOffset = window.app.canvas.ds.offset[0];
	const startTime = performance.now();
	const easeOutCubic = (t) => 1 - Math.pow(1 - t, 3);

	const animate = (time) => {
		let elapsed = time - startTime;
		if (elapsed > durationMs) elapsed = durationMs;
		window.app.canvas.ds.offset[0] = startOffset + (deltaX * easeOutCubic(elapsed / durationMs));
		window.app.canvas.setDirty(true, true);
		if (elapsed < durationMs) requestAnimationFrame(animate);
	};
	requestAnimationFrame(animate);
}

export function showToast(bridge, message, type = 'info') {
	// ComfyUI toast APIs are not stable enough for Nexus to depend on.
	return false;
}

export function executeTabContextAction(bridge, targetTab, actionType) {
	if (!targetTab) return;

	const contextAction = mapNexusWorkflowAction(actionType);
	if (!contextAction) {
		bridge.log?.(`Nexus Error: Unsupported tab action [${actionType}]`);
		return false;
	}

	// Open the context menu invisibly so native menu actions can still be reused.
	let styleEl = document.getElementById('nexus-silent-menu-style') || document.createElement('style');
	styleEl.id = 'nexus-silent-menu-style';
	document.head.appendChild(styleEl);
	styleEl.textContent = '[data-reka-popper-content-wrapper] { opacity: 0 !important; pointer-events: none !important; }';

	openContextMenuOnElement(targetTab);

	return clickWorkflowContextActionWhenReady(bridge, contextAction, actionType)
		.finally(() => {
			setTimeout(() => { styleEl.textContent = ''; }, 100);
		});
}

export async function executeTabCommandAction(bridge, actionType, metadata = undefined) {
	return executeWorkflowCommandAction(bridge, actionType, metadata);
}

async function clickWorkflowContextActionWhenReady(bridge, contextAction, sourceActionType) {
	for (let attempt = 0; attempt < 24; attempt++) {
		await new Promise((resolve) => requestAnimationFrame(resolve));

		const state = getWorkflowContextMenuState(contextAction);
		if (!state.found) {
			continue;
		}

		if (!state.iconMatched) {
			bridge.log?.(`Nexus Error: Workflow context action [${sourceActionType}] icon guard failed at index ${state.index}.`);
			return false;
		}

		if (state.disabled) {
			bridge.log?.(`Nexus: Workflow context action [${sourceActionType}] is disabled.`);
			return false;
		}

		if (!clickWorkflowContextMenuAction(contextAction)) {
			bridge.log?.(`Nexus Error: Workflow context action [${sourceActionType}] could not be clicked.`);
			return false;
		}

		return true;
	}

	bridge.log?.(`Nexus Error: Workflow context action [${sourceActionType}] menu item not found.`);
	const snapshot = getWorkflowContextMenuSnapshot();
	bridge.log?.(`Nexus: Workflow context menu state visible=${snapshot.visible}, itemCount=${snapshot.itemCount}.`);

	if (!snapshot.visible) {
		try {
			const executed = await executeWorkflowContextCommand(contextAction);
			if (executed) {
				bridge.log?.(`Nexus: Workflow context action [${sourceActionType}] executed via command fallback.`);
				return true;
			}
		} catch (error) {
			bridge.log?.(`Nexus Error: Workflow context command fallback failed [${sourceActionType}]: ${error?.message || error}`);
		}
	}

	return false;
}

function openContextMenuOnElement(element) {
	const rect = element.getBoundingClientRect?.();
	const clientX = rect ? rect.left + Math.min(Math.max(rect.width / 2, 8), Math.max(rect.width - 8, 8)) : 0;
	const clientY = rect ? rect.top + Math.min(Math.max(rect.height / 2, 8), Math.max(rect.height - 8, 8)) : 0;
	const common = {
		bubbles: true,
		cancelable: true,
		composed: true,
		view: window,
		button: 2,
		buttons: 2,
		clientX,
		clientY,
		screenX: window.screenX + clientX,
		screenY: window.screenY + clientY,
	};

	element.dispatchEvent(new PointerEvent("pointerover", { ...common, pointerType: "mouse", isPrimary: true }));
	element.dispatchEvent(new PointerEvent("pointerenter", { ...common, pointerType: "mouse", isPrimary: true, bubbles: false }));
	element.dispatchEvent(new MouseEvent("mouseover", common));
	element.dispatchEvent(new MouseEvent("mouseenter", { ...common, bubbles: false }));
	element.dispatchEvent(new PointerEvent("pointerdown", { ...common, pointerType: "mouse", isPrimary: true }));
	element.dispatchEvent(new MouseEvent("mousedown", common));
	element.dispatchEvent(new MouseEvent("contextmenu", common));
	element.dispatchEvent(new MouseEvent("mouseup", common));
	element.dispatchEvent(new PointerEvent("pointerup", { ...common, pointerType: "mouse", isPrimary: true, buttons: 0 }));
}

export function relayShortcut(bridge, key, ctrl, shift = false, alt = false) {
	const normalizedKey = String(key || "").toUpperCase();
	const keyMap = {
		ENTER: { key: "Enter", code: "Enter" },
		SPACE: { key: " ", code: "Space" },
		PERIOD: { key: ".", code: "Period" },
		ESCAPE: { key: "Escape", code: "Escape" },
		BACKSPACE: { key: "Backspace", code: "Backspace" },
		TAB: { key: "Tab", code: "Tab" },
		DELETE: { key: "Delete", code: "Delete" },
		LEFT: { key: "ArrowLeft", code: "ArrowLeft" },
		UP: { key: "ArrowUp", code: "ArrowUp" },
		DOWN: { key: "ArrowDown", code: "ArrowDown" },
		RIGHT: { key: "ArrowRight", code: "ArrowRight" },
	};
	const mapped = keyMap[normalizedKey] || {
		key: normalizedKey.length === 1 ? normalizedKey.toLowerCase() : normalizedKey,
		code: /^[0-9]$/.test(normalizedKey)
			? `Digit${normalizedKey}`
			: normalizedKey.length === 1
				? `Key${normalizedKey}`
				: normalizedKey,
	};
	const eventData = {
		key: mapped.key,
		code: mapped.code,
		ctrlKey: !!ctrl,
		metaKey: false,
		shiftKey: !!shift,
		altKey: !!alt,
		bubbles: true,
		cancelable: true,
		composed: true
	};
	const active = document.activeElement instanceof Node ? document.activeElement : null;
	const canvasTarget = document.getElementById("graph-canvas") || app?.canvas?.canvas || document.querySelector("canvas");
	const target = active && active !== document.body && active !== document.documentElement
		? active
		: canvasTarget || document.body;
	if (target instanceof HTMLElement && document.activeElement !== target) {
		if (!target.hasAttribute("tabindex")) {
			target.setAttribute("tabindex", "-1");
		}
		target.focus?.({ preventScroll: true });
	}
	const dispatch = (type) => {
		try {
			target?.dispatchEvent(new KeyboardEvent(type, eventData));
		} catch {
		}
	};
	dispatch("keydown");
	setTimeout(() => dispatch("keyup"), 20);
}

export function createNewWorkflow(bridge) {
	try {
		if (app.workflowManager?.newWorkflow) return app.workflowManager.newWorkflow();
		const plus = document.querySelector('.pi-plus, [class*="add"]');
		if (plus) (plus.closest('button') || plus).click();
    } catch (e) { bridge.log?.(`New Workflow Failed: ${e?.message || e}`); }
}

export function clickToolbarButton(bridge, selectors) {
	const selectorList = Array.isArray(selectors) ? selectors : [selectors];
	const btn = selectorList.map(selector => document.querySelector(selector)).find(Boolean);
	if (btn) {
		btn.click();
		return true;
	} else {
		bridge.log(`Nexus Error: Toolbar button not found`);
		return false;
	}
}
