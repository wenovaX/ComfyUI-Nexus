namespace ComfyUI_Nexus.Views.Overlays;

using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Services;

public partial class SettingsOverlayView
{
	private static string GetRuntimeBackupLabel(string target)
		=> target switch
		{
			RuntimeBackupTargets.Models => "models",
			RuntimeBackupTargets.CustomNodes => "custom_nodes",
			RuntimeBackupTargets.Input => "input",
			RuntimeBackupTargets.Output => "output",
			RuntimeBackupTargets.Workflows => "workflows",
			_ => target
		};

	private List<string> GetSelectedRuntimeBackupTargets()
	{
		var targets = new List<string>();
		if (_runtimeBackupModelsSelected)
		{
			targets.Add(RuntimeBackupTargets.Models);
		}

		if (_runtimeBackupCustomNodesSelected)
		{
			targets.Add(RuntimeBackupTargets.CustomNodes);
		}

		if (_runtimeBackupInputSelected)
		{
			targets.Add(RuntimeBackupTargets.Input);
		}

		if (_runtimeBackupOutputSelected)
		{
			targets.Add(RuntimeBackupTargets.Output);
		}

		if (_runtimeBackupWorkflowsSelected)
		{
			targets.Add(RuntimeBackupTargets.Workflows);
		}

		return targets;
	}

	private bool HasSelectedRuntimeBackupTarget()
		=> _runtimeBackupModelsSelected
			|| _runtimeBackupCustomNodesSelected
			|| _runtimeBackupInputSelected
			|| _runtimeBackupOutputSelected
			|| _runtimeBackupWorkflowsSelected;

	private bool IsRuntimeBackupFormat(string format)
		=> string.Equals(_editor.Draft.RuntimeBackupFormat, format, StringComparison.Ordinal);

	private void RefreshRuntimeBackupCard()
	{
		string path = RuntimeBackupService.GetConfiguredBackupRoot(_editor.Draft);
		RuntimeBackupPathLabel.Text = path;
		if (RuntimeBackupService.TryGetAvailableSpace(path, out long availableBytes, out string error))
		{
			RuntimeBackupFreeSpaceLabel.Text = LocalizationManager.Format(
				"settings.runtime_backup.available_space",
				RuntimeBackupService.FormatBytes(availableBytes));
			RuntimeBackupFreeSpaceLabel.TextColor = SettingsMutedTextColor;
		}
		else
		{
			RuntimeBackupFreeSpaceLabel.Text = LocalizationManager.Format(
				"settings.runtime_backup.space_unavailable",
				error);
			RuntimeBackupFreeSpaceLabel.TextColor = SettingsWarningTextColor;
		}

		UpdateRuntimeBackupOptionVisuals();
		RebuildRuntimeBackupExternalLibraries();
		if (_runtimeBackupAnalysis is null)
		{
			RuntimeBackupAnalysisLabel.Text = LocalizationManager.Text("settings.runtime_backup.ready_to_calculate");
			RuntimeBackupAnalysisLabel.TextColor = SettingsInfoTextColor;
		}
	}

	private void UpdateRuntimeBackupOptionVisuals()
	{
		ApplyRuntimeBackupOptionVisual(
			RuntimeBackupFolderFormatButton,
			IsRuntimeBackupFormat(RuntimeBackupFormats.Folder));
		ApplyRuntimeBackupOptionVisual(
			RuntimeBackupZipFormatButton,
			IsRuntimeBackupFormat(RuntimeBackupFormats.Zip));
		ApplyRuntimeBackupOptionVisual(RuntimeBackupModelsTargetButton, _runtimeBackupModelsSelected);
		ApplyRuntimeBackupOptionVisual(RuntimeBackupCustomNodesTargetButton, _runtimeBackupCustomNodesSelected);
		ApplyRuntimeBackupOptionVisual(RuntimeBackupInputTargetButton, _runtimeBackupInputSelected);
		ApplyRuntimeBackupOptionVisual(RuntimeBackupOutputTargetButton, _runtimeBackupOutputSelected);
		ApplyRuntimeBackupOptionVisual(RuntimeBackupWorkflowsTargetButton, _runtimeBackupWorkflowsSelected);
		MaintenanceBackupRuntimeButton.IsEnabled = !_isMaintenanceBusy && HasSelectedRuntimeBackupTarget();
	}

	private static void ApplyRuntimeBackupOptionVisual(Button button, bool isSelected)
	{
		if (!button.IsEnabled)
		{
			button.BackgroundColor = Color.FromArgb("#0617222f");
			button.TextColor = Color.FromArgb("#41576a");
			return;
		}

		button.BackgroundColor = isSelected
			? Color.FromArgb("#2631d8ff")
			: Color.FromArgb("#0Affffff");
		button.TextColor = isSelected
			? Color.FromArgb("#f4feff")
			: Color.FromArgb("#7893a8");
	}

	private void WireRuntimeBackupOptionHover(Button button, Func<bool> isSelected)
	{
		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (_, _) =>
		{
			if (!button.IsEnabled) return;
			button.BackgroundColor = Color.FromArgb("#2031d8ff");
			button.TextColor = Color.FromArgb("#f4feff");
		};
		pointer.PointerPressed += (_, _) =>
		{
			if (!button.IsEnabled) return;
			button.BackgroundColor = Color.FromArgb("#3031d8ff");
			button.TextColor = Colors.White;
		};
		pointer.PointerReleased += (_, _) => ApplyRuntimeBackupOptionVisual(button, isSelected());
		pointer.PointerExited += (_, _) => ApplyRuntimeBackupOptionVisual(button, isSelected());
		button.GestureRecognizers.Add(pointer);
	}

	private void RebuildRuntimeBackupExternalLibraries()
	{
		RuntimeBackupExternalLibrariesList.Children.Clear();
		var roots = _editor.Draft.ModelLibraryRoots
			.Select(ExtraModelPathsService.NormalizeFileSystemPath)
			.Where(path => !string.IsNullOrWhiteSpace(path))
			.ToList();
		RuntimeBackupExternalLibrariesPanel.IsVisible = roots.Count > 0;
		foreach (string path in roots)
		{
			var button = new Button
			{
				Text = path,
				Style = (Style)Resources["SettingsTextButtonStyle"],
				CommandParameter = path,
				IsEnabled = Directory.Exists(path),
				LineBreakMode = LineBreakMode.TailTruncation,
				MaximumWidthRequest = 300,
				Margin = new Thickness(0, 0, 6, 4)
			};
			button.Clicked += OnRuntimeBackupExternalLibraryClicked;
			RuntimeBackupExternalLibrariesList.Children.Add(button);
		}
	}

	private void InvalidateRuntimeBackupAnalysis(string? message = null)
	{
		_runtimeBackupAnalysisGeneration++;
		_runtimeBackupAnalysis = null;
		MaintenanceBackupProgressBar.Progress = 0;
		RuntimeBackupActivity.IsRunning = false;
		RuntimeBackupActivity.IsVisible = false;
		RuntimeBackupAnalysisLabel.Text = message
			?? LocalizationManager.Text("settings.runtime_backup.ready_to_calculate");
		RuntimeBackupAnalysisLabel.TextColor = SettingsInfoTextColor;
	}

	private async void OnRuntimeBackupChangePathClicked(object? sender, EventArgs e)
	{
		var result = await _appManager.Platform.FilePicker.PickFolderAsync(
			LocalizationManager.Text("settings.runtime_backup.select_destination"));
		if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Value))
		{
			if (!string.IsNullOrWhiteSpace(result.Message))
			{
				SetMaintenanceStatus(result.Message);
			}
			return;
		}

		if (!_editor.SaveRuntimeBackupPreferences(result.Value, _editor.Draft.RuntimeBackupFormat))
		{
			await ShowValidationAlertAsync(LocalizationManager.Text("settings.runtime_backup.preference_save_failed"));
			return;
		}

		InvalidateRuntimeBackupAnalysis(LocalizationManager.Text("settings.runtime_backup.destination_changed"));
		RefreshRuntimeBackupCard();
		UpdateStateChrome();
	}

	private async void OnRuntimeBackupFolderFormatClicked(object? sender, EventArgs e)
		=> await SaveRuntimeBackupFormatAsync(RuntimeBackupFormats.Folder);

	private async void OnRuntimeBackupZipFormatClicked(object? sender, EventArgs e)
		=> await SaveRuntimeBackupFormatAsync(RuntimeBackupFormats.Zip);

	private async Task SaveRuntimeBackupFormatAsync(string format)
	{
		if (IsRuntimeBackupFormat(format))
		{
			return;
		}

		if (!_editor.SaveRuntimeBackupPreferences(_editor.Draft.RuntimeBackupPath, format))
		{
			await ShowValidationAlertAsync(LocalizationManager.Text("settings.runtime_backup.preference_save_failed"));
			return;
		}

		InvalidateRuntimeBackupAnalysis(LocalizationManager.Text("settings.runtime_backup.format_changed"));
		RefreshRuntimeBackupCard();
		UpdateStateChrome();
	}

	private void OnRuntimeBackupModelsTargetClicked(object? sender, EventArgs e)
	{
		_runtimeBackupModelsSelected = !_runtimeBackupModelsSelected;
		InvalidateRuntimeBackupAnalysis();
		UpdateRuntimeBackupOptionVisuals();
	}

	private void OnRuntimeBackupCustomNodesTargetClicked(object? sender, EventArgs e)
	{
		_runtimeBackupCustomNodesSelected = !_runtimeBackupCustomNodesSelected;
		InvalidateRuntimeBackupAnalysis();
		UpdateRuntimeBackupOptionVisuals();
	}

	private void OnRuntimeBackupInputTargetClicked(object? sender, EventArgs e)
	{
		_runtimeBackupInputSelected = !_runtimeBackupInputSelected;
		InvalidateRuntimeBackupAnalysis();
		UpdateRuntimeBackupOptionVisuals();
	}

	private void OnRuntimeBackupOutputTargetClicked(object? sender, EventArgs e)
	{
		_runtimeBackupOutputSelected = !_runtimeBackupOutputSelected;
		InvalidateRuntimeBackupAnalysis();
		UpdateRuntimeBackupOptionVisuals();
	}

	private void OnRuntimeBackupWorkflowsTargetClicked(object? sender, EventArgs e)
	{
		_runtimeBackupWorkflowsSelected = !_runtimeBackupWorkflowsSelected;
		InvalidateRuntimeBackupAnalysis();
		UpdateRuntimeBackupOptionVisuals();
	}

	private async void OnRuntimeBackupExternalLibraryClicked(object? sender, EventArgs e)
	{
		if (sender is not Button { CommandParameter: string path } || !Directory.Exists(path))
		{
			return;
		}

		var result = await _appManager.Platform.Shell.OpenPathAsync(path);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			SetMaintenanceStatus(result.Message);
		}
	}

	private string RuntimeBackupsPath
		=> ComfyInstall.RuntimeBackupsPath;

	private IReadOnlyList<RuntimeBackupEntry> GetRuntimeBackupFolders()
		=> ComfyInstall.GetRuntimeBackups(includeIncomplete: false);

	private IReadOnlyList<RuntimeBackupEntry> GetRuntimeBackupDeleteFolders()
		=> ComfyInstall.GetRuntimeBackups(includeIncomplete: true);

	private async Task<string?> PickRuntimeRestoreFolderAsync()
	{
		var backupFolders = GetRuntimeBackupFolders();
		var page = Window?.Page ?? Application.Current?.Windows.FirstOrDefault()?.Page;
		if (page is not null)
		{
			string[] options = backupFolders
				.Select(entry => entry.Name)
				.Append(LocalizationManager.Text("settings.runtime_backup.browse_folder"))
				.Append(LocalizationManager.Text("settings.runtime_backup.browse_zip"))
				.ToArray();
			string? selection = await page.DisplayActionSheetAsync(
				LocalizationManager.Text("settings.runtime_backup.restore_title"),
				LocalizationManager.Text("common.cancel"),
				null,
				options);
			if (string.IsNullOrWhiteSpace(selection)
				|| string.Equals(selection, LocalizationManager.Text("common.cancel"), StringComparison.Ordinal))
			{
				return null;
			}

			if (string.Equals(selection, LocalizationManager.Text("settings.runtime_backup.browse_zip"), StringComparison.Ordinal))
			{
				return await PickRuntimeBackupZipAsync();
			}

			if (!string.Equals(selection, LocalizationManager.Text("settings.runtime_backup.browse_folder"), StringComparison.Ordinal))
			{
				return backupFolders.FirstOrDefault(entry =>
					string.Equals(entry.Name, selection, StringComparison.Ordinal))?.Path;
			}
		}

		var result = await _appManager.Platform.FilePicker.PickFolderAsync(
			LocalizationManager.Text("settings.runtime_backup.select_backup_folder"));
		if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value))
		{
			return result.Value;
		}

		if (!string.IsNullOrWhiteSpace(result.Message))
		{
			SetMaintenanceStatus(result.Message);
		}

		return null;
	}

	private async Task<string?> PickRuntimeBackupZipAsync()
	{
		var result = await _appManager.Platform.FilePicker.PickFileAsync(
			LocalizationManager.Text("settings.runtime_backup.select_backup_zip"),
			[".zip"]);
		if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value))
		{
			return result.Value;
		}

		if (!string.IsNullOrWhiteSpace(result.Message))
		{
			SetMaintenanceStatus(result.Message);
		}

		return null;
	}

	private async Task<string?> PickRuntimeBackupFolderToDeleteAsync()
	{
		var backupFolders = GetRuntimeBackupDeleteFolders();
		var page = Window?.Page ?? Application.Current?.Windows.FirstOrDefault()?.Page;
		if (page is null || backupFolders.Count == 0)
		{
			return null;
		}

		var options = backupFolders.ToDictionary(
			entry => $"{entry.Name} [{LocalizationManager.Text(entry.IsComplete
				? "settings.runtime_backup.status_complete"
				: "settings.runtime_backup.status_incomplete")}]",
			entry => entry.Path,
			StringComparer.Ordinal);
		string? selection = await page.DisplayActionSheetAsync(
			LocalizationManager.Text("settings.runtime_backup.delete_title"),
			LocalizationManager.Text("common.cancel"),
			null,
			options.Keys.ToArray());
		return !string.IsNullOrWhiteSpace(selection) && options.TryGetValue(selection, out string? path)
			? path
			: null;
	}
}
