function nextFrame() {
	return new Promise((resolve) => {
		requestAnimationFrame(() => {
			requestAnimationFrame(resolve);
		});
	});
}

function getOpenContent() {
	return document.querySelector(
		'[data-reka-menu-content][role="menu"][data-state="open"][aria-labelledby^="reka-dropdown-menu-trigger-"]'
	);
}

function getTriggerFromContent(content) {
	if (!content) return null;

	const id = content.getAttribute("aria-labelledby");
	if (!id) return null;

	return document.getElementById(id);
}

function getTrigger() {
	const content = getOpenContent();
	const triggerId = content?.getAttribute("aria-labelledby");

	if (triggerId) {
		const trigger = document.getElementById(triggerId);
		if (trigger) return trigger;
	}

	return document.querySelector(
		'button[id^="reka-dropdown-menu-trigger-"][aria-haspopup="menu"][data-state="open"]'
	) ||
		document.querySelector('[data-testid="subgraph-breadcrumb"] button[aria-haspopup="menu"]') ||
		document.querySelector('.subgraph-breadcrumb button[aria-haspopup="menu"]');
}

function isVisible() {
	const content = getOpenContent();
	const trigger = getTrigger();

	return Boolean(
		content?.getAttribute("data-state") === "open" ||
		trigger?.getAttribute("data-state") === "open" ||
		trigger?.getAttribute("aria-expanded") === "true"
	);
}

function isTriggerOpen(trigger = getTrigger()) {
	return (
		trigger?.getAttribute("aria-haspopup") === "menu" &&
		trigger?.getAttribute("data-state") === "open" &&
		trigger?.getAttribute("aria-expanded") === "true"
	);
}

async function clickTrigger(trigger) {
	if (!trigger) return false;

	const oldVisibility = trigger.style.visibility;
	const oldPointer = trigger.style.pointerEvents;
	trigger.style.setProperty("visibility", "visible", "important");
	trigger.style.setProperty("pointer-events", "auto", "important");
	trigger.click();
	await nextFrame();
	trigger.style.setProperty("visibility", oldVisibility, "important");
	trigger.style.setProperty("pointer-events", oldPointer, "important");
	return true;
}

async function close() {
	if (!isVisible()) return false;

	const trigger = getTrigger();
	if (trigger) {
		trigger.click();
		await nextFrame();

		if (!isVisible()) return false;
	}

	const content = getOpenContent();

	for (const target of [
		document.activeElement,
		content,
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

	return isVisible();
}

async function show() {
	const trigger = getTrigger();
	if (!trigger) return false;

	if (isVisible()) return true;

	if (isTriggerOpen(trigger)) {
		await close();
		await nextFrame();
	}

	await clickTrigger(trigger);
	return isVisible();
}

function getState() {
	const content = getOpenContent();
	const trigger = getTriggerFromContent(content);

	return {
		visible: isVisible(),
		content,
		trigger,
		contentId: content?.id || "",
		triggerId: trigger?.id || "",
		triggerOpen: isTriggerOpen(trigger),
	};
}

export const WorkflowActionMenu = {
	getOpenContent,
	getTrigger,
	getTriggerFromContent,
	getState,
	close,
	hide: close,
	isVisible,
	isTriggerOpen,
	show,
	visible: isVisible,
};

globalThis.WorkflowActionMenu = WorkflowActionMenu;
