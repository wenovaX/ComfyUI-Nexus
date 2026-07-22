using System.Text.Json;
using ComfyUI_Nexus.Platform;
using Microsoft.Maui.Controls;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Ui;

internal sealed record BridgeBootProbeResult(
	string Status,
	string ReadyState,
	bool HasShellIdentity,
	bool HasBridge,
	bool HasBootFunction,
	bool HasComfyApp,
	string? Error)
{
	private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
	};

	internal static BridgeBootProbeResult NativeUnavailable { get; } = new(
		"NATIVE_UNAVAILABLE",
		string.Empty,
		HasShellIdentity: false,
		HasBridge: false,
		HasBootFunction: false,
		HasComfyApp: false,
		Error: null);

	internal bool WasBootInvoked
		=> string.Equals(Status, "BOOT_INVOKED", StringComparison.Ordinal);

	internal string ToLogDetail()
	{
		string detail = $"bridge={Status}, readyState={ReadyState}, boot={HasBootFunction}, bridgeObject={HasBridge}, app={HasComfyApp}";
		return string.IsNullOrWhiteSpace(Error)
			? detail
			: $"{detail}, error={Error}";
	}

	internal static BridgeBootProbeResult FromScriptResult(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw) || raw == "null")
		{
			return NativeUnavailable;
		}

		try
		{
			string payload = NexusWebViewScriptResult.UnwrapPayload(raw) ?? raw;
			var result = JsonSerializer.Deserialize<BridgeBootProbeResult>(
				payload,
				CaseInsensitiveJsonOptions);
			return result ?? NativeUnavailable;
		}
		catch
		{
			return new BridgeBootProbeResult(
				"INVALID_PROBE_RESULT",
				string.Empty,
				HasShellIdentity: false,
				HasBridge: false,
				HasBootFunction: false,
				HasComfyApp: false,
				Error: raw);
		}
	}
}

internal static class NexusWebViewScriptResult
{
	internal static string? UnwrapPayload(string raw)
	{
		try
		{
			using var document = JsonDocument.Parse(raw);
			return document.RootElement.ValueKind switch
			{
				JsonValueKind.String => document.RootElement.GetString(),
				JsonValueKind.Null => null,
				_ => document.RootElement.GetRawText(),
			};
		}
		catch (JsonException)
		{
			return raw;
		}
	}

	internal static bool TryReadBoolean(string raw, out bool? value)
	{
		value = null;
		try
		{
			using var document = JsonDocument.Parse(raw);
			switch (document.RootElement.ValueKind)
			{
				case JsonValueKind.True:
					value = true;
					return true;
				case JsonValueKind.False:
					value = false;
					return true;
				case JsonValueKind.Null:
					return true;
				case JsonValueKind.String:
					if (bool.TryParse(document.RootElement.GetString(), out bool parsed))
					{
						value = parsed;
						return true;
					}

					return false;
				default:
					return false;
			}
		}
		catch (JsonException)
		{
			if (bool.TryParse(raw.Trim('"'), out bool parsed))
			{
				value = parsed;
				return true;
			}

			return false;
		}
	}

	internal static bool TryReadString(string raw, out string? value, out JsonValueKind valueKind)
	{
		value = null;
		valueKind = JsonValueKind.Undefined;
		try
		{
			using var document = JsonDocument.Parse(raw);
			valueKind = document.RootElement.ValueKind;
			if (valueKind == JsonValueKind.Null)
			{
				return true;
			}

			if (valueKind != JsonValueKind.String)
			{
				return false;
			}

			value = document.RootElement.GetString();
			return true;
		}
		catch (JsonException)
		{
			value = raw.Trim('"');
			return true;
		}
	}

}

/// <summary>
/// Native-to-WebView command bridge for ComfyUI and the injected Nexus JS agent.
/// </summary>
/// <remarks>
/// Methods in this class should prefer stable JS bridge actions and semantic selectors over broad DOM fallbacks.
/// </remarks>
internal sealed class NexusWebViewBridge
{
	private const string DefaultActionPayloadJson = "{}";
	private const int ScriptSummaryMaxLength = 220;
	private const string NexusBridgeAutoQueueModuleUrl = "/extensions/ComfyUI-NexusBridge/nexus/auto_queue_mode.js";

	private readonly Func<INexusBrowserSurface?> _browserSurfaceAccessor;

	/// <summary>
	/// Creates a bridge around a deferred browser surface accessor.
	/// </summary>
	/// <param name="browserSurfaceAccessor">Returns the current browser surface, or null when it is not ready.</param>
	internal NexusWebViewBridge(Func<INexusBrowserSurface?> browserSurfaceAccessor)
	{
		_browserSurfaceAccessor = browserSurfaceAccessor;
	}

	/// <summary>
	/// Executes raw JavaScript in the active WebView.
	/// </summary>
	/// <param name="script">JavaScript source to execute in the page context.</param>
	internal async Task ExecuteRawScriptAsync(string script)
	{
		var browserSurface = _browserSurfaceAccessor();
		if (browserSurface == null) return;

		NexusLog.Trace($"[NexusWebViewBridge] {SummarizeScript(script)}");

		try
		{
			await EvaluateScriptAsync(browserSurface, script);
		}
		catch (ObjectDisposedException)
		{
		}
		catch (InvalidOperationException)
		{
		}
	}

	/// <summary>
	/// Marks the web page as running inside Nexus shell.
	/// </summary>
	internal Task SetShellIdentityAsync()
		=> ExecuteRawScriptAsync("window.isNexusShell = true;");

	/// <summary>
	/// Starts or re-pings the injected Nexus boot agent and reports the current bridge readiness stage.
	/// </summary>
	internal async Task<BridgeBootProbeResult> BootNexusAsync()
	{
		var browserSurface = _browserSurfaceAccessor();
		if (browserSurface == null)
		{
			return BridgeBootProbeResult.NativeUnavailable;
		}

		const string script = """
			(() => {
				const state = {
					status: "UNKNOWN",
					readyState: document.readyState || "",
					hasShellIdentity: Boolean(window.isNexusShell),
					hasBridge: Boolean(window.__nexusBridge),
					hasBootFunction: typeof window.NexusBoot === "function",
					hasComfyApp: Boolean(window.comfyAPI?.app?.app),
					error: null
				};

				try {
					if (!state.hasBootFunction) {
						state.status = "NO_BOOT_FUNCTION";
						return JSON.stringify(state);
					}

					const result = window.NexusBoot("NEXUS_INIT");
					state.status = result ? "BOOT_INVOKED" : "BOOT_RETURNED_FALSE";
					return JSON.stringify(state);
				} catch (error) {
					state.status = "BOOT_ERROR";
					state.error = String(error?.stack || error?.message || error);
					return JSON.stringify(state);
				}
			})()
			""";

		NexusLog.Trace($"[NexusWebViewBridge] {SummarizeScript(script)}");

		try
		{
			string? raw = await EvaluateScriptAsync(browserSurface, script);
			return BridgeBootProbeResult.FromScriptResult(raw);
		}
		catch (ObjectDisposedException)
		{
			return BridgeBootProbeResult.NativeUnavailable;
		}
		catch (InvalidOperationException)
		{
			return BridgeBootProbeResult.NativeUnavailable;
		}
	}

	/// <summary>
	/// Fetches global subgraph blueprint metadata from ComfyUI and posts it back to native code.
	/// </summary>
	internal Task FetchBlueprintsAsync()
	{
		string globalSubgraphsPath = ComfyApiOptions.GlobalSubgraphsPath;
		string globalSubgraphDetailsPathPrefix = ComfyApiOptions.GlobalSubgraphDetailsPathPrefix;
		string script = $$"""
			(async function() {
				try {
					console.log('[Nexus] Starting blueprint fetch from {{globalSubgraphsPath}}');
					const list = await fetch('{{globalSubgraphsPath}}', {
						cache: 'no-cache',
						headers: { 'Comfy-User': '' }
					}).then(r => r.json()).catch(e => {
						console.error('[Nexus] global_subgraphs fetch failed', e);
						return {};
					});

					const keys = Object.keys(list);
					console.log('[Nexus] Found ' + keys.length + ' global subgraphs');

					const result = [];
					for (const [id, meta] of Object.entries(list)) {
						try {
							const detail = await fetch(`{{globalSubgraphDetailsPathPrefix}}${id}`, {
								cache: 'no-cache',
								headers: { 'Comfy-User': '' }
							}).then(r => r.json());

							const workflow = detail.data ? JSON.parse(detail.data) : null;
							const subgraph = workflow?.definitions?.subgraphs?.[0];

							result.push({
								id,
								name: detail.name || meta.name || subgraph?.name || id,
								category: subgraph?.category || 'Uncategorized',
								source: detail.source || meta.source || null,
								info: detail.info || meta.info || null,
								workflow,
								subgraph
							});
						} catch (e) {
							console.error(`[Nexus] Failed to fetch details for ${id}`, e);
							result.push({
								id,
								name: meta.name || id,
								category: 'Error',
								source: meta.source || null,
								info: meta.info || null,
								error: String(e)
							});
						}
					}
					console.log('[Nexus] Posting BLUEPRINTS_SYNC with ' + result.length + ' items');
					window.__nexusNative?.post?.('BLUEPRINTS_SYNC', result);
				} catch (e) {
					console.error('[Nexus] Critical error in FetchBlueprintsAsync', e);
				}
			})();
		""";
		return ExecuteRawScriptAsync(script);
	}

	internal Task StartMediaAssetJobSyncAsync()
	{
		string script = $$"""
			(() => {
				if (window.NexusMediaAssetJobSync?.started) {
					window.NexusMediaAssetJobSync.snapshot?.('restart');
					return;
				}

				const sync = {
					started: true,
					before: new Set(),
					timer: null,
					intervalMs: 5000,
					limit: 200,
					async listJobs() {
						const response = await fetch(`/api/jobs?status=completed,failed,cancelled&limit=${this.limit}&offset=0`, {
							cache: 'no-store'
						});
						const json = await response.json();
						return (json.jobs || [])
							.map(job => ({
								jobId: String(job.id || ''),
								status: String(job.status || ''),
								filename: job.preview_output?.filename || '',
								subfolder: job.preview_output?.subfolder || '',
								type: job.preview_output?.type || 'output'
							}))
							.filter(job => job.filename && job.status === 'completed');
					},
					post(reason, jobs) {
						window.__nexusNative?.post?.('{{BridgeMessageTypes.MediaAssetsSync}}', {
							reason,
							jobs
						});
					},
					refreshVisibleOutputImages(jobs) {
						const signature = jobs
							.map(job => `${job.jobId}|${job.filename}|${job.subfolder}|${job.type}`)
							.join('\n');
						const token = String(Date.now());
						for (const image of document.querySelectorAll('img[data-testid="main-image"]')) {
							try {
								const url = new URL(image.src, window.location.href);
								if (!url.pathname.endsWith('/api/view') || url.searchParams.get('type') !== 'output') {
									continue;
								}
								if (image.dataset.nexusOutputSignature === signature && url.searchParams.has('nexus_cache_token')) {
									continue;
								}

								url.searchParams.set('nexus_cache_token', token);
								image.dataset.nexusOutputSignature = signature;
								image.src = url.toString();
							} catch {
								// Ignore images with an invalid transient source during ComfyUI rendering.
							}
						}
					},
					refreshVisibleOutputImagesAfterRender(jobs) {
						window.requestAnimationFrame(() =>
							window.requestAnimationFrame(() => this.refreshVisibleOutputImages(jobs)));
					},
					async snapshot(reason = 'snapshot') {
						try {
							const jobs = await this.listJobs();
							this.before = new Set(jobs.map(job => job.jobId));
							this.post(reason, jobs);
							this.refreshVisibleOutputImagesAfterRender(jobs);
							return jobs;
						} catch (error) {
							console.warn('[NexusMediaAssetJobSync] snapshot failed', error);
							return [];
						}
					},
					async tick() {
						try {
							const jobs = await this.listJobs();
							const next = new Set(jobs.map(job => job.jobId));
							const changed = jobs.some(job => !this.before.has(job.jobId)) || next.size !== this.before.size;
							this.before = next;
							if (changed) {
								this.post('diff', jobs);
								this.refreshVisibleOutputImagesAfterRender(jobs);
							}
						} catch (error) {
							console.warn('[NexusMediaAssetJobSync] poll failed', error);
						}
					},
					start() {
						this.snapshot('initial');
						this.timer = window.setInterval(() => this.tick(), this.intervalMs);
					},
					stop() {
						if (this.timer) {
							window.clearInterval(this.timer);
							this.timer = null;
						}
						this.started = false;
					}
				};

				window.NexusMediaAssetJobSync = sync;
				sync.start();
			})();
			""";

		return ExecuteRawScriptAsync(script);
	}

	internal Task RequestMediaAssetJobSnapshotAsync(string reason = "execution-completed")
		=> ExecuteRawScriptAsync($"window.NexusMediaAssetJobSync?.snapshot?.({JsonSerializer.Serialize(reason)});");

	internal Task DeleteMediaAssetJobsAsync(string payloadJson)
		=> InvokeJsonActionAsync(BridgeActions.MediaAssetDeleteHistory, payloadJson);

	internal Task DeleteMediaAssetJobIdsAsync(IReadOnlyList<string> jobIds)
	{
		string payloadJson = JsonSerializer.Serialize(new
		{
			items = jobIds
				.Where(jobId => !string.IsNullOrWhiteSpace(jobId))
				.Distinct(StringComparer.Ordinal)
				.Select(jobId => new { jobId }),
		});

		return DeleteMediaAssetJobsAsync(payloadJson);
	}

	internal Task ClearBeforeUnloadAsync()
		=> ExecuteRawScriptAsync("window.onbeforeunload = null;");

	/// <summary>
	/// Enables or disables non-error browser console mirroring into native diagnostics.
	/// </summary>
	/// <param name="isEnabled">True to mirror log/info/warn output; false keeps only errors mirrored.</param>
	internal Task SetWebLogsEnabledAsync(bool isEnabled)
		=> ExecuteRawScriptAsync($"window._nexusWebLogsEnabled = {isEnabled.ToString().ToLowerInvariant()};");

	internal Task RefreshBookmarksAsync()
		=> InvokeActionAsync(BridgeActions.RefreshBookmarks);

	internal async Task RefreshWorkflowAppDataAsync()
	{
		if (_browserSurfaceAccessor() is null)
		{
			NexusLog.Warning("Workflow app data refresh skipped because the WebView is not ready.");
			return;
		}

		await InvokeJsonActionAsync(BridgeActions.RefreshWorkflowAppData, DefaultActionPayloadJson);
	}

	internal Task<bool> RenameWorkflowByStoreAsync(string oldPath, string newPath)
		=> InvokeJsonActionAsync(
			BridgeActions.RenameWorkflowByStore,
			JsonSerializer.Serialize(new { oldPath, newPath }));

	internal Task<bool> InsertWorkflowFromUserDataAsync(string path)
		=> InvokeJsonActionAsync(
			BridgeActions.InsertWorkflowFromUserData,
			JsonSerializer.Serialize(new { path }));

	internal Task<bool> InsertWorkflowFromJsonAsync(string workflowJson)
		=> InvokeJsonActionAsync(
			BridgeActions.InsertWorkflowFromJson,
			JsonSerializer.Serialize(new { workflowJson }));

	/// <summary>
	/// Animates the web canvas horizontally.
	/// </summary>
	/// <param name="deltaX">Canvas pan delta in browser pixels.</param>
	/// <param name="duration">Animation duration in milliseconds.</param>
	internal Task PanCanvasAsync(double deltaX, int duration)
		=> InvokeActionAsync(BridgeActions.PanCanvasAnimate, $"{{ deltaX: {deltaX}, duration: {duration} }}");

	internal Task InterruptAsync()
		=> InvokeActionAsync(BridgeActions.Interrupt);

	/// <summary>
	/// Clicks the ComfyUI queue button and reads whether the queue settings store entered instant-running mode.
	/// </summary>
	/// <returns>True when the queue button is now a stop button, false when it is not, or null when the button cannot be found.</returns>
	internal async Task<bool?> ToggleInstantRunAsync()
	{
		string script = $$"""
			(async () => {
				const btn = document.querySelector('[data-testid="queue-button"]');
				if (!btn) return null;

				btn.click();
				await new Promise(resolve => requestAnimationFrame(() => resolve()));
				await new Promise(resolve => requestAnimationFrame(() => resolve()));

				const module = await import("{{NexusBridgeAutoQueueModuleUrl}}");
				const state = await module.getAutoQueueState();
				return Boolean(state?.isInstantRunning);
			})();
			""";

		return await ReadBooleanScriptResultAsync(script);
	}

	/// <summary>
	/// Reads whether the queue settings store is in instant-running mode without clicking the queue button.
	/// </summary>
	/// <returns>True for instant-running state, false for other queue modes, or null when the store cannot be read.</returns>
	internal async Task<bool?> GetQueueButtonIsStopAsync()
	{
		string script = $$"""
			(async () => {
				const module = await import("{{NexusBridgeAutoQueueModuleUrl}}");
				const state = await module.getAutoQueueState();
				return Boolean(state?.isInstantRunning);
			})();
			""";

		return await ReadBooleanScriptResultAsync(script);
	}

	private async Task<bool?> ReadBooleanScriptResultAsync(string script)
	{
		var browserSurface = _browserSurfaceAccessor();
		if (browserSurface == null)
		{
			return null;
		}

		string? raw = await EvaluateScriptAsync(browserSurface, script);
		if (string.IsNullOrWhiteSpace(raw) || raw == "null")
		{
			return null;
		}

		return NexusWebViewScriptResult.TryReadBoolean(raw, out bool? result)
			? result
			: null;
	}

	/// <summary>
	/// Queues the active ComfyUI prompt through the JS bridge.
	/// </summary>
	/// <param name="batchCount">Number of times to queue the prompt.</param>
	internal Task QueuePromptAsync(int batchCount)
		=> InvokeActionAsync(BridgeActions.QueuePrompt, $"{{ batchCount: {batchCount} }}");

	internal Task ViewQueueAsync()
		=> InvokeActionAsync(BridgeActions.ViewQueue);

	/// <summary>
	/// Reads whether the web queue panel appears open from stable attributes and classes.
	/// </summary>
	/// <returns>True for open, false for closed, or null when the toggle button cannot be found.</returns>
	internal async Task<bool?> GetViewQueueOpenAsync()
	{
		string script = """
			(() => {
				const btn =
					document.querySelector('button[data-testid="queue-overlay-toggle"]');

				if (!btn) return null;

				const readBoolAttr = (name) => {
					const value = btn.getAttribute(name);
					if (value === "true") return true;
					if (value === "false") return false;
					return null;
				};

				const attrState =
					readBoolAttr("aria-expanded") ??
					readBoolAttr("aria-pressed");
				if (attrState !== null) return attrState;

				const dataState = String(btn.getAttribute("data-state") || "").toLowerCase();
				if (["open", "on", "active"].includes(dataState)) return true;
				if (["closed", "off", "inactive"].includes(dataState)) return false;

				const className = String(btn.className || "");
				if (
					className.includes("side-bar-button-selected") ||
					className.includes("p-highlight") ||
					className.includes("p-togglebutton-checked") ||
					className.includes("bg-primary-background")
				) {
					return true;
				}

				return false;
			})();
			""";

		return await ReadBooleanScriptResultAsync(script);
	}

	/// <summary>
	/// Relays a normalized keyboard shortcut to ComfyUI.
	/// </summary>
	/// <param name="key">Uppercase key name understood by the JS bridge.</param>
	/// <param name="ctrl">Whether Ctrl should be included in the shortcut payload.</param>
	internal Task RelayShortcutAsync(string key, bool ctrl, bool shift = false, bool alt = false)
	{
		string normalizedKey = EscapeJsString(key.ToUpperInvariant());
		return InvokeActionAsync(
			BridgeActions.RelayShortcut,
			$"{{ key: '{normalizedKey}', ctrl: {ctrl.ToString().ToLowerInvariant()}, shift: {shift.ToString().ToLowerInvariant()}, alt: {alt.ToString().ToLowerInvariant()} }}");
	}

	/// <summary>
	/// Primes ComfyUI's keyboard target after native focus hand-offs without invoking a user-facing shortcut.
	/// </summary>
	internal Task AwakeKeyboardRelayAsync()
		=> RelayShortcutAsync("NEXUS_WAKE", ctrl: false);

	/// <summary>
	/// Sets the ComfyUI canvas interaction mode.
	/// </summary>
	/// <param name="mode">Canvas mode name, such as Select or Hand.</param>
	internal Task SetCanvasModeAsync(string mode)
		=> InvokeActionAsync(BridgeActions.SetCanvasMode, JsonSerializer.Serialize(new { mode }));

	internal Task FitViewAsync()
		=> ExecuteRawScriptAsync("""
			(() => {
				const selectors = [
					'button:has(.icon-\\[lucide--focus\\])',
				];
				const button = selectors.map(selector => document.querySelector(selector)).find(Boolean);
				button?.click();
				return Boolean(button);
			})();
			""");

	internal Task OpenZoomControlsAsync()
		=> ExecuteRawScriptAsync("document.querySelector('button[data-testid=\"zoom-controls-button\"]')?.click();");

	internal Task ToggleMinimapAsync()
		=> ExecuteRawScriptAsync("document.querySelector('button[data-testid=\"toggle-minimap-button\"]')?.click();");

	internal Task ToggleLinksAsync()
		=> ExecuteRawScriptAsync("document.querySelector('button[data-testid=\"toggle-link-visibility-button\"]')?.click();");

	/// <summary>
	/// Selects the ComfyUI queue/run mode.
	/// </summary>
	/// <param name="mode">Run mode label from <see cref="RunModeOptions"/>.</param>
	internal Task SetRunModeAsync(string mode)
		=> InvokeActionAsync(BridgeActions.SetRunMode, JsonSerializer.Serialize(new { mode }));

	/// <summary>
	/// Reads the active web run mode from ComfyUI's queue settings store.
	/// </summary>
	/// <returns>Normalized run mode label, or null when it cannot be read.</returns>
	internal async Task<string?> GetRunModeAsync()
	{
		string defaultModeJson = JsonSerializer.Serialize(RunModeOptions.Default);
		string onChangeModeJson = JsonSerializer.Serialize(RunModeOptions.OnChange);
		string instantModeJson = JsonSerializer.Serialize(RunModeOptions.Instant);
		string script = $$"""
			(async () => {
				const module = await import("{{NexusBridgeAutoQueueModuleUrl}}");
				const mode = await module.getAutoQueueMode();

				if (mode === "change") return {{onChangeModeJson}};
				if (mode === "instant-idle" || mode === "instant-running") return {{instantModeJson}};
				if (mode === "disabled") return {{defaultModeJson}};

				return null;
			})()
			""";

		var browserSurface = _browserSurfaceAccessor();
		if (browserSurface == null)
		{
			return null;
		}

		string? raw = await EvaluateScriptAsync(browserSurface, script);
		if (string.IsNullOrWhiteSpace(raw) || raw == "null")
		{
			return null;
		}

		if (NexusWebViewScriptResult.TryReadString(raw, out string? result, out JsonValueKind valueKind))
		{
			return result;
		}

		NexusLog.Trace($"[NexusWebViewBridge] Expected a string run mode result but received {valueKind}.");
		return null;
	}

	/// <summary>
	/// Opens the web Help Center menu aligned against the native rail.
	/// </summary>
	/// <param name="railWidth">Current expanded rail width used as left offset.</param>
	internal Task OpenHelpCenterAsync(double railWidth)
		=> InvokeActionAsync(BridgeActions.HelpCenter, $"{{ railWidth: {railWidth} }}");

	internal Task ToggleBottomPanelAsync()
		=> InvokeActionAsync(BridgeActions.ToggleBottomPanel);

	internal Task ToggleShortcutsAsync()
		=> InvokeActionAsync(BridgeActions.ToggleShortcuts);

	internal Task OpenSettingsAsync()
		=> InvokeActionAsync(BridgeActions.ToggleSettings);

	/// <summary>
	/// Toggles the ComfyUI main menu aligned against the native rail.
	/// </summary>
	/// <param name="railWidth">Current expanded rail width used as left offset.</param>
	internal Task ToggleMainMenuAsync(double railWidth)
		=> InvokeActionAsync(BridgeActions.ToggleMainMenu, JsonSerializer.Serialize(new { railWidth }));

	internal Task CloseMainMenuAsync()
		=> InvokeActionAsync(BridgeActions.CloseMainMenu);

	internal async Task<bool> SwitchWorkflowAsync(int index)
	{
		var browserSurface = _browserSurfaceAccessor();
		if (browserSurface == null)
		{
			return false;
		}

		string script =
			"(async () => { " +
			"try { " +
			"if (!window.NexusAction) { return 'UNAVAILABLE'; } " +
			$"const result = await window.NexusAction?.('{BridgeActions.SwitchWorkflow}', {{ index: {index} }}); " +
			"return result === false ? 'FAILED' : 'OK'; " +
			"} catch (error) { " +
			"return 'ERROR:' + (error?.message || String(error)); " +
			"} " +
			"})()";

		try
		{
			string? result = await EvaluateScriptAsync(browserSurface, script);
			if (!string.IsNullOrWhiteSpace(result) &&
				(result.Contains("UNAVAILABLE", StringComparison.Ordinal) ||
				 result.Contains("FAILED", StringComparison.Ordinal) ||
				 result.Contains("ERROR:", StringComparison.Ordinal)))
			{
				NexusLog.Warning($"Workflow switch bridge action failed: {result}");
				return false;
			}

			return true;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
	}

	internal Task SetRailWidthAsync(double railWidth)
		=> InvokeActionAsync(BridgeActions.SetRailWidth, JsonSerializer.Serialize(new { railWidth }));

	internal Task ToggleAppsAsync()
		=> InvokeActionAsync(BridgeActions.ToggleApps);

	/// <summary>
	/// Sends an asset open request to the web bridge.
	/// </summary>
	/// <param name="request">Asset payload including path/model/node context and optional placement data.</param>
	internal Task NotifyAssetOpenAsync(AssetOpenRequest request)
		=> NotifyAssetInteractionAsync(BridgeActions.AssetBrowserOpen, request);

	/// <summary>
	/// Sends an asset drag-start request so the web side can prepare drag feedback.
	/// </summary>
	/// <param name="request">Asset payload including drag id and source metadata.</param>
	internal Task NotifyAssetDragStartAsync(AssetOpenRequest request)
		=> NotifyAssetInteractionAsync(BridgeActions.AssetBrowserDragStart, request);

	/// <summary>
	/// Shows a lightweight web-side drop feedback effect without changing the asset payload.
	/// </summary>
	/// <param name="request">Asset payload with optional drop client position.</param>
	internal Task NotifyAssetDropFeedbackAsync(AssetOpenRequest request)
		=> NotifyAssetInteractionAsync(BridgeActions.AssetDropFeedback, request);

	/// <summary>
	/// Sends the native rail origin for a later web-side native file drop feedback effect.
	/// </summary>
	/// <param name="request">Asset payload including rail width and display metadata.</param>
	internal Task NotifyAssetDropFeedbackSourceAsync(AssetOpenRequest request)
		=> NotifyAssetInteractionAsync(BridgeActions.AssetDropFeedbackSource, request);

	private Task NotifyAssetInteractionAsync(string action, AssetOpenRequest request)
	{
		string payloadJson = JsonSerializer.Serialize(new
		{
			path = request.FullPath,
			name = request.Name,
			displayName = request.DisplayName,
			extension = request.Extension,
			kind = request.Kind.ToString(),
			mode = request.Mode.ToString(),
			action = request.Action.ToString(),
			sourceRoot = request.SourceRoot,
			modelDirectory = request.ModelDirectory,
			modelPathIndex = request.ModelPathIndex,
			nodeType = request.NodeType,
			dragId = request.DragId,
			dropClientX = request.DropClientX,
			dropClientY = request.DropClientY,
			railWidth = request.RailWidth,
		});

		return InvokeJsonActionAsync(action, payloadJson);
	}

	/// <summary>
	/// Invokes a named Nexus JS bridge action with a JavaScript object payload.
	/// </summary>
	/// <param name="action">Bridge action name from <see cref="BridgeActions"/>.</param>
	/// <param name="payloadJson">Raw JavaScript object literal. Use <see cref="InvokeJsonActionAsync"/> when payload is already serialized JSON.</param>
	internal async Task InvokeActionAsync(string action, string payloadJson = DefaultActionPayloadJson)
	{
		var browserSurface = _browserSurfaceAccessor();
		if (browserSurface == null)
		{
			return;
		}

		string encodedAction = JsonSerializer.Serialize(action);
		string script =
			"(async () => { " +
			"try { " +
			$"const action = {encodedAction}; " +
			$"const payload = ({payloadJson}); " +
			"if (!window.NexusAction) { return 'NO_NEXUS_ACTION:' + action; } " +
			"await window.NexusAction(action, payload); " +
			"return 'OK:' + action; " +
			"} catch (error) { " +
			"console.error('[NexusBridge] action failed', error); " +
			"return 'ERROR:' + (error?.stack || error?.message || String(error)); " +
			"} " +
			"})()";

		NexusLog.Trace($"[NexusWebViewBridge] {SummarizeScript(script)}");

		try
		{
			string? result = await EvaluateScriptAsync(browserSurface, script);
			if (!string.IsNullOrWhiteSpace(result) &&
				(result.Contains("NO_NEXUS_ACTION:", StringComparison.Ordinal) ||
				 result.Contains("ERROR:", StringComparison.Ordinal)))
			{
				NexusLog.Warning($"Bridge action returned {result}");
			}
		}
		catch (ObjectDisposedException)
		{
		}
		catch (InvalidOperationException)
		{
		}
	}

	private async Task<bool> InvokeJsonActionAsync(string action, string payloadJson)
	{
		var browserSurface = _browserSurfaceAccessor();
		if (browserSurface == null)
		{
			return false;
		}

		string encodedAction = JsonSerializer.Serialize(action);
		string encodedPayload = JsonSerializer.Serialize(payloadJson);
		string script =
			"(async () => { " +
			"try { " +
			$"const action = {encodedAction}; " +
			$"const payload = JSON.parse({encodedPayload}); " +
			"if (!window.NexusAction) { return 'NO_NEXUS_ACTION:' + action; } " +
			"await window.NexusAction(action, payload); " +
			"return 'OK:' + action; " +
			"} catch (error) { " +
			"console.error('[NexusBridge] action failed', error); " +
			"return 'ERROR:' + (error?.stack || error?.message || String(error)); " +
			"} " +
			"})()";

		NexusLog.Trace($"[NexusWebViewBridge] {SummarizeScript(script)}");

		try
		{
			string? result = await EvaluateScriptAsync(browserSurface, script);
			if (!string.IsNullOrWhiteSpace(result) &&
				(result.Contains("NO_NEXUS_ACTION:", StringComparison.Ordinal) ||
				 result.Contains("ERROR:", StringComparison.Ordinal)))
			{
				NexusLog.Warning($"Bridge action returned {result}");
				return false;
			}

			return true;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, $"Bridge action failed ({action})");
			throw;
		}
	}

	private static Task<string?> EvaluateScriptAsync(INexusBrowserSurface browserSurface, string script)
	{
		if (MainThread.IsMainThread)
		{
			return browserSurface.ExecuteScriptAsync(script);
		}

		return MainThread.InvokeOnMainThreadAsync(() => browserSurface.ExecuteScriptAsync(script));
	}

	private static string SummarizeScript(string script)
	{
		string compact = script.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
		while (compact.Contains("  ", StringComparison.Ordinal))
		{
			compact = compact.Replace("  ", " ");
		}

		return compact.Length <= ScriptSummaryMaxLength
			? compact
			: $"{compact[..ScriptSummaryMaxLength]}... ({script.Length} chars)";
	}

	private static string EscapeJsString(string value)
		=> value.Replace("\\", "\\\\").Replace("'", "\\'");
}
