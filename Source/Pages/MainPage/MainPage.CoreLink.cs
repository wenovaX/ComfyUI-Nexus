using System.Text.Json;
using ComfyUI_Nexus.Boot;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Setup.Startup;
using ComfyUI_Nexus.Ui;
using Microsoft.Maui.Controls;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	// Development-only startup override. Keep disabled for normal boot routing.
	private static readonly bool ForceProductSetupOnStartup = false;
	private readonly StartupRouteDecider _startupRouteDecider = new();

	private void StartStartupLoginSequence()
	{
		NexusLog.Info("[STARTUP] Scheduling startup route sequence.");
		Dispatcher.Dispatch(() => _ = StartStartupLoginSequenceSafeAsync());
	}

	private async Task StartStartupLoginSequenceSafeAsync()
	{
		try
		{
			await StartStartupLoginSequenceAsync();
		}
		catch (OperationCanceledException ex)
		{
			NexusLog.Exception(ex, "[STARTUP] Startup route sequence canceled");
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[STARTUP] Startup route sequence failed");
			try
			{
				await PrepareStartupRouteForRevealAsync(StartupRouteKind.FullSetup);
				await HideStartupSplashAsync();
			}
			catch (Exception fallbackEx)
			{
				NexusLog.Exception(fallbackEx, "[STARTUP] Failed to show setup after startup error");
			}
		}
	}

	private async Task StartStartupLoginSequenceAsync()
	{
		Log("[STARTUP] Startup route sequence started.");
		if (ForceProductSetupOnStartup)
		{
			Log("Startup route: forced full product setup.");
			await PrepareStartupRouteForRevealAsync(StartupRouteKind.FullSetup);
			await HideStartupSplashAsync();
			return;
		}

		Log("[STARTUP] Deciding startup route.");
		StartupRouteDecision decision = await _startupRouteDecider.DecideAsync(CancellationToken.None);
		Log($"Startup route: {decision.Kind} ({decision.Reason})");

		switch (decision.Kind)
		{
			case StartupRouteKind.DirectLoading:
				await LaunchNexusAppEntryAsync(CancellationToken.None);
				break;
			case StartupRouteKind.MaintenanceRecovery:
				await _loadingOverlayController.EnterServerBootAsync(new(ServerBootEntryKind.MaintenanceRecovery));
				await HideStartupSplashAsync();
				break;
			case StartupRouteKind.ServerLaunchOnly:
				await _loadingOverlayController.EnterServerBootAsync(new(ServerBootEntryKind.Idle));
				await HideStartupSplashAsync();
				break;
			case StartupRouteKind.ServerStartupPending:
				await _loadingOverlayController.EnterServerBootAsync(new(ServerBootEntryKind.ResumePending));
				await HideStartupSplashAsync();
				break;
			default:
				await PrepareStartupRouteForRevealAsync(decision.Kind);
				await HideStartupSplashAsync();
				break;
		}
	}

	private Task PrepareStartupRouteForRevealAsync(StartupRouteKind route)
	{
		return route switch
		{
			StartupRouteKind.FullSetup => PrepareProductSetupForStartupRevealAsync(),
			_ => Task.CompletedTask,
		};
	}

	private async Task PrepareProductSetupForStartupRevealAsync()
	{
		Log("[STARTUP] Preparing full setup route for splash reveal.");
		await ShowProductSetupAsync();
		Log("[STARTUP] Full setup route is ready for splash reveal.");
	}

	private async Task LaunchNexusAppEntryAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		string comfyPath = ComfyPathResolver.ResolveActiveComfyPath();
		if (!Directory.Exists(comfyPath))
		{
			throw new DirectoryNotFoundException($"ComfyUI install path was not found: {comfyPath}");
		}

		PortablePreferences.Set(PreferenceKeys.ComfyUIPath, comfyPath);
		SynchronizeConfiguredComfyPathSurfaces();
		Log($"SYSTEM: Nexus App Entry launching from {comfyPath}");
		// A server restart stops the loading clip while the boot monitor owns the overlay.
		// Restart it before bridge preparation so the hand-off never leaves a static surface.
		StartOverlayAnimations();

		await _loginSequence.RunStartupAsync(
			"ProductSetup.LaunchNexus",
			CreateLoginSequenceSteps());
	}

	private async void OnResetCoreClicked(object? sender, EventArgs e)
	{
		bool confirm = await DisplayAlertAsync(
			LocalizationManager.Text("core_link.reset_title"),
			LocalizationManager.Text("core_link.reset_message"),
			LocalizationManager.Text("core_link.terminate"),
			LocalizationManager.Text("common.cancel"));
		if (confirm)
		{
			PortablePreferences.Remove(PreferenceKeys.ComfyUIPath);
			UpdateComfyManagerAvailability(string.Empty);
			Log("SYSTEM RESET: Core Link terminated by user.");
			StartStartupLoginSequence();
		}
	}

	private async Task ExecuteControlDeckPatchLocalHudAsync()
	{
		try
		{
			string? localHudPath = ResolveLocalHudProjectPath();
			if (localHudPath == null)
			{
				Log("[HUD] Local ComfyUI-HUD project was not found next to the Nexus workspace.");
				await DisplayAlertAsync(
					LocalizationManager.Text("core_link.hud_patch_unavailable_title"),
					LocalizationManager.Text("core_link.hud_patch_missing_local_project"),
					LocalizationManager.Text("common.ok"));
				return;
			}

			string targetHudPath = Path.Combine(GetConfiguredCoreLinkPath(), "custom_nodes", HudBridgeInstaller.HudExtensionFolderName);
			bool confirm = await DisplayAlertAsync(
				LocalizationManager.Text("core_link.patch_local_hud_title"),
				LocalizationManager.Format("core_link.patch_local_hud_message", localHudPath, targetHudPath),
				LocalizationManager.Text("core_link.patch"),
				LocalizationManager.Text("common.cancel"));
			if (!confirm) return;

			var installService = ComfyInstallService.Instance ?? new ComfyInstallService();
			installService.OnMessage = message => Log(message);
			Log($"[HUD] Patching local ComfyUI-HUD from {localHudPath}");
			var result = await installService.PatchLocalHudProjectAsync(localHudPath, CancellationToken.None);
			Log(result.Message);

			await DisplayAlertAsync(
				result.IsSuccess
					? LocalizationManager.Text("core_link.hud_patch_complete_title")
					: LocalizationManager.Text("core_link.hud_patch_failed_title"),
				result.Message,
				LocalizationManager.Text("common.ok"));
		}
		catch (Exception ex)
		{
			Log($"[HUD] Patch failed: {ex.GetType().Name} - {ex.Message}");
			await DisplayAlertAsync(
				LocalizationManager.Text("core_link.hud_patch_failed_title"),
				ex.Message,
				LocalizationManager.Text("common.ok"));
		}
	}

	private async Task ExecuteControlDeckPatchNexusBridgeAsync()
	{
		try
		{
			string customNodesPath = Path.Combine(GetConfiguredCoreLinkPath(), "custom_nodes");
			string targetBridgePath = Path.Combine(customNodesPath, HudBridgeInstaller.NexusBridgeExtensionFolderName);
			string sourceBridgePath = Path.Combine(ComfyInstallService.LocalRuntimePath, "Packages", HudBridgeInstaller.NexusBridgePackageFolderName);

			var installService = ComfyInstallService.Instance ?? new ComfyInstallService();
			if (!installService.IsPackagedNexusBridgeAvailable())
			{
				Log($"[Bridge] Packaged Nexus bridge payload was not found or incomplete: {sourceBridgePath}");
				await DisplayAlertAsync(
					LocalizationManager.Text("core_link.bridge_patch_unavailable_title"),
					LocalizationManager.Format("core_link.bridge_patch_missing_source_message", sourceBridgePath),
					LocalizationManager.Text("common.ok"));
				return;
			}

			bool confirm = await DisplayAlertAsync(
				LocalizationManager.Text("core_link.patch_nexus_bridge_title"),
				LocalizationManager.Format("core_link.patch_nexus_bridge_message", sourceBridgePath, targetBridgePath),
				LocalizationManager.Text("core_link.patch"),
				LocalizationManager.Text("common.cancel"));
			if (!confirm) return;

			installService.OnMessage = message => Log(message);
			Log($"[Bridge] Patching Nexus bridge into {targetBridgePath}");
			var result = await installService.PatchNexusBridgeAsync(CancellationToken.None);
			Log(result.Message);

			await DisplayAlertAsync(
				result.IsSuccess
					? LocalizationManager.Text("core_link.bridge_patch_complete_title")
					: LocalizationManager.Text("core_link.bridge_patch_failed_title"),
				result.Message,
				LocalizationManager.Text("common.ok"));
		}
		catch (Exception ex)
		{
			Log($"[Bridge] Nexus bridge patch failed: {ex.GetType().Name} - {ex.Message}");
			await DisplayAlertAsync(
				LocalizationManager.Text("core_link.bridge_patch_failed_title"),
				ex.Message,
				LocalizationManager.Text("common.ok"));
		}
	}

	private async void OnSelectCoreClicked(object? sender, EventArgs e)
	{
		try
		{
			var result = await PlatformManager.Current.FilePicker.PickFolderAsync(LocalizationManager.Text("core_link.select_comfy_folder"));
			if (!result.IsSupported)
			{
				await DisplayAlertAsync(
					LocalizationManager.Text("core_link.platform_not_supported_title"),
					result.Message ?? LocalizationManager.Text("core_link.folder_selection_not_supported"),
					LocalizationManager.Text("common.ok"));
				return;
			}

			if (!result.IsSuccess)
			{
				if (!string.IsNullOrWhiteSpace(result.Message))
				{
					Log($"Config Error: {result.Message}");
				}

				return;
			}

			string? path = result.Value;
			if (!string.IsNullOrWhiteSpace(path))
			{
				await _loginSequence.RunCoreLinkSelectionAsync(path, CreateLoginSequenceSteps());
			}
		}
		catch (Exception ex) { Log($"Config Error: {ex.Message}"); }
	}

	private LoginSequenceSteps CreateLoginSequenceSteps()
	{
		return new LoginSequenceSteps(
			GetConfiguredCoreLinkPath,
			ShowCoreLinkRequired,
			PrepareStartupCoreLinkAsync,
			PrepareSelectedCoreLinkAsync,
			StartWebViewLoginNavigationAsync,
			ValidateCoreLink,
			ExecuteWebViewColdBoot,
			ApplyWebViewNavigationFailureUi,
			PrepareWebViewNavigationSuccessAsync,
			_webViewBridge.SetShellIdentityAsync,
			InvokeBridgeBootAsync,
			DisableBrowserReloadHandlingAsync,
			ApplyHandshakeTimeoutUi,
			_webViewBridge.FetchBlueprintsAsync,
			SyncHeaderRunModeFromWebAsync,
			SyncViewQueueButtonVisualAsync,
			ShowBootWelcomeAsync,
			ShowBootStandByAsync,
			RunBootSuccessSequenceAsync);
	}

	private static string? ResolveLocalHudProjectPath()
	{
		foreach (string candidate in EnumerateLocalHudProjectCandidates())
		{
			if (LooksLikeLocalHudProject(candidate))
			{
				return Path.GetFullPath(candidate);
			}
		}

		return null;
	}

	private static IEnumerable<string> EnumerateLocalHudProjectCandidates()
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (string root in EnumeratePossibleNexusRoots())
		{
			string? parent = Directory.GetParent(root)?.FullName;
			if (!string.IsNullOrWhiteSpace(parent))
			{
				string siblingHud = Path.Combine(parent, HudBridgeInstaller.HudExtensionFolderName);
				if (seen.Add(Path.GetFullPath(siblingHud)))
				{
					yield return siblingHud;
				}
			}
		}

		string settingsRoot = SetupSettingsService.Instance.Settings.RootPath;
		if (!string.IsNullOrWhiteSpace(settingsRoot))
		{
			string? parent = Directory.GetParent(settingsRoot)?.FullName;
			if (!string.IsNullOrWhiteSpace(parent))
			{
				string siblingHud = Path.Combine(parent, HudBridgeInstaller.HudExtensionFolderName);
				if (seen.Add(Path.GetFullPath(siblingHud)))
				{
					yield return siblingHud;
				}
			}
		}
	}

	private static IEnumerable<string> EnumeratePossibleNexusRoots()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "ComfyUI-Nexus.csproj")) ||
				Directory.Exists(Path.Combine(directory.FullName, "LocalRuntime")))
			{
				yield return directory.FullName;
			}

			directory = directory.Parent;
		}
	}

	private static bool LooksLikeLocalHudProject(string path)
		=> Directory.Exists(path) &&
		   File.Exists(Path.Combine(path, "hud.manifest.json")) &&
		   File.Exists(Path.Combine(path, "__init__.py")) &&
		   Directory.Exists(Path.Combine(path, "js"));

	private string GetConfiguredCoreLinkPath()
		=> ComfyPathResolver.ResolveConfiguredComfyPath();

	private void ShowCoreLinkRequired(string reason)
	{
		_loadingOverlayController.SetConfigOverlayState(isVisible: true, opacity: 1, inputTransparent: false);
		_loadingOverlayController.SetMode(showConfig: true);
		_ = HideStartupSplashAsync();
		Log($"CRITICAL: Core Link required ({reason}). Activation sequence paused.");
	}

	private async Task PrepareStartupCoreLinkAsync(string path)
	{
		PortablePreferences.Set(PreferenceKeys.ComfyUIPath, path);
		SynchronizeConfiguredComfyPathSurfaces();
		Log($"Core Link ready: {path}");
		await EnsureNexusBridgeExtensionReadyAsync();
		_loadingOverlayController.SetMode(showConfig: false);
		await UpdateSystemLoadingStateAsync(true);
		await HideStartupSplashAsync();
	}

	private async Task PrepareSelectedCoreLinkAsync(string path)
	{
		PortablePreferences.Set(PreferenceKeys.ComfyUIPath, path);
		SynchronizeConfiguredComfyPathSurfaces();
		Log($"[Nexus] ComfyUI root configured: {path}");
		await EnsureNexusBridgeExtensionReadyAsync();
		_loadingOverlayController.SetMode(showConfig: false);
		await UpdateSystemLoadingStateAsync(true);
	}

	private async Task EnsureNexusBridgeExtensionReadyAsync()
	{
		var installService = ComfyInstallService.Instance ?? new ComfyInstallService();
		if (installService.IsNexusBridgeExtensionHealthy())
		{
			Log("[Bridge] Nexus bridge extension verified.");
			return;
		}

		Log("[Bridge] Nexus bridge extension missing or incomplete. Repairing before WebView handshake...");
		installService.OnMessage = message => Log(message);
		SetupStepResult result = await installService.PatchNexusBridgeAsync(CancellationToken.None);
		Log(result.Message);

		if (!result.IsSuccess)
		{
			throw new InvalidOperationException($"Nexus bridge repair failed before WebView handshake: {result.Message}");
		}
	}

	private async Task StartWebViewLoginNavigationAsync(int delayMs)
	{
		if (delayMs > 0)
		{
			await Task.Delay(delayMs);
		}

		await WaitForWebViewPlatformReadyAsync();

		await MainThread.InvokeOnMainThreadAsync(async () =>
		{
			Log("System ignition: Connecting to ComfyUI server...");
			await WorkspaceControl.BrowserSurface.NavigateAsync(ComfyApiOptions.LocalBaseUrl);
		});
	}

	private async Task WaitForWebViewPlatformReadyAsync()
	{
		if (_webViewPlatformReady.Task.IsCompleted)
		{
			return;
		}

		Task completed = await Task.WhenAny(_webViewPlatformReady.Task, Task.Delay(TimeSpan.FromSeconds(2)));
		if (completed == _webViewPlatformReady.Task)
		{
			Log("WebView platform bridge ready.");
			return;
		}

		Log("WebView platform bridge readiness timed out. Assigning source anyway.");
	}

	private async void OnGetComfyClicked(object? sender, TappedEventArgs e) => await Microsoft.Maui.ApplicationModel.Launcher.OpenAsync("https://www.comfy.org/download");
	private async void OnGetGitHubClicked(object? sender, TappedEventArgs e) => await Microsoft.Maui.ApplicationModel.Launcher.OpenAsync("https://github.com/Comfy-Org/ComfyUI");

	private async void OnOpenInputFolderClicked(object? sender, EventArgs e) => await OpenComfySubdirectoryAsync("input");

	private async void OnOpenOutputFolderClicked(object? sender, EventArgs e) => await OpenComfySubdirectoryAsync("output");

	private async void OnOpenComfyRootClicked(object? sender, EventArgs e)
	{
		var comfyRoot = ComfyPathResolver.ResolveConfiguredComfyPath();
		if (string.IsNullOrWhiteSpace(comfyRoot) || !Directory.Exists(comfyRoot))
		{
			Log("Asset Hub open failed: ComfyUI root is not available.");
			await DisplayAlertAsync(
				LocalizationManager.Text("core_link.missing_title"),
				LocalizationManager.Text("core_link.comfy_root_not_configured"),
				LocalizationManager.Text("common.ok"));
			return;
		}

		await _assetHubService.OpenInOsAsync(comfyRoot);
	}

	private async Task ProbeComfyUIWorkflowPath()
	{
		_loginSequence.Phase(BootPhase.WorkflowProbeStarted);
		try
		{
			using var httpClient = new HttpClient();
			httpClient.Timeout = TimeSpan.FromSeconds(5);
			var response = await httpClient.GetAsync(ComfyApiOptions.WorkflowProbeUrl);

			if (response.IsSuccessStatusCode)
			{
				string json = await response.Content.ReadAsStringAsync();
				using var doc = JsonDocument.Parse(json);
				int fileCount = doc.RootElement.GetArrayLength();
				Log($"[PROBE] Workflow files found: {fileCount}");

				var knownWorkflowFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var item in doc.RootElement.EnumerateArray())
				{
					string filePath = item.GetProperty("path").GetString() ?? "(unknown)";
					knownWorkflowFiles.Add(Ui.WorkflowTabController.NormalizeWorkflowRelativePath(filePath));
				}
				_tabController.SetKnownWorkflowFiles(knownWorkflowFiles);
				await RefreshWorkflowIndexAndWebAsync();
				_loginSequence.Phase(BootPhase.WorkflowProbeCompleted, $"{fileCount} workflow file(s)");
			}
			else if ((int)response.StatusCode == 404)
			{
				Log("[PROBE] Workflow directory not found yet");
				_tabController.SetKnownWorkflowFiles(Array.Empty<string>());
				_loginSequence.Phase(BootPhase.WorkflowProbeCompleted, "workflow directory not found");
			}
			else
			{
				Log($"[PROBE WARNING] Unexpected response: {(int)response.StatusCode}");
				_loginSequence.Phase(BootPhase.WorkflowProbeFailed, $"status={(int)response.StatusCode}");
			}
		}
		catch (HttpRequestException ex)
		{
			Log($"[PROBE ERROR] Network error: {ex.Message}");
			_loginSequence.Phase(BootPhase.WorkflowProbeFailed, ex.Message);
		}
		catch (TaskCanceledException)
		{
			Log("[PROBE ERROR] Request timed out after 5 seconds.");
			_loginSequence.Phase(BootPhase.WorkflowProbeFailed, "timeout");
		}
		catch (Exception ex)
		{
			Log($"[PROBE ERROR] Unexpected error: {ex.GetType().Name} - {ex.Message}");
			_loginSequence.Phase(BootPhase.WorkflowProbeFailed, $"{ex.GetType().Name}: {ex.Message}");
		}
	}

	private bool ValidateCoreLink()
	{
		string comfyPath = ComfyPathResolver.ResolveConfiguredComfyPath();
		if (string.IsNullOrEmpty(comfyPath) || !Directory.Exists(comfyPath))
		{
			_loginSequence.Phase(BootPhase.CoreLinkValidationFailed, comfyPath);
			_loginSequence.End(BootPhase.CoreLinkValidationFailed);
			Log("SECURITY: Link validation failed. Returning to Configuration Mode.");

			MainThread.BeginInvokeOnMainThread(() =>
			{
				if (LoadingOverlayControl != null)
				{
					_loadingOverlayController.SetConfigOverlayState(isVisible: true, opacity: 1, inputTransparent: false);
					_loadingOverlayController.SetMode(showConfig: true);
				}
				WorkspaceControl.HideBrowserSurface();
			});

			return false;
		}
		return true;
	}

	private async Task OpenComfySubdirectoryAsync(string directoryName)
	{
		var comfyRoot = ComfyPathResolver.ResolveConfiguredComfyPath();
		if (string.IsNullOrWhiteSpace(comfyRoot) || !Directory.Exists(comfyRoot))
		{
			Log($"Asset Hub open failed: '{directoryName}' unavailable because Core Link is missing.");
			await DisplayAlertAsync(
				LocalizationManager.Text("core_link.missing_title"),
				LocalizationManager.Text("core_link.comfy_root_not_configured"),
				LocalizationManager.Text("common.ok"));
			return;
		}

		var targetPath = Path.Combine(comfyRoot, directoryName);
		if (!Directory.Exists(targetPath))
		{
			Log($"Asset Hub open failed: '{targetPath}' was not found.");
			await DisplayAlertAsync(
				LocalizationManager.Text("core_link.folder_missing_title"),
				LocalizationManager.Format("core_link.folder_missing_message", directoryName),
				LocalizationManager.Text("common.ok"));
			return;
		}

		await _assetHubService.OpenInOsAsync(targetPath);
	}
}
