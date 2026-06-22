import { app } from "/scripts/app.js";

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

	const readState = () => {
		const button = document.querySelector('[data-testid="queue-button"]');
		if (!button) {
			return null;
		}

		const text = String(button.innerText || button.textContent || "").trim();
		const variant = button.dataset?.variant || button.getAttribute("data-variant") || "";
		const isStop = text.startsWith("Stop");

		return {
			button,
			text,
			variant,
			isStop,
		};
	};

	const stopObservingButton = () => {
		buttonObserver?.disconnect();
		buttonObserver = null;
		observedButton = null;
	};

	const sendStateIfChanged = (state) => {
		const signature = state ? `${state.text}|${state.variant}|${state.isStop}` : "missing";
		if (signature === lastSignature) {
			return;
		}

		lastSignature = signature;
		bridge.send("QUEUE_BUTTON_STATE_SYNC", state
			? { text: state.text, variant: state.variant, isStop: state.isStop }
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
		animationFrameId = requestAnimationFrame(() => {
			animationFrameId = null;
			scheduled = false;
			if (disposed) {
				return;
			}

			const state = readState();
			updateButtonObserver(state);
			sendStateIfChanged(state);
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

	const trigger = window.NexusSources?.get?.("runModeTrigger") ||
		document.querySelector('[data-testid="queue-mode-menu-trigger"]');
	if (!trigger) {
		bridge.log("SET_RUN_MODE failed: queue mode trigger not found.");
		return false;
	}

	trigger.click();

	const item = await new Promise((resolve) => {
		const timer = window.setInterval(() => {
			const found = [...document.querySelectorAll('[role="menu"] button[role="menuitem"], [role="menu"] button[role="menuitemradio"]')]
				.find((el) => String(el.innerText || el.textContent || "").trim() === targetMode);

			if (found) {
				window.clearInterval(timer);
				resolve(found);
			}
		}, 16);

		window.setTimeout(() => {
			window.clearInterval(timer);
			resolve(null);
		}, 1500);
	});

	if (!item) {
		bridge.log(`SET_RUN_MODE failed: menu item not found (${targetMode}).`);
		return false;
	}

	item.click();

	document.body.dispatchEvent(new PointerEvent("pointerdown", {
		bubbles: true,
		cancelable: true,
		pointerType: "mouse",
		button: 0
	}));

	return true;
}

export async function setCanvasMode(bridge, mode) {
	const targetMode = String(mode || "").trim();
	const validModes = new Set(["Select", "Hand"]);
	if (!validModes.has(targetMode)) {
		bridge.log(`SET_CANVAS_MODE ignored unknown mode: ${targetMode}`);
		return false;
	}

	const trigger = document.querySelector('button[aria-label="Canvas Mode"]');
	if (!trigger) {
		bridge.log("SET_CANVAS_MODE failed: Canvas Mode trigger not found.");
		return false;
	}

	trigger.click();

	const item = await new Promise((resolve) => {
		const timer = window.setInterval(() => {
			const found = [...document.querySelectorAll('[role="menu"][aria-label="Canvas Mode"] button[role="menuitemradio"], [role="dialog"] button[role="menuitemradio"]')]
				.find((el) => {
					const label = String(el.getAttribute("aria-label") || "").trim();
					const text = String(el.innerText || el.textContent || "").trim();
					return label === targetMode || text.includes(targetMode);
				});

			if (found) {
				window.clearInterval(timer);
				resolve(found);
			}
		}, 16);

		window.setTimeout(() => {
			window.clearInterval(timer);
			resolve(null);
		}, 1500);
	});

	if (!item) {
		bridge.log(`SET_CANVAS_MODE failed: menu item not found (${targetMode}).`);
		return false;
	}

	item.click();

	document.body.dispatchEvent(new PointerEvent("pointerdown", {
		bubbles: true,
		cancelable: true,
		pointerType: "mouse",
		button: 0
	}));

	return true;
}

export function enterAppMode(bridge) {
	const btn = window.NexusSources?.get?.("enterAppMode") ||
		document.querySelector('button[aria-label="Enter app mode"]');
	if (!btn) {
		bridge.log("Nexus Error: Enter app mode button not found");
		return;
	}

	const originalCss = btn.style.cssText;
	btn.style.cssText += [
		"position: fixed !important",
		"top: 30px !important",
		"left: 50% !important",
		"width: 32px !important",
		"height: 32px !important",
		"opacity: 0 !important",
		"pointer-events: auto !important",
		"visibility: visible !important",
		"z-index: 999999 !important",
		"transform: translateX(-50%) !important",
	].join(";") + ";";

	btn.click();

	setTimeout(() => {
		btn.style.cssText = originalCss;
	}, 120);

	let attempts = 0;
	const restoreTimer = window.setInterval(() => {
		bridge.ensureAppModeVisible();
		attempts += 1;

		if (attempts >= 12 || document.getElementById("linearCenterPanel")) {
			window.clearInterval(restoreTimer);
		}
	}, 80);
}

export function ensureAppModeVisible(bridge) {
	const welcome = document.querySelector('[data-testid="linear-welcome"]');
	const centerPanel = document.getElementById("linearCenterPanel");
	if (!welcome && !centerPanel) return;

	const localControls = centerPanel
		? centerPanel.querySelectorAll([
			'button[aria-label="Workflow actions"]',
			'button[aria-label="Enter node graph"]',
			'button[aria-label="App builder"]',
			'button[aria-label="Apps"]',
		].join(", "))
		: [];

	[centerPanel, welcome, ...localControls].filter(Boolean).forEach((el) => {
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

	const trigger = window.NexusSources?.get?.("workflowActions") ||
		document.querySelector('button[aria-label="Workflow actions"]');
	if (!trigger) {
		bridge.log(`Nexus Error: Workflow actions trigger not found for [${actionType}]`);
		return;
	}

	trigger.click();

	const labelMap = {
		rename: "Rename",
		duplicate: "Duplicate",
		bookmark: "Add to Bookmarks",
		save: "Save",
		save_as: "Save As",
		export: "Export",
		export_api: "Export (API)",
		clear: "Clear Workflow",
		delete: "Delete Workflow",
	};

	const targetText = labelMap[actionType];
	if (!targetText) {
		return;
	}

	let attempts = 0;
	const interval = setInterval(() => {
		attempts += 1;
		const menu = document.querySelector('[data-reka-menu-content][data-state="open"]');
		const items = menu ? Array.from(menu.querySelectorAll('[role="menuitem"]')) : [];
		const targetItem = items.find(item => (item.textContent || "").trim().includes(targetText));

		if (targetItem) {
			clearInterval(interval);
			targetItem.click();
			return;
		}

		if (attempts > 20) {
			clearInterval(interval);
			bridge.log(`Nexus Error: Workflow action [${targetText}] not found`);
		}
	}, 20);
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
	return false;
}

export function executeTabContextAction(bridge, targetTab, actionType) {
	if (!targetTab) return;

	// Open the context menu invisibly so native menu actions can still be reused.
	let styleEl = document.getElementById('nexus-silent-menu-style') || document.createElement('style');
	styleEl.id = 'nexus-silent-menu-style';
	document.head.appendChild(styleEl);
	styleEl.textContent = '[data-reka-popper-content-wrapper] { opacity: 0 !important; pointer-events: none !important; }';

	targetTab.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, view: window, button: 2 }));

	setTimeout(() => {
		const menu = document.querySelector('[data-reka-menu-content][data-state="open"]');
		if (menu) {
			const items = Array.from(menu.querySelectorAll('[role="menuitem"]'));
			const targetTextMap = {
				rename: "Rename", duplicate: "Duplicate", bookmark: "Add to Bookmarks",
				save: "Save", save_as: "Save As", export: "Export", export_api: "Export (API)",
				clear: "Clear Workflow"
			};
			const searchText = targetTextMap[actionType];
			if (!searchText) {
				bridge.log?.(`Nexus Error: Unsupported tab action [${actionType}]`);
				return;
			}
			const targetItem = items.find(item => item.textContent.trim().includes(searchText));
			if (targetItem) {
				targetItem.click();
			} else {
				bridge.log?.(`Nexus Error: Tab action [${searchText}] not found`);
			}
		}
		setTimeout(() => { styleEl.textContent = ''; }, 100);
	}, 50);
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
	const selectorList = Array.isArray(selectors) ? selectors : [
		`button[aria-label="${selectors}"]`,
		`button[title="${selectors}"]`
	];
	const btn = selectorList.map(selector => document.querySelector(selector)).find(Boolean);
	if (btn) {
		btn.click();
		return true;
	} else {
		bridge.log(`Nexus Error: Toolbar button not found`);
		return false;
	}
}
