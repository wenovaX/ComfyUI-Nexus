using System.Security.Cryptography;
using System.Text;
using Microsoft.Maui.Controls;
using ComfyUI_Nexus.Boot;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Views.Rail.Tools.NodeLibrary;

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
			"Activity Rail Online",
			"All primary shell systems are ready.",
			"ACTIVITY RAIL ONLINE // ALL SYSTEMS NOMINAL",
			LoadingSuccessColor,
			progress: 1);
		await Task.Delay(120);

		_loadingOverlayController.Message(
			"Neural Bridge Stable",
			"Link confirmed. Revealing Nexus.",
			"NEURAL BRIDGE STABLE // LINK CONFIRMED",
			LoadingSuccessColor,
			progress: 1);
		if (LoadingOverlayControl != null)
		{
			_loginSequence.Phase(BootPhase.SuccessVisualsStarted);
			await _loadingOverlayController.PlaySuccessVisualsAsync("OK", LoadingSuccessColor);
		}

		await Task.Delay(180);
		_loginSequence.Phase(BootPhase.StableRequested);
		UpdateSystemLoadingState(false);
		await _serverLifecycle.ActivateShellServicesAsync();
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
			string nextSignature = await Task.Run(() => BuildNodeLibraryManifestSignature(nextNodeLibrary));
			bool hasManifestChanged = !string.Equals(_nodeLibraryManifestSignature, nextSignature, StringComparison.Ordinal);
			bool shouldSyncTree = _nodeLibrary == null || hasManifestChanged;

			if (shouldSyncTree)
			{
				_nodeLibrary = nextNodeLibrary;
				_nodeLibraryManifestSignature = nextSignature;
			}

			string nodeLibrarySummary = await Task.Run(() => GetNodeLibrarySummary(_nodeLibrary!));

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
			RailControl?.PrepareForReveal(_isFileRailExpanded);
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
			"Preloading rail data and overlay layouts before the workspace is revealed.",
			"PREPARING WORKSPACE // PREWARMING RAIL...",
			LoadingInfoColor,
			progress: 0.99);
		_loginSequence.Phase(BootPhase.RailPrewarmStarted);
		try
		{
			await RunStartupPrewarmStepAsync(
				"rail content",
				() => RailControl?.PrewarmContentAsync(_expandedRailWidth) ?? Task.CompletedTask);
			await YieldBetweenStartupPrewarmStepsAsync();

			await RunStartupPrewarmStepAsync(
				"settings overlay",
				() => SettingsOverlayControl?.PrewarmLayoutAsync() ?? Task.CompletedTask);
			await YieldBetweenStartupPrewarmStepsAsync();

			await RunStartupPrewarmStepAsync(
				"help overlay",
				() => HelpOverlayControl?.PrewarmLayoutAsync() ?? Task.CompletedTask);
			await YieldBetweenStartupPrewarmStepsAsync();

			await RunStartupPrewarmStepAsync(
				"about overlay",
				() => AboutOverlayControl?.PrewarmLayoutAsync() ?? Task.CompletedTask);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Exception(ex, "[RAIL_VIEW] Startup rail prewarm failed");
		}

		_loginSequence.Phase(BootPhase.RailPrewarmCompleted, "data prewarm");

		await NexusUiFrame.AwaitDispatcherTurnAsync(this, "STARTUP:PrewarmCompleted");
	}

	private async Task RunStartupPrewarmStepAsync(string label, Func<Task> prewarmAsync)
	{
		if (_isShuttingDown)
		{
			return;
		}

		NexusLog.Info($"[RAIL_VIEW] Startup prewarm: {label} start.");
		await MainThread.InvokeOnMainThreadAsync(prewarmAsync);
		NexusLog.Info($"[RAIL_VIEW] Startup prewarm: {label} completed.");
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

	private async Task PerformSystemReboot()
	{
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

	private void UpdateRebootUI(bool isStarting)
	{
		bool isAlreadyLoading = _isSystemLoading;
		_isBooted = !isStarting;
		if (isStarting)
		{
			_loginSequence.EnsureF5Refresh("WebView navigation starting");
			_loginSequence.Phase(BootPhase.NavigationStarting);
		}

		if (!isStarting || !isAlreadyLoading)
		{
			UpdateSystemLoadingState(isStarting);
		}

		if (isStarting)
		{
			SetLoadingStatus("LINK ESTABLISHED // HANDSHAKE WITH PROTOS...", LoadingInfoColor);
		}
	}

	private Task ExecuteWebViewColdBoot()
	{
		try
		{
			Log("SYSTEM: Unconditional reboot sequence authorized.");
			Log("SYSTEM: Requesting Web Environment Reboot...");
			_loginSequence.Phase(BootPhase.ReloadRequested);
			PlatformManager.Current.WebView.Reload(WorkspaceControl.BrowserView);
		}
		catch (Exception ex)
		{
			Log($"REBOOT ERROR: {ex.Message}");
			_loginSequence.Phase(BootPhase.ReloadFailed, ex.Message);
			WorkspaceControl.BrowserView.Reload();
		}

		return Task.CompletedTask;
	}
}
