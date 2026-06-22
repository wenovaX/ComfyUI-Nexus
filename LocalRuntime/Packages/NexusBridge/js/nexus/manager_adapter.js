const MANAGER_MODULES = {
	common: "/extensions/ComfyUI-Manager/js/common.js",
	customNodes: "/extensions/ComfyUI-Manager/js/custom-nodes-manager.js",
};

export const CUSTOM_NODE_SHOW_MODES = {
	favorites: "Favorites",
};

function isElementVisible(selector) {
	const el = document.querySelector(selector);
	return Boolean(el && el.style.display !== "none" && el.offsetParent !== null);
}

async function getManagerDbMode() {
	try {
		const response = await fetch("/manager/db_mode", { cache: "no-store" });
		if (response.ok) {
			return (await response.text()) || "cache";
		}
	} catch {
	}
	return "cache";
}

function setCustomNodesManagerFilter(mode) {
	const dialog = document.querySelector("#cn-manager-dialog");
	const filter = dialog?.querySelector?.(".cn-manager-filter");
	if (!dialog || !filter) return false;

	dialog.style.display = "flex";
	filter.value = mode;
	filter.dispatchEvent(new Event("change", { bubbles: true }));
	return filter.value === mode;
}

function getCustomNodesManagerMode() {
	const dialog = document.querySelector("#cn-manager-dialog");
	const filter = dialog?.querySelector?.(".cn-manager-filter");
	return String(filter?.value || "");
}

async function importManagerModules() {
	const [common, customNodes] = await Promise.all([
		import(MANAGER_MODULES.common),
		import(MANAGER_MODULES.customNodes),
	]);
	return { common, customNodes };
}

export async function createManagerAdapter(bridge, app) {
	try {
		const modules = await importManagerModules();
		const { common, customNodes } = modules;
		const CustomNodesManager = customNodes.CustomNodesManager;

		if (typeof common.setManagerInstance !== "function") {
			throw new Error("setManagerInstance export not found");
		}

		if (!CustomNodesManager) {
			throw new Error("CustomNodesManager export not found");
		}

		const adapter = {
			available: true,
			common,
			CustomNodesManager,
			fakeManagerInstance: null,

			async clearFakeManagerInstance() {
				if (!this.fakeManagerInstance) return;

				if (this.common.manager_instance === this.fakeManagerInstance) {
					this.common.setManagerInstance(null);
				}

				if (window.__nexusFakeManagerInstance === this.fakeManagerInstance) {
					window.__nexusFakeManagerInstance = null;
				}

				this.fakeManagerInstance = null;
			},

			async ensureManagerInstanceForCustomNodes() {
				if (this.common.manager_instance && this.common.manager_instance !== this.fakeManagerInstance) {
					if (this.common.manager_instance.isVisible) {
						this.common.manager_instance.close?.();
					}

					return this.common.manager_instance;
				}

				const fake = this.fakeManagerInstance || {
					datasrc_combo: { value: await getManagerDbMode() },
					show() {},
					close() {},
					toggleVisibility() {},
					element: null,
				};

				this.fakeManagerInstance = fake;
				window.__nexusFakeManagerInstance = fake;
				this.common.setManagerInstance(fake);
				return fake;
			},

			async closeCustomNodesMode(mode = "") {
				try {
					const instance = this.CustomNodesManager.instance;
					if (!instance?.isVisible) return false;

					if (mode && getCustomNodesManagerMode() !== mode) {
						return false;
					}

					instance.close();
					return true;
				} catch (error) {
					bridge.log(`ManagerAdapter Custom Nodes close failed: ${error?.message || error}`);
					return false;
				}
			},

			async showCustomNodesMode(mode) {
				try {
					const managerDialog = await this.ensureManagerInstanceForCustomNodes();
					if (!this.CustomNodesManager.instance) {
						this.CustomNodesManager.instance = new this.CustomNodesManager(app || window.app, managerDialog);
					}

					this.CustomNodesManager.instance.show(mode);
					await new Promise((resolve) => setTimeout(resolve, 80));

					if (isElementVisible("#cn-manager-dialog")) {
						return true;
					}
				} catch (error) {
					bridge.log(`ManagerAdapter Custom Nodes open failed: ${error?.message || error}`);
				}

				if (isElementVisible("#cn-manager-dialog") && setCustomNodesManagerFilter(mode)) {
					return true;
				}

				return false;
			},

			async showFavorites() {
				const mode = this.CustomNodesManager.ShowMode?.FAVORITES || CUSTOM_NODE_SHOW_MODES.favorites;
				return this.showCustomNodesMode(mode);
			},
		};

		return adapter;
	} catch (error) {
		bridge.log(`ComfyUI-Manager adapter unavailable: ${error?.message || error}`);
		return {
			available: false,
			async clearFakeManagerInstance() {},
			async closeCustomNodesMode() { return false; },
			async showCustomNodesMode() { return false; },
			async showFavorites() { return false; },
		};
	}
}
