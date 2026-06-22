using Microsoft.Maui.Controls.Shapes;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Views.Rail.Tools.Assets;
using IOPath = System.IO.Path;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private const int WorkflowIndexWatcherDebounceMs = 100;
	private static readonly TimeSpan WorkflowMutationActionTimeout = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan WorkflowBatchOperationSettleDelay = TimeSpan.FromSeconds(1);
	private const uint BookmarkHudFadeInLength = 200;
	private const uint BookmarkHudFadeOutLength = 150;
	private const double BookmarkHudSectionFontSize = 11;
	private const double BookmarkHudTitleFontSize = 13;
	private const double BookmarkHudMetaFontSize = 10;
	private const double BookmarkHudEmptyFontSize = 12;
	private const double BookmarkHudButtonFontSize = 11;
	private const double BookmarkHudItemCornerRadius = 8;
	private const double BookmarkHudOpenButtonMinWidth = 64;
	private const double BookmarkHudPinButtonMinWidth = 58;
	private static readonly Color BookmarkHudPinnedAccentColor = NexusColors.AccentDeep;
	private static readonly Color BookmarkHudAvailableAccentColor = NexusColors.AccentText;
	private static readonly Color BookmarkHudEmptyTextColor = Color.FromArgb("#55708f");
	private static readonly Color BookmarkHudSubtitleColor = Color.FromArgb("#66809f");
	private static readonly Color BookmarkHudKnownTextColor = Color.FromArgb("#6f8fab");
	private static readonly Color BookmarkHudOpenButtonBackgroundColor = Color.FromArgb("#132235");
	private static readonly Color BookmarkHudOpenButtonTextColor = NexusColors.TextSoft;
	private static readonly Color BookmarkHudUnpinTextColor = Color.FromArgb("#ff9cb0");
	private static readonly Color BookmarkHudPinTextColor = Color.FromArgb("#7fe7ff");
	private static readonly Color BookmarkHudPinnedBackgroundColor = Color.FromArgb("#111d2c");
	private static readonly Color BookmarkHudKnownBackgroundColor = Color.FromArgb("#101826");
	private static readonly Color BookmarkHudPinnedStrokeColor = Color.FromArgb("#143d50");
	private static readonly Color BookmarkHudKnownStrokeColor = Color.FromArgb("#18263d");
	private string _comfyWorkflowsPath = string.Empty;
	private readonly HashSet<string> _knownWorkflowRelativePaths = new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _workflowIndexRefreshGate = new(1, 1);
	private readonly SemaphoreSlim _workflowIndexScanGate = new(1, 1);
	private readonly object _workflowIndexScheduleLock = new();
	private readonly object _bookmarkLoadLock = new();
	private Task? _bookmarkLoadTask;
	private HashSet<string> _knownWorkflowDirectories = new(StringComparer.OrdinalIgnoreCase);
	private IPlatformDirectoryWatcherSubscription? _workflowIndexWatcher;
	private CancellationTokenSource? _workflowIndexRefreshCts;
	private int _workflowIndexRefreshDirty;
	private readonly SemaphoreSlim _workflowAssetMutationGate = new(1, 1);
	private PendingWorkflowAssetMutation? _pendingWorkflowAssetMutation;
	private volatile bool _workflowBatchMutationDirty;
	private volatile bool _workflowBatchOperationActive;
	private bool _workflowBatchBookmarksDirty;
	private DateTimeOffset _workflowBatchOperationStartedAt;

	private sealed record PendingWorkflowAssetMutation(
		AssetPathMutation Mutation,
		IReadOnlyList<string> OpenWorkflowPaths,
		bool HandledByWeb);

	private void ApplyTabUiTuning()
	{
		double tabHeight = NexusUiTuning.TabButtonHeight;
		HeaderControl.SetTabRowHeights(tabHeight);
	}

	private void RefreshTabsFromLastSync()
		=> _tabController.RefreshFromLastSync();

	private void RefreshAvailableWidthAndTabs(
		ShellLayoutInvalidationReason reason = ShellLayoutInvalidationReason.Unknown)
	{
		double controlDeckWidth = ControlDeckColumn.Width.IsAbsolute
			? ControlDeckColumn.Width.Value
			: 0;
		double headerWidth = Math.Max(0, Width - controlDeckWidth);

		// The header starts after the control deck. The controller handles its own logo and utility buttons.
		_tabController.RefreshLayout(headerWidth);
		_shellLayoutSignals.InvalidateLayout(reason);
	}

	private void InitializeComfyPaths()
	{
		try
		{
			string comfyPath = ComfyPathResolver.ResolveConfiguredComfyPath();
			UpdateComfyManagerAvailability(comfyPath);
			if (string.IsNullOrWhiteSpace(comfyPath))
			{
				return;
			}

			PortablePreferences.Set(PreferenceKeys.ComfyUIPath, comfyPath);

			string userRoot = IOPath.Combine(comfyPath, ComfyPathOptions.UserDirectoryName);
			string userFolder = DiscoverUserFolder(userRoot);
			_comfyWorkflowsPath = IOPath.Combine(userRoot, userFolder, ComfyPathOptions.WorkflowsDirectoryName);
			EnsureComfyWorkspaceDirectories(comfyPath, _comfyWorkflowsPath);

			Log($"[SYSTEM] ComfyUI Workflow Path detected: {_comfyWorkflowsPath}");

			if (RailControl != null)
			{
				RailControl.FixedWorkflowsPath = _comfyWorkflowsPath;
			}

			StartWorkflowIndexWatcher();
			_ = RefreshWorkflowIndexAsync();
		}
		catch (Exception ex)
		{
			Log($"[ERROR] Failed to initialize ComfyUI paths: {ex.Message}");
		}
	}

	private void EnsureComfyWorkspaceDirectories(string comfyPath, string workflowsPath)
	{
		if (string.IsNullOrWhiteSpace(comfyPath) || !Directory.Exists(comfyPath))
		{
			return;
		}

		foreach (string path in new[]
		{
			IOPath.Combine(comfyPath, ComfyPathOptions.InputDirectoryName),
			IOPath.Combine(comfyPath, ComfyPathOptions.OutputDirectoryName),
			IOPath.Combine(comfyPath, ComfyPathOptions.ModelsDirectoryName),
			workflowsPath
		})
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				continue;
			}

			Directory.CreateDirectory(path);
		}
	}

	private void StartWorkflowIndexWatcher()
	{
		_workflowIndexWatcher?.Dispose();
		_workflowIndexWatcher = null;

		if (string.IsNullOrWhiteSpace(_comfyWorkflowsPath) || !Directory.Exists(_comfyWorkflowsPath))
		{
			return;
		}

		var watchResult = PlatformManager.Current.DirectoryWatcher.TryWatch(
			_comfyWorkflowsPath,
			new DirectoryWatcherOptions
			{
				IncludeSubdirectories = true,
			},
			OnWorkflowIndexChanged,
			_ => ScheduleWorkflowIndexRefresh());

		if (!watchResult.IsSuccess || watchResult.Value is null)
		{
			if (!string.IsNullOrWhiteSpace(watchResult.Message))
			{
				NexusLog.Warning($"Workflow index watcher unavailable: {watchResult.Message}");
			}
			return;
		}

		_workflowIndexWatcher = watchResult.Value;
		_workflowIndexWatcher.Start();
	}

	private void OnWorkflowIndexChanged(PlatformDirectoryChange change)
	{
		bool isDirectory = IsWorkflowDirectoryEvent(change);
		bool affectsWorkflowFile = IsWorkflowIndexFile(change.Path) || IsWorkflowIndexFile(change.OldPath);
		if (!isDirectory && !affectsWorkflowFile)
		{
			return;
		}
		if (_workflowBatchOperationActive)
		{
			_workflowBatchMutationDirty = true;
			return;
		}

		ScheduleWorkflowIndexRefresh();
	}

	private static bool IsWorkflowIndexFile(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		return string.Equals(IOPath.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase) &&
			!string.Equals(IOPath.GetFileName(path), ".index.json", StringComparison.OrdinalIgnoreCase);
	}

	private bool IsWorkflowDirectoryEvent(PlatformDirectoryChange change)
	{
		if (!string.IsNullOrWhiteSpace(change.Path) &&
			(Directory.Exists(change.Path) || _knownWorkflowDirectories.Contains(change.Path)))
		{
			return true;
		}

		return !string.IsNullOrWhiteSpace(change.OldPath) &&
			_knownWorkflowDirectories.Contains(change.OldPath);
	}

	private void ScheduleWorkflowIndexRefresh()
	{
		Interlocked.Exchange(ref _workflowIndexRefreshDirty, 1);
		CancellationTokenSource cts;
		lock (_workflowIndexScheduleLock)
		{
			_workflowIndexRefreshCts?.Cancel();
			cts = new CancellationTokenSource();
			_workflowIndexRefreshCts = cts;
		}

		_ = Task.Run(async () =>
		{
			try
			{
				await Task.Delay(WorkflowIndexWatcherDebounceMs, cts.Token);
				await ProcessWorkflowIndexChangesAsync();
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				NexusLog.Exception(ex, "Workflow index refresh failed");
			}
			finally
			{
				lock (_workflowIndexScheduleLock)
				{
					if (ReferenceEquals(_workflowIndexRefreshCts, cts))
					{
						_workflowIndexRefreshCts = null;
					}
				}
				cts.Dispose();
			}
		});
	}

	private async Task ProcessWorkflowIndexChangesAsync()
	{
		await _workflowIndexRefreshGate.WaitAsync();
		try
		{
			while (Interlocked.Exchange(ref _workflowIndexRefreshDirty, 0) != 0)
			{
				await RefreshWorkflowIndexAsync();
				await _webViewBridge.RefreshWorkflowAppDataAsync();
			}
		}
		finally
		{
			_workflowIndexRefreshGate.Release();
		}
	}

	private Task RefreshWorkflowIndexAndWebAsync()
	{
		Interlocked.Exchange(ref _workflowIndexRefreshDirty, 1);
		return ProcessWorkflowIndexChangesAsync();
	}

	private async Task RefreshWorkflowIndexAsync()
	{
		await _workflowIndexScanGate.WaitAsync();
		try
		{
			if (string.IsNullOrWhiteSpace(_comfyWorkflowsPath) || !Directory.Exists(_comfyWorkflowsPath))
			{
				return;
			}

			var enumerationOptions = new EnumerationOptions
			{
				IgnoreInaccessible = true,
				RecurseSubdirectories = true,
				ReturnSpecialDirectories = false,
				AttributesToSkip = FileAttributes.ReparsePoint,
			};
			var relativePaths = Directory.EnumerateFiles(_comfyWorkflowsPath, "*.json", enumerationOptions)
				.Where(path => !string.Equals(IOPath.GetFileName(path), ".index.json", StringComparison.OrdinalIgnoreCase))
				.Select(GetWorkflowRelativePath)
				.Where(path => !string.IsNullOrWhiteSpace(path))
				.Select(WorkflowTabController.NormalizeWorkflowRelativePath)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
			_knownWorkflowDirectories = Directory.EnumerateDirectories(_comfyWorkflowsPath, "*", enumerationOptions)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			await MainThread.InvokeOnMainThreadAsync(async () =>
			{
				_knownWorkflowRelativePaths.Clear();
				foreach (string relativePath in relativePaths)
				{
					_knownWorkflowRelativePaths.Add(relativePath);
				}

				_tabController.SetKnownWorkflowFiles(_knownWorkflowRelativePaths);
				await LoadBookmarks();
				RenderBookmarkHUDList();
				RefreshTabsFromLastSync();
			});
		}
		finally
		{
			_workflowIndexScanGate.Release();
		}
	}

	private string GetWorkflowRelativePath(string fullPath)
	{
		if (string.IsNullOrWhiteSpace(_comfyWorkflowsPath) || !IOPath.IsPathFullyQualified(fullPath))
		{
			return string.Empty;
		}

		string relative = IOPath.GetRelativePath(_comfyWorkflowsPath, fullPath).Replace('\\', '/');
		if (relative.StartsWith("..", StringComparison.Ordinal))
		{
			return string.Empty;
		}

		return WorkflowTabController.NormalizeWorkflowRelativePath(relative);
	}

	private string GetWorkflowRelativePath(string fullPath, bool isDirectory)
	{
		if (!isDirectory)
		{
			return GetWorkflowRelativePath(fullPath);
		}

		if (string.IsNullOrWhiteSpace(_comfyWorkflowsPath) || !IOPath.IsPathFullyQualified(fullPath))
		{
			return string.Empty;
		}

		string relative = IOPath.GetRelativePath(_comfyWorkflowsPath, fullPath).Replace('\\', '/');
		if (relative.StartsWith("..", StringComparison.Ordinal))
		{
			return string.Empty;
		}

		return WorkflowTabController.NormalizeWorkflowDirectoryRelativePath(relative);
	}

	private async Task<AssetMutationPreparationResult> PrepareWorkflowAssetMutationAsync(AssetPathMutation mutation)
	{
		if (!IsWorkflowAssetMutation(mutation))
		{
			return AssetMutationPreparationResult.Proceed;
		}

		await _workflowAssetMutationGate.WaitAsync();
		try
		{
			string sourceRelativePath = GetWorkflowRelativePath(mutation.SourcePath, mutation.IsDirectory);
			var affectedWorkflows = _tabController.GetWorkflowsForPath(sourceRelativePath, mutation.IsDirectory);
			if (CanUseWorkflowStoreMutation(mutation))
			{
				string destinationRelativePath = GetWorkflowRelativePath(mutation.DestinationPath!);
				string oldWebPath = $"workflows/{WorkflowTabController.StripWorkflowPrefix(sourceRelativePath)}";
				string newWebPath = $"workflows/{WorkflowTabController.StripWorkflowPrefix(destinationRelativePath)}";
				if (!await _webViewBridge.RenameWorkflowByStoreAsync(oldWebPath, newWebPath))
				{
					Log($"[WORKFLOW] Web path mutation failed: {oldWebPath} -> {newWebPath}");
					_workflowAssetMutationGate.Release();
					return AssetMutationPreparationResult.Cancel;
				}

				_pendingWorkflowAssetMutation = new PendingWorkflowAssetMutation(
					mutation,
					affectedWorkflows.Select(workflow => workflow.RelativePath).ToArray(),
					HandledByWeb: true);
				return AssetMutationPreparationResult.Handled;
			}

			foreach (var workflow in affectedWorkflows)
			{
				if (!workflow.Modified)
				{
					continue;
				}

				if (!await _tabController.ActivateWorkflowAndWaitAsync(workflow, WorkflowMutationActionTimeout))
				{
					Log($"[WORKFLOW] File mutation cancelled: workflow activation timed out ({workflow.RelativePath}).");
					_workflowAssetMutationGate.Release();
					return AssetMutationPreparationResult.Cancel;
				}

				await InvokeWorkflowMenuActionAsync(WorkflowMenuActions.Save);
				if (!await _tabController.WaitForWorkflowSavedAsync(workflow.RelativePath, WorkflowMutationActionTimeout))
				{
					Log($"[WORKFLOW] File mutation cancelled: workflow save did not complete ({workflow.RelativePath}).");
					_workflowAssetMutationGate.Release();
					return AssetMutationPreparationResult.Cancel;
				}
			}

			var openPaths = affectedWorkflows
				.Select(workflow => WorkflowTabController.NormalizeWorkflowRelativePath(workflow.RelativePath))
				.Where(path => !string.IsNullOrWhiteSpace(path))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
			if (openPaths.Length > 0 &&
				!await _tabController.CloseWorkflowsAndWaitAsync(openPaths, WorkflowMutationActionTimeout))
			{
				Log($"[WORKFLOW] File mutation cancelled: workflow tabs did not close ({sourceRelativePath}).");
				_workflowAssetMutationGate.Release();
				return AssetMutationPreparationResult.Cancel;
			}

			_pendingWorkflowAssetMutation = new PendingWorkflowAssetMutation(mutation, openPaths, HandledByWeb: false);
			return AssetMutationPreparationResult.Proceed;
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "Failed to prepare workflow asset mutation");
			_workflowAssetMutationGate.Release();
			return AssetMutationPreparationResult.Cancel;
		}
	}

	private bool CanUseWorkflowStoreMutation(AssetPathMutation mutation)
	{
		if (mutation.IsDirectory ||
			mutation.Kind == AssetPathMutationKind.Delete ||
			string.IsNullOrWhiteSpace(mutation.DestinationPath) ||
			!File.Exists(mutation.SourcePath) ||
			!IsWorkflowIndexFile(mutation.SourcePath))
		{
			return false;
		}

		return IsInsideWorkflowRoot(mutation.DestinationPath) &&
			IsWorkflowIndexFile(mutation.DestinationPath);
	}

	private async Task CompleteWorkflowAssetMutationAsync(AssetPathMutation mutation, bool succeeded)
	{
		if (!IsWorkflowAssetMutation(mutation))
		{
			return;
		}

		try
		{
			var pending = _pendingWorkflowAssetMutation;
			_pendingWorkflowAssetMutation = null;
			if (!succeeded)
			{
				Log($"[WORKFLOW] File mutation failed after tabs were closed: {mutation.SourcePath}");
				return;
			}

			if (mutation.Kind != AssetPathMutationKind.Delete &&
				!string.IsNullOrWhiteSpace(mutation.DestinationPath))
			{
				string oldRelativePath = GetWorkflowRelativePath(mutation.SourcePath, mutation.IsDirectory);
				string newRelativePath = GetWorkflowRelativePath(mutation.DestinationPath, mutation.IsDirectory);
				if (!string.IsNullOrWhiteSpace(oldRelativePath) && !string.IsNullOrWhiteSpace(newRelativePath))
				{
					bool tabsChanged = _tabController.RemapTrackedWorkflowPaths(oldRelativePath, newRelativePath, mutation.IsDirectory);
					bool bookmarksChanged = await RemapWorkflowBookmarksAsync(
						oldRelativePath,
						newRelativePath,
						mutation.IsDirectory,
						deferPersistence: mutation.IsBatch);
					if (tabsChanged || bookmarksChanged)
					{
						Log($"[WORKFLOW] Path remapped: {oldRelativePath} -> {newRelativePath}");
					}
				}
			}

			if (mutation.Kind == AssetPathMutationKind.Delete)
			{
				await RemoveWorkflowBookmarksForPathAsync(
					GetWorkflowRelativePath(mutation.SourcePath, mutation.IsDirectory),
					mutation.IsDirectory,
					deferPersistence: mutation.IsBatch);
			}
			if (mutation.IsBatch)
			{
				_workflowBatchMutationDirty = true;
				return;
			}

			if (pending?.HandledByWeb == true)
			{
				return;
			}

			await RefreshWorkflowIndexAndWebAsync();

			if (pending == null || mutation.IsDirectory || mutation.Kind == AssetPathMutationKind.Delete ||
				pending.OpenWorkflowPaths.Count == 0 || string.IsNullOrWhiteSpace(mutation.DestinationPath))
			{
				return;
			}

			string destinationRelativePath = GetWorkflowRelativePath(mutation.DestinationPath);
			if (!string.IsNullOrWhiteSpace(destinationRelativePath) && File.Exists(mutation.DestinationPath))
			{
				await LoadWorkflowByPath(destinationRelativePath);
			}
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "Failed to complete workflow asset mutation");
		}
		finally
		{
			_workflowAssetMutationGate.Release();
		}
	}

	private async Task BeginAssetBatchOperationAsync(int itemCount)
	{
		_workflowBatchMutationDirty = false;
		_workflowBatchBookmarksDirty = false;
		_workflowBatchOperationActive = true;
		_workflowBatchOperationStartedAt = DateTimeOffset.UtcNow;
		try
		{
			await SetShutdownBlockerVisibleAsync(
				true,
				LocalizationManager.Text("workflow.batch_operation.blocker_title"),
				LocalizationManager.Format("workflow.batch_operation.blocker_detail", itemCount));
		}
		catch
		{
			_workflowBatchOperationActive = false;
			try
			{
				await SetShutdownBlockerVisibleAsync(false);
			}
			catch (Exception ex)
			{
				NexusLog.Exception(ex, "Failed to clean up the workflow batch operation blocker");
			}
			throw;
		}
	}

	private async Task EndAssetBatchOperationAsync()
	{
		try
		{
			await Task.Delay(WorkflowBatchOperationSettleDelay);
			TimeSpan remainingBounceTime = TimeSpan.FromMilliseconds(ShutdownBlockerLogoBounceLength) -
				(DateTimeOffset.UtcNow - _workflowBatchOperationStartedAt);
			if (remainingBounceTime > TimeSpan.Zero)
			{
				await Task.Delay(remainingBounceTime);
			}

			if (_workflowBatchBookmarksDirty)
			{
				await SaveBookmarksToFile();
				await _webViewBridge.RefreshBookmarksAsync();
				RenderBookmarkHUDList();
			}

			if (_workflowBatchMutationDirty)
			{
				await RefreshWorkflowIndexAndWebAsync();
			}
		}
		finally
		{
			_workflowBatchMutationDirty = false;
			_workflowBatchBookmarksDirty = false;
			_workflowBatchOperationActive = false;
			try
			{
				await SetShutdownBlockerVisibleAsync(false);
			}
			catch (Exception ex)
			{
				NexusLog.Exception(ex, "Failed to hide the workflow batch operation blocker");
			}
		}
	}

	private bool IsWorkflowAssetMutation(AssetPathMutation mutation)
	{
		if (!IsInsideWorkflowRoot(mutation.SourcePath))
		{
			return false;
		}

		return mutation.IsDirectory || IsWorkflowIndexFile(mutation.SourcePath);
	}

	private async Task RemoveWorkflowBookmarksForPathAsync(
		string relativePath,
		bool isDirectory,
		bool deferPersistence = false)
	{
		string normalizedPath = isDirectory
			? WorkflowTabController.NormalizeWorkflowDirectoryRelativePath(relativePath)
			: WorkflowTabController.NormalizeWorkflowRelativePath(relativePath);
		string directoryPrefix = $"{normalizedPath.TrimEnd('/')}/";
		var bookmarks = _tabController.BookmarkedWorkflows.ToHashSet(StringComparer.OrdinalIgnoreCase);
		int removed = bookmarks.RemoveWhere(bookmark => isDirectory
			? WorkflowTabController.NormalizeWorkflowRelativePath(bookmark).StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase)
			: string.Equals(
				WorkflowTabController.NormalizeWorkflowRelativePath(bookmark),
				normalizedPath,
				StringComparison.OrdinalIgnoreCase));
		if (removed == 0)
		{
			return;
		}

		_tabController.SetBookmarkedWorkflows(bookmarks);
		if (deferPersistence)
		{
			_workflowBatchBookmarksDirty = true;
			return;
		}

		await SaveBookmarksToFile();
		await _webViewBridge.RefreshBookmarksAsync();
		RenderBookmarkHUDList();
	}

	private bool IsInsideWorkflowRoot(string fullPath)
	{
		if (string.IsNullOrWhiteSpace(_comfyWorkflowsPath) || string.IsNullOrWhiteSpace(fullPath))
		{
			return false;
		}

		try
		{
			string relative = IOPath.GetRelativePath(_comfyWorkflowsPath, fullPath);
			return !relative.StartsWith("..", StringComparison.Ordinal) &&
				!IOPath.IsPathFullyQualified(relative);
		}
		catch
		{
			return false;
		}
	}

	private async Task<bool> RemapWorkflowBookmarksAsync(
		string oldRelativePath,
		string newRelativePath,
		bool isDirectory,
		bool deferPersistence = false)
	{
		var bookmarks = _tabController.BookmarkedWorkflows.ToHashSet(StringComparer.OrdinalIgnoreCase);
		if (bookmarks.Count == 0)
		{
			return false;
		}

		bool changed = false;
		var remappedBookmarks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string bookmark in bookmarks)
		{
			if (WorkflowTabController.TryRemapWorkflowRelativePath(bookmark, oldRelativePath, newRelativePath, isDirectory, out string remapped))
			{
				remappedBookmarks.Add(remapped);
				changed = true;
			}
			else
			{
				remappedBookmarks.Add(bookmark);
			}
		}

		if (!changed)
		{
			return false;
		}

		_tabController.SetBookmarkedWorkflows(remappedBookmarks);
		if (deferPersistence)
		{
			_workflowBatchBookmarksDirty = true;
			return true;
		}

		await SaveBookmarksToFile();
		await _webViewBridge.RefreshBookmarksAsync();
		RenderBookmarkHUDList();
		return true;
	}

	private string ResolveWorkflowFullPath(string relativePath)
	{
		string stripped = WorkflowTabController.StripWorkflowPrefix(relativePath);
		string[] segments = stripped.Split('/', StringSplitOptions.RemoveEmptyEntries);
		return IOPath.Combine([_comfyWorkflowsPath, .. segments]);
	}

	private async Task HandleWorkflowTabActionAsync(WorkflowTabController.WorkflowTabInfo workflow, WorkflowActionKind kind)
	{
		if (kind == WorkflowActionKind.Bookmark)
		{
			await ToggleBookmarkAsync(workflow);
			return;
		}

		if (kind == WorkflowActionKind.Rename)
		{
			if (!workflow.HasFile)
			{
				return;
			}

			if (workflow.IsRootWorkflowFile)
			{
				await InvokeWorkflowMenuActionAsync(WorkflowMenuActions.Rename);
				return;
			}

			await RenameTrackedSubfolderWorkflowAsync(workflow);
			return;
		}

		if (kind is WorkflowActionKind.Save or WorkflowActionKind.SaveAs)
		{
			await InvokeWorkflowMenuActionAsync(kind == WorkflowActionKind.Save
				? WorkflowMenuActions.Save
				: WorkflowMenuActions.SaveAs);
			return;
		}

		string? action = kind switch
		{
			WorkflowActionKind.Duplicate => WorkflowMenuActions.Duplicate,
			WorkflowActionKind.Export => WorkflowMenuActions.Export,
			WorkflowActionKind.ExportApi => WorkflowMenuActions.ExportApi,
			WorkflowActionKind.Clear => WorkflowMenuActions.Clear,
			WorkflowActionKind.Delete => WorkflowMenuActions.Delete,
			_ => null
		};

		if (!string.IsNullOrWhiteSpace(action))
		{
			await InvokeWorkflowMenuActionAsync(action);
		}
	}

	private async Task RenameTrackedSubfolderWorkflowAsync(WorkflowTabController.WorkflowTabInfo workflow)
	{
		string sourcePath = ResolveWorkflowFullPath(workflow.RelativePath);
		if (!File.Exists(sourcePath))
		{
			await NexusDialogService.AlertAsync(
				LocalizationManager.Text("workflow.dialog.workflow_missing_title"),
				LocalizationManager.Text("workflow.dialog.tracked_workflow_missing_message"));
			await RefreshWorkflowIndexAndWebAsync();
			return;
		}

		string currentBaseName = IOPath.GetFileNameWithoutExtension(sourcePath);
		string? newBaseName = await NexusDialogService.PromptAsync(
			LocalizationManager.Text("workflow.dialog.rename_file_title"),
			LocalizationManager.Format("workflow.dialog.rename_file_message", IOPath.GetDirectoryName(sourcePath)),
			LocalizationManager.Text("common.rename"),
			LocalizationManager.Text("common.cancel"),
			initialValue: currentBaseName,
			maxLength: 180);

		if (string.IsNullOrWhiteSpace(newBaseName))
		{
			return;
		}

		string safeName = SanitizeWorkflowFileBaseName(newBaseName);
		if (string.IsNullOrWhiteSpace(safeName))
		{
			await NexusDialogService.AlertAsync(
				LocalizationManager.Text("workflow.dialog.invalid_name_title"),
				LocalizationManager.Text("workflow.dialog.invalid_file_name_message"));
			return;
		}

		string targetPath = IOPath.Combine(IOPath.GetDirectoryName(sourcePath) ?? _comfyWorkflowsPath, $"{safeName}.json");
		if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		if (File.Exists(targetPath))
		{
			await NexusDialogService.AlertAsync(
				LocalizationManager.Text("workflow.dialog.file_exists_title"),
				LocalizationManager.Format("workflow.dialog.file_exists_message", IOPath.GetFileName(targetPath)));
			return;
		}

		var mutation = new AssetPathMutation(AssetPathMutationKind.Rename, sourcePath, targetPath, IsDirectory: false);
		AssetMutationPreparationResult preparation = await PrepareWorkflowAssetMutationAsync(mutation);
		if (preparation == AssetMutationPreparationResult.Cancel)
		{
			return;
		}
		if (preparation == AssetMutationPreparationResult.Handled)
		{
			await CompleteWorkflowAssetMutationAsync(mutation, succeeded: true);
			return;
		}

		bool succeeded = false;
		try
		{
			File.Move(sourcePath, targetPath);
			succeeded = true;
		}
		finally
		{
			await CompleteWorkflowAssetMutationAsync(mutation, succeeded);
		}
	}

	private static string SanitizeWorkflowFileBaseName(string value)
	{
		string trimmed = value.Trim();
		foreach (char invalid in IOPath.GetInvalidFileNameChars())
		{
			trimmed = trimmed.Replace(invalid, '-');
		}

		return IOPath.GetFileNameWithoutExtension(trimmed);
	}

	private void UpdateComfyManagerAvailability(string comfyPath)
	{
		bool isInstalled = IsComfyManagerInstalled(comfyPath);
		HeaderControl?.SetManagerActionsVisible(isInstalled);
		Log(isInstalled
			? "[SYSTEM] ComfyUI-Manager detected. Manager actions enabled."
			: "[SYSTEM] ComfyUI-Manager not found. Manager actions hidden.");
	}

	private static bool IsComfyManagerInstalled(string comfyPath)
	{
		if (string.IsNullOrWhiteSpace(comfyPath))
		{
			return false;
		}

		string managerPath = IOPath.Combine(
			comfyPath,
			ComfyPathOptions.CustomNodesDirectoryName,
			ComfyPathOptions.ManagerDirectoryName);
		return Directory.Exists(managerPath);
	}

	private string DiscoverUserFolder(string userRoot)
	{
		try
		{
			if (!Directory.Exists(userRoot))
			{
				return ComfyPathOptions.DefaultUserName;
			}

			var dirs = Directory.GetDirectories(userRoot)
				.Select(IOPath.GetFileName)
				.Where(d => d != ComfyPathOptions.ManagerUserDirectoryName && d != null)
				.ToList();

			if (dirs.Contains(ComfyPathOptions.DefaultUserName))
			{
				return ComfyPathOptions.DefaultUserName;
			}

			return dirs.FirstOrDefault() ?? ComfyPathOptions.DefaultUserName;
		}
		catch
		{
			return ComfyPathOptions.DefaultUserName;
		}
	}

	private Task LoadBookmarks()
	{
		lock (_bookmarkLoadLock)
		{
			if (_bookmarkLoadTask is { IsCompleted: false })
			{
				return _bookmarkLoadTask;
			}

			_bookmarkLoadTask = LoadBookmarksCoreAsync();
			return _bookmarkLoadTask;
		}
	}

	private async Task LoadBookmarksCoreAsync()
	{
		if (string.IsNullOrWhiteSpace(_comfyWorkflowsPath))
		{
			_loginSequence.Phase(BootPhase.BookmarksLoadCompleted, "skipped: workflow path unavailable");
			return;
		}

		_loginSequence.Phase(BootPhase.BookmarksLoadStarted);
		var bookmarks = await Ui.WorkflowBookmarkService.SyncAndLoadAsync(_comfyWorkflowsPath);
		_tabController.SetBookmarkedWorkflows(bookmarks);
		Log($"[BOOKMARK] Sync complete via Service. {_tabController.BookmarkedWorkflows.Count} active.");
		_loginSequence.Phase(BootPhase.BookmarksLoadCompleted, $"{_tabController.BookmarkedWorkflows.Count} active");
	}

	private async Task SaveBookmarksToFile()
	{
		if (string.IsNullOrWhiteSpace(_comfyWorkflowsPath))
		{
			return;
		}

		await Ui.WorkflowBookmarkService.SaveAsync(_comfyWorkflowsPath, _tabController.BookmarkedWorkflows);
		Log($"[BOOKMARK] Saved {_tabController.BookmarkedWorkflows.Count} bookmark(s) to .index.json");
	}

	private void UpdateWorkflowContextBar()
	{
		if (HeaderControl == null)
		{
			return;
		}

		var active = _tabController.ActiveWorkflow;
		if (active == null)
		{
			HeaderControl.ClearWorkflowSummary();
			return;
		}

		bool bookmarked = _tabController.IsBookmarked(active);
		HeaderControl.UpdateWorkflowSummary(active.Name, active.HasFile, active.Modified, bookmarked);
	}

	private async Task ToggleBookmarkAsync(string name)
	{
		await LoadBookmarks();
		var active = _tabController.ActiveWorkflow;
		if (active == null || !active.HasFile || string.IsNullOrWhiteSpace(active.RelativePath))
		{
			return;
		}

		await ToggleBookmarkAsync(active);
	}

	private async Task ToggleBookmarkAsync(WorkflowTabController.WorkflowTabInfo workflow)
	{
		await LoadBookmarks();

		if (!workflow.HasFile || string.IsNullOrWhiteSpace(workflow.RelativePath))
		{
			return;
		}

		var bookmarks = _tabController.BookmarkedWorkflows.ToHashSet(StringComparer.OrdinalIgnoreCase);
		string relativePath = WorkflowTabController.NormalizeWorkflowRelativePath(workflow.RelativePath);
		string displayName = GetWorkflowDisplayName(relativePath);
		if (bookmarks.Contains(relativePath))
		{
			bookmarks.Remove(relativePath);
			Log($"[BOOKMARK] Removed: '{displayName}'");
		}
		else
		{
			bookmarks.Add(relativePath);
			Log($"[BOOKMARK] Added: '{displayName}'");
		}

		_tabController.SetBookmarkedWorkflows(bookmarks);
		await SaveBookmarksToFile();
		await _webViewBridge.RefreshBookmarksAsync();

		MainThread.BeginInvokeOnMainThread(RefreshTabsFromLastSync);
	}

	private async void ShowBookmarkHUD()
	{
		await LoadBookmarks();
		BookmarkHudControl.ClearSearch();
		BookmarkHudControl.SetOverlayState(isVisible: true, opacity: 0);
		RenderBookmarkHUDList();

		await BookmarkHudControl.FadeToAsync(1, BookmarkHudFadeInLength);
		BookmarkHudControl.ScrollToTop();
		BookmarkHudControl.FocusSearch();
	}

	private async void OnCloseBookmarkHUDClicked(object? sender, EventArgs e)
	{
		await HideBookmarkHudAsync();
	}

	private void OnBookmarkSearchChanged(object? sender, TextChangedEventArgs e)
	{
		RenderBookmarkHUDList(e.NewTextValue);
	}

	private void RenderBookmarkHUDList(string filter = "")
	{
		if (BookmarkHudControl == null || string.IsNullOrWhiteSpace(_comfyWorkflowsPath))
		{
			return;
		}

		string normalizedFilter = filter.Trim();
		var allBookmarkedPaths = _tabController.BookmarkedWorkflows
			.Where(path => File.Exists(ResolveWorkflowFullPath(path)))
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		var allKnownWorkflowPaths = _knownWorkflowRelativePaths
			.Where(path => File.Exists(ResolveWorkflowFullPath(path)))
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		var bookmarkedPaths = allBookmarkedPaths;
		var knownWorkflowPaths = allKnownWorkflowPaths;

		if (!string.IsNullOrWhiteSpace(normalizedFilter))
		{
			bookmarkedPaths = bookmarkedPaths
				.Where(path => GetWorkflowDisplayName(path).Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase) ||
					path.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase))
				.ToArray();
			knownWorkflowPaths = knownWorkflowPaths
				.Where(path => GetWorkflowDisplayName(path).Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase) ||
					path.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase))
				.ToArray();
		}

		var bookmarkedSet = new HashSet<string>(bookmarkedPaths, StringComparer.OrdinalIgnoreCase);
		var unbookmarkedKnownPaths = knownWorkflowPaths
			.Where(path => !bookmarkedSet.Contains(path))
			.ToArray();

		BookmarkHudControl.ClearItems();

		if (bookmarkedPaths.Length > 0)
		{
			AddSectionHeader(LocalizationManager.Text("workflow.bookmark_hud.bookmarked"), BookmarkHudPinnedAccentColor);
			foreach (string path in bookmarkedPaths)
			{
				BookmarkHudControl.AddItem(CreateWorkflowItem(path, isPinned: true));
			}
		}

		if (unbookmarkedKnownPaths.Length > 0)
		{
			AddSectionHeader(LocalizationManager.Text("workflow.bookmark_hud.available"), BookmarkHudAvailableAccentColor);
			foreach (string path in unbookmarkedKnownPaths)
			{
				BookmarkHudControl.AddItem(CreateWorkflowItem(path, isPinned: false));
			}
		}

		int totalVisible = bookmarkedPaths.Length + unbookmarkedKnownPaths.Length;
		if (totalVisible == 0)
		{
			BookmarkHudControl.AddItem(new Label
			{
				Text = allKnownWorkflowPaths.Length == 0
					? LocalizationManager.Text("workflow.bookmark_hud.no_workflow_files")
					: LocalizationManager.Text("workflow.bookmark_hud.no_filter_matches"),
				TextColor = BookmarkHudEmptyTextColor,
				FontSize = BookmarkHudEmptyFontSize,
				Margin = new Thickness(10, 20),
				HorizontalOptions = LayoutOptions.Center,
				HorizontalTextAlignment = TextAlignment.Center,
				LineBreakMode = LineBreakMode.WordWrap
			});
		}

		int totalKnown = allKnownWorkflowPaths.Length;
		int totalBookmarked = allBookmarkedPaths.Length;
		string countText = string.IsNullOrWhiteSpace(normalizedFilter)
			? LocalizationManager.Format("workflow.bookmark_hud.count", totalBookmarked, totalKnown)
			: LocalizationManager.Format("workflow.bookmark_hud.filtered_count", totalVisible, totalBookmarked, totalKnown);
		BookmarkHudControl.SetCountText(countText);
	}

	private void AddSectionHeader(string text, Color color)
	{
		BookmarkHudControl.AddItem(new Label
		{
			Text = text,
			TextColor = color,
			FontSize = BookmarkHudSectionFontSize,
			FontAttributes = FontAttributes.Bold,
			CharacterSpacing = 1.5,
			Margin = new Thickness(6, 10, 6, 4)
		});
	}

	private Border CreateWorkflowItem(string relativePath, bool isPinned)
	{
		string displayName = GetWorkflowDisplayName(relativePath);
		string displayPath = WorkflowTabController.StripWorkflowPrefix(relativePath);
		var titleLabel = new Label
		{
			Text = displayName,
			TextColor = Colors.White,
			FontSize = BookmarkHudTitleFontSize,
			LineBreakMode = LineBreakMode.TailTruncation,
			VerticalOptions = LayoutOptions.Center
		};

		var subtitleLabel = new Label
		{
			Text = displayPath,
			TextColor = BookmarkHudSubtitleColor,
			FontSize = BookmarkHudMetaFontSize,
			LineBreakMode = LineBreakMode.TailTruncation,
			VerticalOptions = LayoutOptions.Center
		};

		var statusLabel = new Label
		{
			Text = isPinned
				? LocalizationManager.Text("workflow.bookmark_hud.pinned")
				: LocalizationManager.Text("workflow.bookmark_hud.known"),
			TextColor = isPinned ? BookmarkHudPinnedAccentColor : BookmarkHudKnownTextColor,
			FontSize = BookmarkHudMetaFontSize,
			VerticalOptions = LayoutOptions.Center
		};

		var openTap = new TapGestureRecognizer();
		openTap.Tapped += async (s, e) => await LoadWorkflowByPath(relativePath);

		var openButton = CreateBookmarkHudButton(
			LocalizationManager.Text("common.open").ToUpperInvariant(),
			BookmarkHudOpenButtonBackgroundColor,
			BookmarkHudOpenButtonTextColor,
			new Thickness(12, 0),
			BookmarkHudOpenButtonMinWidth);
		openButton.Clicked += async (s, e) => await LoadWorkflowByPath(relativePath);

		var pinButton = CreateBookmarkHudButton(
			isPinned
				? LocalizationManager.Text("workflow.bookmark_hud.unpin")
				: LocalizationManager.Text("workflow.bookmark_hud.pin"),
			Colors.Transparent,
			isPinned ? BookmarkHudUnpinTextColor : BookmarkHudPinTextColor,
			new Thickness(10, 0),
			BookmarkHudPinButtonMinWidth);
		pinButton.Clicked += async (s, e) =>
		{
			await ToggleBookmarkPathAsync(relativePath);
			RenderBookmarkHUDList();
		};

		var textStack = new VerticalStackLayout
		{
			Spacing = 2
		};
		textStack.Children.Add(titleLabel);
		textStack.Children.Add(subtitleLabel);

		var itemGrid = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(GridLength.Auto),
				new ColumnDefinition(GridLength.Auto),
				new ColumnDefinition(GridLength.Auto)
			},
			ColumnSpacing = 10
		};
		Grid.SetColumn(textStack, 0);
		Grid.SetColumn(statusLabel, 1);
		Grid.SetColumn(openButton, 2);
		Grid.SetColumn(pinButton, 3);
		itemGrid.Children.Add(textStack);
		itemGrid.Children.Add(statusLabel);
		itemGrid.Children.Add(openButton);
		itemGrid.Children.Add(pinButton);

		var border = new Border
		{
			BackgroundColor = isPinned ? BookmarkHudPinnedBackgroundColor : BookmarkHudKnownBackgroundColor,
			Stroke = isPinned ? BookmarkHudPinnedStrokeColor : BookmarkHudKnownStrokeColor,
			StrokeThickness = 1,
			Padding = new Thickness(12, 10),
			Margin = new Thickness(0, 2),
			Content = itemGrid
		};
		border.StrokeShape = new RoundRectangle { CornerRadius = BookmarkHudItemCornerRadius };
		border.GestureRecognizers.Add(openTap);
		return border;
	}

	private static Button CreateBookmarkHudButton(
		string text,
		Color backgroundColor,
		Color textColor,
		Thickness padding,
		double minimumWidth)
		=> new()
		{
			Text = text,
			BackgroundColor = backgroundColor,
			TextColor = textColor,
			FontSize = BookmarkHudButtonFontSize,
			Padding = padding,
			MinimumWidthRequest = minimumWidth,
			CornerRadius = (int)BookmarkHudItemCornerRadius
		};

	private string GetWorkflowDisplayName(string relativePath)
	{
		string stripped = WorkflowTabController.StripWorkflowPrefix(relativePath);
		return IOPath.GetFileNameWithoutExtension(stripped);
	}

	private async Task ToggleBookmarkPathAsync(string relativePath)
	{
		await LoadBookmarks();

		string normalized = WorkflowTabController.NormalizeWorkflowRelativePath(relativePath);
		if (string.IsNullOrWhiteSpace(normalized) || !File.Exists(ResolveWorkflowFullPath(normalized)))
		{
			return;
		}

		var bookmarks = _tabController.BookmarkedWorkflows.ToHashSet(StringComparer.OrdinalIgnoreCase);
		if (!bookmarks.Remove(normalized))
		{
			bookmarks.Add(normalized);
		}

		_tabController.SetBookmarkedWorkflows(bookmarks);
		await SaveBookmarksToFile();
		await _webViewBridge.RefreshBookmarksAsync();
		MainThread.BeginInvokeOnMainThread(RefreshTabsFromLastSync);
	}

	private async Task LoadWorkflowByPath(string relativePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(_comfyWorkflowsPath))
		{
			return;
		}

		string normalized = WorkflowTabController.NormalizeWorkflowRelativePath(relativePath);
		string workflowPath = ResolveWorkflowFullPath(normalized);
		if (!File.Exists(workflowPath))
		{
			Log($"[BOOKMARK] Workflow file not found: {workflowPath}");
			await DisplayAlertAsync(
				LocalizationManager.Text("workflow.dialog.workflow_missing_title"),
				LocalizationManager.Format("workflow.dialog.workflow_path_not_found", WorkflowTabController.StripWorkflowPrefix(normalized)),
				LocalizationManager.Text("common.ok"));
			return;
		}

		await MainThread.InvokeOnMainThreadAsync(async () =>
		{
			if (await _tabController.TryActivateTrackedWorkflowAsync(normalized))
			{
				return;
			}

			_tabController.TrackOpenedWorkflow(GetWorkflowDisplayName(normalized), normalized);
			await WebViewUtility.SimulateFileDropAsync(
				WorkspaceControl.BrowserView,
				workflowPath,
				WorkflowTabController.StripWorkflowPrefix(normalized));
			await HideBookmarkHudAsync();
		});
	}

	private async Task HideBookmarkHudAsync()
	{
		await BookmarkHudControl.FadeToAsync(0, BookmarkHudFadeOutLength);
		BookmarkHudControl.SetOverlayState(isVisible: false, opacity: 0);
		BookmarkHudControl.ClearSearch();
	}
}
