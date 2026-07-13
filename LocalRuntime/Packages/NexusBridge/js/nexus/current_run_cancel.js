const JOBS_URL = "/api/jobs?status=in_progress,pending&limit=200&offset=0";

export async function getJobsRaw() {
	const response = await fetch(JOBS_URL, {
		method: "GET",
		credentials: "same-origin",
	});

	if (!response.ok) {
		throw new Error(`GET ${JOBS_URL} failed: ${response.status}`);
	}

	return await response.json();
}

function normalizeJobs(data) {
	if (Array.isArray(data)) {
		return data;
	}

	return data?.jobs ||
		data?.items ||
		data?.data ||
		data?.results ||
		[];
}

function getPromptIdFromJob(job) {
	return job?.prompt_id ||
		job?.promptId ||
		job?.id ||
		job?.prompt?.id ||
		job?.prompt?.prompt_id ||
		null;
}

export async function getCurrentJob() {
	const data = await getJobsRaw();
	const jobs = normalizeJobs(data);

	return jobs.find((job) =>
		["in_progress", "running", "executing"].includes(
			String(job?.status || "").toLowerCase()
		)
	) || jobs[0] || null;
}

export async function getCurrentPromptId() {
	const job = await getCurrentJob();
	return getPromptIdFromJob(job);
}

export async function isCurrentRunCancelEnabled() {
	return Boolean(await getCurrentPromptId());
}

export async function isCurrentRunCancelDisabled() {
	return !(await isCurrentRunCancelEnabled());
}

export async function cancelCurrentRun() {
	const promptId = await getCurrentPromptId();

	if (!promptId) {
		return false;
	}

	const response = await fetch("/api/interrupt", {
		method: "POST",
		credentials: "same-origin",
		headers: {
			"Content-Type": "application/json",
		},
		body: JSON.stringify({
			prompt_id: promptId,
		}),
	});

	if (!response.ok) {
		throw new Error(`POST /api/interrupt failed: ${response.status}`);
	}

	return true;
}

// Debug helpers kept here intentionally:
// - getJobsRaw()
// - getCurrentJob()
// - getCurrentPromptId()
// - isCurrentRunCancelEnabled()
// - isCurrentRunCancelDisabled()
