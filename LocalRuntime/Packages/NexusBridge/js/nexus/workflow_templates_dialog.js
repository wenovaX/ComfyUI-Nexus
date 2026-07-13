import { findAssetModuleByExport } from "./asset_modules.js";

const DEFAULT_TEMPLATE_CATEGORY = "all";
const TEMPLATE_DIALOG_LABEL_ID = "global-workflow-template-selector";
const TEMPLATE_DIALOG_FIXED_ATTRIBUTE = "data-nexus-template-category-fixed";
let workflowTemplateDialogPromise = null;

function isVisibleElement(el) {
	if (!(el instanceof HTMLElement)) {
		return false;
	}

	const style = window.getComputedStyle(el);
	if (style.display === "none" || style.visibility === "hidden" || Number(style.opacity || "1") === 0) {
		return false;
	}

	const rect = el.getBoundingClientRect();
	return rect.width > 0 && rect.height > 0;
}

function findVisibleWorkflowTemplateSurface() {
	const surface = document.querySelector([
		`.p-dialog-mask .p-dialog.global-dialog[aria-labelledby="${TEMPLATE_DIALOG_LABEL_ID}"]`,
		`.p-dialog.global-dialog[aria-labelledby="${TEMPLATE_DIALOG_LABEL_ID}"]`,
		`[role="dialog"][aria-labelledby="${TEMPLATE_DIALOG_LABEL_ID}"]`,
	].join(", "));

	return isVisibleElement(surface) ? surface : null;
}

function getWorkflowTemplateCategorySelectionState(surface) {
	if (!surface) {
		return "unknown";
	}

	const navButtons = Array.from(surface.querySelectorAll([
		".bg-modal-panel-background [role=\"button\"]",
		"[role=\"button\"]",
	].join(", "))).filter(isVisibleElement);

	if (navButtons.length === 0) {
		return "unknown";
	}

	const selected = navButtons.some((button) =>
		button.getAttribute("aria-selected") === "true" ||
		button.getAttribute("aria-current") === "page" ||
		button.getAttribute("data-state") === "active" ||
		Array.from(button.classList).some((className) =>
			className.includes("surface-selected") ||
			className.includes("-selected") ||
			className.includes("selected"))
	);

	return selected ? "selected" : "none";
}

function getWorkflowTemplateCategoryButtons(surface) {
	if (!surface) {
		return [];
	}

	return Array.from(surface.querySelectorAll([
		".bg-modal-panel-background [role=\"button\"]",
		"nav [role=\"button\"]",
	].join(", "))).filter(isVisibleElement);
}

function selectWorkflowTemplateDefaultCategory(surface) {
	const firstCategoryButton = getWorkflowTemplateCategoryButtons(surface)[0];
	if (!firstCategoryButton) {
		return false;
	}

	firstCategoryButton.click();
	return true;
}

function nextAnimationFrame() {
	return new Promise((resolve) => requestAnimationFrame(resolve));
}

function looksLikeWorkflowTemplateDialogFactory(value) {
	if (typeof value !== "function") {
		return false;
	}

	const source = Function.prototype.toString.call(value);
	return source.includes("global-workflow-template-selector") &&
		source.includes("initialCategory") &&
		source.includes("trackTemplateLibraryOpened");
}

function isWorkflowTemplateDialog(value) {
	return value &&
		typeof value.show === "function" &&
		typeof value.hide === "function";
}

async function tryCreateWorkflowTemplateDialog(candidate) {
	const { key, value: useWorkflowTemplateSelectorDialog } = candidate;
	if (typeof useWorkflowTemplateSelectorDialog !== "function") {
		return null;
	}

	const dialog = useWorkflowTemplateSelectorDialog();
	if (!isWorkflowTemplateDialog(dialog)) {
		console.warn(`[NexusBridge] Ignoring invalid workflow template dialog export: ${key}`);
		return null;
	}

	return dialog;
}

async function findWorkflowTemplateDialog() {
	try {
		const candidate = await findAssetModuleByExport(looksLikeWorkflowTemplateDialogFactory);
		return tryCreateWorkflowTemplateDialog(candidate);
	} catch {
		return null;
	}
}

async function getWorkflowTemplateDialog() {
	if (!workflowTemplateDialogPromise) {
		workflowTemplateDialogPromise = findWorkflowTemplateDialog()
			.then((dialog) => {
				if (!dialog) {
					throw new Error("workflow template dialog export not found");
				}

				return dialog;
			})
			.catch((error) => {
				workflowTemplateDialogPromise = null;
				throw error;
			});
	}

	return workflowTemplateDialogPromise;
}

export async function showWorkflowTemplatesDialog(options = {}) {
	const dialog = await getWorkflowTemplateDialog();
	const initialCategory = options.initialCategory || DEFAULT_TEMPLATE_CATEGORY;
	dialog.show("nexus", { initialCategory });
	await nextAnimationFrame();
	const surface = findVisibleWorkflowTemplateSurface();
	surface?.setAttribute(TEMPLATE_DIALOG_FIXED_ATTRIBUTE, initialCategory);
	return true;
}

export async function ensureWorkflowTemplatesDefaultCategory(options = {}) {
	const surface = findVisibleWorkflowTemplateSurface();
	if (!surface) {
		return false;
	}

	const selectionState = getWorkflowTemplateCategorySelectionState(surface);
	if (selectionState === "selected") {
		surface.setAttribute(TEMPLATE_DIALOG_FIXED_ATTRIBUTE, "selected");
		return false;
	}

	if (selectionState === "none" && selectWorkflowTemplateDefaultCategory(surface)) {
		await nextAnimationFrame();
		if (getWorkflowTemplateCategorySelectionState(surface) === "selected") {
			surface.setAttribute(TEMPLATE_DIALOG_FIXED_ATTRIBUTE, DEFAULT_TEMPLATE_CATEGORY);
			return true;
		}
	}

	if (surface.hasAttribute(TEMPLATE_DIALOG_FIXED_ATTRIBUTE)) {
		return false;
	}

	if (selectionState !== "none") {
		return false;
	}

	const initialCategory = options.initialCategory || DEFAULT_TEMPLATE_CATEGORY;
	surface.setAttribute(TEMPLATE_DIALOG_FIXED_ATTRIBUTE, initialCategory);

	await nextAnimationFrame();
	if (!findVisibleWorkflowTemplateSurface()) {
		return false;
	}

	await showWorkflowTemplatesDialog({ initialCategory });
	return true;
}
