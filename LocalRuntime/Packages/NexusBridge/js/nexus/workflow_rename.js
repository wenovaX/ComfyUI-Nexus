export async function renameWorkflowByStore(payload = {}) {
	const oldPath = String(payload.oldPath || "").trim();
	const newPath = String(payload.newPath || "").trim();
	const store = window.comfyAPI?.app?.app?.extensionManager?.workflow;

	if (!oldPath || !newPath) {
		throw new Error("Workflow rename paths are required");
	}

	if (!store?.renameWorkflow) {
		throw new Error("workflowStore.renameWorkflow not found");
	}

	const findWorkflow = () =>
		store.getWorkflowByPath?.(oldPath) ||
		store.workflows?.find?.((candidate) => candidate.path === oldPath) ||
		store.openWorkflows?.find?.((candidate) => candidate.path === oldPath);

	let workflow = findWorkflow();
	if (!workflow && store.syncWorkflows) {
		await store.syncWorkflows();
		workflow = findWorkflow();
	}

	if (!workflow) {
		throw new Error(`workflow not found: ${oldPath}`);
	}

	return await store.renameWorkflow(workflow, newPath);
}
