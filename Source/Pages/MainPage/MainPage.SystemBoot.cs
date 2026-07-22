using System.Security.Cryptography;
using System.Text;
using Microsoft.Maui.Controls;
using ComfyUI_Nexus.Boot;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Views.Rail.Tools.NodeLibrary;

using ComfyUI_Nexus.Views.Overlays.Controllers;
namespace ComfyUI_Nexus;

public partial class MainPage
{
	private bool _isSuccessSequenceActive;

	private async Task TriggerSuccessSequence()
	{
		_isSuccessSequenceActive = true;
		_loginSequence.Phase(BootPhase.SuccessSequenceStarted);

		await InitializeNexusUiAsync();

		_loadingOverlayController.Message(
			LocalizationManager.Text("loading.preparing_nexus_title"),
			LocalizationManager.Text("loading.preparing_nexus_detail"),
			LocalizationManager.Text("loading.preparing_nexus_status"),
			LoadingInfoColor,
			progress: 1);
		await Task.Delay(120);

		_loadingOverlayController.Message(
			LocalizationManager.Text("loading.bridge_online_title"),
			LocalizationManager.Text("loading.bridge_online_detail"),
			LocalizationManager.Text("loading.bridge_online_status"),
			LoadingSuccessColor,
			progress: 1);
		if (LoadingOverlayControl != null)
		{
			_loginSequence.Phase(BootPhase.SuccessVisualsStarted);
			await _loadingOverlayController.PlaySuccessVisualsAsync("OK", LoadingSuccessColor);
		}

		await Task.Delay(180);
		_loginSequence.Phase(BootPhase.StableRequested);
		await PrepareSystemShellForStableRevealAsync();
		await _serverLifecycle.ActivateShellServicesAsync();
		await HeaderControl.AwaitSystemStatusLayoutAsync();
		await CompleteSystemLoadingRevealAsync();
		QueueDeferredOverlayPrewarm();
	}

	private async Task InitializeNexusUiAsync()
	{
		await PrepareActivityRailForRevealAsync();
		await SynchronizeNodeLibraryAsync();
		await PrewarmActivityRailContentAsync();
	}

	private async Task SynchronizeNodeLibraryAsync()
	{
		_loadingOverlayController.Message(
			"Synchronizing Node Library",
			"Scanning local manifests and preparing the searchable rail.",
			"SYNCHRONIZING NODE LIBRARY // SCANNING MANIFESTS...",
			LoadingInfoColor,
			progress: 0.98);
		Log("SYSTEM: Initializing Node Library Manifest...");
		_loginSequence.Phase(BootPhase.NodeLibrarySyncStarted);
		try
		{
			bool shouldFetchManifest = _nodeLibrary == null || _loginSequence.CurrentType is LoginSequenceType.F5Refresh or LoginSequenceType.CoreLinkSelection;
			var nextNodeLibrary = shouldFetchManifest
				? await _nodeLibraryService.FetchNodesAsync()
				: _nodeLibrary!;
			string nextSignature = await _bridgeOperations.RunBackgroundAsync(
				NexusBackgroundLane.Cpu,
				"node-library-signature",
				_ => BuildNodeLibraryManifestSignature(nextNodeLibrary));
			bool hasManifestChanged = !string.Equals(_nodeLibraryManifestSignature, nextSignature, StringComparison.Ordinal);
			bool shouldSyncTree = _nodeLibrary == null || hasManifestChanged;

			if (shouldSyncTree)
			{
				_nodeLibrary = nextNodeLibrary;
				_nodeLibraryManifestSignature = nextSignature;
			}

			string nodeLibrarySummary = await _bridgeOperations.RunBackgroundAsync(
				NexusBackgroundLane.Cpu,
				"node-library-summary",
				_ => GetNodeLibrarySummary(_nodeLibrary!));

			Log(!shouldFetchManifest
				? $"SYSTEM: Node Library cache reused. {nodeLibrarySummary}"
				: shouldSyncTree
					? $"SYSTEM: Node Library Online. {nodeLibrarySummary}"
					: $"SYSTEM: Node Library unchanged. {nodeLibrarySummary}");
			_loginSequence.Phase(BootPhase.NodeLibrarySyncCompleted, !shouldFetchManifest
				? $"cached, {nodeLibrarySummary}"
				: shouldSyncTree
					? nodeLibrarySummary
					: $"unchanged, {nodeLibrarySummary}");

			if (shouldSyncTree)
			{
				// Force UI data sync after node data arrives.
				await MainThread.InvokeOnMainThreadAsync(async () =>
				{
					if (RailControl == null)
					{
						Log("NODE LIBRARY ERROR: RailControl is NULL during refresh trigger.");
					}
					else
					{
						Log("SYSTEM: Triggering Node Library UI Sync...");
						if (_latestBlueprints != null)
						{
							await RailControl.UpdateBlueprintsAsync(_latestBlueprints);
						}
						else
						{
							await RailControl.SyncNodeLibraryTreeAsync(forceRebuild: true);
						}
					}
				});
			}
		}
		catch (Exception ex)
		{
			Log($"NODE LIBRARY ERROR: {ex.Message}");
			_loginSequence.Phase(BootPhase.NodeLibrarySyncFailed, ex.Message);
		}
	}

	private async Task PrepareActivityRailForRevealAsync()
	{
		// Prepare chrome before the loading overlay fades out so the layout can reveal without a first-frame hitch.
		_loadingOverlayController.Message(
			"Mounting Activity Rail",
			"Preparing workspace chrome before the browser surface is revealed.",
			"MOUNTING ACTIVITY RAIL // PREPARING WORKSPACE...",
			LoadingInfoColor,
			progress: 0.96);
		await MainThread.InvokeOnMainThreadAsync(() =>
		{
			RailControl?.PrepareForDisplay(_isFileRailExpanded);
			ApplyLeftChromeVisibilityState();
		});
		_loginSequence.Phase(BootPhase.RailPrepared, $"expanded={_isFileRailExpanded}");
	}

	private async Task PrewarmActivityRailContentAsync()
	{
		if (_isShuttingDown)
		{
			return;
		}

		_loadingOverlayController.Message(
			"Preparing Workspace",
			"Preloading the activity rail before the workspace is revealed.",
			"PREPARING WORKSPACE // PREWARMING RAIL...",
			LoadingInfoColor,
			progress: 0.99);
		_loginSequence.Phase(BootPhase.RailPrewarmStarted);
		try
		{
			await RunStartupPrewarmStepAsync(
				"rail content",
				() => RailControl?.PrewarmContentAsync(_expandedRailWidth) ?? Task.CompletedTask);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Exception(ex, "[RAIL_VIEW] Startup rail prewarm failed");
		}

		_loginSequence.Phase(BootPhase.RailPrewarmCompleted, "data prewarm");

		await NexusUiFrame.AwaitDispatcherTurnAsync(this, "STARTUP:PrewarmCompleted");
	}

	private void QueueDeferredOverlayPrewarm()
	{
		if (_isShuttingDown)
		{
			return;
		}

		_latestOperations.RequestLatest(
			"startup-overlay-prewarm",
			RunDeferredOverlayPrewarmAsync);
	}

	private async Task RunDeferredOverlayPrewarmAsync(NexusOperationLease lease)
	{
		if (!lease.IsCurrent || _isShuttingDown)
		{
			return;
		}

		try
		{
			await RunStartupPrewarmStepAsync(
				"settings overlay",
				() => SettingsOverlayControl?.PrewarmLayoutAsync() ?? Task.CompletedTask);
			if (!lease.IsCurrent || _isShuttingDown)
			{
				return;
			}

			await YieldBetweenStartupPrewarmStepsAsync();
			await RunStartupPrewarmStepAsync(
				"help overlay",
				() => HelpOverlayControl?.PrewarmLayoutAsync() ?? Task.CompletedTask);
			if (!lease.IsCurrent || _isShuttingDown)
			{
				return;
			}

			await YieldBetweenStartupPrewarmStepsAsync();
			await RunStartupPrewarmStepAsync(
				"about overlay",
				() => AboutOverlayControl?.PrewarmLayoutAsync() ?? Task.CompletedTask);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Exception(ex, "[OVERLAY] Deferred startup prewarm failed");
		}
	}

	private async Task RunStartupPrewarmStepAsync(string label, Func<Task> prewarmAsync)
	{
		if (_isShuttingDown)
		{
			return;
		}

		NexusLog.Trace($"[RAIL_VIEW] Startup prewarm: {label} start.");
		await MainThread.InvokeOnMainThreadAsync(prewarmAsync);
		NexusLog.Trace($"[RAIL_VIEW] Startup prewarm: {label} completed.");
	}

	private async Task YieldBetweenStartupPrewarmStepsAsync()
	{
		await NexusUiFrame.AwaitDispatcherTurnAsync(this, "STARTUP:PrewarmStep");
	}

	private static string BuildNodeLibraryManifestSignature(NodeLibraryRoot nodeLibrary)
	{
		var builder = new StringBuilder();

		foreach (var entry in EnumerateManifestEntries(nodeLibrary)
			.GroupBy(static entry => entry.Type, StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static entry => entry.Type, StringComparer.OrdinalIgnoreCase))
		{
			builder
				.Append(entry.Type).Append('\u001f')
				.Append(entry.DisplayName).Append('\u001f')
				.Append(entry.Description).Append('\u001f')
				.Append(entry.Category).Append('\u001f')
				.Append(entry.PythonModule).Append('\u001f')
				.Append(entry.GroupKind).Append('\u001f')
				.Append(entry.ColorHex).Append('\n');
		}

		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
		return Convert.ToHexString(hash);
	}

	private static IEnumerable<NodeLibraryEntry> EnumerateManifestEntries(NodeLibraryRoot nodeLibrary)
	{
		foreach (var entry in EnumerateEntries(nodeLibrary.NexusFamilyRoot))
		{
			yield return entry;
		}

		foreach (var entry in EnumerateEntries(nodeLibrary.PartnerRoot))
		{
			yield return entry;
		}

		foreach (var entry in EnumerateEntries(nodeLibrary.ComfyRoot))
		{
			yield return entry;
		}

		foreach (var entry in EnumerateEntries(nodeLibrary.ExtensionRoot))
		{
			yield return entry;
		}
	}

	private static IEnumerable<NodeLibraryEntry> EnumerateEntries(NodeCategoryNode node)
	{
		foreach (var entry in node.Nodes)
		{
			yield return entry;
		}

		foreach (var child in node.SubCategories)
		{
			foreach (var entry in EnumerateEntries(child))
			{
				yield return entry;
			}
		}
	}

	private static string GetNodeLibrarySummary(NodeLibraryRoot nodeLibrary)
	{
		static int CountNodes(NodeCategoryNode node)
			=> node.Nodes.Count + node.SubCategories.Sum(CountNodes);

		int comfyCount = CountNodes(nodeLibrary.ComfyRoot);
		int partnerCount = CountNodes(nodeLibrary.PartnerRoot);
		int extensionCount = CountNodes(nodeLibrary.ExtensionRoot);
		int total = comfyCount + partnerCount + extensionCount;

		return $"total={total}, comfy={comfyCount}, partner={partnerCount}, ext={extensionCount}";
	}

	private async Task ExecuteControlDeckManualRebootAsync()
	{
		await PerformSystemReboot();
	}

	private async Task ExecuteControlDeckShutdownServerAsync()
	{
		await ExecuteControlDeckServerShutdownAsync(showServerLaunchPanel: true);
	}

	private async Task ExecuteControlDeckBootServerAsync()
	{
		if (!_serverLifecycle.Allows(ServerLifecycleCapability.Boot))
		{
			NexusLog.Trace($"[LIFECYCLE] Server boot ignored while {_serverLifecycle.Snapshot.State} is active.");
			return;
		}

		if (_appManager.ServerProcesses.FindServerProcess() != null)
		{
			Log("[LIFECYCLE] Server boot ignored because ComfyUI is already running.");
			return;
		}

		try
		{
			Log("[LIFECYCLE] Server-only boot requested from Nexus Control Deck.");
			ServerLifecycleResult result = await _serverLifecycle.RunAsync(new ServerLifecycleRequest(ServerLifecycleMode.BootServerOnly));
			if (!result.IsSuccess)
			{
				throw new InvalidOperationException(result.Message);
			}

			Log("[LIFECYCLE] Server-only boot confirmed. Use RETRY to reconnect the Nexus UI.");
		}
		catch (Exception ex)
		{
			Log($"[LIFECYCLE] Server-only boot failed: {ex.GetType().Name} - {ex.Message}");
		}
	}

	private async Task<bool> ExecuteControlDeckServerShutdownAsync(bool showServerLaunchPanel)
	{
		if (!_serverLifecycle.Allows(ServerLifecycleCapability.Shutdown))
		{
			NexusLog.Trace($"[LIFECYCLE] Server shutdown ignored while {_serverLifecycle.Snapshot.State} is active.");
			return false;
		}

		await CloseAllPopupSurfacesAsync();
		await SetShutdownBlockerVisibleAsync(
			true,
			showServerLaunchPanel ? "SHUTTING DOWN SERVER" : "KILLING SERVER",
			"Stopping shell services and verifying the ComfyUI process has ended...");

		try
		{
			ServerLifecycleResult result = await _serverLifecycle.RunAsync(new ServerLifecycleRequest(ServerLifecycleMode.Shutdown));
			if (!result.IsSuccess)
			{
				throw new InvalidOperationException(result.Message);
			}

			_isBooted = false;
			_bootReadyHandled = false;
			if (showServerLaunchPanel)
			{
				await _loadingOverlayController.EnterServerBootAsync(new(ServerBootEntryKind.Idle));
			}

			return true;
		}
		catch (Exception ex)
		{
			Log($"[SYSTEM] Server shutdown failed: {ex.GetType().Name} - {ex.Message}");
			await DisplayAlertAsync(
				"SERVER SHUTDOWN FAILED",
				ex.Message,
				LocalizationManager.Text("common.ok"));
			return false;
		}
		finally
		{
			if (InputBlockerOverlay.IsVisible)
			{
				await SetShutdownBlockerVisibleAsync(false);
			}
		}
	}

	private async void OnLoadingRetryRequested(object? sender, EventArgs e)
	{
		if (!CanAcceptWebRefresh(out string blockedReason))
		{
			ShowLoadingRetryBlockedState(blockedReason);
			return;
		}

		Uri readinessEndpoint = new(new Uri(ComfyApiOptions.GetLocalBaseUrl(_appManager.Settings.Settings)), "api/object_info");
		_loadingOverlayController.Hold(
			"Checking ComfyUI",
			"Confirming the local ComfyUI API before reconnecting Nexus.",
			"CHECKING COMFYUI API...",
			LoadingInfoColor);

		try
		{
			LocalHttpProbeResult probe = await LocalServerProbe.TryGetAsync(readinessEndpoint, CancellationToken.None);
			if (probe.State != LocalHttpProbeState.Responded || probe.StatusCode != System.Net.HttpStatusCode.OK)
			{
				string detail = probe.State switch
				{
					LocalHttpProbeState.NotListening => "ComfyUI is not listening yet. Start the server, wait for it to finish booting, then retry.",
					LocalHttpProbeState.Responded => $"ComfyUI API returned HTTP {(int)probe.StatusCode!.Value}. Wait for a ready server or restart the application.",
					_ => "ComfyUI accepted the connection but its API is not ready. Wait briefly, then retry.",
				};

				_loadingOverlayController.Error(
					LocalizationManager.Text("loading.comfy_unreachable_title"),
					detail,
					LocalizationManager.Text("loading.comfy_unreachable_status"),
					LoadingWarningColor);
				return;
			}

			await PerformSystemReboot();
		}
		catch (Exception ex)
		{
			Log($"[RETRY] ComfyUI API readiness check failed: {ex.GetType().Name} - {ex.Message}");
			_loadingOverlayController.Error(
				LocalizationManager.Text("loading.comfy_unreachable_title"),
				"The local ComfyUI API could not be checked. Restart ComfyUI or restart Nexus, then retry.",
				LocalizationManager.Text("loading.comfy_unreachable_status"),
				LoadingWarningColor);
		}
	}

	private async void OnServerBootSetupRequested(object? sender, EventArgs e)
	{
		StartServerBootSetupRouteTiming();
		WriteServerBootSetupRouteTiming("Splash route requested from server boot.");
		await ShowStartupSplashAsync();
		try
		{
			Task setupPreparation = ShowProductSetupAsync();
			await PlayStartupSplashBouncesUntilReadyAsync(setupPreparation);
			await setupPreparation;
			WriteServerBootSetupRouteTiming("Setup cache and reveal preparation completed.");
		}
		finally
		{
			WriteServerBootSetupRouteTiming("Starting splash exit.");
			await HideStartupSplashAsync(includeBounce: false);
			WriteServerBootSetupRouteTiming("Splash route completed.");
		}
	}

	private void ShowLoadingRetryBlockedState(string reason)
	{
		string detail = reason.StartsWith("server-lifecycle-", StringComparison.Ordinal)
			? "ComfyUI is still starting or stopping. Wait for the server lifecycle to finish, then retry."
			: "Nexus is already reconnecting to ComfyUI. Wait for the current attempt to finish before retrying.";

		Log($"[RETRY] Reconnect request deferred: {reason}");
		_loadingOverlayController.Error(
			LocalizationManager.Text("loading.comfy_unreachable_title"),
			detail,
			LocalizationManager.Text("loading.comfy_unreachable_status"),
			LoadingWarningColor);
	}

	private async Task PerformSystemReboot()
	{
		if (!CanAcceptWebRefresh(out string blockedReason))
		{
			NexusLog.Trace($"[REFRESH] Ignored while loading is active: {blockedReason}");
			return;
		}

		if (_isRebooting) return;
		_isRebooting = true;

		try
		{
			await _loginSequence.RunRefreshAsync(
				"Manual or WebView refresh requested",
				CreateLoginSequenceSteps());
		}
		finally
		{
			_isRebooting = false;
		}
	}

	private bool CanAcceptWebRefresh(out string reason)
	{
		if (!_loginSequence.Allows(LoginSequenceCapability.Refresh))
		{
			reason = "login-sequence-active";
			return false;
		}

		if (!_serverLifecycle.Allows(ServerLifecycleCapability.Refresh))
		{
			reason = $"server-lifecycle-{_serverLifecycle.Snapshot.State}";
			return false;
		}

		reason = string.Empty;
		return true;
	}

	private void UpdateRebootUI(bool isStarting)
	{
		_isBooted = !isStarting;
		if (isStarting)
		{
			_loginSequence.Phase(BootPhase.NavigationStarting);
		}
	}

	private async Task ExecuteWebViewColdBoot()
	{
		try
		{
			Log("SYSTEM: WebView reconnect sequence authorized.");
			Log("SYSTEM: Requesting explicit ComfyUI navigation...");
			_loginSequence.Phase(BootPhase.ReloadRequested);
			await UpdateSystemLoadingStateAsync(true);
			await WorkspaceControl.BrowserSurface.NavigateAsync(ComfyApiOptions.GetLocalBaseUrl(_appManager.Settings.Settings));
		}
		catch (Exception ex)
		{
			Log($"REBOOT ERROR: {ex.Message}");
			_loginSequence.Phase(BootPhase.ReloadFailed, ex.Message);
			ApplyWebViewNavigationFailureUi($"Navigation request failed: {ex.Message}");
		}
	}
}
