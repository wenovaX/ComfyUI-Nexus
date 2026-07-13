import { app } from "/scripts/app.js";

function getCanvasPasteTarget() {
	const hostApp = window.comfyAPI?.app?.app || window.app || app;
	const canvas = hostApp?.canvas || app?.canvas;
	if (!canvas?._pasteFromClipboard) {
		throw new Error("canvas._pasteFromClipboard not found");
	}

	return canvas;
}

function createWorkflowClipboardData(workflow) {
	return {
		nodes: workflow?.nodes ?? [],
		links: workflow?.links ?? [],
		groups: workflow?.groups ?? [],
		reroutes: workflow?.reroutes ?? [],
		subgraphs: workflow?.subgraphs ?? workflow?.definitions?.subgraphs ?? [],
	};
}

function pasteWorkflowClipboard(canvas, clipboardData, options = {}) {
	localStorage.setItem("litegrapheditor_clipboard", JSON.stringify(clipboardData));
	return canvas._pasteFromClipboard({
		position: options.position ?? canvas.graph_mouse,
		connectInputs: options.connectInputs ?? false,
	});
}

export async function insertWorkflowFromUserData(path, options = {}) {
	const normalizedPath = String(path || "").replace(/^\/+/, "");
	if (!normalizedPath) {
		throw new Error("workflow userdata path is empty");
	}

	const response = await fetch(`/api/userdata/${encodeURIComponent(normalizedPath)}`, {
		cache: "no-cache",
		headers: { "Comfy-User": "" },
	});

	if (!response.ok) {
		throw new Error(`failed to load workflow: ${response.status}`);
	}

	const workflow = await response.json();
	return insertWorkflowFromJson(workflow, options);
}

export function insertWorkflowFromJson(workflowJson, options = {}) {
	const workflow = typeof workflowJson === "string"
		? JSON.parse(workflowJson)
		: workflowJson;
	if (!workflow || typeof workflow !== "object" || !Array.isArray(workflow.nodes)) {
		throw new Error("workflow JSON is invalid");
	}

	const canvas = getCanvasPasteTarget();
	return pasteWorkflowClipboard(canvas, createWorkflowClipboardData(workflow), options);
}

export function normalizeAssetPayload(bridge, payload) {
	if (!payload || (!payload.path && !payload.nodeType)) {
		return null;
	}

	return {
		path: String(payload.path || ""),
		name: String(payload.name || ""),
		displayName: String(payload.displayName || payload.name || ""),
		extension: String(payload.extension || "").toLowerCase(),
		kind: String(payload.kind || "GenericFile"),
		mode: String(payload.mode || ""),
		action: String(payload.action || "Open"),
		sourceRoot: String(payload.sourceRoot || "assets"),
		modelDirectory: String(payload.modelDirectory || ""),
		modelPathIndex: Number.isFinite(payload.modelPathIndex) ? payload.modelPathIndex : 0,
		nodeType: String(payload.nodeType || ""),
		dragId: String(payload.dragId || ""),
		dropClientX: Number.isFinite(payload.dropClientX) ? payload.dropClientX : null,
		dropClientY: Number.isFinite(payload.dropClientY) ? payload.dropClientY : null,
		railWidth: Number.isFinite(payload.railWidth) ? payload.railWidth : 0,
	};
}

export function markAssetDragProcessed(bridge, payload) {
	const dragId = String(payload?.dragId || "");
	if (!dragId) {
		return false;
	}

	if (bridge.processedAssetDragIds.has(dragId)) {
		return true;
	}

	bridge.processedAssetDragIds.add(dragId);
	if (bridge.processedAssetDragIds.size > 128) {
		const first = bridge.processedAssetDragIds.values().next().value;
		bridge.processedAssetDragIds.delete(first);
	}

	return false;
}

export function handleAssetBrowserDragStart(bridge, payload) {
	const normalized = bridge.normalizeAssetPayload(payload);
	if (!normalized) {
		return;
	}

	normalized.dragStartedAt = Date.now();
	normalized.expiresAt = Date.now() + 15000;
	bridge.lastAssetDragIntent = normalized;
	window.__nexusActiveAssetDrag = normalized;
	window.NexusShowAssetDragCue?.(normalized);
	bridge.debugModel("drag-start", normalized);
}

export function handleAssetDropFeedback(bridge, payload) {
	const normalized = bridge.normalizeAssetPayload(payload);
	if (!normalized) {
		return;
	}

	window.NexusShowAssetDropFeedback?.(bridge.getRailSpawnOriginClientPosition(normalized.railWidth), {
		modelName: normalized.displayName || normalized.name || normalized.mode || "Asset",
		targetClientX: normalized.dropClientX,
		targetClientY: normalized.dropClientY,
	});
}

export function handleAssetDropFeedbackSource(bridge, payload) {
	const normalized = bridge.normalizeAssetPayload(payload);
	if (!normalized) {
		return;
	}

	window.__nexusAssetDropFeedbackSource = {
		...normalized,
		expiresAt: Date.now() + 15000,
	};
}

export function handleAssetBrowserOpen(bridge, payload) {
	const normalized = bridge.normalizeAssetPayload(payload);
	if (!normalized) {
		return;
	}

	bridge.lastAssetIntent = normalized;
	window.__nexusLastAssetIntent = normalized;
	bridge.debugModel("asset-open", {
		...normalized,
		pathRaw: normalized.path,
		pathJson: JSON.stringify(normalized.path),
		pathHasBackslash: normalized.path.includes("\\"),
		pathHasForwardSlash: normalized.path.includes("/"),
	});

	try {
		document.dispatchEvent(new CustomEvent("nexus:asset-open", {
			detail: normalized,
		}));

		if (normalized.kind === "ModelFile") {
			document.dispatchEvent(new CustomEvent("nexus:model-selected", {
				detail: {
					...normalized,
					category: bridge.resolveModelCategory(normalized),
				},
			}));
		}
	} catch (e) {
		bridge.log(`Asset intent event dispatch failed: ${e.message}`);
	}

	if (normalized.kind === "ModelFile") {
		if (bridge.markAssetDragProcessed(normalized)) {
			bridge.debugModel("asset-open-duplicate", normalized);
			return;
		}

		const category = bridge.resolveModelCategory(normalized);
		const label = category ? `${category} / ${normalized.name}` : normalized.name;
		normalized.dropCanvasPos = bridge.resolveNodeSpawnCanvasPosition(normalized);
		const applied = bridge.applyModelSelection(normalized);
		if (applied) {
			window.NexusShowAssetDropFeedback?.(
				bridge.getRailSpawnOriginClientPosition(normalized.railWidth),
				withDropTargetClientPosition(applied, normalized)
			);
			bridge.showToast(`Created model node: ${label}`, "success");
		} else {
			bridge.showToast(`Model selected: ${label}`, "info");
		}
		return;
	}

	if (normalized.kind === "WorkflowJson") {
		return;
	}

	if (normalized.mode === "Node" || normalized.nodeType) {
		normalized.dropCanvasPos = bridge.resolveNodeSpawnCanvasPosition(normalized);
		const applied = bridge.applyNodeSelection(normalized);
		if (applied) {
			window.NexusShowAssetDropFeedback?.(
				bridge.getRailSpawnOriginClientPosition(normalized.railWidth),
				withDropTargetClientPosition(applied, normalized)
			);
			bridge.showToast(`Created node: ${normalized.displayName || normalized.nodeType}`, "success");
		}
		return;
	}
}

export function getCanvasCenterCanvasPosition(bridge) {
	const centerClient = bridge.getCanvasCenterClientPosition();
	return bridge.resolveAssetDropCanvasPosition({
		dropClientX: centerClient.x,
		dropClientY: centerClient.y
	}) || [0, 0];
}

export function getCanvasCenterClientPosition(bridge) {
	try {
		const canvas = document.getElementById("graph-canvas") || app.canvas?.canvas;
		const rect = canvas?.getBoundingClientRect?.();
		if (rect) {
			return {
				x: rect.left + rect.width / 2,
				y: rect.top + rect.height / 2,
			};
		}
	} catch (_) {}

	return {
		x: window.innerWidth / 2,
		y: window.innerHeight / 2,
	};
}

export function getRailSpawnOriginClientPosition(bridge, railWidthPx = 0) {
	try {
		const canvas = document.getElementById("graph-canvas") || app.canvas?.canvas;
		const rect = canvas?.getBoundingClientRect?.();
		const railWidth = Math.max(0, Number(railWidthPx || 0));
		if (rect) {
			return {
				x: Math.min(rect.right - 24, rect.left + railWidth + 10),
				y: rect.top + rect.height / 2,
			};
		}
	} catch (_) {}

	return {
		x: Math.max(10, Number(railWidthPx || 0) + 10),
		y: window.innerHeight / 2,
	};
}

function withDropTargetClientPosition(result, payload) {
	if (!result || !Number.isFinite(payload?.dropClientX) || !Number.isFinite(payload?.dropClientY)) {
		return result;
	}

	return {
		...result,
		targetClientX: Number(payload.dropClientX),
		targetClientY: Number(payload.dropClientY),
	};
}

export function resolveAssetDropCanvasPosition(bridge, payload) {
	if (!Number.isFinite(payload?.dropClientX) || !Number.isFinite(payload?.dropClientY)) {
		return null;
	}

	try {
		const canvasEl = document.getElementById("graph-canvas") || document.querySelector("canvas");
		const rect = canvasEl?.getBoundingClientRect?.();
		const ds = app.canvas?.ds;
		if (!rect || !ds?.offset || !Number.isFinite(ds?.scale)) {
			return null;
		}

		const canvasX = Number(payload.dropClientX);
		const canvasY = Number(payload.dropClientY);
		const scale = Math.max(Number(ds.scale || 1), 0.001);

		const rawPos = [
			(canvasX - Number(rect.left || 0)) / scale - Number(ds.offset[0] || 0),
			(canvasY - Number(rect.top || 0)) / scale - Number(ds.offset[1] || 0),
		];

		// If it's not a drag-drop (no specific drop point), apply jitter
		if (!payload.dragId) {
			return bridge.jitterNodePosition(rawPos);
		}
		return rawPos;
	} catch (error) {
		bridge.debugModel("drop-position-error", {
			message: error?.message || String(error),
			dropClientX: payload?.dropClientX,
			dropClientY: payload?.dropClientY,
		});
		return null;
	}
}

export function resolveNodeSpawnCanvasPosition(bridge, payload) {
	if (Number.isFinite(payload?.dropClientX)
		&& Number.isFinite(payload?.dropClientY)
		&& bridge.isClientPointInsideCanvas(payload.dropClientX, payload.dropClientY)) {
		const dropPos = bridge.resolveAssetDropCanvasPosition(payload);
		if (Array.isArray(dropPos)) {
			return dropPos;
		}
	}

	return bridge.getRandomCentralCanvasPosition(0.3);
}

export function isClientPointInsideCanvas(bridge, clientX, clientY) {
	try {
		const canvasEl = document.getElementById("graph-canvas") || document.querySelector("canvas");
		const rect = canvasEl?.getBoundingClientRect?.();
		if (!rect) {
			return false;
		}

		return clientX >= rect.left
			&& clientX <= rect.right
			&& clientY >= rect.top
			&& clientY <= rect.bottom;
	} catch (_) {
		return false;
	}
}

export function getRandomCentralCanvasPosition(bridge, spreadRatio = 0.3) {
	try {
		const canvasEl = document.getElementById("graph-canvas") || document.querySelector("canvas");
		const rect = canvasEl?.getBoundingClientRect?.();
		const ds = app.canvas?.ds;
		if (!rect || !ds?.offset || !Number.isFinite(ds?.scale)) {
			return bridge.getCanvasCenterCanvasPosition();
		}

		const spread = Math.min(Math.max(Number(spreadRatio) || 0.3, 0), 1);
		const clientX = rect.left + rect.width * (0.5 + (Math.random() - 0.5) * spread);
		const clientY = rect.top + rect.height * (0.5 + (Math.random() - 0.5) * spread);
		const scale = Math.max(Number(ds.scale || 1), 0.001);
		return [
			(clientX - rect.left) / scale - Number(ds.offset[0] || 0),
			(clientY - rect.top) / scale - Number(ds.offset[1] || 0),
		];
	} catch (_) {
		return bridge.getCanvasCenterCanvasPosition();
	}
}

export function jitterNodePosition(bridge, pos) {
	// Randomize within a small area (approx 100-200 units) to avoid overlapping
	const range = 150;
	return [
		pos[0] + (Math.random() - 0.5) * range,
		pos[1] + (Math.random() - 0.5) * range
	];
}

export function resolveModelCategory(bridge, payload) {
	if (payload?.modelDirectory) {
		return String(payload.modelDirectory).toLowerCase();
	}

	if (!payload?.path) {
		return "";
	}

	const normalizedPath = String(payload.path).replace(/\\/g, "/").toLowerCase();
	const marker = "/models/";
	const markerIndex = normalizedPath.indexOf(marker);
	if (markerIndex === -1) {
		return "";
	}

	const remainder = normalizedPath.slice(markerIndex + marker.length);
	const [category] = remainder.split("/");
	return category || "";
}

export function applyModelSelection(bridge, payload) {
	try {
		const modelDirectory = bridge.resolveModelCategory(payload);
		bridge.debugModel("apply-start", {
			modelDirectory,
			fileName: payload?.name,
			displayName: payload?.displayName,
			path: payload?.path,
		});

		const createdTargetInfo = bridge.tryCreateModelProviderNode(modelDirectory, payload?.dropCanvasPos);
		if (createdTargetInfo?.widget) {
			bridge.debugModel("apply-created-target", {
				modelDirectory,
				fileName: payload?.name,
				targetNode: createdTargetInfo.node?.comfyClass || createdTargetInfo.node?.type || "",
				targetWidget: createdTargetInfo.widget?.name || createdTargetInfo.widget?.label || "",
			});
			return bridge.applyModelToTarget(payload, modelDirectory, createdTargetInfo);
		}

		bridge.debugModel("apply-no-target", {
			modelDirectory,
			fileName: payload?.name,
		});
		return false;
	} catch (error) {
		bridge.debugModel("apply-error", {
			message: error?.message || String(error),
			fileName: payload?.name,
			modelDirectory: bridge.resolveModelCategory(payload),
		});
		bridge.log(`Model apply failed: ${error.message}`);
		return false;
	}
}

export function applyNodeSelection(bridge, payload) {
	try {
		const nodeType = payload.nodeType || payload.name;
		if (!nodeType) return false;

		if (nodeType.startsWith("SubgraphBlueprint.")) {
			const bpId = nodeType.substring("SubgraphBlueprint.".length);

			// Blueprints use graph canvas coordinates, same as regular Nexus node placement.
			let dropPos = payload.dropCanvasPos;
			if (!dropPos) {
				dropPos = bridge.resolveNodeSpawnCanvasPosition(payload);
			}

			(async () => {
				try {
					const detail = await fetch(`/api/global_subgraphs/${bpId}`, {
						cache: "no-cache",
						headers: { "Comfy-User": "" }
					}).then(r => r.json());

					const workflow = JSON.parse(detail.data);

					const items = {
						nodes: structuredClone(workflow.nodes || []),
						links: structuredClone(workflow.links || []),
						groups: structuredClone(workflow.groups || []),
						reroutes: structuredClone(workflow.reroutes || []),
						subgraphs: structuredClone(workflow.definitions?.subgraphs || [])
					};

					window.app.canvas._deserializeItems(items, {
						position: dropPos,
						connectInputs: false
					});

					window.app.graph.setDirtyCanvas(true, true);
					window.app.canvas.setDirty(true, true);

					bridge.showToast(`Created Blueprint: ${payload.displayName || bpId}`, "success");
				} catch (e) {
					bridge.log(`Blueprint apply failed: ${e.message}`);
					bridge.showToast(`Failed to create Blueprint: ${e.message}`, "error");
				}
			})();

			return false; // Return false so the sync toast isn't shown, the async block handles it
		}

		const node = LiteGraph.createNode(nodeType);
		if (!node) {
			bridge.log(`Nexus Error: Failed to create node of type [${nodeType}]`);
			return false;
		}

		node.pos = Array.isArray(payload.dropCanvasPos)
			? payload.dropCanvasPos
			: bridge.resolveNodeSpawnCanvasPosition(payload);

		app.graph.add(node);
		app.canvas.selectNode(node);

		// Bring newly created node to the top of the render stack
		bridge.bringNodeToFront(node);

		return {
			handled: true,
			nodeId: node.id ?? null,
			nodeTitle: node.title || node.comfyClass || node.type || "",
			modelName: payload.displayName || payload.nodeType || nodeType,
			created: true,
		};
	} catch (error) {
		bridge.log(`Node apply failed: ${error.message}`);
		return false;
	}
}

export function getVisibleCenterCanvasPosition(bridge, railWidthPx = 0) {
	try {
		const ds = app.canvas?.ds;
		const canvasEl = document.getElementById("graph-canvas") || app.canvas?.canvas;
		if (!ds?.offset || !Number.isFinite(ds?.scale) || !canvasEl) {
			return bridge.getCanvasCenterCanvasPosition();
		}

		const rect = canvasEl.getBoundingClientRect();
		const scale = Math.max(ds.scale, 0.001);

		const visibleCenterClientX = rect.left + rect.width / 2;
		const visibleCenterClientY = rect.top + rect.height / 2;

		// Convert client coords to canvas coords
		const centerPos = [
			(visibleCenterClientX - rect.left) / scale - ds.offset[0],
			(visibleCenterClientY - rect.top) / scale - ds.offset[1],
		];
		return bridge.jitterNodePosition(centerPos);
	} catch (e) {
		return bridge.getCanvasCenterCanvasPosition();
	}
}

export function bringNodeToFront(bridge, node) {
	try {
		const list = app.graph?._nodes;
		if (!list || !node) return;

		const idx = list.indexOf(node);
		if (idx === -1) return;

		list.splice(idx, 1);
		list.push(node);
		app.graph.setDirtyCanvas(true, true);
	} catch (e) {
		// silent
	}
}

export function applyModelToTarget(bridge, payload, modelDirectory, targetInfo) {
	targetInfo.widget.value = payload.name;
	if (typeof targetInfo.widget.callback === "function") {
		try {
			targetInfo.widget.callback(payload.name, null, targetInfo.node);
		} catch (callbackError) {
			bridge.log(`Model widget callback failed: ${callbackError.message}`);
		}
	}

	if (targetInfo.node) {
		targetInfo.node.setDirtyCanvas?.(true, true);
		targetInfo.node.graph?.setDirtyCanvas?.(true, true);
		targetInfo.node.graph?.change?.();
	}

	bridge.debugModel("apply-success", {
		modelDirectory,
		fileName: payload?.name,
		targetNode: targetInfo.node?.comfyClass || targetInfo.node?.type || "",
		targetWidget: targetInfo.widget?.name || targetInfo.widget?.label || "",
	});

	app.canvas?.setDirty?.(true, true);
	window.app?.graph?.setDirtyCanvas?.(true, true);
	window.app?.graph?.change?.();
	app.canvas?.draw?.(true, true);
	bridge.scheduleSync(80);
	return {
		handled: true,
		nodeId: targetInfo.node?.id ?? null,
		nodeTitle: targetInfo.node?.title || targetInfo.node?.comfyClass || targetInfo.node?.type || "",
		widgetName: targetInfo.widget?.name || targetInfo.widget?.label || "",
		modelName: payload?.name || "",
		created: true,
	};
}

export function handleAssetIntentDrop(bridge, intent, dropInfo = {}) {
	const normalized = bridge.normalizeAssetPayload(intent);
	if (!normalized) {
		return false;
	}

	if (normalized.expiresAt && Date.now() > normalized.expiresAt) {
		bridge.debugModel("drop-expired", normalized);
		return false;
	}

	if (normalized.mode !== "Model" && normalized.kind !== "ModelFile") {
		bridge.debugModel("drop-ignored", {
			mode: normalized.mode,
			kind: normalized.kind,
			name: normalized.name,
		});
		return false;
	}

	normalized.dropCanvasPos = Array.isArray(dropInfo.canvasPos) ? dropInfo.canvasPos : null;
	normalized.dropClientX = Number.isFinite(dropInfo.clientX) ? Number(dropInfo.clientX) : normalized.dropClientX;
	normalized.dropClientY = Number.isFinite(dropInfo.clientY) ? Number(dropInfo.clientY) : normalized.dropClientY;
	bridge.debugModel("drop-intent", {
		name: normalized.name,
		modelDirectory: normalized.modelDirectory,
		canvasPos: normalized.dropCanvasPos,
	});

	const applied = bridge.applyModelSelection(normalized);
	if (applied) {
		bridge.markAssetDragProcessed(normalized);
		window.__nexusActiveAssetDrag = null;
		window.NexusHideAssetDragCue?.();
		bridge.showToast(`Created model node: ${normalized.name}`, "success");
	}

	return withDropTargetClientPosition(applied, normalized);
}

export function tryCreateModelProviderNode(bridge, modelDirectory, preferredPosition = null) {
	try {
		const provider = bridge.getModelNodeProvider(modelDirectory);
		bridge.debugModel("provider-resolved", bridge.describeProvider(provider));

		const graph = window.app?.graph || app?.graph || app?.canvas?.graph;
		if (!graph) {
			bridge.debugModel("provider-no-graph", { modelDirectory });
			return null;
		}

		const nodeSpec = bridge.resolveModelNodeSpec(modelDirectory, provider);
		const node = bridge.createGraphNodeFromSpec(nodeSpec);
		if (!node) {
			bridge.debugModel("provider-create-failed", {
				modelDirectory,
				provider: bridge.describeProvider(provider),
				nodeSpec,
			});
			return null;
		}

		node.pos = Array.isArray(preferredPosition)
			? preferredPosition
			: bridge.getNextModelNodePosition();
		if (nodeSpec.title) {
			node.title = nodeSpec.title;
		}

		if (!graph._nodes?.includes?.(node)) {
			graph.add?.(node);
		}
		bridge.bringGraphNodeToFront(graph, node);

		app.canvas?.selectNode?.(node, false);
		node.setDirtyCanvas?.(true, true);
		graph.setDirtyCanvas?.(true, true);
		graph.change?.();
		app.canvas?.setDirty?.(true, true);
		app.canvas?.draw?.(true, true);

		const directWidget = node.widgets?.find((candidate) => {
			const widgetName = String(candidate?.name || candidate?.label || "").toLowerCase();
			return widgetName === String(nodeSpec.widgetName || provider?.key || "").toLowerCase();
		});
		const widget = directWidget || bridge.findMatchingWidget(node, bridge.getModelWidgetPredicates(modelDirectory));

		if (!widget) {
			bridge.debugModel("provider-widget-miss", {
				provider: bridge.describeProvider(provider),
				nodeSpec,
				node: bridge.describeGraphNode(node),
			});
			return null;
		}

		bridge.debugModel("provider-create-success", {
			modelDirectory,
			nodeSpec,
			node: bridge.describeGraphNode(node),
			widget: widget?.name || widget?.label || "",
		});
		return { node, widget };
	} catch (error) {
		bridge.debugModel("provider-create-error", {
			modelDirectory,
			message: error?.message || String(error),
		});
		return null;
	}
}

export function bringGraphNodeToFront(bridge, graph, node) {
	try {
		if (!graph || !node || !Array.isArray(graph._nodes)) {
			return;
		}

		const index = graph._nodes.indexOf(node);
		if (index >= 0 && index !== graph._nodes.length - 1) {
			graph._nodes.splice(index, 1);
			graph._nodes.push(node);
		}

		app.canvas?.bringToFront?.(node);
		app.canvas?.selectNode?.(node, false);
	} catch (error) {
		bridge.debugModel("bring-front-error", {
			message: error?.message || String(error),
			node: bridge.describeGraphNode(node),
		});
	}
}

export function getModelNodeProvider(bridge, modelDirectory) {
	const store = bridge.modelToNodeStore || bridge.discoveredPinia?._s?.get?.("modelToNode");
	if (!store) {
		bridge.debugModel("provider-no-store", { modelDirectory });
		return null;
	}

	try {
		const providers = typeof store.getAllNodeProviders === "function"
			? store.getAllNodeProviders(modelDirectory)
			: [];
		bridge.debugModel("provider-list", {
			modelDirectory,
			providers: Array.isArray(providers)
				? providers.map((provider) => bridge.describeProvider(provider))
				: bridge.describeValue(providers),
		});
	} catch (error) {
		bridge.debugModel("provider-list-error", {
			modelDirectory,
			message: error?.message || String(error),
		});
	}

	if (typeof store.getNodeProvider === "function") {
		return store.getNodeProvider(modelDirectory);
	}

	return null;
}

export function resolveModelNodeSpec(bridge, modelDirectory, provider) {
	const normalizedDirectory = String(modelDirectory || "").toLowerCase();
	const providerSpec = {
		type: provider?.nodeDef?.name || "",
		title: provider?.nodeDef?.display_name || provider?.nodeDef?.name || "",
		widgetName: provider?.key || "",
	};

	const fallbackSpecs = {
		checkpoints: { type: "CheckpointLoaderSimple", title: "Load Checkpoint", widgetName: "ckpt_name" },
		diffusion_models: { type: "UNETLoader", title: "Load Diffusion Model", widgetName: "unet_name" },
		loras: { type: "LoraLoader", title: "Load LoRA", widgetName: "lora_name" },
		animatediff_motion_lora: { type: "ADE_AnimateDiffLoRALoader", title: "Load AnimateDiff LoRA", widgetName: "lora_name" },
		vae: { type: "VAELoader", title: "Load VAE", widgetName: "vae_name" },
		vae_approx: { type: "VAELoader", title: "Load VAE", widgetName: "vae_name" },
		controlnet: { type: "ControlNetLoader", title: "Load ControlNet Model", widgetName: "control_net_name" },
		clip_vision: { type: "CLIPVisionLoader", title: "Load CLIP Vision", widgetName: "clip_name" },
	};

	const fallbackSpec = fallbackSpecs[normalizedDirectory] || null;
	return providerSpec.type ? providerSpec : fallbackSpec;
}

export function createGraphNodeFromSpec(bridge, nodeSpec) {
	const nodeType = nodeSpec?.type;
	if (!nodeType) {
		return null;
	}

	const liteGraph = window.LiteGraph || globalThis.LiteGraph;
	if (liteGraph?.createNode) {
		const node = liteGraph.createNode(nodeType);
		if (node) {
			return node;
		}
	}

	if (app.canvas?.graph?.createNode) {
		const node = app.canvas.graph.createNode(nodeType);
		if (node) {
			return node;
		}
	}

	return null;
}

export function getNextModelNodePosition(bridge) {
	return bridge.getRandomCentralCanvasPosition(0.3);
}

export function clampModelNodePositionToSafeArea(bridge, pos, safeAreaRatio = 0.5) {
	try {
		const canvasEl = document.getElementById("graph-canvas") || document.querySelector("canvas");
		const rect = canvasEl?.getBoundingClientRect?.();
		const ds = app.canvas?.ds;
		if (!rect || !ds?.offset || !Number.isFinite(ds?.scale)) {
			return pos;
		}

		const ratio = Math.min(Math.max(Number(safeAreaRatio) || 0.5, 0.1), 1);
		const insetRatio = (1 - ratio) / 2;
		const scale = Math.max(Number(ds.scale || 1), 0.001);
		const safeLeftClient = rect.width * insetRatio;
		const safeRightClient = rect.width * (1 - insetRatio);
		const safeTopClient = rect.height * insetRatio;
		const safeBottomClient = rect.height * (1 - insetRatio);
		const minGraphX = (safeLeftClient - Number(ds.offset[0] || 0)) / scale;
		const maxGraphX = (safeRightClient - Number(ds.offset[0] || 0)) / scale;
		const minGraphY = (safeTopClient - Number(ds.offset[1] || 0)) / scale;
		const maxGraphY = (safeBottomClient - Number(ds.offset[1] || 0)) / scale;

		return [
			Math.min(Math.max(Number(pos?.[0] || 0), minGraphX), maxGraphX),
			Math.min(Math.max(Number(pos?.[1] || 0), minGraphY), maxGraphY),
		];
	} catch (error) {
		return pos;
	}
}

export function jitterModelNodePosition(bridge, pos, safeAreaRatio = null) {
	const jitter = 42;
	const jittered = [
		pos[0] + (Math.random() - 0.5) * jitter,
		pos[1] + (Math.random() - 0.5) * jitter,
	];

	return Number.isFinite(safeAreaRatio)
		? bridge.clampModelNodePositionToSafeArea(jittered, safeAreaRatio)
		: jittered;
}

export function describeProvider(bridge, provider) {
	if (!provider) {
		return null;
	}

	return {
		type: provider.constructor?.name || typeof provider,
		key: provider.key,
		nodeDefName: provider.nodeDef?.name,
		nodeDefDisplayName: provider.nodeDef?.display_name,
		keys: bridge.getObjectPreviewKeys(provider),
		nodeDefKeys: bridge.getObjectPreviewKeys(provider.nodeDef),
	};
}

export function probeModelProvider(bridge, directory = "checkpoints") {
	const provider = bridge.getModelNodeProvider(directory);
	const graph = app.canvas?.graph;
	const summary = {
		directory,
		provider: bridge.describeProvider(provider),
		hasLiteGraph: !!(window.LiteGraph || globalThis.LiteGraph),
		graphHasAdd: typeof graph?.add === "function",
		graphHasCreateNode: typeof graph?.createNode === "function",
		nodeDefStoreHasNode: !!bridge.discoveredPinia?._s?.get?.("nodeDef")?.nodeDefsByName?.[provider?.nodeDef?.name],
	};

	window.__nexusLastModelProviderProbe = summary;
	return summary;
}

export function describeGraphNode(bridge, node) {
	if (!node) {
		return null;
	}

	return {
		title: node.title,
		comfyClass: node.comfyClass,
		type: node.type,
		widgets: Array.isArray(node.widgets)
			? node.widgets.map((widget) => widget?.name || widget?.label || "")
			: [],
	};
}

export function getModelWidgetPredicates(bridge, modelDirectory) {
	const containsAny = (text, parts) => parts.some((part) => text.includes(part));
	const byNameParts = (...parts) => (widgetName, nodeName) =>
		containsAny(widgetName, parts) || containsAny(nodeName, parts);

	const normalizedDirectory = String(modelDirectory || "").toLowerCase();

	switch (normalizedDirectory) {
		case "checkpoints":
		case "diffusion_models":
			return [
				byNameParts("ckpt_name", "checkpoint", "checkpoint_loader", "model_loader"),
				byNameParts("model", "unet_name"),
			];
		case "loras":
		case "animatediff_motion_lora":
			return [
				byNameParts("lora_name", "lora"),
				byNameParts("model"),
			];
		case "vae":
		case "vae_approx":
			return [
				byNameParts("vae_name", "vae"),
			];
		case "controlnet":
			return [
				byNameParts("control_net_name", "controlnet_model", "controlnet"),
			];
		case "clip_vision":
			return [
				byNameParts("clip_name", "clip_vision"),
			];
		case "text_encoders":
			return [
				byNameParts("text_encoder", "clip_name", "encoder"),
			];
		default:
			return [
				byNameParts(normalizedDirectory),
				byNameParts("model"),
			];
	}
}

export function findMatchingWidget(bridge, node, predicates) {
	if (!node?.widgets || !Array.isArray(node.widgets)) {
		return null;
	}

	const nodeName = String(node.comfyClass || node.type || "").toLowerCase();
	const widgets = node.widgets;

	for (const predicate of predicates) {
		const widget = widgets.find((candidate) => {
			const widgetName = String(candidate?.name || candidate?.label || "").toLowerCase();
			return widgetName && predicate(widgetName, nodeName);
		});

		if (widget) {
			return widget;
		}
	}

	return null;
}

export function debugModel(bridge, stage, payload) {
	if (!bridge.modelDebugEnabled) {
		return;
	}

	let payloadText = "";
	try {
		payloadText = JSON.stringify(payload);
	} catch (error) {
		payloadText = `[unserializable payload: ${error?.message || String(error)}]`;
	}

	try {
		bridge.log(`[MODEL_DEBUG] ${stage} :: ${payloadText}`);
	} catch (error) {}
}
