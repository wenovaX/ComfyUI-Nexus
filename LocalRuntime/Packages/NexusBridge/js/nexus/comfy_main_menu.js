const triggerSelector =
	'nav[data-testid="side-toolbar"] .comfy-menu-button-wrapper';

const menuSelector = [
	'[data-pc-name="tieredmenu"].comfy-command-menu',
	".p-tieredmenu.comfy-command-menu",
	".comfy-command-menu",
].join(", ");

let hostedTrigger = null;

function nextFrame() {
	return new Promise((resolve) => {
		requestAnimationFrame(() => {
			requestAnimationFrame(resolve);
		});
	});
}

function getTrigger() {
	if (hostedTrigger?.isConnected) {
		return hostedTrigger;
	}

	hostedTrigger = null;
	return document.querySelector(triggerSelector) ||
		document.querySelector(".comfy-menu-button-wrapper");
}

function resetHostedTrigger(remove = false) {
	if (remove && hostedTrigger?.parentElement === document.body) {
		hostedTrigger.remove();
	}

	hostedTrigger = null;
}

function hostTrigger() {
	const trigger = getTrigger();
	if (!trigger) return null;

	hostedTrigger = trigger;

	if (trigger.parentElement !== document.body) {
		document.body.appendChild(trigger);
	}

	trigger.style.setProperty("position", "fixed", "important");
	trigger.style.setProperty("top", "8px", "important");
	trigger.style.setProperty("left", "0px", "important");
	trigger.style.setProperty("width", "48px", "important");
	trigger.style.setProperty("height", "48px", "important");
	trigger.style.setProperty("min-width", "48px", "important");
	trigger.style.setProperty("min-height", "48px", "important");
	trigger.style.setProperty("opacity", "0", "important");
	trigger.style.setProperty("visibility", "visible", "important");
	trigger.style.setProperty("pointer-events", "auto", "important");
	trigger.style.setProperty("z-index", "-1", "important");

	return trigger;
}

function getTriggerClickTarget() {
	return hostTrigger();
}

function getDom() {
	return (
		Array.from(document.querySelectorAll(menuSelector))
			.filter((el) => {
				const rect = el.getBoundingClientRect();
				const style = getComputedStyle(el);

				return (
					rect.width > 0 &&
					rect.height > 0 &&
					style.display !== "none" &&
					style.visibility !== "hidden" &&
					Number(style.opacity || "1") > 0
				);
			})
			.at(-1) ?? null
	);
}

function visible() {
	return Boolean(getDom());
}

function rectOf(el) {
	if (!el) return null;

	const rect = el.getBoundingClientRect();

	return {
		x: Math.round(rect.x),
		y: Math.round(rect.y),
		w: Math.round(rect.width),
		h: Math.round(rect.height),
		top: Math.round(rect.top),
		right: Math.round(rect.right),
		bottom: Math.round(rect.bottom),
		left: Math.round(rect.left),
	};
}

function getRect() {
	return rectOf(getDom());
}

function getTriggerRect() {
	return rectOf(getTrigger());
}

async function show() {
	if (visible()) return getDom();

	const trigger = getTriggerClickTarget();

	if (!trigger) {
		throw new Error("Comfy menu trigger not found");
	}

	trigger.click();
	await nextFrame();

	let dom = getDom();
	if (dom) return dom;

	await nextFrame();
	dom = getDom();
	if (dom) return dom;

	resetHostedTrigger(true);
	const freshTrigger = getTriggerClickTarget();
	if (!freshTrigger || freshTrigger === trigger) {
		return null;
	}

	freshTrigger.click();
	await nextFrame();

	dom = getDom();
	if (dom) return dom;

	await nextFrame();
	return getDom();
}

async function hide() {
	if (!visible()) return null;

	const menu = getDom();
	for (const target of [
		document.activeElement,
		menu,
		document.body,
		document.documentElement,
		document,
		window,
	]) {
		if (!target) continue;

		for (const type of ["keydown", "keyup"]) {
			target.dispatchEvent(new KeyboardEvent(type, {
				key: "Escape",
				code: "Escape",
				keyCode: 27,
				which: 27,
				bubbles: true,
				cancelable: true,
				composed: true,
			}));
		}
	}

	await nextFrame();

	if (!visible()) return null;

	// Last fallback: the next show() recreates the menu through the real trigger.
	getDom()?.remove();
	await nextFrame();

	return null;
}

async function toggle() {
	const trigger = getTriggerClickTarget();

	if (!trigger) {
		throw new Error("Comfy menu trigger not found");
	}

	trigger.click();
	await nextFrame();

	let dom = getDom();
	if (dom) return dom;

	resetHostedTrigger(true);
	const freshTrigger = getTriggerClickTarget();
	if (!freshTrigger || freshTrigger === trigger) {
		return null;
	}

	freshTrigger.click();
	await nextFrame();

	return getDom();
}

function setPosition(x, y) {
	const el = getDom();

	if (!el) {
		throw new Error("Comfy menu DOM not found. Call await ComfyMainMenu.show() first.");
	}

	el.style.setProperty("position", "fixed", "important");
	el.style.setProperty("inset", "auto", "important");
	el.style.setProperty("left", `${Math.round(x)}px`, "important");
	el.style.setProperty("top", `${Math.round(y)}px`, "important");
	el.style.setProperty("right", "auto", "important");
	el.style.setProperty("bottom", "auto", "important");
	el.style.setProperty("transform", "none", "important");

	return getRect();
}

function moveBy(dx, dy) {
	const rect = getRect();

	if (!rect) {
		throw new Error("Comfy menu DOM not found. Call await ComfyMainMenu.show() first.");
	}

	return setPosition(rect.x + dx, rect.y + dy);
}

function getState() {
	const trigger = getTrigger();
	const dom = getDom();

	return {
		visible: visible(),
		trigger,
		dom,
		triggerRect: rectOf(trigger),
		menuRect: rectOf(dom),
		menuText: dom?.innerText ?? null,
		menuClassName: dom?.className ?? null,
	};
}

export const ComfyMainMenu = {
	show,
	hide,
	toggle,
	visible,
	getDom,
	getRect,
	getTrigger,
	getTriggerClickTarget,
	getTriggerRect,
	resetHostedTrigger,
	setPosition,
	moveBy,
	getState,
};

globalThis.ComfyMainMenu = ComfyMainMenu;
