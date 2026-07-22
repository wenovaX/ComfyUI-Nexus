namespace ComfyUI_Nexus.Views.Overlays;

using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Services;

public partial class SettingsOverlayView
{
	private void RebuildModelLibrariesList()
	{
		ModelLibrariesList.Children.Clear();
		var draft = _editor.Draft;
		if (draft.ModelLibraryRoots.Count == 0)
		{
			ModelLibrariesList.Children.Add(CreateModelLibraryRow(
				string.Empty,
				LocalizationManager.Format("settings.model_libraries.library", 1),
				index: -1));
			return;
		}

		for (int index = 0; index < draft.ModelLibraryRoots.Count; index++)
		{
			string path = ExtraModelPathsService.NormalizeFileSystemPath(draft.ModelLibraryRoots[index]);
			ModelLibrariesList.Children.Add(CreateModelLibraryRow(
				path,
				LocalizationManager.Format("settings.model_libraries.library", index + 1),
				index));
		}
	}

	private View CreateModelLibraryRow(string path, string title, int index)
	{
		var titleLabel = new Label
		{
			Text = title,
			Style = (Style)Resources["SettingsKeyStyle"],
			VerticalOptions = LayoutOptions.Center
		};
		var pathLabel = new Label
		{
			Text = path.Length == 0
				? LocalizationManager.Text("settings.model_libraries.not_connected")
				: Directory.Exists(path)
					? path
					: LocalizationManager.Format("settings.model_libraries.unavailable", path),
			Style = (Style)Resources["SettingsValueStyle"],
			LineBreakMode = LineBreakMode.TailTruncation,
			VerticalOptions = LayoutOptions.Center
		};
		if (path.Length > 0 && !Directory.Exists(path))
		{
			pathLabel.TextColor = SettingsWarningTextColor;
		}
		var textStack = new VerticalStackLayout
		{
			Spacing = 3,
			VerticalOptions = LayoutOptions.Center,
			Children = { titleLabel, pathLabel }
		};

		var actionLayout = new HorizontalStackLayout
		{
			Spacing = 6,
			HorizontalOptions = LayoutOptions.End,
			VerticalOptions = LayoutOptions.Center
		};
		if (path.Length > 0)
		{
			actionLayout.Children.Add(CreateModelLibraryButton(
				LocalizationManager.Text("settings.model_libraries.open"),
				OnOpenModelLibraryClicked,
				new ModelLibraryActionContext(index, path),
				isEnabled: Directory.Exists(path)));
		}

		actionLayout.Children.Add(CreateModelLibraryButton(
			path.Length == 0
				? LocalizationManager.Text("settings.model_libraries.connect")
				: LocalizationManager.Text("settings.model_libraries.replace"),
			OnReplaceModelLibraryClicked,
			new ModelLibraryActionContext(index, path)));

		if (path.Length > 0)
		{
			actionLayout.Children.Add(CreateModelLibraryButton(
				LocalizationManager.Text("settings.model_libraries.remove"),
				OnRemoveModelLibraryClicked,
				new ModelLibraryActionContext(index, path)));
		}

		var rowGrid = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Auto)
			},
			ColumnSpacing = 10
		};
		rowGrid.Children.Add(textStack);
		Grid.SetColumn(actionLayout, 1);
		rowGrid.Children.Add(actionLayout);

		return new Border
		{
			Style = (Style)Resources["SettingsInsetBoxStyle"],
			StrokeThickness = 0,
			Padding = new Thickness(12, 9),
			Content = rowGrid
		};
	}

	private Button CreateModelLibraryButton(
		string text,
		EventHandler clicked,
		ModelLibraryActionContext context,
		bool isEnabled = true)
	{
		var button = new Button
		{
			Text = text,
			Style = (Style)Resources["SettingsTextButtonStyle"],
			CommandParameter = context,
			IsEnabled = isEnabled
		};
		button.Clicked += clicked;
		return button;
	}

	private async void OnAddModelLibraryClicked(object? sender, EventArgs e)
	{
		await SelectModelLibraryAsync(_editor.Draft.ModelLibraryRoots.Count);
	}

	private async void OnSyncModelLibraryStructureClicked(object? sender, EventArgs e)
	{
		const string operationId = "sync-model-library-structure";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		await SetSettingsOperationBlockerVisibleAsync(
			true,
			LocalizationManager.Text("settings.model_libraries.syncing_title"),
			LocalizationManager.Text("settings.model_libraries.syncing_detail"));
		try
		{
			var settings = SettingsService.Settings;
			string comfyPath = GetEffectiveComfyPath(settings);
			var applyResult = await Task.Run(() =>
			{
				ExtraModelPathsResult result = ExtraModelPathsService.TryApply(
					settings,
					comfyPath,
					out ExtraModelPathsTransaction? transaction);
				return (Result: result, Transaction: transaction);
			});

			if (!applyResult.Result.IsSuccess)
			{
				applyResult.Transaction?.Rollback();
				SetModelLibrariesStatus(applyResult.Result.Message, SettingsFailureTextColor);
				return;
			}

			applyResult.Transaction?.Commit();
			_modelLibrariesRestartRequired = true;
			SetModelLibrariesStatus(
				LocalizationManager.Text("settings.model_libraries.sync_complete"),
				SettingsSuccessTextColor);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[MODEL PATHS] Structure synchronization failed");
			SetModelLibrariesStatus(
				LocalizationManager.Format("settings.model_libraries.sync_failed", ex.Message),
				SettingsFailureTextColor);
		}
		finally
		{
			await SetSettingsOperationBlockerVisibleAsync(false);
			EndOperation(operationId);
			UpdateStateChrome();
		}
	}

	private void SetModelLibrariesStatus(string message, Color color)
	{
		ModelLibrariesStatusLabel.Text = message;
		ModelLibrariesStatusLabel.TextColor = color;
		ModelLibrariesStatusLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
	}

	private async void OnReplaceModelLibraryClicked(object? sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: ModelLibraryActionContext context })
		{
			await SelectModelLibraryAsync(context.Index);
		}
	}

	private async void OnRemoveModelLibraryClicked(object? sender, EventArgs e)
	{
		if (sender is not Button { CommandParameter: ModelLibraryActionContext context })
		{
			return;
		}

		if (context.Index >= 0 && context.Index < _editor.Draft.ModelLibraryRoots.Count)
		{
			_editor.Draft.ModelLibraryRoots.RemoveAt(context.Index);
		}

		RebuildModelLibrariesList();
		RebuildRuntimeBackupExternalLibraries();
		UpdateStateChrome();
		await Task.CompletedTask;
	}

	private async void OnOpenModelLibraryClicked(object? sender, EventArgs e)
	{
		if (sender is not Button { CommandParameter: ModelLibraryActionContext context }
			|| string.IsNullOrWhiteSpace(context.Path))
		{
			return;
		}

		var result = await NexusAppManager.Instance.Platform.Shell.OpenPathAsync(context.Path);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			await ShowValidationAlertAsync(result.Message);
		}
	}

	private async Task SelectModelLibraryAsync(int index)
	{
		var result = await NexusAppManager.Instance.Platform.FilePicker.PickFolderAsync(
			LocalizationManager.Text("settings.model_libraries.folder_picker_title"));
		if (!result.IsSupported || !result.IsSuccess || string.IsNullOrWhiteSpace(result.Value))
		{
			return;
		}

		string normalized = ExtraModelPathsService.NormalizeFileSystemPath(result.Value);
		string currentPath = index >= 0 && index < _editor.Draft.ModelLibraryRoots.Count
			? ExtraModelPathsService.NormalizeFileSystemPath(_editor.Draft.ModelLibraryRoots[index])
			: string.Empty;
		if (string.Equals(currentPath, normalized, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		if (ExtraModelPathsService.ContainsRoot(_editor.Draft, normalized))
		{
			await ShowValidationAlertAsync(LocalizationManager.Text("settings.model_libraries.duplicate"));
			return;
		}

		if (index >= 0 && index < _editor.Draft.ModelLibraryRoots.Count)
		{
			_editor.Draft.ModelLibraryRoots[index] = normalized;
		}
		else
		{
			_editor.Draft.ModelLibraryRoots.Add(normalized);
		}

		RebuildModelLibrariesList();
		RebuildRuntimeBackupExternalLibraries();
		UpdateStateChrome();
	}

	private async void OnScanModelDuplicatesClicked(object? sender, EventArgs e)
	{
		const string operationId = "scan-model-duplicates";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		if (_modelDuplicateScanCancellation is not null)
		{
			return;
		}

		int scanGeneration = ++_modelDuplicateScanGeneration;
		_modelDuplicateScanCancellation = new CancellationTokenSource();
		CancellationToken cancellationToken = _modelDuplicateScanCancellation.Token;
		await SetSettingsOperationBlockerVisibleAsync(
			true,
			LocalizationManager.Text("settings.model_duplicates.scanning_title"),
			LocalizationManager.Text("settings.model_duplicates.scanning_detail"),
			allowCancel: true);
		_modelDuplicateScanResult = null;
		_modelDuplicateGroupIndex = 0;
		UpdateModelDuplicateScanUi();
		UpdateStateChrome();
		try
		{
			string comfyPath = GetEffectiveComfyPath(_editor.Draft);
			var roots = _editor.Draft.ModelLibraryRoots.ToList();
			var progress = new Progress<ModelDuplicateScanProgress>(scanProgress =>
			{
				if (scanGeneration != _modelDuplicateScanGeneration || cancellationToken.IsCancellationRequested)
				{
					return;
				}

				SettingsOperationBlockerDetailLabel.Text = GetModelDuplicateScanProgressText(scanProgress);
				UpdateSettingsOperationBlockerProgress(scanProgress.Progress);
			});
			ModelDuplicateScanResult result = await Task.Run(
				() => ModelDuplicateScanService.ScanAsync(comfyPath, roots, cancellationToken, progress),
				cancellationToken);
			if (scanGeneration != _modelDuplicateScanGeneration || cancellationToken.IsCancellationRequested)
			{
				return;
			}

			_modelDuplicateScanResult = result;
			_modelDuplicateGroupIndex = 0;
			UpdateModelDuplicateScanUi();
		}
		catch (OperationCanceledException)
		{
			if (scanGeneration == _modelDuplicateScanGeneration)
			{
				ModelDuplicateStatusLabel.Text = LocalizationManager.Text("settings.model_duplicates.cancelled");
				ModelDuplicateStatusLabel.TextColor = SettingsMutedTextColor;
			}
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[MODEL DUPLICATES] Scan failed");
			if (scanGeneration == _modelDuplicateScanGeneration)
			{
				ModelDuplicateStatusLabel.Text = LocalizationManager.Format(
					"settings.model_duplicates.scan_failed",
					ex.Message);
				ModelDuplicateStatusLabel.TextColor = SettingsFailureTextColor;
			}
		}
		finally
		{
			await SetSettingsOperationBlockerVisibleAsync(false);
			if (ReferenceEquals(_modelDuplicateScanCancellation, null) == false
				&& scanGeneration == _modelDuplicateScanGeneration)
			{
				_modelDuplicateScanCancellation.Dispose();
				_modelDuplicateScanCancellation = null;
			}

			EndOperation(operationId);
			UpdateStateChrome();
		}
	}

	private static string GetModelDuplicateScanProgressText(ModelDuplicateScanProgress progress)
	{
		return progress.Stage switch
		{
			ModelDuplicateScanStage.DiscoveringFiles => LocalizationManager.Format(
				"settings.model_duplicates.progress_discovering",
				progress.ProcessedFiles,
				RuntimeBackupService.FormatBytes(progress.ProcessedBytes)),
			ModelDuplicateScanStage.PreparingHashes => LocalizationManager.Format(
				"settings.model_duplicates.progress_preparing",
				progress.TotalFiles,
				RuntimeBackupService.FormatBytes(progress.TotalBytes)),
			ModelDuplicateScanStage.HashingFiles => LocalizationManager.Format(
				"settings.model_duplicates.progress_hashing",
				progress.ProcessedFiles,
				progress.TotalFiles,
				RuntimeBackupService.FormatBytes(progress.ProcessedBytes),
				RuntimeBackupService.FormatBytes(progress.TotalBytes)),
			ModelDuplicateScanStage.WritingReport => LocalizationManager.Text("settings.model_duplicates.progress_report"),
			_ => LocalizationManager.Text("settings.model_duplicates.scanning_detail")
		};
	}

	private async void OnOpenModelDuplicateReportClicked(object? sender, EventArgs e)
	{
		if (_modelDuplicateScanResult is not { ReportPath.Length: > 0 } result || !File.Exists(result.ReportPath))
		{
			return;
		}

		var openResult = await NexusAppManager.Instance.Platform.Shell.OpenPathAsync(result.ReportPath);
		if (!openResult.IsSuccess && !string.IsNullOrWhiteSpace(openResult.Message))
		{
			SetModelLibrariesStatus(openResult.Message, SettingsFailureTextColor);
		}
	}

	private async void OnOpenDuplicateLocation1Clicked(object? sender, EventArgs e)
		=> await OpenCurrentDuplicatePathAsync(0);

	private async void OnOpenDuplicateLocation2Clicked(object? sender, EventArgs e)
		=> await OpenCurrentDuplicatePathAsync(1);

	private void OnNextModelDuplicateClicked(object? sender, EventArgs e)
	{
		if (_modelDuplicateScanResult is not { Groups.Count: > 0 } result)
		{
			return;
		}

		_modelDuplicateGroupIndex = (_modelDuplicateGroupIndex + 1) % result.Groups.Count;
		UpdateModelDuplicateScanUi();
	}

	private async Task OpenCurrentDuplicatePathAsync(int fileIndex)
	{
		ModelDuplicateGroup? group = GetCurrentDuplicateGroup();
		if (group is null || fileIndex < 0 || fileIndex >= group.Files.Count)
		{
			return;
		}

		ModelDuplicateFile file = group.Files[fileIndex];
		string path = System.IO.Path.GetDirectoryName(file.FullPath) ?? file.SourceRoot;
		var result = await NexusAppManager.Instance.Platform.Shell.OpenPathAsync(path);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			SetModelLibrariesStatus(result.Message, SettingsFailureTextColor);
		}
	}

	private void UpdateModelDuplicateScanUi()
	{
		if (_modelDuplicateScanResult is null)
		{
			ModelDuplicateStatusLabel.Text = LocalizationManager.Text("settings.model_duplicates.ready");
			ModelDuplicateStatusLabel.TextColor = SettingsMutedTextColor;
			UpdateModelDuplicateScanButtons(_activeOperations.Count > 0 || _isComfyActionBusy);
			return;
		}

		if (!_modelDuplicateScanResult.HasDuplicates)
		{
			ModelDuplicateStatusLabel.Text = LocalizationManager.Format(
				"settings.model_duplicates.none_found",
				_modelDuplicateScanResult.ScannedFileCount,
				RuntimeBackupService.FormatBytes(_modelDuplicateScanResult.ScannedBytes));
			ModelDuplicateStatusLabel.TextColor = SettingsSuccessTextColor;
			UpdateModelDuplicateScanButtons(_activeOperations.Count > 0 || _isComfyActionBusy);
			return;
		}

		ModelDuplicateGroup group = GetCurrentDuplicateGroup()!;
		string foundKey = group.Files.Count > 2
			? "settings.model_duplicates.found_many"
			: "settings.model_duplicates.found";
		ModelDuplicateStatusLabel.Text = LocalizationManager.Format(
			foundKey,
			_modelDuplicateGroupIndex + 1,
			_modelDuplicateScanResult.Groups.Count,
			group.Files[0].FileName,
			RuntimeBackupService.FormatBytes(group.Length),
			group.Files.Count,
			Math.Max(0, group.Files.Count - 2));
		ModelDuplicateStatusLabel.TextColor = SettingsWarningTextColor;
		UpdateModelDuplicateScanButtons(_activeOperations.Count > 0 || _isComfyActionBusy);
	}

	private void UpdateModelDuplicateScanButtons(bool hasActiveOperations)
	{
		bool hasResult = _modelDuplicateScanResult is not null;
		bool hasGroups = _modelDuplicateScanResult is { Groups.Count: > 0 };
		ModelDuplicateGroup? group = GetCurrentDuplicateGroup();
		OpenModelDuplicateReportButton.IsEnabled = !hasActiveOperations
			&& hasResult
			&& File.Exists(_modelDuplicateScanResult!.ReportPath);
		OpenDuplicateLocation1Button.IsEnabled = !hasActiveOperations && group?.Files.Count >= 1;
		OpenDuplicateLocation2Button.IsEnabled = !hasActiveOperations && group?.Files.Count >= 2;
		NextModelDuplicateButton.IsEnabled = !hasActiveOperations && hasGroups;
	}

	private ModelDuplicateGroup? GetCurrentDuplicateGroup()
	{
		if (_modelDuplicateScanResult is not { Groups.Count: > 0 } result)
		{
			return null;
		}

		_modelDuplicateGroupIndex = Math.Clamp(_modelDuplicateGroupIndex, 0, result.Groups.Count - 1);
		return result.Groups[_modelDuplicateGroupIndex];
	}

	private const string InternalSourceKind = "internal";
	private const string ExternalSourceKind = "external";

	private sealed record ModelLibraryActionContext(int Index, string Path);
}
