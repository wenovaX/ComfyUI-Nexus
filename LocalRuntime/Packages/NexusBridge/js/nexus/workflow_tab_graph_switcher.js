import { findAssetExportByKey, findPiniaStoreFactoryById } from "./asset_modules.js";

let workflowStorePromise = null;
let workflowServicePromise = null;
let workflowServiceSourceLogged = false;

const WORKFLOW_SERVICE_EXPORT_KEYS = ["J"];

function getComfyApp() {
	return window.comfyAPI?.app?.app || window.app || null;
}

function getPiniaFromApp(comfyApp) {
	return comfyApp?.extensionManager?.command?._p ||
		comfyApp?.extensionManager?.workflow?._p ||
		document.querySelector("#vue-app")?.__vue_app__?._context?.provides?.pinia ||
		document.querySelector("#vue-app")?._vnode?.appContext?.provides?.pinia ||
		null;
}

function readMaybeRef(value) {
	return value && typeof value === "object" && "value" in value
		? value.value
		: value;
}

function summarizeWorkflow(workflow, index, activeWorkflow, openWorkflows) {
	return {
		index,
		key: workflow?.key ?? null,
		id: workflow?.id ?? null,
		path: workflow?.path ?? null,
		filename: workflow?.filename ?? null,
		fullFilename: workflow?.fullFilename ?? null,
		isActive:
			workflow === activeWorkflow ||
			workflow?.key === activeWorkflow?.key ||
			workflow?.path === activeWorkflow?.path,
		isFirst: index === 0,
		isLast: index === openWorkflows.length - 1,
		isTemporary: Boolean(workflow?.isTemporary),
		isPersisted: Boolean(workflow?.isPersisted),
		isModified: Boolean(workflow?.isModified ?? workflow?._isModified),
		activeMode: workflow?.activeMode ?? null,
		initialMode: workflow?.initialMode ?? null,
	};
}

async function discoverWorkflowStore() {
	const piniaStore = getPiniaFromApp(getComfyApp())?._s?.get?.("workflow");
	if (piniaStore && Array.isArray(piniaStore.openWorkflows)) {
		return piniaStore;
	}

	const found = await findPiniaStoreFactoryById("workflow");
	const store = found.value();
	if (!store || store.$id !== "workflow" || !Array.isArray(store.openWorkflows)) {
		throw new Error("matched workflow store but shape is invalid");
	}

	return store;
}

export async function getWorkflowStore() {
	if (!workflowStorePromise) {
		workflowStorePromise = discoverWorkflowStore()
			.catch((error) => {
				workflowStorePromise = null;
				throw error;
			});
	}

	return workflowStorePromise;
}

async function discoverWorkflowService(bridge = null) {
	const candidates = [
		{ source: "bridge.workflowService", service: bridge?.workflowService },
		{ source: "app.workflowService", service: getComfyApp()?.workflowService },
		{ source: "window.workflowService", service: window.workflowService },
		{ source: "app.vue.provides.workflowService", service: getComfyApp()?.vue?._context?.provides?.workflowService },
		{ source: "vue-app.provides.workflowService", service: document.querySelector("#vue-app")?.__vue_app__?._context?.provides?.workflowService },
	];

	for (const candidate of candidates) {
		if (candidate.service && typeof candidate.service.openWorkflow === "function") {
			return { source: candidate.source, service: candidate.service };
		}
	}

	const found = await findAssetExportByKey(/\/assets\/dialogService-[^/]+\.js(?:\?|$)/, WORKFLOW_SERVICE_EXPORT_KEYS);
	if (typeof found.value !== "function") {
		throw new Error(`workflow service export is not a function: ${found.key}`);
	}

	const service = found.value();
	if (!service || typeof service.openWorkflow !== "function") {
		throw new Error("matched workflow service but shape is invalid");
	}

	return { source: `dialogService.${found.key}`, service };
}

export async function getWorkflowService(bridge = null) {
	if (!workflowServicePromise) {
		workflowServicePromise = discoverWorkflowService(bridge)
			.then(({ source, service }) => {
				if (bridge) bridge.workflowService = service;
				if (bridge && !workflowServiceSourceLogged) {
					workflowServiceSourceLogged = true;
					bridge.log?.(`[Bridge] workflowService source=${source}`);
				}
				return service;
			})
			.catch((error) => {
				workflowServicePromise = null;
				throw error;
			});
	}

	return workflowServicePromise;
}

export async function getWorkflowTabs() {
	const store = await getWorkflowStore();
	const openWorkflows = store.openWorkflows || [];
	const activeWorkflow = store.activeWorkflow;
	return openWorkflows.map((workflow, index) =>
		summarizeWorkflow(workflow, index, activeWorkflow, openWorkflows)
	);
}

export async function getActiveWorkflowTab() {
	const store = await getWorkflowStore();
	const openWorkflows = store.openWorkflows || [];
	const index = openWorkflows.findIndex((workflow) =>
		workflow === store.activeWorkflow ||
		workflow?.key === store.activeWorkflow?.key ||
		workflow?.path === store.activeWorkflow?.path
	);
	return index < 0
		? null
		: summarizeWorkflow(openWorkflows[index], index, store.activeWorkflow, openWorkflows);
}

export async function resolveWorkflow(target) {
	const store = await getWorkflowStore();
	const openWorkflows = store.openWorkflows || [];

	if (typeof target === "number") {
		const workflow = openWorkflows[target];
		if (!workflow) throw new Error(`Workflow tab not found by index: ${target}`);
		return workflow;
	}

	if (typeof target === "string") {
		const workflow = openWorkflows.find((candidate) =>
			candidate?.path === target ||
			candidate?.key === target ||
			candidate?.filename === target ||
			candidate?.fullFilename === target
		);
		if (!workflow) throw new Error(`Workflow tab not found by path/key/name: ${target}`);
		return workflow;
	}

	if (target?.path || target?.key) {
		const workflow = openWorkflows.find((candidate) =>
			candidate?.path === target.path ||
			candidate?.key === target.key
		);
		if (!workflow) throw new Error(`Workflow tab not found: ${target.path ?? target.key}`);
		return workflow;
	}

	throw new Error("target must be index, path, key, filename, or workflow-like object");
}

export async function switchWorkflowGraph(target, bridge = null) {
	const workflow = await resolveWorkflow(target);
	const store = await getWorkflowStore();

	if (
		workflow === store.activeWorkflow ||
		workflow?.key === store.activeWorkflow?.key ||
		workflow?.path === store.activeWorkflow?.path
	) {
		return {
			ok: true,
			changed: false,
			active: await getActiveWorkflowTab(),
		};
	}

	const service = await getWorkflowService(bridge);
	const result = await service.openWorkflow(workflow);
	return {
		ok: true,
		changed: true,
		result,
		active: await getActiveWorkflowTab(),
	};
}

export async function getGraphState() {
	const app = window.app || window.comfyAPI?.app || null;
	const graph = app?.graph || app?.canvas?.graph || null;

	if (!graph) {
		return {
			appFound: Boolean(app),
			graphFound: false,
		};
	}

	const nodes = graph.nodes || graph._nodes || [];
	const links = graph.links || graph._links || [];
	return {
		appFound: Boolean(app),
		graphFound: true,
		nodeCount: Array.isArray(nodes) ? nodes.length : null,
		linkCount: Array.isArray(links) ? links.length : null,
		last_node_id: graph.last_node_id ?? null,
		last_link_id: graph.last_link_id ?? null,
		firstNodes: Array.isArray(nodes)
			? nodes.slice(0, 10).map((node) => ({
				id: node.id ?? null,
				type: node.type ?? null,
				title: node.title ?? null,
			}))
			: null,
	};
}

export async function getWorkflowTabGraphState() {
	return {
		active: await getActiveWorkflowTab(),
		tabs: await getWorkflowTabs(),
		graph: await getGraphState(),
	};
}

export const WorkflowTabGraphSwitcher = {
	getWorkflowStore,
	getWorkflowService,
	getTabs: getWorkflowTabs,
	getActiveTab: getActiveWorkflowTab,
	getGraphState,
	getState: getWorkflowTabGraphState,
	resolveWorkflow,
	switchTo: switchWorkflowGraph,
	switchToIndex: switchWorkflowGraph,
	switchToPath: switchWorkflowGraph,
};

globalThis.WorkflowTabGraphSwitcher = WorkflowTabGraphSwitcher;
