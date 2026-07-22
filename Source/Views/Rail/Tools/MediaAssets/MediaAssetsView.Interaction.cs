using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Views.Overlays;
using ComfyUI_Nexus.Views.Rail.Contracts;

namespace ComfyUI_Nexus.Views.Rail.Tools.MediaAssets;

public partial class MediaAssetsView
{	private void OnMediaAssetsSearchTextChanged(object? sender, TextChangedEventArgs e)
	{
		if (_isResettingSearchText)
		{
			return;
		}

		_searchText = e.NewTextValue?.Trim() ?? string.Empty;
		ClearMediaAssetsSearchButton.IsVisible = _searchText.Length > 0;
		ClearSelection();
		_ = ResetActiveScrollAsync();
		ApplyProjectionToCurrentSurface();
	}

	private void OnClearMediaAssetsSearchTapped(object? sender, TappedEventArgs e)
	{
		if (!string.IsNullOrEmpty(MediaAssetsSearchEntry.Text))
		{
			MediaAssetsSearchEntry.Text = string.Empty;
		}
	}

	private void OnMediaAssetsSearchPointerEntered(object? sender, PointerEventArgs e)
		=> _searchVisuals?.SetHovered(true);

	private void OnMediaAssetsSearchPointerExited(object? sender, PointerEventArgs e)
		=> _searchVisuals?.SetHovered(false);

	private void OnMediaAssetsSearchEntryFocused(object? sender, FocusEventArgs e)
	{
		_searchVisuals?.SetFocused(true);
		_searchTextController?.RefreshNativeSelectionColors();
	}

	private void OnMediaAssetsSearchEntryUnfocused(object? sender, FocusEventArgs e)
		=> _searchVisuals?.SetFocused(false);

	private void OnSelectAllMediaAssetsTapped(object? sender, TappedEventArgs e)
	{
		SelectAllVisibleAssets();
	}

	private void OnDeselectAllMediaAssetsTapped(object? sender, TappedEventArgs e)
	{
		ClearSelection();
	}

	private void ResetSearchText()
	{
		_isResettingSearchText = true;
		_searchText = string.Empty;
		MediaAssetsSearchEntry.Text = string.Empty;
		ClearMediaAssetsSearchButton.IsVisible = false;
		_isResettingSearchText = false;
	}

	private async void OnAssetDoubleTapped(object? sender, TappedEventArgs e)
	{
		if (sender is BindableObject { BindingContext: MediaAssetEntry entry })
		{
			await OpenAssetAsync(entry);
		}
	}

	private void OnAssetTapped(object? sender, TappedEventArgs e)
	{
		if (sender is not BindableObject { BindingContext: MediaAssetEntry entry })
		{
			return;
		}

		SelectFromPointer(entry.FullPath);
	}

	private void SelectFromPointer(string path)
	{
		if (NexusAppManager.Instance.Platform.Keyboard.IsShiftPressed())
		{
			SelectRange(path);
			return;
		}

		if (NexusAppManager.Instance.Platform.Keyboard.IsCtrlPressed())
		{
			ToggleSelection(path);
			return;
		}

		SelectSingle(path);
	}

	private void SelectSingle(string path)
	{
		_selection.ReplaceWithSingle(path);
		SyncVisibleSelectionState();
	}

	private void ToggleSelection(string path)
	{
		if (_selection.Contains(path))
		{
			var remaining = _selection.Paths
				.Where(selectedPath => !string.Equals(selectedPath, path, StringComparison.OrdinalIgnoreCase))
				.ToList();
			_selection.ReplaceAll(remaining, remaining.LastOrDefault());
		}
		else
		{
			var replacement = _selection.Paths.Append(path).ToList();
			_selection.ReplaceAll(replacement, path);
		}

		SyncVisibleSelectionState();
	}

	private void SelectRange(string path)
	{
		var visiblePaths = GetActiveSurface()
			.Items
			.Select(item => item.FullPath)
			.ToList();
		_selection.SelectRange(path, visiblePaths, _ => { }, _ => { });
		SyncVisibleSelectionState();
	}

	private void SelectAllVisibleAssets()
	{
		var visiblePaths = GetActiveSurface()
			.Items
			.Where(item => File.Exists(item.FullPath))
			.Select(item => item.FullPath)
			.ToList();
		if (visiblePaths.Count == 0)
		{
			return;
		}

		_selection.ReplaceAll(visiblePaths, visiblePaths[0]);
		SyncVisibleSelectionState();
	}

	private async Task OpenAssetAsync(MediaAssetEntry? entry)
	{
		if (entry == null || !File.Exists(entry.FullPath))
		{
			return;
		}

		if (TryRaiseViewerRequest(entry))
		{
			return;
		}

		var result = await NexusAppManager.Instance.Platform.Shell.OpenPathAsync(entry.FullPath);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			NexusLog.Warning($"Failed to open media asset: {result.Message}");
		}
	}

	private bool TryRaiseViewerRequest(MediaAssetEntry entry)
	{
		if (ViewerRequested == null || !TryCreateViewerRequest(entry.FullPath, out var request))
		{
			return false;
		}

		ViewerRequested.Invoke(this, request);
		return true;
	}

	private static MediaViewerItem ToMediaViewerItem(MediaAssetEntry item)
		=> new(
			item.Name,
			item.FullPath,
			JobId: item.JobId,
			Type: item.Type,
			Subfolder: item.Subfolder,
			IsBatchInferred: item.IsBatchInferred);

	private MediaAssetScopeSurface? GetSurfaceContaining(string path)
	{
		if (GetSurface(MediaAssetScope.Output).Items.Any(item => string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase)))
		{
			return GetSurface(MediaAssetScope.Output);
		}

		if (GetSurface(MediaAssetScope.Input).Items.Any(item => string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase)))
		{
			return GetSurface(MediaAssetScope.Input);
		}

		return null;
	}

	private MediaAssetEntry? GetPrimarySelectedEntry()
	{
		string? primaryPath = _selection.GetPrimarySelectedPath();
		if (string.IsNullOrWhiteSpace(primaryPath))
		{
			return null;
		}

		return GetSurface(MediaAssetScope.Output).Items
			.Concat(GetSurface(MediaAssetScope.Input).Items)
			.FirstOrDefault(item => string.Equals(item.FullPath, primaryPath, StringComparison.OrdinalIgnoreCase));
	}

	private async Task RevealAssetAsync(MediaAssetEntry? entry)
	{
		if (entry == null)
		{
			return;
		}

		PrepareContextSelection(entry);
		await _fileOperations.RevealInExplorerAsync(entry.FullPath);
	}

	private async Task BeginCopySelectedAsync(MediaAssetEntry? entry)
	{
		PrepareContextSelection(entry);
		var paths = GetSelectedExistingPaths();
		_fileOperations.BeginCopySelected(paths);
		await CopyFilesToSystemClipboardAsync(paths);
	}

	private async Task MoveSelectionAsync(MediaAssetEntry? entry)
	{
		PrepareContextSelection(entry);
		var selectedItems = GetSelectedExistingEntries();
		if (selectedItems.Count == 0)
		{
			return;
		}

		var folderResult = await NexusAppManager.Instance.Platform.FilePicker.PickFolderAsync(
			LocalizationManager.Text("views.rail.tools.media_assets.media_assets_view.move_picker_title"));
		if (!folderResult.IsSuccess || string.IsNullOrWhiteSpace(folderResult.Value))
		{
			if (!string.IsNullOrWhiteSpace(folderResult.Message))
			{
				NexusLog.Warning($"[MEDIA_ASSETS] Move target selection failed: {folderResult.Message}");
			}

			return;
		}

		string destinationDirectory = folderResult.Value;
		if (!Directory.Exists(destinationDirectory))
		{
			NexusLog.Warning($"[MEDIA_ASSETS] Move target directory was not found: {destinationDirectory}");
			return;
		}

		var movableItems = selectedItems
			.Where(item => !string.Equals(Path.GetDirectoryName(item.FullPath), destinationDirectory, StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (movableItems.Count == 0)
		{
			return;
		}

		bool hasConflict = HasMoveNameConflict(movableItems, destinationDirectory);
		string message = LocalizationManager.Format(
			"views.rail.tools.media_assets.media_assets_view.move_confirm_message",
			movableItems.Count,
			destinationDirectory);
		if (hasConflict)
		{
			message = $"{message}{Environment.NewLine}{Environment.NewLine}{LocalizationManager.Text("views.rail.tools.media_assets.media_assets_view.move_conflict_message")}";
		}

		bool shouldMove = await _appManager.Dialogs.ConfirmAsync(
			LocalizationManager.Text("views.rail.tools.media_assets.media_assets_view.move_confirm_title"),
			message,
			LocalizationManager.Text("views.rail.tools.media_assets.media_assets_view.move"),
			LocalizationManager.Text("common.cancel"));
		if (!shouldMove)
		{
			return;
		}

		try
		{
			var movedItems = await _operations.RunBackgroundAsync(
				NexusBackgroundLane.FileIo,
				"media-move",
				_ => MoveMediaItems(movableItems, destinationDirectory));
			if (movedItems.Count == 0)
			{
				return;
			}

			ClearSelection();
			RefreshAfterFileOperation(GetMoveTouchedDirectories(movedItems).ToArray());
			await CleanupMovedOutputJobsAsync(movedItems);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[MEDIA_ASSETS] Failed to move selected media files");
			throw;
		}
	}

	private static bool HasMoveNameConflict(IReadOnlyList<MediaAssetEntry> items, string destinationDirectory)
	{
		var reservedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var item in items)
		{
			string destinationPath = Path.Combine(destinationDirectory, item.Name);
			if (!reservedDestinations.Add(destinationPath) || File.Exists(destinationPath))
			{
				return true;
			}
		}

		return false;
	}

	private static List<MovedMediaAsset> MoveMediaItems(IReadOnlyList<MediaAssetEntry> items, string destinationDirectory)
	{
		var movedItems = new List<MovedMediaAsset>();
		var reservedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var item in items)
		{
			if (!File.Exists(item.FullPath))
			{
				continue;
			}

			string destinationPath = GetUniqueMoveDestinationPath(
				Path.Combine(destinationDirectory, item.Name),
				reservedDestinations);
			reservedDestinations.Add(destinationPath);
			if (string.Equals(item.FullPath, destinationPath, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			MoveFileWithFallback(item.FullPath, destinationPath);
			movedItems.Add(new MovedMediaAsset(item, item.FullPath, destinationPath));
		}

		return movedItems;
	}

	private static string GetUniqueMoveDestinationPath(string destinationPath, ISet<string> reservedDestinations)
	{
		if (!File.Exists(destinationPath) &&
			!reservedDestinations.Contains(destinationPath))
		{
			return destinationPath;
		}

		string directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
		string fileName = Path.GetFileNameWithoutExtension(destinationPath);
		string extension = Path.GetExtension(destinationPath);
		int suffix = 1;

		while (true)
		{
			string candidate = Path.Combine(directory, $"{fileName} - Copy {suffix}{extension}");
			if (!File.Exists(candidate) &&
				!reservedDestinations.Contains(candidate))
			{
				return candidate;
			}

			suffix++;
		}
	}

	private static void MoveFileWithFallback(string sourcePath, string destinationPath)
	{
		try
		{
			File.Move(sourcePath, destinationPath);
		}
		catch (IOException)
		{
			File.Copy(sourcePath, destinationPath, overwrite: false);
			File.Delete(sourcePath);
		}
	}

	private static IEnumerable<string?> GetMoveTouchedDirectories(IReadOnlyList<MovedMediaAsset> movedItems)
	{
		foreach (var item in movedItems)
		{
			yield return Path.GetDirectoryName(item.SourcePath);
			yield return Path.GetDirectoryName(item.DestinationPath);
		}
	}

	private async Task CleanupMovedOutputJobsAsync(IReadOnlyList<MovedMediaAsset> movedItems)
	{
		if (_staleOutputJobCleanupHandler == null)
		{
			return;
		}

		var jobIds = movedItems
			.Select(item => item.Entry.JobId)
			.Where(jobId => !string.IsNullOrWhiteSpace(jobId))
			.Select(jobId => jobId!)
			.Distinct(StringComparer.Ordinal)
			.ToList();
		if (jobIds.Count == 0)
		{
			return;
		}

		try
		{
			await _staleOutputJobCleanupHandler(jobIds);
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"[MEDIA_ASSETS] Failed to clean moved output jobs: {ex.GetType().Name} - {ex.Message}");
		}
	}

	private async Task CopySelectedAssetPathsAsync(MediaAssetEntry? entry)
	{
		PrepareContextSelection(entry);
		var paths = GetSelectedExistingPaths();
		if (paths.Count == 0)
		{
			return;
		}

		try
		{
			await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(
				string.Join(Environment.NewLine, paths));
		}
		catch
		{
		}
	}

	private async Task RenameSelectionAsync(MediaAssetEntry? entry)
	{
		PrepareContextSelection(entry);
		await _fileOperations.RenameSelectionAsync(GetSelectedExistingPaths());
	}

	private async Task DeleteSelectionAsync(MediaAssetEntry? entry)
	{
		if (_deleteSelectionInFlight)
		{
			return;
		}

		PrepareContextSelection(entry);
		var selectedItems = GetSelectedExistingEntries();
		if (selectedItems.Count == 0)
		{
			return;
		}

		string message = selectedItems.Count == 1
			? LocalizationManager.Format(
				"views.overlays.media_viewer.delete_media_message",
				Path.GetFileName(selectedItems[0].FullPath))
			: LocalizationManager.Format(
				"views.rail.tools.media_assets.media_assets_view.delete_selected_message",
				selectedItems.Count);

		_deleteSelectionInFlight = true;
		try
		{
			await _appManager.Dialogs.ConfirmAsync(
				LocalizationManager.Text("views.overlays.media_viewer.delete_media_title"),
				message,
				LocalizationManager.Text("common.delete"),
				LocalizationManager.Text("common.cancel"),
				onOk: async () =>
			{
				try
				{
					var viewerItems = selectedItems.Select(ToMediaViewerItem).ToList();
					if (_deleteHandler != null)
					{
						bool deleted = await _deleteHandler(viewerItems);
						if (!deleted)
						{
							return NexusDialogActionResult.KeepOpen;
						}
					}
					else
					{
						var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
						foreach (var item in selectedItems)
						{
							string? parentDirectory = Path.GetDirectoryName(item.FullPath);
							if (!string.IsNullOrWhiteSpace(parentDirectory))
							{
								touchedDirectories.Add(parentDirectory);
							}

							if (File.Exists(item.FullPath))
							{
								File.Delete(item.FullPath);
							}
						}

						RefreshAfterFileOperation(touchedDirectories.ToArray());
					}

					_selection.Clear();
				}
				catch (Exception ex)
				{
					NexusLog.Exception(ex, "[MEDIA_ASSETS] Failed to delete selected media files");
					throw;
				}

				return NexusDialogActionResult.Close;
			},
			returnFocusTarget: NexusDialogReturnFocusTarget.App);
		}
		finally
		{
			_deleteSelectionInFlight = false;
		}
	}

	private static async Task CopyFilesToSystemClipboardAsync(IReadOnlyList<string> paths)
	{
		if (paths.Count == 0)
		{
			return;
		}

		var result = await NexusAppManager.Instance.Platform.FileClipboard.SetFilesAsync(paths, cut: false);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			NexusLog.Warning($"[MEDIA_ASSETS] Failed to set OS file clipboard: {result.Message}");
		}
	}

	private void PrepareContextSelection(MediaAssetEntry? entry)
	{
		if (entry == null)
		{
			return;
		}

		if (_selection.Contains(entry.FullPath))
		{
			_selection.EnsureAnchor(entry.FullPath);
		}
		else
		{
			_selection.ReplaceWithSingle(entry.FullPath);
		}

		SyncVisibleSelectionState();
	}

	private List<string> GetSelectedExistingPaths()
		=> GetSurface(MediaAssetScope.Output).Items
			.Concat(GetSurface(MediaAssetScope.Input).Items)
			.Select(item => item.FullPath)
			.Where(_selection.Contains)
			.Where(File.Exists)
			.ToList();

	private List<MediaAssetEntry> GetSelectedExistingEntries()
		=> GetSurface(MediaAssetScope.Output).Items
			.Concat(GetSurface(MediaAssetScope.Input).Items)
			.Where(item => _selection.Contains(item.FullPath))
			.Where(item => File.Exists(item.FullPath))
			.ToList();

	private void ClearSelection()
	{
		_selection.Clear();
		SyncVisibleSelectionState();
	}

	private void SyncVisibleSelectionState()
	{
		foreach (var item in GetSurface(MediaAssetScope.Output).Items)
		{
			item.IsSelected = _selection.Contains(item.FullPath);
		}

		foreach (var item in GetSurface(MediaAssetScope.Input).Items)
		{
			item.IsSelected = _selection.Contains(item.FullPath);
		}

		foreach (var cell in GetSurface(MediaAssetScope.Output).VisibleCells.Values)
		{
			cell.RefreshVisualState();
		}

		foreach (var cell in GetSurface(MediaAssetScope.Input).VisibleCells.Values)
		{
			cell.RefreshVisualState();
		}
	}

	private async Task OpenPathAsync(string path)
	{
		var result = await NexusAppManager.Instance.Platform.Shell.OpenPathAsync(path);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			NexusLog.Warning($"Failed to open media assets path: {result.Message}");
		}
	}

}
