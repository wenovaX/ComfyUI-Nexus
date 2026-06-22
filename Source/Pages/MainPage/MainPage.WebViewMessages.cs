using System.Text.Json;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using Microsoft.Maui.Controls;

using ComfyUI_Nexus.Views;
using ComfyUI_Nexus.Views.Overlays;
using ComfyUI_Nexus.Views.Rail.Tools.MediaAssets;
using ComfyUI_Nexus.Views.Rail.Tools.NodeLibrary;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private const int BridgeDiagnosticsSummaryIntervalSeconds = 2;
	private const int BridgeJsLogThrottleMs = 500;
	private const int ControlDeckPulseMinIntervalMs = 750;
	private const int WebIgnoredErrorThrottleMs = 5000;
	private const int WebTransientConsoleThrottleMs = 5000;
	private static readonly int[] MediaAssetSnapshotBurstOffsetsMs = [0, 500, 1500, 3000];

	private readonly object _bridgeDiagnosticsGate = new();
	private readonly Dictionary<string, int> _bridgeMessageCounts = new();
	private int _workflowSyncAppliedCount = 0;
	private int _workflowSyncSkippedCount = 0;
	private DateTime _lastBridgeSummaryUtc = DateTime.UtcNow;
	private DateTime _lastControlDeckWebPulseUtc = DateTime.MinValue;
	private int _webPulseErrorCount;
	private bool _pulseIsRunning;
	private bool _pulseInstantStop;
	private string _lastAppliedCursor = CssCursorNames.Default;

	private async Task ProcessMessage(string raw)
	{
		try
		{
			using var doc = JsonDocument.Parse(raw);
			var root = doc.RootElement;
			string type = root.GetProperty("type").GetString() ?? string.Empty;
			var data = root.GetProperty("data").Clone();

			RefreshControlDeckWebPulse(type != BridgeMessageTypes.Heartbeat);
			if (_bridgeDiagnosticsEnabled) RecordBridgeMessage(type);
			await DispatchBridgeMessageAsync(type, data);
			if (_bridgeDiagnosticsEnabled) FlushBridgeDiagnosticsIfDue();
		}
		catch (Exception ex)
		{
			Log($"ERROR: {ex.Message}");
		}
	}

	private Task DispatchBridgeMessageAsync(string type, JsonElement data)
	{
		return type switch
		{
			BridgeMessageTypes.Heartbeat => Task.CompletedTask,
			BridgeMessageTypes.JsLog => HandleJsLogAsync(data),
			BridgeMessageTypes.WebConsole => HandleWebConsoleAsync(data),
			BridgeMessageTypes.WebError => HandleWebErrorAsync(data),
			BridgeMessageTypes.BootReady => HandleBootReadyAsync(data),
			BridgeMessageTypes.RefreshRequest => HandleRefreshRequestAsync(),
			BridgeMessageTypes.WorkflowSync => HandleWorkflowSyncAsync(data),
			BridgeMessageTypes.FocusChange => HandleFocusChangeAsync(data),
			BridgeMessageTypes.GpuStats => HandleGpuStatsAsync(data),
			BridgeMessageTypes.ModeUpdate => HandleModeUpdateAsync(data),
			BridgeMessageTypes.QueueUpdate => HandleQueueUpdateAsync(data),
			BridgeMessageTypes.ExecutionStateSync => HandleExecutionStateSyncAsync(data),
			BridgeMessageTypes.QueueButtonStateSync => HandleQueueButtonStateSyncAsync(data),
			BridgeMessageTypes.BatchCountSync => HandleBatchCountSyncAsync(data),
			BridgeMessageTypes.CursorChange => HandleCursorChangeAsync(data),
			BridgeMessageTypes.UiStateUpdate => HandleUiStateUpdateAsync(data),
			BridgeMessageTypes.BlueprintsSync => HandleBlueprintsSyncAsync(data),
			BridgeMessageTypes.MediaAssetsSync => HandleMediaAssetsSyncAsync(data),
			_ => Task.CompletedTask
		};
	}

	private Task HandleMediaAssetsSyncAsync(JsonElement data)
	{
		var jobs = ParseMediaAssetJobs(data);
		MainThread.BeginInvokeOnMainThread(() =>
		{
			RailControl?.SyncMediaAssetsFromJobs(jobs);
		});
		return Task.CompletedTask;
	}

	private static List<MediaAssetJobPreview> ParseMediaAssetJobs(JsonElement data)
	{
		var jobs = new List<MediaAssetJobPreview>();
		if (!data.TryGetProperty("jobs", out var jobsElement) || jobsElement.ValueKind != JsonValueKind.Array)
		{
			return jobs;
		}

		foreach (var item in jobsElement.EnumerateArray())
		{
			string filename = GetString(item, "filename");
			if (string.IsNullOrWhiteSpace(filename))
			{
				continue;
			}

			string type = GetString(item, "type");
			jobs.Add(new MediaAssetJobPreview(
				GetString(item, "jobId"),
				GetString(item, "status"),
				filename,
				GetString(item, "subfolder"),
				string.IsNullOrWhiteSpace(type) ? "output" : type));
		}

		return jobs;
	}

	private static string GetString(JsonElement item, string propertyName)
		=> item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString() ?? string.Empty
			: string.Empty;

	private Task HandleUiStateUpdateAsync(JsonElement data)
	{
		ApplyUiStateFlag(data, "terminalOpen", ToolbarControl.SetTerminalActive);
		ApplyUiStateFlag(data, "shortcutsOpen", ToolbarControl.SetShortcutsActive);
		ApplyUiStateFlag(data, "minimapOpen", ToolbarControl.SetMinimapActive);
		ApplyUiStateFlag(data, "propertiesOpen", HeaderControl.SetPropertiesActive);
		ApplyUiStateFlag(data, "appsOpen", RailControl.SetAppsSelected);
		ApplyUiStateFlag(data, "settingsOpen", RailControl.SetSettingsSelected);
		ApplyUiStateFlag(data, "templatesOpen", RailControl.SetTemplatesSelected);
		ApplyUiStateFlag(data, "jobsOpen", HeaderControl.SetViewQueueActive);
		return Task.CompletedTask;
	}

	private static void ApplyUiStateFlag(JsonElement data, string propertyName, Action<bool> apply)
	{
		if (!data.TryGetProperty(propertyName, out var property))
		{
			return;
		}

		bool isOpen = property.GetBoolean();
		MainThread.BeginInvokeOnMainThread(() => apply(isOpen));
	}

	private async Task HandleBlueprintsSyncAsync(JsonElement data)
	{
		try
		{
			var blueprints = await Task.Run(() => ParseBlueprintItems(data));

			NexusLog.Trace($"[BLUEPRINT] Parsed {blueprints.Count} blueprint items.");
			_latestBlueprints = blueprints;
			await MainThread.InvokeOnMainThreadAsync(() =>
			{
				if (_nodeLibrary == null)
				{
					return;
				}

				RailControl.UpdateBlueprints(blueprints);
			});
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"[BLUEPRINT] Failed to parse blueprints: {ex.Message}");
		}
	}

	private static List<BlueprintItem> ParseBlueprintItems(JsonElement data)
	{
		var items = new List<BlueprintItem>();
		if (data.ValueKind != JsonValueKind.Array)
		{
			return items;
		}

		int fallbackIndex = 0;
		foreach (JsonElement item in data.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			string id = GetTextProperty(item, "id");
			if (string.IsNullOrWhiteSpace(id))
			{
				id = $"blueprint-{fallbackIndex++}";
			}

			string name = GetTextProperty(item, "name");
			if (string.IsNullOrWhiteSpace(name))
			{
				name = id;
			}

			string category = GetTextProperty(item, "category");
			if (string.IsNullOrWhiteSpace(category))
			{
				category = "Uncategorized";
			}

			items.Add(new BlueprintItem
			{
				Id = id,
				Name = name,
				Category = category,
				Source = CloneProperty(item, "source"),
				Info = CloneProperty(item, "info"),
				Error = GetTextProperty(item, "error"),
				Workflow = CloneProperty(item, "workflow"),
				Subgraph = CloneProperty(item, "subgraph")
			});
		}

		return items;
	}

	private static string GetTextProperty(JsonElement item, string name)
	{
		if (!item.TryGetProperty(name, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
		{
			return string.Empty;
		}

		return value.ValueKind == JsonValueKind.String
			? value.GetString() ?? string.Empty
			: value.GetRawText();
	}

	private static JsonElement? CloneProperty(JsonElement item, string name)
	{
		if (!item.TryGetProperty(name, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
		{
			return null;
		}

		return value.Clone();
	}

	private Task HandleModeUpdateAsync(JsonElement data)
	{
		string mode = data.GetProperty("mode").GetString() ?? CanvasModeOptions.Unknown;
		MainThread.BeginInvokeOnMainThread(() =>
		{
			ToolbarControl.SetMode(mode == CanvasModeOptions.HandUpper);
		});
		return Task.CompletedTask;
	}

	private Task HandleQueueUpdateAsync(JsonElement data)
	{
		int count = data.GetProperty("count").GetInt32();
		MainThread.BeginInvokeOnMainThread(() =>
		{
			HeaderControl.SetQueueCount(count);
		});
		return Task.CompletedTask;
	}

	private Task HandleJsLogAsync(JsonElement data)
	{
		string message = data.GetString() ?? string.Empty;
		if (_bridgeDiagnosticsEnabled)
		{
			LogThrottled("bridge-js-log", $"[JS] {message}", BridgeJsLogThrottleMs);
		}
		return Task.CompletedTask;
	}

	private Task HandleWebErrorAsync(JsonElement data)
	{
		string kind = data.TryGetProperty("kind", out var kindProp) ? kindProp.GetString() ?? "error" : "error";
		string message = data.TryGetProperty("message", out var messageProp) ? messageProp.GetString() ?? string.Empty : data.ToString();
		string source = data.TryGetProperty("source", out var sourceProp) ? sourceProp.GetString() ?? string.Empty : string.Empty;
		int line = data.TryGetProperty("line", out var lineProp) && lineProp.ValueKind == JsonValueKind.Number ? lineProp.GetInt32() : 0;
		int column = data.TryGetProperty("column", out var columnProp) && columnProp.ValueKind == JsonValueKind.Number ? columnProp.GetInt32() : 0;
		string stack = data.TryGetProperty("stack", out var stackProp) ? stackProp.GetString() ?? string.Empty : string.Empty;

		if (IsIgnorableWebError(message, source, stack))
		{
			LogThrottled("web-ignored-error", $"[WEB:IGNORED] {message}", WebIgnoredErrorThrottleMs);
			return Task.CompletedTask;
		}

		_webPulseErrorCount = Math.Min(99, _webPulseErrorCount + 1);
		RefreshControlDeckWebPulse(force: true);

		string location = string.IsNullOrWhiteSpace(source) ? string.Empty : $" @ {source}:{line}:{column}";
		Log($"[WEB:ERROR] {kind}: {message}{location}");
		if (!string.IsNullOrWhiteSpace(stack))
		{
			Log($"[WEB:STACK] {stack}");
		}

		return Task.CompletedTask;
	}

	private Task HandleWebConsoleAsync(JsonElement data)
	{
		string raw = data.GetString() ?? string.Empty;
		int sepIdx = raw.IndexOf('|');
		if (sepIdx > 0)
		{
			string level = raw.Substring(0, sepIdx);
			string msg = raw.Substring(sepIdx + 1);
			if (IsTransientWebConsoleNoise(msg))
			{
				LogThrottled("web-transient-console", $"[WEB:{level}] {msg}", WebTransientConsoleThrottleMs);
				return Task.CompletedTask;
			}

			Log($"[WEB:{level}] {msg}");
		}
		else
		{
			Log($"[WEB] {raw}");
		}
		return Task.CompletedTask;
	}

	private static bool IsIgnorableWebError(string message, string source, string stack)
	{
		if (message.Contains("ResizeObserver loop completed with undelivered notifications", StringComparison.OrdinalIgnoreCase) ||
			message.Contains("ResizeObserver loop limit exceeded", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return string.Equals(message, "Unknown script error", StringComparison.OrdinalIgnoreCase)
			&& string.IsNullOrWhiteSpace(source)
			&& string.IsNullOrWhiteSpace(stack);
	}

	private static bool IsTransientWebConsoleNoise(string message)
		=> message.Contains("ComfyApp graph accessed before initialization", StringComparison.OrdinalIgnoreCase);

	private Task HandleBootReadyAsync(JsonElement data)
	{
		if (_bootReadyHandled)
		{
			_ = _webViewBridge.StartMediaAssetJobSyncAsync();
			return Task.CompletedTask;
		}

		_bootReadyHandled = true;
		_isBooted = true;
		string agentId = data.TryGetProperty("agentId", out var agentProp) ? agentProp.GetString() ?? string.Empty : string.Empty;
		Log($"SUCCESS: Agent {agentId} Synchronized.");
		_loginSequence.NotifyBootReady(agentId);
		_ = _webViewBridge.StartMediaAssetJobSyncAsync();

		return Task.CompletedTask;
	}

	private async Task ShowBootWelcomeAsync()
	{
		_loadingOverlayController.Message(
			LocalizationManager.Text("loading.bridge_online_title"),
			LocalizationManager.Text("loading.bridge_online_detail"),
			LocalizationManager.Text("loading.bridge_online_status"),
			LoadingSuccessColor,
			progress: 0.86);
		await Task.Delay(220);
	}

	private async Task ShowBootStandByAsync()
	{
		_loadingOverlayController.Hold(
			LocalizationManager.Text("loading.finalizing_title"),
			LocalizationManager.Text("loading.finalizing_detail"),
			LocalizationManager.Text("loading.finalizing_status"),
			LoadingInfoColor,
			progress: 0.94);
		await Task.Delay(180);
	}

	private async Task RunBootSuccessSequenceAsync()
	{
		await TriggerSuccessSequence();
	}

	private Task HandleRefreshRequestAsync()
	{
		if (_isRebooting) return Task.CompletedTask;
		MainThread.BeginInvokeOnMainThread(() =>
		{
			_ = PerformSystemReboot();
		});
		return Task.CompletedTask;
	}

	private async Task HandleWorkflowSyncAsync(JsonElement data)
	{
		await MainThread.InvokeOnMainThreadAsync(() =>
		{
			bool applied = _tabController.TryApplyWorkflowSync(data);
			RecordWorkflowSync(applied);
		});
	}

	private Task HandleFocusChangeAsync(JsonElement data)
	{
		bool isFocused = data.GetBoolean();
		MainThread.BeginInvokeOnMainThread(() =>
		{
			try
			{
				WorkspaceControl.SetFocusState(isFocused);
				SetShellFocusSignatureState(isFocused);
			}
			catch
			{
			}
		});

		return Task.CompletedTask;
	}

	private void SetShellFocusSignatureState(bool isFocused)
	{
		const string focusBreathAnimationName = "ShellFocusSignatureBreath";
		this.AbortAnimation(focusBreathAnimationName);

		if (!isFocused)
		{
			FocusIndicatorBox.ScaleY = 1;
			_ = FocusIndicatorBox.FadeToAsync(0, 180, Easing.CubicOut);
			return;
		}

		FocusIndicatorBox.Opacity = 0.86;
		FocusIndicatorBox.ScaleY = 1;
		var breath = new Animation(progress =>
		{
			double pulse = Math.Sin(progress * Math.PI);
			FocusIndicatorBox.Opacity = 0.72 + (pulse * 0.28);
			FocusIndicatorBox.ScaleY = 1 + (pulse * 0.35);
		});
		breath.Commit(
			this,
			focusBreathAnimationName,
			rate: 16,
			length: 1800,
			easing: Easing.Linear,
			repeat: () => true);
	}

	private Task HandleCursorChangeAsync(JsonElement data)
	{
		if (_isAssetDragActive)
		{
			return Task.CompletedTask;
		}

		string cssCursor = data.GetString() ?? CssCursorNames.Default;
		if (cssCursor == _lastAppliedCursor)
		{
			return Task.CompletedTask;
		}

		_lastAppliedCursor = cssCursor;

		MainThread.BeginInvokeOnMainThread(() =>
		{
			PlatformManager.Current.Cursor.SetCssCursor(WorkspaceControl.BrowserView, cssCursor);
		});

		return Task.CompletedTask;
	}

	private void RecordBridgeMessage(string type)
	{
		lock (_bridgeDiagnosticsGate)
		{
			if (_bridgeMessageCounts.TryGetValue(type, out int count))
			{
				_bridgeMessageCounts[type] = count + 1;
			}
			else
			{
				_bridgeMessageCounts[type] = 1;
			}
		}
	}

	private void RecordWorkflowSync(bool applied)
	{
		lock (_bridgeDiagnosticsGate)
		{
			if (applied)
			{
				_workflowSyncAppliedCount++;
			}
			else
			{
				_workflowSyncSkippedCount++;
			}
		}
	}

	private void FlushBridgeDiagnosticsIfDue()
	{
		List<string> parts;
		lock (_bridgeDiagnosticsGate)
		{
			var now = DateTime.UtcNow;
			if ((now - _lastBridgeSummaryUtc).TotalSeconds < BridgeDiagnosticsSummaryIntervalSeconds)
			{
				return;
			}

			_lastBridgeSummaryUtc = now;

			int totalMessages = _bridgeMessageCounts.Values.Sum();
			if (totalMessages == 0 && _workflowSyncAppliedCount == 0 && _workflowSyncSkippedCount == 0)
			{
				return;
			}

			if (_bridgeMessageCounts.Count == 1 &&
				_bridgeMessageCounts.TryGetValue(BridgeMessageTypes.Heartbeat, out int heartbeatOnlyCount) &&
				heartbeatOnlyCount > 0 &&
				_workflowSyncAppliedCount == 0 &&
				_workflowSyncSkippedCount == 0)
			{
				_bridgeMessageCounts.Clear();
				return;
			}

			parts = new List<string>();
			foreach (var pair in _bridgeMessageCounts.OrderByDescending(x => x.Value))
			{
				if (pair.Value > 0)
				{
					parts.Add($"{pair.Key}={pair.Value}");
				}
			}

			if (_workflowSyncAppliedCount > 0 || _workflowSyncSkippedCount > 0)
			{
				parts.Add($"sync(applied={_workflowSyncAppliedCount}, skipped={_workflowSyncSkippedCount})");
			}

			_bridgeMessageCounts.Clear();
			_workflowSyncAppliedCount = 0;
			_workflowSyncSkippedCount = 0;
		}

		Log($"[BRIDGE] {string.Join(", ", parts)}");
	}

	private void RefreshBridgeDiagnosticsButtonState()
	{
		ControlDeckControl.SetBridgeDiagnosticsState(_bridgeDiagnosticsEnabled);
	}

	private void SetBridgeDiagnosticsEnabled(bool isEnabled)
	{
		_bridgeDiagnosticsEnabled = isEnabled;
		RefreshBridgeDiagnosticsButtonState();
		RefreshControlDeckWebPulse(force: true);

		lock (_bridgeDiagnosticsGate)
		{
			_bridgeMessageCounts.Clear();
			_workflowSyncAppliedCount = 0;
			_workflowSyncSkippedCount = 0;
			_lastBridgeSummaryUtc = DateTime.UtcNow;
		}

		Log(_bridgeDiagnosticsEnabled ? "Bridge diagnostics enabled." : "Bridge diagnostics disabled.");
	}

	private void OnToggleBridgeDiagnosticsClicked(object? sender, EventArgs e)
	{
		SetBridgeDiagnosticsEnabled(!_bridgeDiagnosticsEnabled);
	}

	private Task HandleExecutionStateSyncAsync(JsonElement data)
	{
		bool isRunning = data.GetProperty("isRunning").GetBoolean();
		_pulseIsRunning = isRunning;
		MainThread.BeginInvokeOnMainThread(() =>
		{
			HeaderControl.SetExecutionState(isRunning);
			ControlDeckControl.SetPulseRun(isRunning, _pulseInstantStop);
		});

		if (string.Equals(_currentRunMode, RunModeOptions.Instant, StringComparison.OrdinalIgnoreCase))
		{
			_ = SyncInstantQueueButtonVisualAsync();
		}

		if (!isRunning)
		{
			QueueMediaAssetJobSnapshotBurst();
		}

		return Task.CompletedTask;
	}

	private void QueueMediaAssetJobSnapshotBurst()
	{
		_mediaAssetSnapshotBurstCts?.Cancel();
		var cts = new CancellationTokenSource();
		_mediaAssetSnapshotBurstCts = cts;
		_ = RunMediaAssetJobSnapshotBurstAsync(cts);
	}

	private async Task RunMediaAssetJobSnapshotBurstAsync(CancellationTokenSource cancellationTokenSource)
	{
		var cancellationToken = cancellationTokenSource.Token;
		try
		{
			int previousOffsetMs = 0;
			foreach (int offsetMs in MediaAssetSnapshotBurstOffsetsMs)
			{
				int delayMs = offsetMs - previousOffsetMs;
				if (delayMs > 0)
				{
					await Task.Delay(delayMs, cancellationToken);
				}

				previousOffsetMs = offsetMs;
				cancellationToken.ThrowIfCancellationRequested();
				await _webViewBridge.RequestMediaAssetJobSnapshotAsync("execution-completed-burst");
			}
		}
		catch (OperationCanceledException)
		{
			// New execution-completed events supersede older snapshot bursts.
		}
		catch (Exception ex)
		{
			Log($"Media asset snapshot burst failed: {ex.GetType().Name} - {ex.Message}");
		}
		finally
		{
			if (ReferenceEquals(_mediaAssetSnapshotBurstCts, cancellationTokenSource))
			{
				_mediaAssetSnapshotBurstCts = null;
			}

			cancellationTokenSource.Dispose();
		}
	}

	private Task HandleQueueButtonStateSyncAsync(JsonElement data)
	{
		bool isStopButton = data.TryGetProperty("isStop", out var isStopProp) &&
			isStopProp.ValueKind == JsonValueKind.True;
		bool isInstantMode = string.Equals(_currentRunMode, RunModeOptions.Instant, StringComparison.OrdinalIgnoreCase) || isStopButton;

		if (!isInstantMode)
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				HeaderControl.SetInstantQueueButtonStop(false);
				RefreshControlDeckRunPulse(isInstantStop: false);
			});
			return Task.CompletedTask;
		}

		if (isStopButton)
		{
			_currentRunMode = RunModeOptions.Instant;
		}

		MainThread.BeginInvokeOnMainThread(() =>
		{
			if (isStopButton)
			{
				HeaderControl.SetRunMode(RunModeOptions.Instant);
			}

			HeaderControl.SetInstantQueueButtonStop(isStopButton);
			RefreshControlDeckRunPulse(isInstantStop: isStopButton);
		});

		return Task.CompletedTask;
	}

	private Task HandleBatchCountSyncAsync(JsonElement data)
	{
		int count = data.GetProperty("value").GetInt32();
		MainThread.BeginInvokeOnMainThread(() =>
		{
			HeaderControl.SetQueueCount(count);
		});
		return Task.CompletedTask;
	}

	private void RefreshControlDeckWebPulse(bool force = false)
	{
		var now = DateTime.UtcNow;
		if (!force && (now - _lastControlDeckWebPulseUtc).TotalMilliseconds < ControlDeckPulseMinIntervalMs)
		{
			return;
		}

		_lastControlDeckWebPulseUtc = now;
		MainThread.BeginInvokeOnMainThread(() =>
		{
			ControlDeckControl.SetPulseWeb(
				isLive: true,
				errorCount: _webPulseErrorCount,
				bridgeTraceEnabled: _bridgeDiagnosticsEnabled,
				webLogsEnabled: _webLogsEnabled,
				devToolsEnabled: _devToolsEnabled);
		});
	}

	private void RefreshControlDeckRunPulse(bool isInstantStop)
	{
		_pulseInstantStop = isInstantStop;
		ControlDeckControl.SetPulseRun(_pulseIsRunning, _pulseInstantStop);
	}

}
