using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

public partial class AssetsBrowserView
{
	private void ApplyPendingFileSystemChanges(IReadOnlyList<string> dirtyDirectories)
	{
		if (_isApplyingWatcherChanges)
		{
			_treeWatcher.RefreshImmediately(dirtyDirectories.Cast<string?>().ToArray());
			return;
		}

		_isApplyingWatcherChanges = true;
		NexusLog.Info($"[ASSET_WATCHER] Apply started. root='{_rootPath}', dirty={dirtyDirectories.Count}, search={IsSearchActive}");
		try
		{
			if (dirtyDirectories.Count == 0)
			{
				ReloadTree(resetExpansion: false, invalidateLayout: false);
				if (IsSearchActive)
				{
					_ = RefreshSearchResultsAsync(immediate: true);
				}
				return;
			}

			var candidateDirectories = dirtyDirectories
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

			if (!anyPatched)
			{
				RenderTree();
			}
			else
			{
				RefreshVisibleSelectionState();
			}

			if (IsSearchActive)
			{
				_ = RefreshSearchResultsAsync(immediate: true);
			}

			_ = RefreshWorkflowBookmarksSectionAsync();
			NexusLog.Info($"[ASSET_WATCHER] Apply completed. root='{_rootPath}', patched={anyPatched}");
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[ASSET_WATCHER] Apply failed");
		}
		finally
		{
			_isApplyingWatcherChanges = false;
			_treeWatcher.RequestPendingRefresh();
		}
	}

	private void RefreshDirectoriesImmediately(params string?[] dirtyPaths)
	{
		_treeWatcher.RefreshImmediately(dirtyPaths);

		if (IsSearchActive)
		{
			_ = RefreshSearchResultsAsync(immediate: true);
		}

		_ = RefreshWorkflowBookmarksSectionAsync();
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

		ReplaceVisibleBranchRows();

		return true;
	}

	private bool TryPatchRootBranch()
	{
		InvalidateDirectoryCache(_rootPath);
		_rootNodes.Clear();
		_rootNodes.AddRange(CreateNodesForDirectory(_rootPath, 0, null));

		ReplaceVisibleRootRows();
		return true;
	}

	private void ReplaceVisibleBranchRows()
	{
		RenderTree();
	}

	private void ReplaceVisibleRootRows()
	{
		RenderTree();
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
