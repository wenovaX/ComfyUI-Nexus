import { applyHidePolicy, applyRelocatePolicy, applyStripPolicy, protectElementPath } from "./policy.js";
import { getRulesByPolicy } from "./registry.js";
import { scanSources } from "./sources.js";

function queryRuleTargets(rule) {
	return (rule.selectors || []).flatMap((selector) => {
		try {
			return Array.from(document.querySelectorAll(selector));
		} catch (error) {
			return [];
		}
	});
}

function createSlot(rule, policy, targets) {
	return {
		id: rule.id,
		policy,
		optional: Boolean(rule.optional),
		found: targets.length > 0,
	};
}

function summarizeScan(counts, slots) {
	const missing = slots.filter((slot) => !slot.optional && !slot.found);
	return {
		counts,
		slots,
		complete: missing.length === 0,
		missing,
	};
}

function reportScannerState(bridge, state, message) {
	if (bridge.exileScannerState === state && bridge.exileScannerMessage === message) {
		return;
	}

	bridge.exileScannerState = state;
	bridge.exileScannerMessage = message;
	bridge.log?.(`[Exile] ${message}`);
}

export function scanExileTargets() {
	if (document.documentElement.classList.contains("nexus-exile-disabled")) {
		return {
			counts: { protect: 0, hide: 0, strip: 0, reserve: 0, rehost: 0, relocate: 0 },
			slots: [],
			complete: true,
			missing: [],
			disabled: true,
		};
	}

	const counts = { protect: 0, hide: 0, strip: 0, reserve: 0, rehost: 0, relocate: 0 };
	const slots = [];

	for (const rule of getRulesByPolicy("protect")) {
		const targets = queryRuleTargets(rule);
		slots.push(createSlot(rule, "protect", targets));
		for (const el of targets) {
			protectElementPath(el, rule.id);
			counts.protect += 1;
		}
	}

	for (const rule of getRulesByPolicy("hide")) {
		const targets = queryRuleTargets(rule);
		slots.push(createSlot(rule, "hide", targets));
		for (const el of targets) {
			if (applyHidePolicy(el, rule)) counts.hide += 1;
		}
	}

	for (const rule of getRulesByPolicy("strip")) {
		const targets = queryRuleTargets(rule);
		slots.push(createSlot(rule, "strip", targets));
		for (const el of targets) {
			if (applyStripPolicy(el, rule)) counts.strip += 1;
		}
	}

	for (const source of scanSources()) {
		slots.push({
			id: `${source.ruleId}:${source.key}`,
			policy: source.policy,
			optional: Boolean(source.optional),
			found: source.found,
		});
		if (source.found && source.policy in counts) counts[source.policy] += 1;
	}

	for (const rule of getRulesByPolicy("relocate")) {
		const targets = queryRuleTargets(rule);
		slots.push(createSlot(rule, "relocate", targets));
		for (const el of targets) {
			if (applyRelocatePolicy(el, rule)) counts.relocate += 1;
		}
	}

	return summarizeScan(counts, slots);
}

export function startExileScanner(bridge, scan = scanExileTargets) {
	let scanQueued = false;
	let firstCompleteAt = 0;
	const parkAfterCompleteMs = 2500;

	const run = () => {
		scanQueued = false;
		if (document.documentElement.classList.contains("nexus-exile-disabled")) {
			stopExileScanner(bridge);
			reportScannerState(bridge, "disabled", "UI isolation disabled for debugging.");
			return;
		}

		bridge.lastExileScan = scan();

		if (bridge.lastExileScan?.complete) {
			if (!firstCompleteAt) {
				firstCompleteAt = Date.now();
				reportScannerState(bridge, "settling", `All required slots found. Settling for ${parkAfterCompleteMs}ms.`);
				scheduleRun(parkAfterCompleteMs);
				return;
			}

			if (Date.now() - firstCompleteAt >= parkAfterCompleteMs) {
				stopExileScanner(bridge);
				reportScannerState(bridge, "parked", "All required slots stable. Scanner parked.");
			}
			return;
		}

		firstCompleteAt = 0;
		const missing = bridge.lastExileScan?.missing?.map((slot) => slot.id).join(", ") || "unknown";
		reportScannerState(bridge, "watching", `Waiting for required slots: ${missing}`);
	};

	const scheduleRun = (delay = 80) => {
		if (scanQueued) return;
		scanQueued = true;
		window.setTimeout(run, delay);
	};

	run();

	if (bridge.exileScanTimer) {
		clearInterval(bridge.exileScanTimer);
	}
	bridge.exileScanTimer = setInterval(scheduleRun, 2000);

	if (bridge.exileObserver) {
		bridge.exileObserver.disconnect();
	}
	bridge.exileObserver = new MutationObserver((mutations) => {
		if (mutations.every((mutation) => mutation.type === "attributes" && mutation.attributeName?.startsWith("data-nexus-exile"))) {
			return;
		}

		scheduleRun();
	});
	bridge.exileObserver.observe(document.documentElement, { childList: true, subtree: true });

	return run;
}

export function stopExileScanner(bridge) {
	if (bridge.exileScanTimer) {
		clearInterval(bridge.exileScanTimer);
		bridge.exileScanTimer = null;
	}

	if (bridge.exileObserver) {
		bridge.exileObserver.disconnect();
		bridge.exileObserver = null;
	}
}
