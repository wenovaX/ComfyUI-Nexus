namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

public partial class AssetsBrowserView
{
	private bool _deleteSelectionInFlight;
	private bool _renameSelectionInFlight;

	private void BeginCopySelected(IReadOnlyList<string>? paths = null)
	{
		paths ??= GetSelectedExistingPaths();
		if (!CanCopySelection(paths))
		{
			return;
		}

		_fileOperations.BeginCopySelected(paths);
	}

	private void BeginCutSelected(IReadOnlyList<string>? paths = null)
	{
		paths ??= GetSelectedExistingPaths();
		if (!CanCutSelection(paths))
		{
			return;
		}

		_fileOperations.BeginCutSelected(paths);
	}

	internal Task ImportDroppedPathsAsync(IReadOnlyList<string> sourcePaths, string? targetPath = null)
		=> _fileOperations.ImportDroppedPathsAsync(sourcePaths, targetPath);

	internal Task MovePathsAsync(IReadOnlyList<string> sourcePaths, string destinationDirectory)
		=> _fileOperations.MovePathsAsync(sourcePaths, destinationDirectory);

	internal Task DuplicatePathsAsync(IReadOnlyList<string> sourcePaths, string destinationDirectory)
		=> _fileOperations.DuplicatePathsAsync(sourcePaths, destinationDirectory);

	private Task PasteIntoSelectionAsync(string? explicitTargetPath = null)
		=> _fileOperations.PasteIntoSelectionAsync(explicitTargetPath);

	private async Task RenameSelectionAsync(IReadOnlyList<string>? paths = null)
	{
		if (_renameSelectionInFlight)
		{
			return;
		}

		paths ??= GetSelectedExistingPaths();
		if (!CanRenameSelection(paths))
		{
			return;
		}

		_renameSelectionInFlight = true;
		try
		{
			await _fileOperations.RenameSelectionAsync(paths);
		}
		finally
		{
			_renameSelectionInFlight = false;
		}
	}

	private async Task DeleteSelectionAsync(IReadOnlyList<string>? paths = null)
	{
		if (_deleteSelectionInFlight)
		{
			return;
		}

		paths ??= GetSelectedExistingPaths();
		if (!CanDeleteSelection(paths))
		{
			return;
		}

		_deleteSelectionInFlight = true;
		try
		{
			await _fileOperations.DeleteSelectionAsync(paths);
		}
		finally
		{
			_deleteSelectionInFlight = false;
		}
	}

	private bool CanCopySelection(IReadOnlyList<string> paths)
	{
		if (paths.Count == 0)
		{
			return false;
		}

		AssetOperationPolicy policy = _currentProfile?.CopyPolicy ?? AssetOperationPolicy.All;
		return IsSelectionAllowedByPolicy(paths, policy);
	}

	private bool CanCutSelection(IReadOnlyList<string> paths)
	{
		if (paths.Count == 0)
		{
			return false;
		}

		AssetOperationPolicy policy = _currentProfile?.CutPolicy ?? AssetOperationPolicy.All;
		return IsSelectionAllowedByPolicy(paths, policy);
	}

	private bool CanPasteIntoSelection(IReadOnlyList<string> paths)
	{
		if (paths.Count == 0)
		{
			return false;
		}

		AssetOperationPolicy policy = _currentProfile?.PastePolicy ?? AssetOperationPolicy.All;
		if (policy == AssetOperationPolicy.None)
		{
			return false;
		}

		return _currentProfile?.AllowInternalMove == true ||
			_currentProfile?.AllowDropImport == true;
	}

	private bool CanDuplicateSelection(IReadOnlyList<string> paths)
	{
		if (paths.Count == 0 || _currentProfile?.AllowDuplicate != true)
		{
			return false;
		}

		return paths.All(File.Exists);
	}

	private bool CanRenameSelection(IReadOnlyList<string> paths)
	{
		if (paths.Count == 0)
		{
			return false;
		}

		AssetOperationPolicy policy = _currentProfile?.RenamePolicy ?? AssetOperationPolicy.All;
		return IsSelectionAllowedByPolicy(paths, policy);
	}

	private bool CanDeleteSelection(IReadOnlyList<string> paths)
	{
		if (paths.Count == 0)
		{
			return false;
		}

		AssetOperationPolicy policy = _currentProfile?.DeletePolicy ?? AssetOperationPolicy.All;
		return IsSelectionAllowedByPolicy(paths, policy);
	}

	private static bool IsSelectionAllowedByPolicy(IReadOnlyList<string> paths, AssetOperationPolicy policy)
	{
		return policy switch
		{
			AssetOperationPolicy.None => false,
			AssetOperationPolicy.All => true,
			AssetOperationPolicy.FileOnly => paths.All(File.Exists),
			AssetOperationPolicy.DirectoryOnly => paths.All(Directory.Exists),
			_ => false,
		};
	}

	private void PrepareContextSelection(RailTreeNode node, bool preserveMultiSelection)
		=> PrepareContextSelection(node.FullPath, preserveMultiSelection);

	private void PrepareContextSelection(string path, bool preserveMultiSelection)
	{
		if (preserveMultiSelection && _selection.Contains(path))
		{
			_fileOperations.PrepareContextSelection(path, preserveMultiSelection);
			return;
		}

		SelectPath(path);
	}
}
