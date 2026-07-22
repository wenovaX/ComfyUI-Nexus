using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

/// <summary>
/// Executes mutating filesystem operations for the asset browser and reports touched directories back to the view.
/// </summary>
internal sealed class AssetFileOperationService
{
	private sealed record PasteOperation(
		string SourcePath,
		string DestinationPath,
		string? SourceDirectory,
		bool SourceIsDirectory,
		bool HasConflict);

	private sealed record MoveOperation(
		string SourcePath,
		string DestinationPath,
		string? SourceDirectory,
		string SourceName,
		bool HasConflict);

	private sealed record DuplicateOperation(
		string SourcePath,
		string DestinationPath,
		string DestinationDirectory);

	private readonly AssetSelectionController _selection;
	private readonly AssetClipboardController _clipboard;
	private readonly NexusDialogService _dialogs;
	private readonly Func<string> _getRootPath;
	private readonly Func<string, Task> _openInOsAsync;
	private readonly Action<string?[]> _refreshDirectoriesImmediately;
	private readonly Action _refreshVisibleSelectionState;
	private readonly Action<string, string>? _notifyFolderCreated;
	private readonly Action<string>? _notifyDirectoryContentAdded;
	private readonly Func<AssetPathMutation, Task<AssetMutationPreparationResult>>? _prepareMutationAsync;
	private readonly Func<AssetPathMutation, bool, Task>? _completeMutationAsync;
	private readonly Func<int, Task>? _beginBatchOperationAsync;
	private readonly Func<Task>? _endBatchOperationAsync;
	private readonly Func<string, bool>? _shouldRegenerateCopiedWorkflowMetadata;

	/// <summary>
	/// Creates a file operation service around view-owned selection, clipboard, and refresh callbacks.
	/// </summary>
	/// <param name="selection">Shared selection state for asset rows.</param>
	/// <param name="clipboard">Shared copy/cut clipboard state.</param>
	/// <param name="getRootPath">Returns the current asset root directory.</param>
	/// <param name="openInOsAsync">Fallback opener for folders or files.</param>
	/// <param name="refreshDirectoriesImmediately">Refresh callback receiving directories touched by a mutation.</param>
	/// <param name="refreshVisibleSelectionState">Refresh callback for visible row selection/clipboard visuals.</param>
	/// <param name="notifyFolderCreated">Optional callback receiving parent path and created folder path.</param>
	/// <param name="notifyDirectoryContentAdded">Optional callback receiving a directory that should be expanded before refresh.</param>
	internal AssetFileOperationService(
		NexusDialogService dialogs,
		AssetSelectionController selection,
		AssetClipboardController clipboard,
		Func<string> getRootPath,
		Func<string, Task> openInOsAsync,
		Action<string?[]> refreshDirectoriesImmediately,
		Action refreshVisibleSelectionState,
		Action<string, string>? notifyFolderCreated = null,
		Action<string>? notifyDirectoryContentAdded = null,
		Func<AssetPathMutation, Task<AssetMutationPreparationResult>>? prepareMutationAsync = null,
		Func<AssetPathMutation, bool, Task>? completeMutationAsync = null,
		Func<int, Task>? beginBatchOperationAsync = null,
		Func<Task>? endBatchOperationAsync = null,
		Func<string, bool>? shouldRegenerateCopiedWorkflowMetadata = null)
	{
		_dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
		_selection = selection;
		_clipboard = clipboard;
		_getRootPath = getRootPath;
		_openInOsAsync = openInOsAsync;
		_refreshDirectoriesImmediately = refreshDirectoriesImmediately;
		_refreshVisibleSelectionState = refreshVisibleSelectionState;
		_notifyFolderCreated = notifyFolderCreated;
		_notifyDirectoryContentAdded = notifyDirectoryContentAdded;
		_prepareMutationAsync = prepareMutationAsync;
		_completeMutationAsync = completeMutationAsync;
		_beginBatchOperationAsync = beginBatchOperationAsync;
		_endBatchOperationAsync = endBatchOperationAsync;
		_shouldRegenerateCopiedWorkflowMetadata = shouldRegenerateCopiedWorkflowMetadata;
	}

	private Task<AssetMutationPreparationResult> PrepareMutationAsync(AssetPathMutation mutation)
		=> _prepareMutationAsync?.Invoke(mutation) ?? Task.FromResult(AssetMutationPreparationResult.Proceed);

	private Task CompleteMutationAsync(AssetPathMutation mutation, bool succeeded)
		=> _completeMutationAsync?.Invoke(mutation, succeeded) ?? Task.CompletedTask;

	private Task BeginBatchOperationAsync(int itemCount)
		=> _beginBatchOperationAsync?.Invoke(itemCount) ?? Task.CompletedTask;

	private Task EndBatchOperationAsync()
		=> _endBatchOperationAsync?.Invoke() ?? Task.CompletedTask;

	private async Task<bool> ExecuteMutationAsync(AssetPathMutation mutation, Action mutate)
	{
		NexusLog.Info($"[ASSET_MUTATION] Start: {mutation.Kind} '{mutation.SourcePath}' -> '{mutation.DestinationPath}'");
		AssetMutationPreparationResult preparation = await PrepareMutationAsync(mutation);
		if (preparation == AssetMutationPreparationResult.Cancel)
		{
			return false;
		}

		bool succeeded = false;
		try
		{
			if (preparation == AssetMutationPreparationResult.Proceed)
			{
				await Task.Run(mutate);
			}
			succeeded = true;
			return true;
		}
		finally
		{
			await CompleteMutationAsync(mutation, succeeded);
			NexusLog.Info($"[ASSET_MUTATION] Complete: {mutation.Kind}, succeeded={succeeded}");
		}
	}

	/// <summary>
	/// Stores the current selection as a copy operation.
	/// </summary>
	/// <param name="selectedPaths">Absolute paths selected in the asset browser.</param>
	internal void BeginCopySelected(IReadOnlyList<string> selectedPaths)
	{
		if (selectedPaths.Count == 0)
		{
			return;
		}

		_clipboard.SetCopy(selectedPaths);
		_refreshVisibleSelectionState();
	}

	/// <summary>
	/// Stores the current selection as a cut operation.
	/// </summary>
	/// <param name="selectedPaths">Absolute paths selected in the asset browser.</param>
	internal void BeginCutSelected(IReadOnlyList<string> selectedPaths)
	{
		if (selectedPaths.Count == 0)
		{
			return;
		}

		_clipboard.SetCut(selectedPaths);
		_refreshVisibleSelectionState();
	}

	/// <summary>
	/// Imports external OS-dropped files or directories into the asset browser.
	/// </summary>
	/// <param name="sourcePaths">Absolute source paths supplied by the OS drag/drop payload.</param>
	/// <param name="targetPath">Optional target directory; defaults to the current root path.</param>
	internal async Task ImportDroppedPathsAsync(IReadOnlyList<string> sourcePaths, string? targetPath = null)
	{
		if (sourcePaths == null || sourcePaths.Count == 0)
		{
			return;
		}

		string destinationDirectory = targetPath ?? _getRootPath();
		if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
		{
			return;
		}

		var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			destinationDirectory
		};

		try
		{
			foreach (var sourcePath in sourcePaths)
			{
				if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
				{
					continue;
				}

				string sourceName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
				string destinationPath = GetUniqueDestinationPath(Path.Combine(destinationDirectory, sourceName));

				if (Directory.Exists(sourcePath))
				{
					if (destinationDirectory.StartsWith(sourcePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					CopyDirectory(sourcePath, destinationPath);
				}
				else if (File.Exists(sourcePath))
				{
					CopyFile(sourcePath, destinationPath);
				}
			}

			_refreshDirectoriesImmediately(touchedDirectories.ToArray());
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "Failed to import paths");
		}
	}

	/// <summary>
	/// Moves existing asset-browser items into another directory after user confirmation.
	/// </summary>
	/// <param name="sourcePaths">Absolute source paths to move.</param>
	/// <param name="destinationDirectory">Absolute destination directory.</param>
	internal async Task MovePathsAsync(IReadOnlyList<string> sourcePaths, string destinationDirectory)
	{
		if (sourcePaths == null || sourcePaths.Count == 0 || string.IsNullOrWhiteSpace(destinationDirectory))
		{
			return;
		}

		var validSources = sourcePaths
			.Where(File.Exists)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Where(path =>
			{
				string? currentDir = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
				return !string.Equals(currentDir, destinationDirectory, StringComparison.OrdinalIgnoreCase);
			})
			.ToList();

		if (validSources.Count == 0)
		{
			return;
		}

		string targetName = Path.GetFileName(destinationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (string.IsNullOrWhiteSpace(targetName))
		{
			targetName = destinationDirectory;
		}

		var operations = ResolveMoveOperations(validSources, destinationDirectory);
		if (operations.Count == 0)
		{
			return;
		}

		string message = BuildMoveConfirmationMessage(operations, targetName);

		await _dialogs.ConfirmAsync(
			LocalizationManager.Text("views.rail.tools.assets.file_operations.move_title"),
			message,
			LocalizationManager.Text("views.rail.tools.assets.file_operations.move_button"),
			LocalizationManager.Text("common.cancel"),
			onOk: async () =>
			{
				var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { destinationDirectory };
				bool isBatch = operations.Count > 1;
				if (isBatch)
				{
					await BeginBatchOperationAsync(operations.Count);
					await Task.Yield();
				}
				try
				{
					foreach (var operation in operations)
					{
						if (!File.Exists(operation.SourcePath))
						{
							continue;
						}

						if (!string.IsNullOrWhiteSpace(operation.SourceDirectory))
						{
							touchedDirectories.Add(operation.SourceDirectory);
						}

						var mutation = new AssetPathMutation(
							AssetPathMutationKind.Move,
							operation.SourcePath,
							operation.DestinationPath,
							IsDirectory: false,
							IsBatch: isBatch);
						bool moved = await ExecuteMutationAsync(
							mutation,
							() => File.Move(operation.SourcePath, operation.DestinationPath));
						if (!moved)
						{
							return NexusDialogActionResult.KeepOpen;
						}
					}

					_refreshDirectoriesImmediately(touchedDirectories.ToArray());
				}
				catch (Exception ex)
				{
					NexusLog.Exception(ex, "Failed to move paths");
					throw;
				}
				finally
				{
					if (isBatch)
					{
						await EndBatchOperationAsync();
					}
				}

				return NexusDialogActionResult.Close;
			});
	}

	internal async Task DuplicatePathsAsync(IReadOnlyList<string> sourcePaths, string destinationDirectory)
	{
		if (sourcePaths == null || sourcePaths.Count == 0 || string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
		{
			return;
		}

		var operations = ResolveDuplicateOperations(sourcePaths, destinationDirectory);
		if (operations.Count == 0)
		{
			return;
		}

		bool isBatch = operations.Count > 1;
		if (isBatch)
		{
			await BeginBatchOperationAsync(operations.Count);
			await Task.Yield();
		}

		try
		{
			var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { destinationDirectory };
			foreach (var operation in operations)
			{
				if (!File.Exists(operation.SourcePath))
				{
					continue;
				}

				CopyFile(operation.SourcePath, operation.DestinationPath);
				touchedDirectories.Add(operation.DestinationDirectory);
			}

			_notifyDirectoryContentAdded?.Invoke(destinationDirectory);
			_refreshDirectoriesImmediately(touchedDirectories.ToArray());
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "Failed to duplicate asset paths");
			throw;
		}
		finally
		{
			if (isBatch)
			{
				await EndBatchOperationAsync();
			}
		}
	}

	private static List<DuplicateOperation> ResolveDuplicateOperations(IReadOnlyList<string> sourcePaths, string destinationDirectory)
	{
		var operations = new List<DuplicateOperation>();
		var reservedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string sourcePath in sourcePaths
					 .Where(File.Exists)
					 .Distinct(StringComparer.OrdinalIgnoreCase))
		{
			string sourceName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			string requestedDestinationPath = Path.Combine(destinationDirectory, sourceName);
			string destinationPath = GetUniqueDestinationPath(requestedDestinationPath, reservedDestinations);
			string? destinationParent = Path.GetDirectoryName(destinationPath);
			if (string.IsNullOrWhiteSpace(destinationParent))
			{
				continue;
			}

			operations.Add(new DuplicateOperation(sourcePath, destinationPath, destinationParent));
			reservedDestinations.Add(destinationPath);
		}

		return operations;
	}

	private static List<MoveOperation> ResolveMoveOperations(IReadOnlyList<string> validSources, string destinationDirectory)
	{
		var operations = new List<MoveOperation>(validSources.Count);
		var reservedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string sourcePath in validSources)
		{
			string? sourceDirectory = Path.GetDirectoryName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			if (!string.IsNullOrWhiteSpace(sourceDirectory) &&
				string.Equals(sourceDirectory, destinationDirectory, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string sourceName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			string requestedDestinationPath = Path.Combine(destinationDirectory, sourceName);
			string destinationPath = GetUniqueDestinationPath(requestedDestinationPath, reservedDestinations);
			operations.Add(new MoveOperation(
				sourcePath,
				destinationPath,
				sourceDirectory,
				sourceName,
				!string.Equals(requestedDestinationPath, destinationPath, StringComparison.OrdinalIgnoreCase)));
			reservedDestinations.Add(destinationPath);
		}

		return operations;
	}

	private static string BuildMoveConfirmationMessage(IReadOnlyList<MoveOperation> operations, string targetName)
	{
		if (operations.Count == 1)
		{
			return LocalizationManager.Format(
				"views.rail.tools.assets.file_operations.move_single_message",
				operations[0].SourceName,
				targetName);
		}

		const int previewLimit = 6;
		var previewNames = operations
			.Take(previewLimit)
			.Select(operation => operation.HasConflict
				? $"{operation.SourceName} -> {Path.GetFileName(operation.DestinationPath)}"
				: operation.SourceName);
		string preview = string.Join(Environment.NewLine, previewNames.Select(name => $"- {name}"));
		if (operations.Count > previewLimit)
		{
			preview += Environment.NewLine + LocalizationManager.Format(
				"views.rail.tools.assets.file_operations.move_more_items_message",
				operations.Count - previewLimit);
		}

		return LocalizationManager.Format(
			"views.rail.tools.assets.file_operations.move_multiple_file_message",
			operations.Count,
			targetName,
			preview);
	}

	internal async Task AddFolderAsync(string destinationDirectory)
	{
		if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
		{
			return;
		}

		await _dialogs.PromptAsync(
			LocalizationManager.Text("views.rail.tools.assets.file_operations.add_folder_title"),
			LocalizationManager.Format("views.rail.tools.assets.file_operations.add_folder_prompt_message", destinationDirectory),
			LocalizationManager.Text("common.ok"),
			LocalizationManager.Text("common.cancel"),
			placeholder: LocalizationManager.Text("views.rail.tools.assets.file_operations.folder_name_placeholder"),
			initialValue: LocalizationManager.Text("views.rail.tools.assets.file_operations.new_folder_name"),
			maxLength: 180,
			onOk: folderName => CreateFolderAsync(destinationDirectory, folderName));
	}

	private Task<NexusDialogActionResult> CreateFolderAsync(string destinationDirectory, string folderName)
	{
		string sanitizedName = folderName.Trim();
		if (string.IsNullOrWhiteSpace(sanitizedName))
		{
			throw new InvalidOperationException(LocalizationManager.Text("views.rail.tools.assets.file_operations.folder_name_required"));
		}

		if (sanitizedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
			sanitizedName.Contains(Path.DirectorySeparatorChar) ||
			sanitizedName.Contains(Path.AltDirectorySeparatorChar))
		{
			throw new InvalidOperationException(LocalizationManager.Text("views.rail.tools.assets.file_operations.folder_name_invalid"));
		}

		string destinationPath = Path.Combine(destinationDirectory, sanitizedName);
		if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
		{
			throw new InvalidOperationException(LocalizationManager.Format("views.rail.tools.assets.file_operations.folder_exists_message", sanitizedName));
		}

		Directory.CreateDirectory(destinationPath);
		_notifyFolderCreated?.Invoke(destinationDirectory, destinationPath);
		_refreshDirectoriesImmediately([destinationDirectory, destinationPath]);
		return Task.FromResult(NexusDialogActionResult.Close);
	}

	private async Task<NexusDialogActionResult> RenameBatchAsync(
		IReadOnlyList<string> selectedPaths,
		string baseName,
		Action<IReadOnlyList<string>, IReadOnlyCollection<string>> onRenamed)
	{
		if (string.IsNullOrWhiteSpace(baseName))
		{
			throw new InvalidOperationException(LocalizationManager.Text("views.rail.tools.assets.file_operations.batch_rename_empty_message"));
		}

		try
		{
			var orderedPaths = selectedPaths
				.OrderBy(path => Path.GetDirectoryName(path), StringComparer.OrdinalIgnoreCase)
				.ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
				.ToList();

			int width = Math.Max(2, orderedPaths.Count.ToString().Length);
			var renamedTargets = new List<string>(orderedPaths.Count);
			var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			bool isBatch = orderedPaths.Count > 1;
			if (isBatch)
			{
				await BeginBatchOperationAsync(orderedPaths.Count);
				await Task.Yield();
			}

			try
			{
				for (int i = 0; i < orderedPaths.Count; i++)
				{
					string sourcePath = orderedPaths[i];
					string? sourceParentDirectory = Path.GetDirectoryName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
					if (string.IsNullOrWhiteSpace(sourceParentDirectory))
					{
						continue;
					}

					touchedDirectories.Add(sourceParentDirectory);

					bool sourceIsDirectory = Directory.Exists(sourcePath);
					if (!sourceIsDirectory && !File.Exists(sourcePath))
					{
						continue;
					}

					string extension = sourceIsDirectory ? string.Empty : Path.GetExtension(sourcePath);
					string candidateName = $"{baseName.Trim()} {(i + 1).ToString($"D{width}")}{extension}";
					string candidatePath = GetUniqueDestinationPath(Path.Combine(sourceParentDirectory, candidateName));

					var mutation = new AssetPathMutation(AssetPathMutationKind.Rename, sourcePath, candidatePath, sourceIsDirectory, IsBatch: isBatch);
					bool renamed = await ExecuteMutationAsync(
						mutation,
						() =>
						{
							if (sourceIsDirectory) Directory.Move(sourcePath, candidatePath);
							else File.Move(sourcePath, candidatePath);
						});
					if (!renamed)
					{
						return NexusDialogActionResult.KeepOpen;
					}
					renamedTargets.Add(candidatePath);
				}
			}
			finally
			{
				if (isBatch)
				{
					await EndBatchOperationAsync();
				}
			}

			onRenamed(renamedTargets, touchedDirectories);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "Failed to batch rename paths");
			throw;
		}

		return NexusDialogActionResult.Close;
	}

	/// <summary>
	/// Pastes the current clipboard into a resolved target directory.
	/// </summary>
	/// <param name="explicitTargetPath">Optional explicit directory or file path; file paths paste into their parent directory.</param>
	internal async Task PasteIntoSelectionAsync(string? explicitTargetPath = null)
	{
		if (_clipboard.Count == 0)
		{
			return;
		}

		string destinationDirectory = ResolvePasteDirectory(explicitTargetPath);
		if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
		{
			return;
		}

		var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			destinationDirectory
		};
		var operations = ResolvePasteOperations(destinationDirectory);
		if (operations.Count == 0)
		{
			return;
		}

		if (operations.Any(operation => operation.HasConflict))
		{
			bool confirmed = await ConfirmPasteConflictAsync();
			if (!confirmed)
			{
				return;
			}
		}

		try
		{
			foreach (var operation in operations)
			{
				if (!string.IsNullOrWhiteSpace(operation.SourceDirectory))
				{
					touchedDirectories.Add(operation.SourceDirectory);
				}

				if (_clipboard.IsCutMode)
				{
					var mutation = new AssetPathMutation(
						AssetPathMutationKind.Move,
						operation.SourcePath,
						operation.DestinationPath,
						operation.SourceIsDirectory);
					bool moved = await ExecuteMutationAsync(
						mutation,
						() =>
						{
							if (operation.SourceIsDirectory) Directory.Move(operation.SourcePath, operation.DestinationPath);
							else File.Move(operation.SourcePath, operation.DestinationPath);
						});
					if (!moved)
					{
						return;
					}
				}
				else if (operation.SourceIsDirectory)
				{
					CopyDirectory(operation.SourcePath, operation.DestinationPath);
				}
				else if (File.Exists(operation.SourcePath))
				{
					CopyFile(operation.SourcePath, operation.DestinationPath);
				}

				string? destinationParent = operation.SourceIsDirectory
					? Directory.GetParent(operation.DestinationPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName
					: Path.GetDirectoryName(operation.DestinationPath);

				if (!string.IsNullOrWhiteSpace(destinationParent))
				{
					touchedDirectories.Add(destinationParent);
				}
			}

			if (_clipboard.IsCutMode)
			{
				_clipboard.Clear();
			}

			_notifyDirectoryContentAdded?.Invoke(destinationDirectory);
			_refreshDirectoriesImmediately(touchedDirectories.ToArray());
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "Failed to paste asset paths");
		}
	}

	private List<PasteOperation> ResolvePasteOperations(string destinationDirectory)
	{
		var operations = new List<PasteOperation>();
		var reservedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var sourcePath in _clipboard.Snapshot())
		{
			bool sourceIsDirectory = Directory.Exists(sourcePath);
			if (!sourceIsDirectory && !File.Exists(sourcePath))
			{
				continue;
			}

			string? sourceDirectory = sourceIsDirectory
				? Directory.GetParent(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName
				: Path.GetDirectoryName(sourcePath);

			if (_clipboard.IsCutMode &&
				!string.IsNullOrWhiteSpace(sourceDirectory) &&
				string.Equals(sourceDirectory, destinationDirectory, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (sourceIsDirectory &&
				destinationDirectory.StartsWith(sourcePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string sourceName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			string requestedDestinationPath = Path.Combine(destinationDirectory, sourceName);
			string destinationPath = GetUniqueDestinationPath(requestedDestinationPath, reservedDestinations);
			operations.Add(new PasteOperation(
				sourcePath,
				destinationPath,
				sourceDirectory,
				sourceIsDirectory,
				!string.Equals(requestedDestinationPath, destinationPath, StringComparison.OrdinalIgnoreCase)));
			reservedDestinations.Add(destinationPath);
		}

		return operations;
	}

	private async Task<bool> ConfirmPasteConflictAsync()
	{
		bool confirmed = false;
		await _dialogs.ConfirmAsync(
			LocalizationManager.Text("views.rail.tools.assets.file_operations.paste_conflict_title"),
			LocalizationManager.Text("views.rail.tools.assets.file_operations.paste_conflict_message"),
			LocalizationManager.Text("common.ok"),
			LocalizationManager.Text("common.cancel"),
			onOk: () =>
			{
				confirmed = true;
				return Task.FromResult(NexusDialogActionResult.Close);
			});
		return confirmed;
	}

	/// <summary>
	/// Renames one item or batch-renames multiple selected items.
	/// </summary>
	/// <param name="selectedPaths">Absolute paths to rename.</param>
	internal async Task RenameSelectionAsync(IReadOnlyList<string> selectedPaths)
	{
		if (selectedPaths.Count == 0)
		{
			return;
		}

		if (selectedPaths.Count != 1)
		{
			IReadOnlyList<string>? renamedTargets = null;
			IReadOnlyCollection<string>? touchedDirectories = null;
			await _dialogs.PromptAsync(
				LocalizationManager.Text("views.rail.tools.assets.file_operations.batch_rename_title"),
				LocalizationManager.Text("views.rail.tools.assets.file_operations.batch_rename_message"),
				LocalizationManager.Text("common.rename"),
				LocalizationManager.Text("common.cancel"),
				initialValue: "Item",
				maxLength: 120,
				onOk: value => RenameBatchAsync(
					selectedPaths,
					value,
					(targets, directories) =>
					{
						renamedTargets = targets;
						touchedDirectories = directories;
					}));
			if (renamedTargets != null && touchedDirectories != null)
			{
				await ApplyRenamePresentationAsync(renamedTargets, touchedDirectories);
			}
			return;
		}

		string selectedPath = selectedPaths[0];
		bool isDirectory = Directory.Exists(selectedPath);
		string currentName = Path.GetFileName(selectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		string currentExtension = isDirectory ? string.Empty : Path.GetExtension(selectedPath);
		string currentBaseName = isDirectory ? currentName : Path.GetFileNameWithoutExtension(selectedPath);
		string promptMessage = isDirectory
			? LocalizationManager.Text("views.rail.tools.assets.file_operations.rename_message")
			: LocalizationManager.Format("views.rail.tools.assets.file_operations.rename_file_message", currentExtension);

		string? renamedTarget = null;
		string? touchedDirectory = null;
		await _dialogs.PromptAsync(
			LocalizationManager.Text("common.rename"),
			promptMessage,
			LocalizationManager.Text("common.rename"),
			LocalizationManager.Text("common.cancel"),
			initialValue: currentBaseName,
			maxLength: 260,
			onOk: value => RenameSingleAsync(
				selectedPath,
				isDirectory,
				currentName,
				currentExtension,
				value,
				(target, directory) =>
				{
					renamedTarget = target;
					touchedDirectory = directory;
				}));
		if (!string.IsNullOrWhiteSpace(renamedTarget) && !string.IsNullOrWhiteSpace(touchedDirectory))
		{
			await ApplyRenamePresentationAsync([renamedTarget], [touchedDirectory, renamedTarget]);
		}
	}

	private async Task<NexusDialogActionResult> RenameSingleAsync(
		string selectedPath,
		bool isDirectory,
		string currentName,
		string currentExtension,
		string renamedBase,
		Action<string, string> onRenamed)
	{
		if (string.IsNullOrWhiteSpace(renamedBase))
		{
			throw new InvalidOperationException(LocalizationManager.Text("views.rail.tools.assets.file_operations.rename_message"));
		}

		string targetName = isDirectory ? renamedBase.Trim() : $"{renamedBase.Trim()}{currentExtension}";
		if (string.Equals(targetName, currentName, StringComparison.Ordinal))
		{
			return NexusDialogActionResult.Close;
		}

		string? parentDirectory = Path.GetDirectoryName(selectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (string.IsNullOrWhiteSpace(parentDirectory))
		{
			throw new InvalidOperationException(LocalizationManager.Text("views.rail.tools.assets.file_operations.parent_folder_unavailable"));
		}

		string targetPath = Path.Combine(parentDirectory, targetName);
		if (File.Exists(targetPath) || Directory.Exists(targetPath))
		{
			throw new InvalidOperationException(LocalizationManager.Format(
				"views.rail.tools.assets.file_operations.folder_exists_message",
				targetName));
		}

		var mutation = new AssetPathMutation(AssetPathMutationKind.Rename, selectedPath, targetPath, isDirectory);
		try
		{
			bool renamed = await ExecuteMutationAsync(
				mutation,
				() =>
				{
					if (isDirectory) Directory.Move(selectedPath, targetPath);
					else if (File.Exists(selectedPath)) File.Move(selectedPath, targetPath);
					else throw new FileNotFoundException(
						LocalizationManager.Text("views.rail.tools.assets.file_operations.selected_item_missing"),
						selectedPath);
				});
			if (!renamed)
			{
				return NexusDialogActionResult.KeepOpen;
			}

			onRenamed(targetPath, parentDirectory);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "Failed to rename path");
			throw;
		}

		return NexusDialogActionResult.Close;
	}

	private Task ApplyRenamePresentationAsync(
		IReadOnlyList<string> renamedTargets,
		IReadOnlyCollection<string> touchedDirectories)
	{
		return MainThread.InvokeOnMainThreadAsync(() =>
		{
			_selection.ReplaceAll(renamedTargets, renamedTargets.LastOrDefault());
			_refreshDirectoriesImmediately(touchedDirectories.ToArray());
		});
	}

	/// <summary>
	/// Deletes selected files or directories after user confirmation.
	/// </summary>
	/// <param name="selectedPaths">Absolute paths to delete.</param>
	internal async Task DeleteSelectionAsync(IReadOnlyList<string> selectedPaths)
	{
		if (selectedPaths.Count == 0)
		{
			return;
		}

		string currentName = selectedPaths.Count == 1
			? Path.GetFileName(selectedPaths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
			: $"{selectedPaths.Count} item(s)";

		await _dialogs.ConfirmAsync(
			LocalizationManager.Text("common.delete"),
			LocalizationManager.Format("views.rail.tools.assets.file_operations.delete_message", currentName),
			LocalizationManager.Text("common.delete"),
			LocalizationManager.Text("common.cancel"),
			returnFocusTarget: NexusDialogReturnFocusTarget.App,
			onOk: async () =>
			{
				bool isBatch = selectedPaths.Count > 1;
				if (isBatch)
				{
					await BeginBatchOperationAsync(selectedPaths.Count);
					await Task.Yield();
				}
				try
				{
					var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

					foreach (string selectedPath in selectedPaths
									 .OrderByDescending(path => path.Length)
									 .ToList())
					{
						string? parentDirectory = Directory.GetParent(selectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName
							?? Path.GetDirectoryName(selectedPath);
						if (!string.IsNullOrWhiteSpace(parentDirectory))
						{
							touchedDirectories.Add(parentDirectory);
						}

						bool isDirectory = Directory.Exists(selectedPath);
						if (!isDirectory && !File.Exists(selectedPath))
						{
							continue;
						}

						var mutation = new AssetPathMutation(
							AssetPathMutationKind.Delete,
							selectedPath,
							null,
							isDirectory,
							IsBatch: isBatch);
						bool deleted = await ExecuteMutationAsync(
							mutation,
							() =>
							{
								if (isDirectory) Directory.Delete(selectedPath, true);
								else File.Delete(selectedPath);
							});
						if (!deleted)
						{
							return NexusDialogActionResult.KeepOpen;
						}
					}

					_selection.Clear();
					_refreshDirectoriesImmediately(touchedDirectories.ToArray());
				}
				catch (Exception ex)
				{
					NexusLog.Exception(ex, "Failed to delete paths");
					throw;
				}
				finally
				{
					if (isBatch)
					{
						await EndBatchOperationAsync();
					}
				}

				return NexusDialogActionResult.Close;
			});
	}

	/// <summary>
	/// Copies a path string to the system clipboard.
	/// </summary>
	/// <param name="path">Absolute path to copy.</param>
	internal static async Task CopyPathAsync(string path)
	{
		try
		{
			await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(path);
		}
		catch
		{
		}
	}

	/// <summary>
	/// Reveals a file or folder in the platform file manager, falling back to opening its parent folder.
	/// </summary>
	/// <param name="path">Absolute file or folder path to reveal.</param>
	internal async Task RevealInExplorerAsync(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		var result = await NexusAppManager.Instance.Platform.Shell.RevealInFileManagerAsync(path);
		if (result.IsSuccess)
		{
			return;
		}

		bool isDirectory = await Task.Run(() => Directory.Exists(path));
		string? folder = isDirectory ? path : Path.GetDirectoryName(path);
		if (!string.IsNullOrWhiteSpace(folder))
		{
			await _openInOsAsync(folder);
		}

		if (!string.IsNullOrWhiteSpace(result.Message))
		{
			NexusLog.Warning($"Failed to reveal in file manager: {result.Message}");
		}
	}

	/// <summary>
	/// Updates selection anchor before opening a context menu.
	/// </summary>
	/// <param name="path">Path whose context menu is being opened.</param>
	/// <param name="preserveMultiSelection">True when right-clicking inside an existing multi-selection.</param>
	internal void PrepareContextSelection(string path, bool preserveMultiSelection)
	{
		if (preserveMultiSelection && _selection.Contains(path))
		{
			_selection.EnsureAnchor(path);
		}
	}

	private string ResolvePasteDirectory(string? explicitTargetPath = null)
	{
		string rootPath = _getRootPath();
		string? primaryPath = explicitTargetPath ?? _selection.GetPrimarySelectedPath();
		if (string.IsNullOrWhiteSpace(primaryPath))
		{
			return rootPath;
		}

		if (Directory.Exists(primaryPath))
		{
			return primaryPath;
		}

		return Path.GetDirectoryName(primaryPath) ?? rootPath;
	}

	private static string GetUniqueDestinationPath(string destinationPath, ISet<string>? reservedDestinations = null)
	{
		if (!File.Exists(destinationPath) &&
			!Directory.Exists(destinationPath) &&
			reservedDestinations?.Contains(destinationPath) != true)
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
				!Directory.Exists(candidate) &&
				reservedDestinations?.Contains(candidate) != true)
			{
				return candidate;
			}

			suffix++;
		}
	}

	private void CopyDirectory(string sourceDirectory, string destinationDirectory)
	{
		Directory.CreateDirectory(destinationDirectory);
		var options = new EnumerationOptions
		{
			IgnoreInaccessible = false,
			RecurseSubdirectories = true,
			ReturnSpecialDirectories = false,
			AttributesToSkip = FileAttributes.ReparsePoint,
		};

		foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", options))
		{
			string targetDirectory = directory.Replace(sourceDirectory, destinationDirectory, StringComparison.OrdinalIgnoreCase);
			Directory.CreateDirectory(targetDirectory);
		}

		foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", options))
		{
			string targetFile = file.Replace(sourceDirectory, destinationDirectory, StringComparison.OrdinalIgnoreCase);
			string? targetParent = Path.GetDirectoryName(targetFile);
			if (!string.IsNullOrWhiteSpace(targetParent))
			{
				Directory.CreateDirectory(targetParent);
			}

			CopyFile(file, targetFile);
		}
	}

	private void CopyFile(string sourcePath, string destinationPath)
	{
		File.Copy(sourcePath, destinationPath, overwrite: false);
		if (_shouldRegenerateCopiedWorkflowMetadata?.Invoke(destinationPath) != true)
		{
			return;
		}

		string temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.nexus-copy";
		try
		{
			JsonNode? root = JsonNode.Parse(File.ReadAllText(destinationPath, Encoding.UTF8));
			if (root is not JsonObject workflow)
			{
				throw new InvalidDataException($"Copied workflow is not a JSON object: {destinationPath}");
			}

			workflow["id"] = Guid.NewGuid().ToString();
			workflow["revision"] = 0;
			File.WriteAllText(
				temporaryPath,
				workflow.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
				new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			File.Move(temporaryPath, destinationPath, overwrite: true);
		}
		catch
		{
			if (File.Exists(temporaryPath))
			{
				File.Delete(temporaryPath);
			}
			if (File.Exists(destinationPath))
			{
				File.Delete(destinationPath);
			}
			throw;
		}
	}
}
