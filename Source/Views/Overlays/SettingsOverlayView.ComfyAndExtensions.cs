namespace ComfyUI_Nexus.Views.Overlays;

using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Diagnostics;
using ComfyUI_Nexus.Setup.Diagnostics.Nodes;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;

public partial class SettingsOverlayView
{	private void OnUseVenvClicked(object? sender, EventArgs e)
	{
		_editor.Draft.ServerPythonMode = PythonExecutionModes.Venv;
		_editor.Draft.PendingVenvDelete = false;
		RemoveDraftBootTask(PendingBootTaskIds.VenvDelete);
		if (!File.Exists(_appManager.Paths.ActiveVenvPythonExe)
			&& !HasDraftBootTask(PendingBootTaskIds.VenvCreate)
			&& !HasDraftBootTask(PendingBootTaskIds.VenvRebuild))
		{
			AddDraftBootTask(PendingBootTaskIds.VenvCreate, PendingBootTaskOrigins.VenvModeSelection);
		}

		UpdatePythonModeButtons();
		UpdateStateChrome();
	}

	private void OnUseLocalRuntimeClicked(object? sender, EventArgs e)
	{
		if (_isComfyActionBusy)
		{
			return;
		}

		if (!string.IsNullOrWhiteSpace(_editor.Draft.ComfyPath))
		{
			_lastCustomComfyPath = _editor.Draft.ComfyPath;
		}

		_editor.Draft.InstallMode = SetupInstallModes.LocalRuntime;
		_editor.Draft.ComfyPath = string.Empty;
		UpdateComfyModeButtons();
		UpdateStateChrome();
	}

	private void OnUseRemoteComfyCoreClicked(object? sender, EventArgs e)
		=> SelectComfyCoreSource(ComfyCoreSources.RemoteLatest);

	private void OnUseBuiltInComfyCoreClicked(object? sender, EventArgs e)
		=> SelectComfyCoreSource(ComfyCoreSources.BuiltIn);

	private void SelectComfyCoreSource(string source)
	{
		if (_isComfyActionBusy
			|| !ComfyCoreSources.IsKnown(source)
			|| !string.Equals(_editor.Draft.InstallMode, SetupInstallModes.LocalRuntime, StringComparison.Ordinal))
		{
			return;
		}

		if (string.Equals(_editor.Draft.ComfyCoreSource, source, StringComparison.Ordinal))
		{
			return;
		}

		_editor.Draft.ComfyCoreSource = source;
		_comfyUpdatesAvailable = 0;
		ComfyApplyUpdateButton.IsVisible = false;
		ComfyApplyUpdateButton.IsEnabled = false;
		if (string.Equals(source, SettingsService.Settings.ComfyCoreSource, StringComparison.Ordinal))
		{
			RemoveDraftBootTask(PendingBootTaskIds.ComfyUpdate, PendingBootTaskOrigins.ComfyCoreSourceSelection);
			ComfyUpdateValueLabel.Text = LocalizationManager.Text("views.overlays.settings_overlay_view.comfy_core_source_unchanged");
		}
		else
		{
			AddDraftBootTask(PendingBootTaskIds.ComfyUpdate, PendingBootTaskOrigins.ComfyCoreSourceSelection);
			ComfyUpdateValueLabel.Text = string.Equals(source, ComfyCoreSources.BuiltIn, StringComparison.Ordinal)
				? LocalizationManager.Text("views.overlays.settings_overlay_view.comfy_core_source_builtin_scheduled")
				: LocalizationManager.Text("views.overlays.settings_overlay_view.comfy_core_source_remote_scheduled");
		}

		UpdateComfyModeButtons();
		UpdateStateChrome();
	}

	private async void OnSelectComfyPathClicked(object? sender, EventArgs e)
	{
		if (_isComfyActionBusy)
		{
			return;
		}

		if (!TryBeginOperation("select-comfy-path"))
		{
			return;
		}

		SetComfyActionBusy(true);
		try
		{
			if (sender == UseCustomComfyButton && !string.IsNullOrWhiteSpace(_lastCustomComfyPath))
			{
				_editor.Draft.InstallMode = SetupInstallModes.ExistingComfyPath;
				_editor.Draft.ComfyPath = _lastCustomComfyPath;
				UpdateComfyModeButtons();
				UpdateStateChrome();
				return;
			}

			var result = await NexusAppManager.Instance.Platform.FilePicker.PickFolderAsync("Select ComfyUI Folder");
			if (!result.IsSupported || !result.IsSuccess || string.IsNullOrWhiteSpace(result.Value))
			{
				if (!string.IsNullOrWhiteSpace(result.Message))
				{
					await ShowValidationAlertAsync(result.Message);
				}

				return;
			}

			_editor.Draft.InstallMode = SetupInstallModes.ExistingComfyPath;
			_editor.Draft.ComfyPath = result.Value;
			_lastCustomComfyPath = result.Value;
			UpdateComfyModeButtons();
			UpdateStateChrome();
		}
		finally
		{
			SetComfyActionBusy(false);
			EndOperation("select-comfy-path");
		}
	}

	private async void OnOpenComfyFolderClicked(object? sender, EventArgs e)
	{
		if (_isComfyActionBusy)
		{
			return;
		}

		if (!TryBeginOperation("open-comfy-folder"))
		{
			return;
		}

		SetComfyActionBusy(true);
		try
		{
			string path = GetEffectiveComfyPath(_editor.Draft);
			var result = await NexusAppManager.Instance.Platform.Shell.OpenPathAsync(path);
			if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
			{
				await ShowValidationAlertAsync(result.Message);
			}
		}
		finally
		{
			SetComfyActionBusy(false);
			EndOperation("open-comfy-folder");
		}
	}

	private async void OnCheckComfyUpdatesClicked(object? sender, EventArgs e)
	{
		await CheckComfyUpdatesAsync();
	}

	private async Task CheckComfyUpdatesAsync()
	{
		if (_isComfyActionBusy)
		{
			return;
		}

		if (!TryBeginOperation("check-comfy-updates"))
		{
			return;
		}

		SetComfyActionBusy(true);
		CancellationToken lifecycleToken = _lifetimeCts.Token;
		try
		{
			_comfyUpdatesAvailable = 0;
			ComfyApplyUpdateButton.IsVisible = false;
			ComfyApplyUpdateButton.IsEnabled = false;
			ComfyUpdateValueLabel.Text = "Checking ComfyUI repository status...";
			await Task.Yield();

			if (string.Equals(_editor.Draft.ComfyCoreSource, ComfyCoreSources.BuiltIn, StringComparison.Ordinal)
				&& string.Equals(_editor.Draft.InstallMode, SetupInstallModes.LocalRuntime, StringComparison.Ordinal))
			{
				ComfyUpdateValueLabel.Text = LocalizationManager.Text("views.overlays.settings_overlay_view.comfy_core_source_builtin_check");
				return;
			}

			string comfyPath = GetEffectiveComfyPath(_editor.Draft);
			string gitPath = ResolveToolProbePath(_editor.Draft.GitSource, _editor.Draft.GitPath, "git");
			var checkResult = await Task.Run(async () =>
			{
				if (!Directory.Exists(comfyPath))
				{
					return (Message: "ComfyUI folder does not exist. Select a valid path before checking updates.", UpdateCount: 0);
				}

				if (!Directory.Exists(System.IO.Path.Combine(comfyPath, ".git")))
				{
					return (Message: "This ComfyUI folder is not a git checkout. Update check is unavailable.", UpdateCount: 0);
				}

				(int ExitCode, string Output, string Error) fetchResult = await NexusSoftTimeout.AwaitAsync(
					ProcessRunner.RunAsync(gitPath, "fetch --quiet", comfyPath, null, lifecycleToken),
					TimeSpan.FromSeconds(30),
					() => UiThread.TryBeginInvoke(
						() =>
						{
							if (!_isUnloaded && !lifecycleToken.IsCancellationRequested)
							{
								ComfyUpdateValueLabel.Text = "Checking ComfyUI repository status. Still waiting for Git...";
							}
						},
						"SETTINGS:COMFY_UPDATE_PENDING"));

				if (fetchResult.ExitCode != 0)
				{
					return (Message: $"Update check failed: {GetProcessError(fetchResult)}", UpdateCount: 0);
				}

				(int ExitCode, string Output, string Error) upstreamResult = await NexusSoftTimeout.AwaitAsync(
					ProcessRunner.RunAsync(gitPath, "rev-list --count HEAD..@{u}", comfyPath, null, lifecycleToken),
					TimeSpan.FromSeconds(30),
					() => UiThread.TryBeginInvoke(
						() =>
						{
							if (!_isUnloaded && !lifecycleToken.IsCancellationRequested)
							{
								ComfyUpdateValueLabel.Text = "Reading upstream repository status. Still waiting for Git...";
							}
						},
						"SETTINGS:COMFY_UPDATE_PENDING"));

				if (upstreamResult.ExitCode != 0)
				{
					return (Message: "No upstream tracking branch detected. Advanced tag/revision update can be configured later.", UpdateCount: 0);
				}

				string rawCount = upstreamResult.Output
					.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
					.FirstOrDefault() ?? "0";
				if (!int.TryParse(rawCount.Trim(), out int updateCount))
				{
					return (Message: "Repository checked, but the update count could not be parsed.", UpdateCount: 0);
				}

				string message = updateCount == 0
					? "ComfyUI repository is up to date."
					: $"{updateCount} upstream commit(s) available. Review the warning before updating.";
				return (Message: message, UpdateCount: updateCount);
			});

			if (lifecycleToken.IsCancellationRequested || _isUnloaded)
			{
				return;
			}

			ComfyUpdateValueLabel.Text = checkResult.Message;
			int updateCount = checkResult.UpdateCount;
			_comfyUpdatesAvailable = updateCount;
			ComfyApplyUpdateButton.IsVisible = updateCount > 0;
			ComfyApplyUpdateButton.IsEnabled = updateCount > 0;
		}
		catch (OperationCanceledException) when (lifecycleToken.IsCancellationRequested)
		{
		}
		finally
		{
			SetComfyActionBusy(false);
			EndOperation("check-comfy-updates");
		}
	}

	private async void OnApplyComfyUpdateClicked(object? sender, EventArgs e)
	{
		await ApplyComfyUpdateAsync();
	}

	private async void OnExtensionsScanClicked(object? sender, EventArgs e)
	{
		await RefreshExtensionsStatusAsync(++_extensionsProbeId, CancellationToken.None, userRequested: true);
	}

	private async void OnExtensionsSyncUpdateClicked(object? sender, EventArgs e)
	{
		await QueueManagedExtensionsAsync(reinstall: false);
	}

	private async void OnExtensionsReinstallClicked(object? sender, EventArgs e)
	{
		await QueueManagedExtensionsAsync(reinstall: true);
	}

	private async void OnExtensionsOpenFolderClicked(object? sender, EventArgs e)
	{
		if (!TryBeginOperation("open-extensions-folder"))
		{
			return;
		}

		ExtensionsOpenFolderButton.IsEnabled = false;
		try
		{
			Directory.CreateDirectory(GetCustomNodesPath());
			var result = await NexusAppManager.Instance.Platform.Shell.OpenPathAsync(GetCustomNodesPath());
			if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
			{
				await ShowValidationAlertAsync(result.Message);
			}
		}
		finally
		{
			ExtensionsOpenFolderButton.IsEnabled = true;
			EndOperation("open-extensions-folder");
		}
	}

	private async void OnExtensionsRestoreHudSamplesClicked(object? sender, EventArgs e)
	{
		const string operationId = "restore-hud-samples";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		SetHudSamplesBusy(true);
		try
		{
			string sourcePath = System.IO.Path.Combine(GetCustomNodesPath(), HudBridgeInstaller.HudExtensionFolderName, "hud_sample");
			string targetPath = System.IO.Path.Combine(
				_appManager.Paths.ConfiguredComfyPath,
				"user",
				"default",
				"workflows",
				"hud_sample");
			if (!Directory.Exists(sourcePath) ||
				!Directory.EnumerateFiles(sourcePath, "*.json", SearchOption.AllDirectories).Any())
			{
				return;
			}

			bool hasExistingSamples = Directory.Exists(targetPath) &&
				Directory.EnumerateFiles(targetPath, "*.json", SearchOption.AllDirectories).Any();
			HudSampleRestoreMode? restoreMode = hasExistingSamples
				? await ChooseHudSampleRestoreModeAsync(sourcePath, targetPath)
				: await ConfirmHudSampleRestoreAsync(sourcePath, targetPath);
			if (restoreMode == null)
			{
				return;
			}

			SetHudSampleRestoreStatus(
				LocalizationManager.Text("views.overlays.settings_overlay_view.restoring_hud_samples"),
				SettingsInfoTextColor);
			SetupStepResult result = await ComfyInstall.RestoreHudSamplesAsync(
				overwriteExisting: restoreMode == HudSampleRestoreMode.Replace,
				CancellationToken.None);
			SetHudSampleRestoreStatus(
				result.IsSuccess
					? LocalizationManager.Text("views.overlays.settings_overlay_view.hud_samples_restored")
					: LocalizationManager.Format(
						"views.overlays.settings_overlay_view.hud_samples_restore_failed",
						result.Message),
				result.IsSuccess ? SettingsSuccessTextColor : SettingsFailureTextColor);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETTINGS] HUD sample restore failed");
			SetHudSampleRestoreStatus(
				LocalizationManager.Format(
					"views.overlays.settings_overlay_view.hud_samples_restore_failed",
					ex.Message),
				SettingsFailureTextColor);
		}
		finally
		{
			SetHudSamplesBusy(false);
			EndOperation(operationId);
		}
	}

	private void SetHudSampleRestoreStatus(string message, Color color)
	{
		HudSampleRestoreStatusLabel.Text = message;
		HudSampleRestoreStatusLabel.TextColor = color;
		HudSampleRestoreStatusLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
	}

	private async Task<HudSampleRestoreMode?> ConfirmHudSampleRestoreAsync(string sourcePath, string targetPath)
	{
		bool confirmed = await _appManager.Dialogs.ConfirmAsync(
			LocalizationManager.Text("views.overlays.settings_overlay_view.restore_hud_samples_title"),
			LocalizationManager.Format(
				"views.overlays.settings_overlay_view.restore_hud_samples_message",
				sourcePath,
				targetPath),
			LocalizationManager.Text("views.overlays.settings_overlay_view.restore_samples"),
			LocalizationManager.Text("common.cancel"));
		return confirmed ? HudSampleRestoreMode.MissingOnly : null;
	}

	private async Task<HudSampleRestoreMode?> ChooseHudSampleRestoreModeAsync(string sourcePath, string targetPath)
	{
		HudSampleRestoreMode? selectedMode = null;
		await _appManager.Dialogs.ChoiceAsync(
			LocalizationManager.Text("views.overlays.settings_overlay_view.restore_hud_samples_title"),
			LocalizationManager.Format(
				"views.overlays.settings_overlay_view.restore_hud_samples_conflict_message",
				sourcePath,
				targetPath),
			[
				new NexusDialogChoice(
					LocalizationManager.Text("views.overlays.settings_overlay_view.restore_missing"),
					() =>
					{
						selectedMode = HudSampleRestoreMode.MissingOnly;
						return Task.FromResult(NexusDialogActionResult.Close);
					}),
				new NexusDialogChoice(
					LocalizationManager.Text("views.overlays.settings_overlay_view.replace_samples"),
					() =>
					{
						selectedMode = HudSampleRestoreMode.Replace;
						return Task.FromResult(NexusDialogActionResult.Close);
					},
					IsDanger: true)
			],
			LocalizationManager.Text("common.cancel"));
		return selectedMode;
	}

	private async Task RefreshExtensionsStatusAsync(
		int probeId,
		CancellationToken cancellationToken = default,
		bool userRequested = false)
	{
		if (userRequested && !TryBeginOperation("scan-extensions"))
		{
			return;
		}

		if (userRequested)
		{
			SetExtensionsBusy(
				true,
				LocalizationManager.Text("views.overlays.settings_overlay_view.scanning_extensions_title"),
				LocalizationManager.Text("views.overlays.settings_overlay_view.scanning_extensions_detail"));
		}

		try
		{
			ExtensionsStatusValueLabel.Text = LocalizationManager.Text("settings.extensions.scanning_status");
			var node = new ManagerExtensionDiagnosticNode(ComfyInstall);
			HealthState health = await node.CheckHealthAsync(cancellationToken);
			var revisions = await ScanManagedExtensionGitStatusAsync(cancellationToken);
			if (probeId != _extensionsProbeId)
			{
				return;
			}

			RebuildManagedExtensionOptions();
			foreach (ManagedExtensionOption option in _managedExtensionOptions)
			{
				if (revisions.TryGetValue(option.Folder, out string? revision))
				{
					option.Revision = revision ?? option.Revision;
				}
			}

			RebuildManagedExtensionSelectionList();
			ExtensionsStatusValueLabel.Text = health switch
			{
				HealthState.Healthy => LocalizationManager.Format("settings.extensions.scan_ready", node.EnvironmentPath),
				HealthState.NeedsRecovery => LocalizationManager.Format("settings.extensions.scan_missing", node.EnvironmentPath),
				_ => LocalizationManager.Format("settings.extensions.scan_failed_status", node.EnvironmentPath)
			};
			ExtensionsStatusValueLabel.TextColor = health switch
			{
				HealthState.Healthy => SettingsSuccessTextColor,
				HealthState.NeedsRecovery => SettingsRequiredTextColor,
				_ => SettingsFailureTextColor
			};
			ExtensionsSyncUpdateButton.IsEnabled = true;
			ExtensionsReinstallButton.IsEnabled = true;
		}
		catch (OperationCanceledException)
		{
			NexusLog.Trace("[SETTINGS:UI] Managed extension scan canceled.");
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETTINGS:UI] Managed extension scan failed");
			ExtensionsStatusValueLabel.Text = LocalizationManager.Format(
				"views.overlays.settings_overlay_view.extension_scan_failed",
				ex.Message);
			ExtensionsStatusValueLabel.TextColor = SettingsFailureTextColor;
		}
		finally
		{
			if (userRequested)
			{
				SetExtensionsBusy(false);
				EndOperation("scan-extensions");
			}
		}
	}

	private async Task<Dictionary<string, string>> ScanManagedExtensionGitStatusAsync(CancellationToken cancellationToken)
	{
		string customNodesPath = GetCustomNodesPath();
		SetupSettings settings = SettingsService.Settings;
		string gitExecutable = ResolveToolProbePath(settings.GitSource, settings.GitPath, "git");
		var folders = new List<string>
		{
			HudBridgeInstaller.ManagerExtensionFolderName,
			HudBridgeInstaller.HudExtensionFolderName,
			HudBridgeInstaller.NexusBridgeExtensionFolderName
		};
		folders.AddRange(SettingsService.Settings.EssentialNodes.Select(node => node.Folder));
		return await Task.Run(() =>
		{
			var revisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();
				string path = System.IO.Path.Combine(customNodesPath, folder);
				bool isInstalled = IsManagedExtensionInstalled(path);
				revisions[folder] = GetManagedExtensionScanStatus(path, isInstalled, gitExecutable);
			}

			return revisions;
		}, cancellationToken);
	}

	private static string GetManagedExtensionScanStatus(string path, bool isInstalled, string gitExecutable)
	{
		if (!isInstalled)
		{
			return "not installed";
		}

		if (!Directory.Exists(System.IO.Path.Combine(path, ".git")))
		{
			return "local package";
		}

		string status = RunGitMetadata(path, "status --porcelain=v2 --branch", gitExecutable);
		if (string.IsNullOrWhiteSpace(status))
		{
			return "git status unavailable";
		}

		string branch = ReadGitStatusValue(status, "# branch.head ");
		string revision = ReadGitStatusValue(status, "# branch.oid ");
		string upstream = ReadGitStatusValue(status, "# branch.upstream ");
		string aheadBehind = ReadGitStatusValue(status, "# branch.ab ");
		if (revision.Length > 8)
		{
			revision = revision[..8];
		}

		string identity = !string.IsNullOrWhiteSpace(branch) &&
			!string.Equals(branch, "(detached)", StringComparison.OrdinalIgnoreCase)
				? $"{branch} @ {revision}"
				: string.IsNullOrWhiteSpace(revision)
					? "git revision unknown"
					: $"rev {revision}";
		if (string.IsNullOrWhiteSpace(upstream))
		{
			return $"{identity} - upstream unknown";
		}

		int behindCount = ParseGitBehindCount(aheadBehind);
		return behindCount == 0
			? $"{identity} - up to date"
			: $"{identity} - {behindCount} update(s) available";
	}

	private static string ReadGitStatusValue(string status, string prefix)
		=> status
			.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
			.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))
			?[prefix.Length..]
			.Trim() ?? string.Empty;

	private static int ParseGitBehindCount(string aheadBehind)
	{
		foreach (string part in aheadBehind.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (part.StartsWith("-", StringComparison.Ordinal) &&
				int.TryParse(part.AsSpan(1), out int behindCount))
			{
				return behindCount;
			}
		}

		return 0;
	}

	private async Task QueueManagedExtensionsAsync(bool reinstall)
	{
		const string operationId = "repair-extensions";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		bool isBusy = false;
		try
		{
			var selectedTargets = _managedExtensionOptions
				.Where(option => option.IsSelected)
				.Select(option => option.Folder)
				.ToList();
			if (selectedTargets.Count == 0)
			{
				await ShowValidationAlertAsync(LocalizationManager.Text("settings.extensions.select_at_least_one"));
				return;
			}

			bool confirmed = await ShowConfirmationAsync(
				LocalizationManager.Text(reinstall
					? "settings.extensions.confirm_reinstall_title"
					: "settings.extensions.confirm_sync_title"),
				LocalizationManager.Format(
					reinstall
						? "settings.extensions.confirm_reinstall_message"
						: "settings.extensions.confirm_sync_message",
					string.Join($"{Environment.NewLine}- ", selectedTargets)),
				LocalizationManager.Text(reinstall
					? "settings.extensions.queue_reinstall"
					: "settings.extensions.queue_sync"),
				LocalizationManager.Text("common.cancel"));
			if (!confirmed)
			{
				return;
			}

			isBusy = true;
			SetExtensionsBusy(
				true,
				LocalizationManager.Text(reinstall
					? "views.overlays.settings_overlay_view.queueing_extension_reinstall_title"
					: "views.overlays.settings_overlay_view.queueing_extension_update_title"),
				LocalizationManager.Text("views.overlays.settings_overlay_view.queueing_extensions_detail"));
			await QueueBootTaskAsync(
				PendingBootTaskIds.ExtensionRepair,
				LocalizationManager.Text(reinstall
					? "views.overlays.settings_overlay_view.queueing_extension_reinstall_title"
					: "views.overlays.settings_overlay_view.queueing_extension_update_title"),
				LocalizationManager.Text("views.overlays.settings_overlay_view.queueing_extensions_detail"),
				saveDraft: true,
				targetFolders: selectedTargets,
				action: reinstall ? PendingBootTaskActions.ExtensionReinstall : PendingBootTaskActions.ExtensionSync,
				afterRefresh: () =>
				{
					ExtensionsStatusValueLabel.Text = reinstall
						? LocalizationManager.Text("settings.extensions.reinstall_scheduled")
						: LocalizationManager.Text("settings.extensions.sync_scheduled");
					_repositoryRestartRequired = false;
				},
				showBlocker: false);
		}
		finally
		{
			if (isBusy)
			{
				SetExtensionsBusy(false);
			}
			EndOperation(operationId);
		}
	}

	private async Task ApplyComfyUpdateAsync()
	{
		if (_isComfyActionBusy)
		{
			return;
		}

		if (!TryBeginOperation("apply-comfy-update"))
		{
			return;
		}

		SetComfyActionBusy(true);
		bool shouldRestoreUpdateButton = false;
		try
		{
			if (_comfyUpdatesAvailable <= 0)
			{
				ComfyUpdateValueLabel.Text = "Check updates first. No pending ComfyUI update is currently selected.";
				return;
			}

			shouldRestoreUpdateButton = true;
			ComfyApplyUpdateButton.IsVisible = false;
			ComfyApplyUpdateButton.IsEnabled = false;
			bool confirmed = await ShowConfirmationAsync(
				"Update ComfyUI?",
				"This will queue git pull, requirements sync, and CUDA PyTorch repair for the next server boot.",
				"Update",
				"Cancel");
			if (!confirmed)
			{
				return;
			}

			shouldRestoreUpdateButton = false;
			bool queued = await QueueBootTaskAsync(
				PendingBootTaskIds.ComfyUpdate,
				"QUEUEING COMFYUI UPDATE",
				"Saving the selected ComfyUI path and adding update + dependency repair to the next boot checklist...",
				saveDraft: true,
				afterRefresh: () =>
				{
					_repositoryRestartRequired = false;
					_comfyUpdatesAvailable = 0;
					ComfyApplyUpdateButton.IsVisible = false;
					ComfyUpdateValueLabel.Text = "ComfyUI update scheduled. Restart the server to pull updates and repair runtime dependencies before boot.";
				});
			if (!queued)
			{
				shouldRestoreUpdateButton = _comfyUpdatesAvailable > 0;
			}
		}
		finally
		{
			if (shouldRestoreUpdateButton && _comfyUpdatesAvailable > 0)
			{
				ComfyApplyUpdateButton.IsVisible = true;
			}

			SetComfyActionBusy(false);
			EndOperation("apply-comfy-update");
		}
	}

}
