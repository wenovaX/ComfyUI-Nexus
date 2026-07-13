using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Views.Rail;

namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

public partial class AssetsBrowserView
{
	private bool CanApplyWatcherBatch(RailDirectoryWatchBatch batch)
	{
		return _isRailActive
			&& _isReady
			&& IsVisible
			&& Handler is not null
			&& CurrentTreeSource == AssetTreeSource.FileSystem
			&& _treeWatcher.IsCurrent(batch.RootPath, batch.Generation)
			&& string.Equals(_rootPath, batch.RootPath, StringComparison.OrdinalIgnoreCase);
	}

	private bool ApplyPendingFileSystemChanges(RailDirectoryWatchBatch batch)
	{
		if (_isApplyingWatcherChanges)
		{
			_treeWatcher.NotifyMutation(batch.DirtyDirectories);
			return true;
		}

		if (!CanApplyWatcherBatch(batch))
		{
			return false;
		}

		_isApplyingWatcherChanges = true;
		try
		{
			if (batch.RequiresFullRefresh || batch.DirtyDirectories.Count == 0)
			{
				TryPatchRootBranch();
				RenderTree();
				if (IsSearchActive)
				{
					_ = RefreshSearchResultsAsync(immediate: true);
				}

				RefreshVisibleSelectionState();
				_ = RefreshWorkflowBookmarksSectionAsync();
				return true;
			}

			var candidateDirectories = batch.DirtyDirectories
				.Where(path => !string.IsNullOrWhiteSpace(path))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(path => path.Length)
				.ToList();

			bool anyPatched = false;

			if (candidateDirectories.Any(path => string.Equals(path, _rootPath, StringComparison.OrdinalIgnoreCase)))
			{
				anyPatched |= TryPatchRootBranch();
				candidateDirectories = candidateDirectories
					.Where(path => !string.Equals(path, _rootPath, StringComparison.OrdinalIgnoreCase))
					.ToList();
			}

			foreach (string dirtyDirectory in candidateDirectories)
			{
				anyPatched |= TryPatchDirectoryBranch(dirtyDirectory);
			}

			NormalizeSelectionState();

			if (anyPatched)
			{
				RenderTree();
				RefreshVisibleSelectionState();
			}

			if (IsSearchActive)
			{
				_ = RefreshSearchResultsAsync(immediate: true);
			}

			_ = RefreshWorkflowBookmarksSectionAsync();
			return true;
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[ASSET_WATCHER] Apply failed");
			return false;
		}
		finally
		{
			_isApplyingWatcherChanges = false;
		}
	}

	private void RefreshDirectoriesImmediately(params string?[] dirtyPaths)
	{
		_treeWatcher.NotifyMutation(dirtyPaths);
	}

	private void InvalidateDirectoryCache(string directoryPath)
	{
		_childrenCache.Remove(directoryPath);
	}

	private void RefreshDirectoryNode(string directoryPath)
	{
		if (!TryGetNode(directoryPath, out var node) || !node.IsDirectory)
		{
			return;
		}

		node.Children.Clear();
		node.ChildrenLoaded = node.IsExpanded;

		if (Directory.Exists(directoryPath) && node.IsExpanded)
		{
			node.Children.AddRange(CreateNodesForDirectory(directoryPath, node.Depth + 1, node));
		}
		else if (!Directory.Exists(directoryPath))
		{
			node.IsExpanded = false;
			_expandedPaths.Remove(node.FullPath);
		}
	}

	private bool TryPatchDirectoryBranch(string dirtyDirectory)
	{
		string? anchorPath = ResolvePatchAnchorDirectory(dirtyDirectory);
		if (string.IsNullOrWhiteSpace(anchorPath) || !TryGetNode(anchorPath, out var anchorNode) || !anchorNode.IsDirectory)
		{
			return false;
		}

		InvalidateDirectoryCache(anchorPath);
		RefreshDirectoryNode(anchorPath);
		UpdateCachedChevron(anchorNode);

		return true;
	}

	private bool TryPatchRootBranch()
	{
		InvalidateDirectoryCache(_rootPath);
		_rootNodes.Clear();
		_rootNodes.AddRange(CreateNodesForDirectory(_rootPath, 0, null));
		return true;
	}

	private string? ResolvePatchAnchorDirectory(string directoryPath)
	{
		string? current = directoryPath;
		while (!string.IsNullOrWhiteSpace(current)
			   && !string.Equals(current, _rootPath, StringComparison.OrdinalIgnoreCase))
		{
			if (TryGetNode(current, out var node) && node.IsDirectory)
			{
				return current;
			}

			current = Directory.GetParent(current)?.FullName;
		}

		return null;
	}
}
