using ComfyUI_Nexus.Diagnostics;
using System.Collections.ObjectModel;
using System.Text.Json;
using ComfyUI_Nexus.AssetHub;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Views.Rail.Tools;
using ComfyUI_Nexus.Views.Rail.Contracts;
using ComfyUI_Nexus.Views.Rail;

namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

public partial class AssetsBrowserView : ContentView, IAssetRailTool
{
	private const int AssetTreeRowPrewarmCount = 50;
	private const int AssetSearchRowPrewarmCount = 50;
	private const int AssetPoolPrewarmBatchSize = 24;
	private const int AssetTreeRenderBatchSize = 120;
	private const int ModelApiTreeBuildBatchSize = 200;
	private const double WorkflowBookmarkRowHeight = 26;
	private const double WorkflowBookmarkRowSpacing = 3;
	private const double WorkflowBookmarkMaxScrollHeight = 112;
	private const double WorkflowBookmarkScrollThumbMinHeight = 22;
	private const int FolderCreateSelectionDelayMs = 160;
	private const int ModelThumbnailHoverDelayMs = 190;
	private const double ModelThumbnailPreviewMaxWidth = 260;
	private const double ModelThumbnailPreviewMaxHeight = 260;

	private static readonly Color RowHoverColor = Color.FromArgb("#121d2b");
	private static readonly Color RowSelectedColor = Color.FromArgb("#17324a");
	private static readonly Color FixedCardActiveBackgroundColor = Color.FromArgb("#153247");
	private static readonly Color FixedCardInactiveBackgroundColor = Color.FromArgb("#101723");
	private static readonly Color FixedCardHoverBackgroundColor = Color.FromArgb("#132131");
	private static readonly Color FixedCardInactiveStrokeColor = Color.FromArgb("#223347");
	private static readonly Color FixedCardInactiveTextColor = Color.FromArgb("#d7e4f3");
	private static readonly Color AssetsRailLightAccentColor = Color.FromArgb("#8de7ff");
	private static readonly Color BookmarkActionColor = AssetsRailLightAccentColor;
	private static readonly Color BookmarkActiveBackgroundColor = Color.FromArgb("#1a2b3d");
	private static readonly Color BookmarkActiveStrokeColor = Color.FromArgb("#3d5c7a");
	private static readonly Color BookmarkInactiveBackgroundColor = Color.FromArgb("#090e14");
	private static readonly Color AssetDropHighlightColor = AssetsRailLightAccentColor;

	private sealed class RailTreeNode
	{
		public required string Name { get; init; }
		public required string FullPath { get; init; }
		public required bool IsDirectory { get; init; }
		public int Depth { get; init; }
		public bool IsExpanded { get; set; }
		public bool ChildrenLoaded { get; set; }
		public List<RailTreeNode> Children { get; } = [];
		public RailTreeNode? Parent { get; set; }
		public string IconKey { get; set; } = "file";
		public int? ModelFileCount { get; set; }
	}

	private sealed record RailTreeEntry(string Name, string FullPath, bool IsDirectory, int? ModelFileCount = null);

	private sealed record ModelRootShortcut(int Index, string Label, string Path);

	private static readonly string[] ModelApiCategories =
	[
		"checkpoints",
		"loras",
		"vae",
		"text_encoders",
		"diffusion_models",
		"clip_vision",
		"style_models",
		"embeddings",
		"diffusers",
		"vae_approx",
		"controlnet",
		"gligen",
		"upscale_models",
		"latent_upscale_models",
		"hypernetworks",
		"photomaker",
		"classifiers",
		"model_patches",
		"audio_encoders",
		"frame_interpolation",
		"animatediff_models",
		"animatediff_motion_lora",
		"animatediff_video_formats",
		"ipadapter",
		"sams",
		"onnx",
		"ultralytics_bbox",
		"ultralytics_segm",
		"ultralytics"
	];

	private readonly List<RailTreeNode> _rootNodes = [];
	private readonly ObservableCollection<RailTreeNode> _visibleTreeNodes = [];
	private readonly Dictionary<string, Grid> _rowMap = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Grid> _searchRowMap = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _expandedPaths = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, IReadOnlyList<RailTreeEntry>> _childrenCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Dictionary<string, IReadOnlyList<RailTreeEntry>>> _childrenCachesByRoot = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, bool> _modelApiCacheReadyByRoot = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, ModelAssetThumbnail?> _modelThumbnailPathCache = new(StringComparer.OrdinalIgnoreCase);
	private bool _modelApiTreeCacheReady;
	private string _rootPath = string.Empty;
	private AssetRootProfile? _currentProfile;
	private readonly AssetSelectionController _selection = new();
	private readonly RailDirectoryWatchController _treeWatcher;
	private readonly AssetFileOperationService _fileOperations;
	private readonly AssetHubNativeService _assetHubService = new();
	private readonly NexusOperationController _latestOperations = new("assets-browser");
	private bool _isAnimating;
	private readonly AssetClipboardController _clipboard = new();
	private readonly List<string> _bookmarkedPaths = [];
	private readonly List<string> _activeBookmarkPaths = [];
	private readonly HashSet<string> _workflowBookmarkPaths = new(StringComparer.OrdinalIgnoreCase);
	private readonly ObservableCollection<AssetHubItem> _searchResults = [];
	private bool _suppressNextAssetListSurfaceSelectionClear;
	private readonly RailLoadingOverlayController _loadingOverlay;
	private bool _bookmarksLoaded;
	private bool _isCurrentSectionExpanded = true;
	private readonly SemaphoreSlim _treeLock = new(1, 1);
	private bool _isTreeLoading;
	private string? _pendingRootPath;
	private bool _isInternalPickerUpdate;
	private bool _bookmarksDirty = true;
	private bool _isReady;
	private bool _isRailActive;
	private int _treeRenderVersion;
	private int _searchRenderVersion;
	private int _modelThumbnailHoverVersion;
	private bool _isApplyingWatcherChanges;
	private AssetOpenRequest? _activeDragRequest;
	private string? _activeDragRootPath;
	private IReadOnlyList<string>? _activeDragPaths;
	private Task? _poolPrewarmTask;
	private DataTemplate? _treeRowTemplate;
	private DataTemplate? _searchRowTemplate;
	private int _workflowBookmarksRenderVersion;
	private Func<AssetPathMutation, Task<AssetMutationPreparationResult>>? _prepareAssetMutationAsync;
	private Func<AssetPathMutation, bool, Task>? _completeAssetMutationAsync;
	private Func<int, Task>? _beginBatchOperationAsync;
	private Func<Task>? _endBatchOperationAsync;
	private bool _isWorkflowBatchOperationActive;
	private RailSearchVisualController? _searchVisuals;
	private NexusEntryTextController? _searchTextController;

	public event EventHandler<AssetOpenRequest>? FileOpenRequested;
	public event EventHandler<AssetOpenRequest>? AssetInteractionRequested;
	public event EventHandler<ModelAssetThumbnailPreviewRequest>? ModelThumbnailPreviewRequested;
	public event EventHandler? ModelThumbnailPreviewDismissed;
	internal event EventHandler? WorkflowBookmarksChanged;

	private string _fixedWorkflowsPath = "";
	public string FixedWorkflowsPath
	{
		get => _fixedWorkflowsPath;
		set
		{
			_fixedWorkflowsPath = value;
			RenderFixedCards();
			RenderBookmarks();
			_ = RefreshWorkflowBookmarksSectionAsync();
		}
	}

	public AssetsBrowserView()
	{
		InitializeComponent();
		_loadingOverlay = new RailLoadingOverlayController(RailLoadingOverlay);
		_searchVisuals = new RailSearchVisualController(RailSearchBorder, RailSearchEntry);
		_searchTextController = new NexusEntryTextController(RailSearchEntry, RailSearchBorder);
		new RailSearchClearButtonController(RailSearchClearButton, RailSearchClearLabel);
		ConfigureTreeVirtualization();
		ConfigureSearchVirtualization();
		_treeWatcher = new RailDirectoryWatchController(
			"ASSET_WATCHER",
			Dispatcher,
			CanApplyWatcherBatch,
			ApplyPendingFileSystemChanges);
		_fileOperations = new AssetFileOperationService(
			_selection,
			_clipboard,
			() => _rootPath,
			OpenInOsAsync,
			RefreshDirectoriesImmediately,
			RefreshVisibleSelectionState,
			NotifyFolderCreated,
			NotifyDirectoryContentAdded,
			mutation => _prepareAssetMutationAsync?.Invoke(mutation) ?? Task.FromResult(AssetMutationPreparationResult.Proceed),
			(mutation, succeeded) => _completeAssetMutationAsync?.Invoke(mutation, succeeded) ?? Task.CompletedTask,
			BeginWorkflowBatchOperationAsync,
			EndWorkflowBatchOperationAsync,
			ShouldRegenerateCopiedWorkflowMetadata);
		InitializeChromeState();
		Loaded += OnAssetsLoaded;
		Unloaded += OnAssetsUnloaded;
	}

	private void NotifyDirectoryContentAdded(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(_rootPath))
		{
			return;
		}

		foreach (string path in EnumerateDirectoryPathToRoot(directoryPath))
		{
			_expandedPaths.Add(path);
			if (TryGetNode(path, out var node) && node.IsDirectory)
			{
				node.IsExpanded = true;
				node.ChildrenLoaded = false;
				UpdateCachedChevron(node);
			}
		}
	}

	internal void SetPathMutationHandlers(
		Func<AssetPathMutation, Task<AssetMutationPreparationResult>> prepareMutationAsync,
		Func<AssetPathMutation, bool, Task> completeMutationAsync,
		Func<int, Task> beginBatchOperationAsync,
		Func<Task> endBatchOperationAsync)
	{
		_prepareAssetMutationAsync = prepareMutationAsync;
		_completeAssetMutationAsync = completeMutationAsync;
		_beginBatchOperationAsync = beginBatchOperationAsync;
		_endBatchOperationAsync = endBatchOperationAsync;
	}

	private async Task BeginWorkflowBatchOperationAsync(int itemCount)
	{
		_isWorkflowBatchOperationActive = !string.IsNullOrWhiteSpace(FixedWorkflowsPath) &&
			string.Equals(
				Path.GetFullPath(_rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
				Path.GetFullPath(FixedWorkflowsPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
				StringComparison.OrdinalIgnoreCase);
		if (_isWorkflowBatchOperationActive && _beginBatchOperationAsync != null)
		{
			await _beginBatchOperationAsync(itemCount);
		}
	}

	private async Task EndWorkflowBatchOperationAsync()
	{
		if (!_isWorkflowBatchOperationActive)
		{
			return;
		}

		_isWorkflowBatchOperationActive = false;
		if (_endBatchOperationAsync != null)
		{
			await _endBatchOperationAsync();
		}
	}

	private bool ShouldRegenerateCopiedWorkflowMetadata(string destinationPath)
	{
		if (string.IsNullOrWhiteSpace(FixedWorkflowsPath) ||
			!string.Equals(Path.GetExtension(destinationPath), ".json", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(Path.GetFileName(destinationPath), ".index.json", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		try
		{
			string relativePath = Path.GetRelativePath(FixedWorkflowsPath, destinationPath);
			return !relativePath.StartsWith("..", StringComparison.Ordinal) &&
				!Path.IsPathFullyQualified(relativePath);
		}
		catch
		{
			return false;
		}
	}

	private void NotifyFolderCreated(string parentPath, string createdPath)
	{
		if (!string.IsNullOrWhiteSpace(parentPath))
		{
			_expandedPaths.Add(parentPath);
			if (TryGetNode(parentPath, out var parentNode) && parentNode.IsDirectory)
			{
				parentNode.IsExpanded = true;
				parentNode.ChildrenLoaded = false;
				UpdateCachedChevron(parentNode);
			}
		}

		if (HasSearchText)
		{
			RailSearchEntry.Text = string.Empty;
			ExitSearchMode();
		}

		MainThread.BeginInvokeOnMainThread(async () =>
		{
			await Task.Delay(FolderCreateSelectionDelayMs);
			SelectPath(createdPath);
			RefreshVisibleSelectionState();
		});
	}

	private IEnumerable<string> EnumerateDirectoryPathToRoot(string directoryPath)
	{
		var paths = new Stack<string>();
		string? current = directoryPath;
		while (!string.IsNullOrWhiteSpace(current))
		{
			if (string.Equals(current, _rootPath, StringComparison.OrdinalIgnoreCase))
			{
				break;
			}
			if (!IsPathInsideRoot(current))
			{
				break;
			}

			paths.Push(current);
			current = Directory.GetParent(current)?.FullName;
		}

		while (paths.Count > 0)
		{
			yield return paths.Pop();
		}
	}

	private bool IsPathInsideRoot(string path)
	{
		try
		{
			string relative = Path.GetRelativePath(_rootPath, path);
			return !relative.StartsWith("..", StringComparison.Ordinal) &&
				!Path.IsPathFullyQualified(relative);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Pre-creates reusable asset tree and search rows so opening the rail does not allocate the first visible batch.
	/// </summary>
	internal Task EnsurePoolPrewarmedAsync()
	{
		_poolPrewarmTask ??= PrewarmPoolsAsync();
		return _poolPrewarmTask;
	}

	private async Task PrewarmPoolsAsync()
	{
		await PrewarmTreeVirtualRowsAsync();
		await PrewarmSearchVirtualRowsAsync();
	}

	private void ConfigureTreeVirtualization()
	{
		_treeRowTemplate = new DataTemplate(() =>
		{
			var row = AssetTreeRowFactory.Create(WireAssetTreeRow);
			row.BindingContextChanged += OnVirtualTreeRowBindingContextChanged;
			return row;
		});

		RailTreeVirtualList.ItemTemplateSelector = new SingleTemplateSelector(_treeRowTemplate);
		RailTreeVirtualList.ItemHeightSelector = _ => 28;
		RailTreeVirtualList.ItemsSource = _visibleTreeNodes;
	}

	private Task PrewarmTreeVirtualRowsAsync()
	{
		var sample = new RailTreeNode
		{
			Name = string.Empty,
			FullPath = "__asset_tree_prewarm__",
			IsDirectory = false,
		};

		return RailTreeVirtualList.PrewarmViewPoolAsync(
			[sample],
			AssetTreeRowPrewarmCount,
			AssetPoolPrewarmBatchSize,
			CancellationToken.None);
	}

	private void OnVirtualTreeRowBindingContextChanged(object? sender, EventArgs e)
	{
		if (sender is not Grid row)
		{
			return;
		}

		if (!string.IsNullOrWhiteSpace(row.StyleId) &&
			_rowMap.TryGetValue(row.StyleId, out var mappedRow) &&
			ReferenceEquals(mappedRow, row))
		{
			_rowMap.Remove(row.StyleId);
		}

		if (row.BindingContext is RailTreeNode node)
		{
			UpdateNodeRow(row, node);
			row.StyleId = node.FullPath;
			_rowMap[node.FullPath] = row;
			return;
		}

		AssetTreeRowFactory.Reset(row);
		row.StyleId = string.Empty;
	}

	private sealed class SingleTemplateSelector(DataTemplate template) : DataTemplateSelector
	{
		protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
			=> template;
	}

	private void ConfigureSearchVirtualization()
	{
		_searchRowTemplate = new DataTemplate(() =>
		{
			var row = CreateSearchResultRow();
			row.BindingContextChanged += OnVirtualSearchRowBindingContextChanged;
			return row;
		});

		RailSearchVirtualList.ItemTemplateSelector = new SingleTemplateSelector(_searchRowTemplate);
		RailSearchVirtualList.ItemHeightSelector = _ => 54;
		RailSearchVirtualList.ItemsSource = _searchResults;
	}

	private Task PrewarmSearchVirtualRowsAsync()
	{
		var sample = new AssetHubItem(
			"__asset_search_prewarm__",
			"__asset_search_prewarm__",
			AssetHubItemType.File,
			0,
			null,
			false,
			false);

		return RailSearchVirtualList.PrewarmViewPoolAsync(
			[sample],
			AssetSearchRowPrewarmCount,
			AssetPoolPrewarmBatchSize,
			CancellationToken.None);
	}

	private void OnVirtualSearchRowBindingContextChanged(object? sender, EventArgs e)
	{
		if (sender is not Grid row)
		{
			return;
		}

		if (!string.IsNullOrWhiteSpace(row.StyleId) &&
			_searchRowMap.TryGetValue(row.StyleId, out var mappedRow) &&
			ReferenceEquals(mappedRow, row))
		{
			_searchRowMap.Remove(row.StyleId);
		}

		if (row.BindingContext is AssetHubItem item)
		{
			UpdateSearchResultRow(row, item);
			row.StyleId = item.FullPath;
			_searchRowMap[item.FullPath] = row;
			return;
		}

		ResetSearchResultRow(row);
		row.StyleId = string.Empty;
	}

	View IRailToolView.View => this;
	bool IRailToolView.IsReady => _isReady;
	bool IRailToolView.IsBusy => _isTreeLoading;

	async Task IRailToolView.PrewarmAsync(CancellationToken cancellationToken)
	{
		// Startup prewarm is intentionally pool/bookmark-only. Filesystem work starts when the tool is visible.
		await EnsurePoolPrewarmedAsync();
		await EnsureBookmarksLoadedAsync();
	}

	void IRailToolView.PrepareOpenShell()
	{
		_isReady = false;
		_loadingOverlay.Show();
	}

	async Task IRailToolView.OpenAsync(CancellationToken cancellationToken)
	{
		var perf = RailPerformanceDiagnostics.Start();
		_isReady = false;
		_isRailActive = true;
		if (_rootNodes.Count == 0 || _treeWatcher.NeedsRefresh)
		{
			RailPerformanceDiagnostics.Mark("AssetsOpenBuildTreeStart", perf, $"nodes={_rootNodes.Count}, dirty={_treeWatcher.NeedsRefresh}");
			await BuildTreeAsync();
			_treeWatcher.MarkReconciled();
			RailPerformanceDiagnostics.Mark("AssetsOpenBuildTreeCompleted", perf, $"nodes={_rootNodes.Count}");
		}

		RailPerformanceDiagnostics.Mark("AssetsMaterializeStart", perf);
		await RailTreeVirtualList.MaterializeVisibleViewsAsync(cancellationToken);
		RailPerformanceDiagnostics.Mark("AssetsMaterializeCompleted", perf);
		await _loadingOverlay.HideAsync();

		if (cancellationToken.IsCancellationRequested)
		{
			return;
		}

		_isReady = true;
		if (CurrentTreeSource == AssetTreeSource.FileSystem)
		{
			_treeWatcher.SetActive(true);
		}
		RailPerformanceDiagnostics.Mark("AssetsOpenReady", perf);
	}

	void IRailToolView.ResetPresentation()
	{
		_isReady = false;
		_isRailActive = false;
		_latestOperations.InvalidateAll();
		_treeWatcher.SetActive(false);
	}

	private async void OnAssetsLoaded(object? sender, EventArgs e)
	{
		await EnsureBookmarksLoadedAsync();
		ConfigureAssetWatcherRoot();
	}

	private void OnAssetsUnloaded(object? sender, EventArgs e)
	{
		HideModelThumbnailPreview();
		_latestOperations.StopAll();
		_isRailActive = false;
		_treeWatcher.Dispose();
	}

	private async Task EnsureBookmarksLoadedAsync()
	{
		if (_bookmarksLoaded)
		{
			return;
		}

		_bookmarksLoaded = true;

		try
		{
			var bookmarks = await _assetHubService.LoadBookmarksAsync();
			_bookmarkedPaths.Clear();
			_bookmarkedPaths.AddRange(bookmarks.Where(Directory.Exists));
			_bookmarkedPaths.Sort(StringComparer.OrdinalIgnoreCase);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "Failed to load asset bookmarks");
		}

		MainThread.BeginInvokeOnMainThread(() =>
		{
			RenderFixedCards();
			_bookmarksDirty = true;
			RenderBookmarks();
		});
	}

	private async Task PersistBookmarksAsync()
	{
		try
		{
			await _assetHubService.SaveBookmarksAsync(_bookmarkedPaths);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "Failed to save asset bookmarks");
		}
	}

	/// <summary>
	/// Switches the asset browser to a root directory, resolving fixed profiles when possible.
	/// </summary>
	/// <param name="rootPath">Absolute path for Output/Input/Models/Workflows or a custom folder.</param>
	public void SetRootPath(string rootPath)
		=> SetRootProfile(ResolveProfileForPath(rootPath));

	internal void RefreshConfiguredRoots()
	{
		RenderFixedCards();
		RenderBookmarks();
		RefreshBackgroundContextMenu();

		if (_currentProfile == null || string.Equals(_currentProfile.Id, "custom", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		AssetRootProfile? refreshedProfile = GetFixedProfiles()
			.FirstOrDefault(profile => string.Equals(profile.Id, _currentProfile.Id, StringComparison.OrdinalIgnoreCase));
		if (refreshedProfile == null)
		{
			return;
		}

		if (!string.Equals(refreshedProfile.Path, _rootPath, StringComparison.OrdinalIgnoreCase))
		{
			ApplyRootProfile(refreshedProfile);
		}
		else if (string.Equals(refreshedProfile.Id, "models", StringComparison.OrdinalIgnoreCase))
		{
			ApplyModelLibraryLocationChrome();
		}
	}

	private void SetRootProfile(AssetRootProfile profile)
	{
		if (IsCurrentRootProfile(profile))
		{
			return;
		}

		if (_isTreeLoading)
		{
			_pendingRootPath = profile.Path ?? string.Empty;
			_loadingOverlay.Show();
			return;
		}

		ApplyRootProfile(profile);
	}

	private void ApplyRootPath(string rootPath)
		=> ApplyRootProfile(ResolveProfileForPath(rootPath));

	private bool IsCurrentRootProfile(AssetRootProfile profile)
		=> string.Equals(_rootPath, profile.Path ?? string.Empty, StringComparison.OrdinalIgnoreCase);

	private void ApplyRootProfile(AssetRootProfile profile)
	{
		_treeWatcher.SetActive(false);
		HideModelThumbnailPreview(clearCache: true);
		SaveCurrentTreeEntryCache();

		_currentProfile = profile;
		_rootPath = profile.Path ?? string.Empty;
		ConfigureAssetWatcherRoot();
		_selection.Clear();
		bool hasCachedTree = RestoreTreeEntryCache(_rootPath);

		if (string.IsNullOrWhiteSpace(_rootPath))
		{
			RailCurrentLocationTitleLabel.Text = "No root selected";
			RailPathLabel.Text = string.Empty;
			ToolTipProperties.SetText(RailCurrentLocationTitleLabel, string.Empty);
			ToolTipProperties.SetText(RailPathLabel, string.Empty);
		}
		else if (profile.TreeSource == AssetTreeSource.ModelApi)
		{
			ApplyModelLibraryLocationChrome();
		}
		else
		{
			string name = Path.GetFileName(_rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			RailCurrentLocationTitleLabel.Text = string.IsNullOrWhiteSpace(name) ? _rootPath : name;
			RailPathLabel.Text = _rootPath;
			ToolTipProperties.SetText(RailCurrentLocationTitleLabel, _rootPath);
			ToolTipProperties.SetText(RailPathLabel, _rootPath);
		}

		RenderFixedCards();
		RenderBookmarks();
		RefreshBackgroundContextMenu();
		_ = RefreshWorkflowBookmarksSectionAsync();
		if (HasSearchText)
		{
			_ = RefreshSearchResultsAsync(immediate: true);
		}
		else
		{
			ExitSearchMode();
		}
		if (_isRailActive)
		{
			ReloadTree(resetExpansion: true, invalidateLayout: false, clearDataCache: !hasCachedTree);
		}
	}

	private void ApplyModelLibraryLocationChrome()
	{
		var roots = GetModelRootShortcuts()
			.Where(root => Directory.Exists(root.Path))
			.ToList();
		int externalCount = roots.Count(root => root.Index > 0);
		RailCurrentLocationTitleLabel.Text = LocalizationManager.Text("views.rail.tools.assets.assets_browser_view.model_libraries");
		RailPathLabel.Text = externalCount == 0
			? LocalizationManager.Text("views.rail.tools.assets.assets_browser_view.model_libraries_internal_only")
			: LocalizationManager.Format(
				"views.rail.tools.assets.assets_browser_view.model_libraries_external_count",
				externalCount);
		string tooltip = string.Join(
			Environment.NewLine,
			roots.Select(root => $"{root.Label}: {root.Path}"));
		ToolTipProperties.SetText(RailCurrentLocationTitleLabel, tooltip);
		ToolTipProperties.SetText(RailPathLabel, tooltip);
	}

	private void OnBookmarksPickerSelectedIndexChanged(object? sender, EventArgs e)
	{
		if (_isInternalPickerUpdate) return;

		int index = RailBookmarksPicker.SelectedIndex;
		if (index >= 0 && index < _activeBookmarkPaths.Count)
		{
			var selectedPath = _activeBookmarkPaths[index];

			// Update remove button visibility (only for valid paths)
			RailRemoveBookmarkLabel.IsVisible = !string.IsNullOrEmpty(selectedPath);

			if (!string.IsNullOrEmpty(selectedPath))
			{
				Dispatcher.Dispatch(() =>
				{
					SetRootPath(selectedPath);
				});
			}
		}
	}

	private async void OnRemoveBookmarkClicked(object? sender, EventArgs e)
	{
		int index = RailBookmarksPicker.SelectedIndex;
		if (index >= 0 && index < _activeBookmarkPaths.Count)
		{
			var pathToRemove = _activeBookmarkPaths[index];
			if (!string.IsNullOrEmpty(pathToRemove) && _bookmarkedPaths.Contains(pathToRemove))
			{
				_bookmarkedPaths.Remove(pathToRemove);
				_bookmarksDirty = true;
				RenderBookmarks();
				await PersistBookmarksAsync();
			}
		}
	}

	private void OnRemoveBookmarkPointerEntered(object? sender, EventArgs e)
	{
		RailRemoveBookmarkLabel.Opacity = 1.0;
		RailRemoveBookmarkLabel.TextColor = Colors.White;
	}

	private void OnRemoveBookmarkPointerExited(object? sender, EventArgs e)
	{
		RailRemoveBookmarkLabel.Opacity = 0.8;
		RailRemoveBookmarkLabel.TextColor = Color.FromArgb("#FF856E");
	}

	/// <summary>
	/// Requests a full tree refresh for the active root.
	/// </summary>
	public void RefreshTree()
	{
		ReloadTree(resetExpansion: false, invalidateLayout: false, clearDataCache: true);
	}

	/// <summary>
	/// Handles keyboard file operations for the active asset profile.
	/// </summary>
	/// <param name="key">Normalized key pressed by the user.</param>
	/// <param name="ctrl">Whether Ctrl is pressed.</param>
	/// <param name="shift">Whether Shift is pressed.</param>
	/// <returns>True when the asset browser consumed the shortcut.</returns>
	public bool CanHandleKeyboardShortcut(NexusKey key, bool ctrl, bool shift)
	{
		return CanHandleAssetContextShortcut(key, ctrl, shift);
	}

	public bool TryHandleKeyboardShortcut(NexusKey key, bool ctrl, bool shift)
	{
		return TryHandleAssetContextShortcut(key, ctrl, shift);
	}

	private async Task BuildTreeAsync(bool clearDataCache = false)
	{
		if (!await _treeLock.WaitAsync(0)) return; // Already running

		try
		{
			NexusLog.Trace($"[ASSET_TREE] Build started: root='{_rootPath}', clearCache={clearDataCache}");
			_isTreeLoading = true;

			if (clearDataCache)
			{
				_childrenCache.Clear();
				_modelApiTreeCacheReady = false;
				ClearTreeEntryCache(_rootPath);
				ReturnAllAssetTreeRows();
			}

			await _loadingOverlay.ShowAsync();

			System.Threading.Interlocked.Increment(ref _treeRenderVersion);
			_rootNodes.Clear();
			_rowMap.Clear();

			if (string.IsNullOrWhiteSpace(_rootPath) || !Directory.Exists(_rootPath))
			{
				ReturnAllAssetTreeRows();
				ShowTreeEmptyState(LocalizationManager.Text("views.rail.tools.assets.assets_browser_view.root_path_unavailable"));
				return;
			}

			try
			{
				if (CurrentTreeSource == AssetTreeSource.ModelApi && !_modelApiTreeCacheReady)
				{
					await BuildModelApiTreeCacheAsync();
				}

				var nodes = CreateNodesForDirectory(_rootPath, 0, null);
				_rootNodes.AddRange(nodes);

				await RenderTreeAndWaitAsync();
			}
			catch (Exception ex)
			{
				ReturnAllAssetTreeRows();
				ShowTreeEmptyState(LocalizationManager.Format("views.rail.tools.assets.assets_browser_view.rail_error", ex.Message));
			}
		}
		finally
		{
			SaveCurrentTreeEntryCache();

			await _loadingOverlay.HideAsync();
			_isTreeLoading = false;
			_treeLock.Release();
			NexusLog.Trace($"[ASSET_TREE] Build completed: root='{_rootPath}', nodes={_rootNodes.Count}");

			string? pendingRoot = _pendingRootPath;
			_pendingRootPath = null;
			if (!string.IsNullOrWhiteSpace(pendingRoot) &&
				!string.Equals(pendingRoot, _rootPath, StringComparison.OrdinalIgnoreCase))
			{
				ApplyRootPath(pendingRoot);
			}
		}
	}

	private async Task BuildModelApiTreeCacheAsync()
	{
		string modelsRoot = GetModelsRootPath();
		if (string.IsNullOrWhiteSpace(modelsRoot))
		{
			return;
		}

		_modelApiTreeCacheReady = false;
		foreach (string key in _childrenCache.Keys
			.Where(key => string.Equals(key, modelsRoot, StringComparison.OrdinalIgnoreCase)
				|| key.StartsWith(modelsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
				|| key.StartsWith(modelsRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			.ToList())
		{
			_childrenCache.Remove(key);
		}

		var entriesByDirectory = new Dictionary<string, List<RailTreeEntry>>(StringComparer.OrdinalIgnoreCase)
		{
			[modelsRoot] = []
		};
		var modelCountsByDirectory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		AddModelFilesystemDirectories(entriesByDirectory, modelsRoot);

		using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
		httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Comfy-User", string.Empty);

		foreach (string category in ModelApiCategories)
		{
			try
			{
				using var response = await httpClient.GetAsync(ComfyApiOptions.ModelCategoryUrl(category));
				if (!response.IsSuccessStatusCode)
				{
					continue;
				}

				using var stream = await response.Content.ReadAsStreamAsync();
				using var doc = await JsonDocument.ParseAsync(stream);
				if (doc.RootElement.ValueKind != JsonValueKind.Array)
				{
					continue;
				}

				int itemsSinceYield = 0;
				foreach (var item in doc.RootElement.EnumerateArray())
				{
					string modelName = GetModelApiItemName(item);
					if (string.IsNullOrWhiteSpace(modelName))
					{
						continue;
					}

					AddModelApiItem(entriesByDirectory, modelCountsByDirectory, modelsRoot, category, modelName);
					itemsSinceYield++;
					if (itemsSinceYield >= ModelApiTreeBuildBatchSize)
					{
						itemsSinceYield = 0;
						await Task.Yield();
					}
				}
			}
			catch
			{
			}

			await Task.Yield();
		}

		foreach (var pair in entriesByDirectory)
		{
			_childrenCache[pair.Key] = pair.Value
				.GroupBy(entry => entry.FullPath, StringComparer.OrdinalIgnoreCase)
				.Select(group =>
				{
					var first = group.First();
					return first.IsDirectory
						? first with { ModelFileCount = modelCountsByDirectory.GetValueOrDefault(first.FullPath, 0) }
						: first;
				})
				.OrderByDescending(entry => entry.IsDirectory)
				.ThenByDescending(entry => entry.IsDirectory && (entry.ModelFileCount ?? 0) > 0)
				.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}

		_modelApiTreeCacheReady = true;
	}

	private static string GetModelApiItemName(JsonElement item)
	{
		if (item.ValueKind == JsonValueKind.String)
		{
			return item.GetString() ?? string.Empty;
		}

		if (item.ValueKind == JsonValueKind.Object &&
			item.TryGetProperty("name", out var nameProperty) &&
			nameProperty.ValueKind == JsonValueKind.String)
		{
			return nameProperty.GetString() ?? string.Empty;
		}

		return string.Empty;
	}

	private static void AddModelApiItem(
		Dictionary<string, List<RailTreeEntry>> entriesByDirectory,
		Dictionary<string, int> modelCountsByDirectory,
		string modelsRoot,
		string category,
		string modelName)
	{
		string categoryPath = Path.Combine(modelsRoot, category);
		AddDirectoryEntry(entriesByDirectory, modelsRoot, category, categoryPath);

		string currentDirectory = categoryPath;
		string normalizedName = modelName.Replace('\\', '/');
		string[] parts = normalizedName.Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0)
		{
			return;
		}

		for (int index = 0; index < parts.Length; index++)
		{
			string part = parts[index];
			bool isFile = index == parts.Length - 1;
			string entryPath = Path.Combine(currentDirectory, part);

			if (isFile)
			{
				IncrementModelDirectoryCounts(modelCountsByDirectory, modelsRoot, categoryPath, currentDirectory);
				EnsureDirectoryList(entriesByDirectory, currentDirectory)
					.Add(new RailTreeEntry(part, Path.Combine(categoryPath, normalizedName.Replace('/', Path.DirectorySeparatorChar)), false));
				continue;
			}

			AddDirectoryEntry(entriesByDirectory, currentDirectory, part, entryPath);
			currentDirectory = entryPath;
		}
	}

	private static void AddModelFilesystemDirectories(
		Dictionary<string, List<RailTreeEntry>> entriesByDirectory,
		string modelsRoot)
	{
		if (!Directory.Exists(modelsRoot))
		{
			return;
		}

		var options = new EnumerationOptions
		{
			IgnoreInaccessible = true,
			RecurseSubdirectories = false,
			ReturnSpecialDirectories = false,
			AttributesToSkip = FileAttributes.ReparsePoint,
		};
		foreach (string directory in Directory.EnumerateDirectories(modelsRoot, "*", options))
		{
			AddDirectoryEntry(entriesByDirectory, modelsRoot, Path.GetFileName(directory), directory);
		}
	}

	private static void IncrementModelDirectoryCounts(
		Dictionary<string, int> modelCountsByDirectory,
		string modelsRoot,
		string categoryPath,
		string leafDirectory)
	{
		IncrementCount(modelCountsByDirectory, modelsRoot);
		IncrementCount(modelCountsByDirectory, categoryPath);

		string current = leafDirectory;
		while (!string.IsNullOrWhiteSpace(current) &&
			   !string.Equals(current, modelsRoot, StringComparison.OrdinalIgnoreCase) &&
			   !string.Equals(current, categoryPath, StringComparison.OrdinalIgnoreCase))
		{
			IncrementCount(modelCountsByDirectory, current);
			current = Path.GetDirectoryName(current) ?? string.Empty;
		}
	}

	private static void IncrementCount(Dictionary<string, int> counts, string key)
	{
		counts[key] = counts.GetValueOrDefault(key) + 1;
	}

	private static void AddDirectoryEntry(
		Dictionary<string, List<RailTreeEntry>> entriesByDirectory,
		string parentDirectory,
		string name,
		string fullPath)
	{
		EnsureDirectoryList(entriesByDirectory, parentDirectory)
			.Add(new RailTreeEntry(name, fullPath, true));
		EnsureDirectoryList(entriesByDirectory, fullPath);
	}

	private static List<RailTreeEntry> EnsureDirectoryList(
		Dictionary<string, List<RailTreeEntry>> entriesByDirectory,
		string directory)
	{
		if (!entriesByDirectory.TryGetValue(directory, out var entries))
		{
			entries = [];
			entriesByDirectory[directory] = entries;
		}

		return entries;
	}

	private List<RailTreeNode> CreateNodesForDirectory(string directoryPath, int depth, RailTreeNode? parent)
	{
		var nodes = new List<RailTreeNode>();
		foreach (var entry in GetCachedChildren(directoryPath))
		{
			nodes.Add(CreateNode(entry, depth, parent));
		}
		return nodes;
	}

	private RailTreeNode CreateNode(RailTreeEntry entry, int depth, RailTreeNode? parent)
	{
		var node = new RailTreeNode
		{
			Name = entry.Name,
			FullPath = entry.FullPath,
			IsDirectory = entry.IsDirectory,
			Depth = depth,
			IsExpanded = entry.IsDirectory && _expandedPaths.Contains(entry.FullPath),
			ChildrenLoaded = !entry.IsDirectory,
			Parent = parent,
			IconKey = GetIconForEntry(entry),
			ModelFileCount = entry.ModelFileCount,
		};

		if (node.IsDirectory && node.IsExpanded && _childrenCache.ContainsKey(node.FullPath))
		{
			node.ChildrenLoaded = true;
			node.Children.AddRange(CreateNodesForDirectory(node.FullPath, depth + 1, node));
		}

		return node;
	}

	private IReadOnlyList<RailTreeEntry> GetCachedChildren(string directoryPath)
	{
		if (_childrenCache.TryGetValue(directoryPath, out var cachedEntries))
		{
			return cachedEntries;
		}

		if (CurrentTreeSource == AssetTreeSource.ModelApi &&
			_modelApiTreeCacheReady &&
			IsPathWithinModelsRoot(directoryPath))
		{
			return [];
		}

		var entries = new List<RailTreeEntry>();
		bool filterModelFilesOnly = CurrentFiltersModelFilesOnly;

		try
		{
			var options = new EnumerationOptions
			{
				IgnoreInaccessible = true,
				RecurseSubdirectories = false,
				ReturnSpecialDirectories = false,
				AttributesToSkip = FileAttributes.ReparsePoint,
			};
			foreach (var directory in Directory.EnumerateDirectories(directoryPath, "*", options).OrderBy(child => child, StringComparer.OrdinalIgnoreCase))
			{
				entries.Add(new RailTreeEntry(Path.GetFileName(directory), directory, true));
			}

			foreach (var file in Directory.EnumerateFiles(directoryPath, "*", options).OrderBy(child => child, StringComparer.OrdinalIgnoreCase))
			{
				string fileName = Path.GetFileName(file);
				if (string.Equals(fileName, ".index.json", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				if (filterModelFilesOnly && !_assetHubService.IsModelFile(file))
				{
					continue;
				}

				entries.Add(new RailTreeEntry(fileName, file, false));
			}
		}
		catch
		{
		}

		_childrenCache[directoryPath] = entries;
		return entries;
	}

	private static bool IsPathWithinModelsRoot(string path)
	{
		string modelsRoot = GetModelsRootPath();
		if (string.IsNullOrWhiteSpace(modelsRoot) || string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		return string.Equals(path, modelsRoot, StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith(modelsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith(modelsRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}

	private void RenderTree()
	{
		_ = RenderTreeAndWaitAsync();
	}

	private Task RenderTreeAndWaitAsync()
	{
		int renderVersion = System.Threading.Interlocked.Increment(ref _treeRenderVersion);
		return RenderTreeAsync(renderVersion);
	}

	private async Task RenderTreeAsync(int renderVersion)
	{
		try
		{
			if (_rootNodes.Count == 0)
			{
				ShowTreeEmptyState(LocalizationManager.Text("views.rail.tools.assets.assets_browser_view.folder_empty"));
				return;
			}

			var visibleNodes = EnumerateVisibleNodes(_rootNodes).ToList();
			RailTreeEmptyLabel.IsVisible = false;
			RailTreeVirtualList.IsVisible = !IsSearchActive;
			RailTreeVirtualList.BeginBatchUpdate();
			try
			{
				_visibleTreeNodes.Clear();
				_rowMap.Clear();
				int nodesSinceYield = 0;
				foreach (var node in visibleNodes)
				{
					if (renderVersion != _treeRenderVersion)
					{
						return;
					}

					_visibleTreeNodes.Add(node);
					nodesSinceYield++;
					if (nodesSinceYield >= AssetTreeRenderBatchSize)
					{
						nodesSinceYield = 0;
						await Task.Yield();
					}
				}
			}
			finally
			{
				RailTreeVirtualList.EndBatchUpdate();
			}

			await Task.CompletedTask;
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[ASSETS] Failed to render asset tree");
			if (renderVersion == _treeRenderVersion)
			{
				ReturnAllAssetTreeRows();
				ShowTreeEmptyState(LocalizationManager.Format("views.rail.tools.assets.assets_browser_view.rail_error", ex.Message));
			}
		}
	}

	private async Task RefreshWorkflowBookmarksSectionAsync()
	{
		int renderVersion = Interlocked.Increment(ref _workflowBookmarksRenderVersion);
		if (!IsWorkflowsRootActive())
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				if (renderVersion == _workflowBookmarksRenderVersion)
				{
					RailWorkflowBookmarksSection.IsVisible = false;
					RailWorkflowBookmarksList.Clear();
					RailWorkflowBookmarksCountLabel.Text = string.Empty;
				}
			});
			return;
		}

		HashSet<string> bookmarks;
		try
		{
			bookmarks = await WorkflowBookmarkService.SyncAndLoadAsync(_fixedWorkflowsPath);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[ASSETS] Failed to load workflow bookmarks");
			bookmarks = [];
		}

		var items = bookmarks
			.Select(relativePath => new WorkflowBookmarkAssetItem(
				WorkflowTabController.NormalizeWorkflowRelativePath(relativePath),
				ResolveWorkflowBookmarkFullPath(relativePath)))
			.Where(item => File.Exists(item.FullPath))
			.Where(MatchesWorkflowBookmarkSearch)
			.OrderBy(item => WorkflowTabController.StripWorkflowPrefix(item.RelativePath), StringComparer.OrdinalIgnoreCase)
			.ToArray();

		MainThread.BeginInvokeOnMainThread(() =>
		{
			if (renderVersion != _workflowBookmarksRenderVersion)
			{
				return;
			}

			_workflowBookmarkPaths.Clear();
			foreach (string bookmark in bookmarks)
			{
				_workflowBookmarkPaths.Add(WorkflowTabController.NormalizeWorkflowRelativePath(bookmark));
			}
			RenderWorkflowBookmarksSection(items);
		});
	}

	private void RenderWorkflowBookmarksSection(IReadOnlyList<WorkflowBookmarkAssetItem> items)
	{
		RailWorkflowBookmarksList.Clear();
		RailWorkflowBookmarksSection.IsVisible = IsWorkflowsRootActive() && items.Count > 0;
		RailWorkflowBookmarksCountLabel.Text = items.Count.ToString();
		RailWorkflowBookmarksScrollView.HeightRequest = Math.Min(
			WorkflowBookmarkMaxScrollHeight,
			(items.Count * WorkflowBookmarkRowHeight) + (Math.Max(0, items.Count - 1) * WorkflowBookmarkRowSpacing));
		RailWorkflowBookmarksScrollTrack.HeightRequest = RailWorkflowBookmarksScrollView.HeightRequest;

		foreach (var item in items)
		{
			RailWorkflowBookmarksList.Add(CreateWorkflowBookmarkRow(item));
		}

		_ = RailWorkflowBookmarksScrollView.ScrollToAsync(0, 0, animated: false);
		UpdateWorkflowBookmarkScrollBar();
		RailWorkflowBookmarksScrollView.Dispatcher.Dispatch(UpdateWorkflowBookmarkScrollBar);
	}

	private void OnWorkflowBookmarksScrolled(object? sender, ScrolledEventArgs e)
		=> UpdateWorkflowBookmarkScrollBar();

	private void UpdateWorkflowBookmarkScrollBar()
	{
		double viewportHeight = RailWorkflowBookmarksScrollView.HeightRequest;
		double contentHeight = RailWorkflowBookmarksList.Height;
		if (contentHeight <= 0)
		{
			contentHeight =
				RailWorkflowBookmarksList.Count * WorkflowBookmarkRowHeight +
				Math.Max(0, RailWorkflowBookmarksList.Count - 1) * WorkflowBookmarkRowSpacing;
		}

		bool canScroll = contentHeight > viewportHeight + 1;
		RailWorkflowBookmarksScrollTrack.Opacity = canScroll ? 0.9 : 0;
		RailWorkflowBookmarksScrollTrack.InputTransparent = !canScroll;
		if (!canScroll)
		{
			RailWorkflowBookmarksScrollThumb.TranslationY = 0;
			return;
		}

		double trackHeight = viewportHeight;
		double thumbHeight = Math.Max(
			WorkflowBookmarkScrollThumbMinHeight,
			trackHeight * Math.Clamp(viewportHeight / contentHeight, 0.12, 1));
		double scrollRange = Math.Max(1, contentHeight - viewportHeight);
		double thumbRange = Math.Max(0, trackHeight - thumbHeight);
		double scrollRatio = Math.Clamp(RailWorkflowBookmarksScrollView.ScrollY / scrollRange, 0, 1);

		RailWorkflowBookmarksScrollThumb.HeightRequest = thumbHeight;
		RailWorkflowBookmarksScrollThumb.TranslationY = thumbRange * scrollRatio;
	}

	private bool MatchesWorkflowBookmarkSearch(WorkflowBookmarkAssetItem item)
	{
		string query = NormalizeSearchQuery(RailSearchEntry?.Text);
		if (string.IsNullOrWhiteSpace(query))
		{
			return true;
		}

		string displayPath = WorkflowTabController.StripWorkflowPrefix(item.RelativePath);
		string displayName = Path.GetFileNameWithoutExtension(displayPath);
		return displayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
			displayPath.Contains(query, StringComparison.OrdinalIgnoreCase);
	}

	private View CreateWorkflowBookmarkRow(WorkflowBookmarkAssetItem item)
	{
		string displayPath = WorkflowTabController.StripWorkflowPrefix(item.RelativePath);
		string displayName = Path.GetFileNameWithoutExtension(displayPath);
		string parentPath = Path.GetDirectoryName(displayPath)?.Replace('\\', '/') ?? string.Empty;

		var row = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = GridLength.Star },
				new ColumnDefinition { Width = GridLength.Auto },
			},
			ColumnSpacing = 7,
			HeightRequest = 26,
			Padding = new Thickness(6, 0),
			BackgroundColor = Color.FromArgb("#101a29"),
		};

		var icon = new Label
		{
			Text = "{}",
			TextColor = NexusColors.Accent,
			FontSize = 10,
			FontAttributes = FontAttributes.Bold,
			VerticalOptions = LayoutOptions.Center,
			HorizontalOptions = LayoutOptions.Center,
			InputTransparent = true,
		};

		var title = new Label
		{
			Text = string.IsNullOrWhiteSpace(displayName) ? displayPath : displayName,
			TextColor = Color.FromArgb("#d7e4f3"),
			FontSize = 11,
			LineBreakMode = LineBreakMode.TailTruncation,
			VerticalOptions = LayoutOptions.Center,
			InputTransparent = true,
		};

		if (!string.IsNullOrWhiteSpace(parentPath))
		{
			ToolTipProperties.SetText(title, displayPath);
		}

		var remove = new Label
		{
			Text = "x",
			TextColor = Color.FromArgb("#ff9cb0"),
			FontSize = 11,
			FontAttributes = FontAttributes.Bold,
			WidthRequest = 18,
			HeightRequest = 18,
			HorizontalTextAlignment = TextAlignment.Center,
			VerticalTextAlignment = TextAlignment.Center,
			VerticalOptions = LayoutOptions.Center,
			HorizontalOptions = LayoutOptions.End,
			Opacity = 0.82,
		};

		var openTap = new TapGestureRecognizer();
		openTap.Tapped += (s, e) => OpenWorkflowBookmark(item.FullPath);
		row.GestureRecognizers.Add(openTap);

		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (s, e) => row.BackgroundColor = RowHoverColor;
		pointer.PointerExited += (s, e) => row.BackgroundColor = Color.FromArgb("#101a29");
		row.GestureRecognizers.Add(pointer);

		var removeTap = new TapGestureRecognizer();
		removeTap.Tapped += async (s, e) => await RemoveWorkflowBookmarkAsync(item.RelativePath);
		remove.GestureRecognizers.Add(removeTap);
		var removePointer = new PointerGestureRecognizer();
		removePointer.PointerEntered += (s, e) => remove.TextColor = Colors.White;
		removePointer.PointerExited += (s, e) => remove.TextColor = Color.FromArgb("#ff9cb0");
		remove.GestureRecognizers.Add(removePointer);

		row.Add(icon, 0, 0);
		row.Add(title, 1, 0);
		row.Add(remove, 2, 0);
		return row;
	}

	private void OpenWorkflowBookmark(string fullPath)
	{
		if (!File.Exists(fullPath))
		{
			_ = RefreshWorkflowBookmarksSectionAsync();
			return;
		}

		FileOpenRequested?.Invoke(this, CreateOpenRequest(fullPath));
	}

	private async Task RemoveWorkflowBookmarkAsync(string relativePath)
	{
		if (string.IsNullOrWhiteSpace(_fixedWorkflowsPath))
		{
			return;
		}

		var bookmarks = await WorkflowBookmarkService.SyncAndLoadAsync(_fixedWorkflowsPath);
		bookmarks.Remove(WorkflowTabController.NormalizeWorkflowRelativePath(relativePath));
		await WorkflowBookmarkService.SaveAsync(_fixedWorkflowsPath, bookmarks);
		_workflowBookmarkPaths.Remove(WorkflowTabController.NormalizeWorkflowRelativePath(relativePath));
		await RefreshWorkflowBookmarksSectionAsync();
		WorkflowBookmarksChanged?.Invoke(this, EventArgs.Empty);
	}

	private string ResolveWorkflowBookmarkFullPath(string relativePath)
	{
		string stripped = WorkflowTabController.StripWorkflowPrefix(relativePath);
		string[] segments = stripped.Split('/', StringSplitOptions.RemoveEmptyEntries);
		return Path.Combine([_fixedWorkflowsPath, .. segments]);
	}

	private bool IsWorkflowsRootActive()
		=> !string.IsNullOrWhiteSpace(_rootPath)
			&& !string.IsNullOrWhiteSpace(_fixedWorkflowsPath)
			&& string.Equals(_rootPath, _fixedWorkflowsPath, StringComparison.OrdinalIgnoreCase);

	private readonly record struct WorkflowBookmarkAssetItem(string RelativePath, string FullPath);

	private void ShowTreeEmptyState(string message)
	{
		_visibleTreeNodes.Clear();
		_rowMap.Clear();
		RailTreeVirtualList.IsVisible = false;
		RailTreeEmptyLabel.Text = message;
		RailTreeEmptyLabel.IsVisible = !IsSearchActive;
	}

	private bool IsSelected(RailTreeNode node)
		=> _selection.IsSelected(node.FullPath);

	private static bool IsEmptyModelDirectory(RailTreeNode node)
		=> node.IsDirectory && node.ModelFileCount == 0;

	private AssetTreeSource CurrentTreeSource
		=> _currentProfile?.TreeSource ?? AssetTreeSource.FileSystem;

	private bool CurrentAllowsDropImport
		=> _currentProfile?.AllowDropImport ?? true;

	private bool CurrentAllowsInternalMove
		=> _currentProfile?.AllowInternalMove ?? true;

	private bool CurrentAllowsBookmarkDrop
		=> _currentProfile?.AllowBookmarkDrop ?? true;

	private bool CurrentFiltersModelFilesOnly
		=> _currentProfile?.FilterModelFilesOnly ?? false;

	private bool CurrentUsesRecursiveSearch
		=> _currentProfile?.SearchRecursive ?? true;

	private bool CurrentSearchIncludesDirectories
		=> _currentProfile?.SearchIncludesDirectories ?? true;

	private DirectoryWatcherOptions CurrentWatcherOptions
		=> _currentProfile?.WatcherOptions ?? DirectoryWatcherOptions.Default;

	private void ConfigureAssetWatcherRoot()
	{
		_treeWatcher.ConfigureRoot(
			CurrentTreeSource == AssetTreeSource.FileSystem ? _rootPath : null,
			CurrentTreeSource == AssetTreeSource.FileSystem ? CurrentWatcherOptions : null);
	}

	private void SelectNode(RailTreeNode node)
		=> SelectPath(node.FullPath);

	private void SelectNodeFromInput(RailTreeNode node)
	{
		bool ctrl = PlatformManager.Current.Keyboard.IsCtrlPressed();
		bool shift = PlatformManager.Current.Keyboard.IsShiftPressed();

		if (shift)
		{
			SelectNodeRange(node);
			return;
		}

		if (ctrl)
		{
			ToggleNodeSelection(node);
			return;
		}

		SelectNode(node);
	}

	private void ToggleNodeSelection(RailTreeNode node)
		=> TogglePathSelection(node.FullPath);

	private void SelectNodeRange(RailTreeNode node)
	{
		var visiblePaths = EnumerateVisibleNodes(_rootNodes)
			.Select(n => n.FullPath)
			.ToList();
		SelectPathRange(node.FullPath, visiblePaths);
	}

	private string? GetPrimarySelectedPath()
		=> _selection.GetPrimarySelectedPath();

	private List<string> GetSelectedExistingPaths()
		=> _selection.GetExistingPaths();

	private void RefreshVisibleSelectionState()
	{
		foreach (var root in _rootNodes)
		{
			foreach (var node in EnumerateNodes([root]))
			{
				if (_rowMap.TryGetValue(node.FullPath, out var row))
				{
					UpdateNodeRow(row, node);
				}
			}
		}

		if (IsSearchActive)
		{
			RefreshSearchResultsSelectionState();
		}
	}

	private void RequestImmediateRefresh()
	{
		_treeWatcher.NotifyMutation([_rootPath]);
	}

	private void ReloadTree(bool resetExpansion, bool invalidateLayout, bool clearDataCache = true)
		=> _ = ReloadTreeAsync(resetExpansion, invalidateLayout, clearDataCache);

	private async Task ReloadTreeAsync(bool resetExpansion, bool invalidateLayout, bool clearDataCache)
	{
		try
		{
			if (resetExpansion)
			{
				_expandedPaths.Clear();
			}

			NormalizeSelectionState();
			await BuildTreeAsync(clearDataCache);
			_treeWatcher.MarkReconciled();
			if (_isRailActive && _isReady && CurrentTreeSource == AssetTreeSource.FileSystem)
			{
				_treeWatcher.SetActive(true);
			}

			_ = invalidateLayout;
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[ASSET_TREE] Reload failed");
		}
	}

	private void NormalizeSelectionState()
	{
		_selection.Normalize();
	}

	private void SaveCurrentTreeEntryCache()
	{
		if (string.IsNullOrWhiteSpace(_rootPath))
		{
			return;
		}

		_childrenCachesByRoot[_rootPath] = new Dictionary<string, IReadOnlyList<RailTreeEntry>>(
			_childrenCache,
			StringComparer.OrdinalIgnoreCase);
		_modelApiCacheReadyByRoot[_rootPath] = _modelApiTreeCacheReady;
	}

	private bool RestoreTreeEntryCache(string rootPath)
	{
		_childrenCache.Clear();
		_modelApiTreeCacheReady = false;

		if (string.IsNullOrWhiteSpace(rootPath) ||
			!_childrenCachesByRoot.TryGetValue(rootPath, out var cachedChildren))
		{
			return false;
		}

		foreach (var pair in cachedChildren)
		{
			_childrenCache[pair.Key] = pair.Value;
		}

		_modelApiTreeCacheReady = _modelApiCacheReadyByRoot.GetValueOrDefault(rootPath);
		return true;
	}

	private void ClearTreeEntryCache(string rootPath)
	{
		if (string.IsNullOrWhiteSpace(rootPath))
		{
			return;
		}

		_childrenCachesByRoot.Remove(rootPath);
		_modelApiCacheReadyByRoot.Remove(rootPath);
		_modelThumbnailPathCache.Clear();
	}

	private void ClearRowSelection(string path)
	{
		if (_rowMap.TryGetValue(path, out var row))
		{
			row.BackgroundColor = Colors.Transparent;
		}

		if (_searchRowMap.TryGetValue(path, out var searchRow))
		{
			searchRow.BackgroundColor = Colors.Transparent;
		}
	}

	private void ClearSelection()
	{
		foreach (string selectedPath in _selection.Paths.ToArray())
		{
			ClearRowSelection(selectedPath);
		}

		_selection.Clear();
	}

	private void ApplyRowSelection(string path)
	{
		if (_rowMap.TryGetValue(path, out var row))
		{
			row.BackgroundColor = RowSelectedColor;
		}

		if (_searchRowMap.TryGetValue(path, out var searchRow))
		{
			searchRow.BackgroundColor = RowSelectedColor;
		}
	}

	private void SelectPath(string path)
		=> _selection.SelectSingle(path, ClearRowSelection, ApplyRowSelection);

	private async void OnAssetListSurfaceTapped(object? sender, EventArgs e)
	{
		await Task.Yield();
		if (_suppressNextAssetListSurfaceSelectionClear)
		{
			_suppressNextAssetListSurfaceSelectionClear = false;
			return;
		}

		ClearSelection();
	}

	private void TogglePathSelection(string path)
		=> _selection.Toggle(path, ClearRowSelection, ApplyRowSelection);

	private void SelectPathRange(string path, IReadOnlyList<string> visiblePaths)
		=> _selection.SelectRange(path, visiblePaths, ClearRowSelection, ApplyRowSelection);

	private static IEnumerable<RailTreeNode> EnumerateVisibleNodes(IEnumerable<RailTreeNode> nodes)
	{
		var pending = new Stack<RailTreeNode>(nodes.Reverse());
		while (pending.Count > 0)
		{
			RailTreeNode node = pending.Pop();
			yield return node;

			if (!node.IsDirectory || !node.IsExpanded)
			{
				continue;
			}

			for (int index = node.Children.Count - 1; index >= 0; index--)
			{
				pending.Push(node.Children[index]);
			}
		}
	}

	private void HighlightBranch(RailTreeNode? node, bool highlight)
	{
		if (node == null) return;

		var branchNodes = EnumerateVisibleNodes(new List<RailTreeNode> { node });
		foreach (var n in branchNodes)
		{
			if (_rowMap.TryGetValue(n.FullPath, out var row))
			{
				row.BackgroundColor = highlight
					? RowHoverColor
					: (_selection.Contains(n.FullPath) ? RowSelectedColor : Colors.Transparent);
			}
		}
	}

	private void ReturnAllAssetTreeRows()
	{
		HideModelThumbnailPreview();
		_visibleTreeNodes.Clear();
		_rowMap.Clear();
		RailTreeEmptyLabel.IsVisible = false;
		RailTreeVirtualList.IsVisible = !IsSearchActive;
	}

	private void WireAssetTreeRow(Grid row, Label chevron)
	{
		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (s, e) =>
		{
			if (TryGetBoundNode(s, out var targetNode) &&
				_rowMap.TryGetValue(targetNode.FullPath, out var targetRow))
			{
				targetRow.BackgroundColor = _selection.Contains(targetNode.FullPath)
					? RowSelectedColor
					: RowHoverColor;
				BeginModelThumbnailHover(targetNode.FullPath, targetNode.Name, targetNode.IsDirectory, targetRow);
			}
		};
		pointer.PointerExited += (s, e) =>
		{
			if (TryGetBoundNode(s, out var targetNode) &&
				_rowMap.TryGetValue(targetNode.FullPath, out var targetRow))
			{
				targetRow.BackgroundColor = _selection.Contains(targetNode.FullPath)
					? RowSelectedColor
					: Colors.Transparent;
				EndModelThumbnailHover(targetNode.FullPath, targetNode.IsDirectory);
			}
		};
		row.GestureRecognizers.Add(pointer);

		var dropGesture = new DropGestureRecognizer { AllowDrop = true };
		dropGesture.DragOver += (s, e) =>
		{
			if (!TryGetBoundNode(s, out var targetNode))
			{
				e.AcceptedOperation = Microsoft.Maui.Controls.DataPackageOperation.None;
				return;
			}

			if (IsAssetIntentOnlyDrag(e.Data))
			{
				_currentDragPaths = null;
				e.AcceptedOperation = Microsoft.Maui.Controls.DataPackageOperation.None;
				return;
			}

			var effectiveTarget = targetNode.IsDirectory ? targetNode : targetNode.Parent;
			_dropHandledByRow = true;
			if (!CanAcceptFileDrop(e.Data))
			{
				e.AcceptedOperation = Microsoft.Maui.Controls.DataPackageOperation.None;
				HighlightBranch(effectiveTarget, false);
				return;
			}

			if (effectiveTarget != null && _currentDragPaths != null)
			{
				if (!IsDropValid(_currentDragPaths, effectiveTarget.FullPath))
				{
					e.AcceptedOperation = Microsoft.Maui.Controls.DataPackageOperation.None;
					HighlightBranch(effectiveTarget, false);
					return;
				}
			}

			e.AcceptedOperation = GetAcceptedFileDropOperation(e.Data);
			HighlightBranch(effectiveTarget, true);
		};
		dropGesture.DragLeave += (s, e) =>
		{
			var effectiveTarget = TryGetBoundNode(s, out var targetNode)
				? targetNode.IsDirectory ? targetNode : targetNode.Parent
				: null;
			HighlightBranch(effectiveTarget, false);
			_dropHandledByRow = false;
		};
		dropGesture.Drop += async (s, e) =>
		{
			if (!TryGetBoundNode(s, out var targetNode))
			{
				_dropHandledByRow = false;
				return;
			}

			if (IsAssetIntentOnlyDrag(e.Data))
			{
				_dropHandledByRow = false;
				return;
			}
			if (!CanAcceptFileDrop(e.Data))
			{
				_dropHandledByRow = false;
				return;
			}

			_dropHandledByRow = true;
			var droppedPaths = await TryGetDroppedPathsWithActiveFallbackAsync(e);
			var effectiveTarget = targetNode.IsDirectory ? targetNode : targetNode.Parent;

			if (droppedPaths.Count > 0)
			{
				string destination = effectiveTarget?.FullPath ?? _rootPath;

				if (IsCurrentRootDrag(e.Data))
				{
					if (IsDuplicateDragRequested())
					{
						await DuplicatePathsAsync(droppedPaths, destination);
					}
					else
					{
						await MovePathsAsync(droppedPaths, destination);
					}
				}
				else
				{
					await ImportDroppedPathsAsync(droppedPaths, destination);
				}
			}

			HighlightBranch(effectiveTarget, false);
		};
		row.GestureRecognizers.Add(dropGesture);

		var selectTap = new TapGestureRecognizer { NumberOfTapsRequired = 1 };
		selectTap.Tapped += (s, e) =>
		{
			_suppressNextAssetListSurfaceSelectionClear = true;
			if (TryGetBoundNode(s, out var tappedNode))
			{
				SelectNodeFromInput(tappedNode);
			}
		};
		row.GestureRecognizers.Add(selectTap);

		PlatformManager.Current.Interactions.AttachDoubleTap(row, async () =>
		{
			if (!TryGetBoundNode(row, out var tappedNode))
			{
				return;
			}

			SelectNode(tappedNode);
			if (tappedNode.IsDirectory)
			{
				if (IsEmptyModelDirectory(tappedNode))
				{
					return;
				}

				await ToggleNodeExpansionAsync(tappedNode);
				return;
			}

			var request = CreateOpenRequest(tappedNode.FullPath);
			FileOpenRequested?.Invoke(this, request);
		});

		var chevronTap = new TapGestureRecognizer();
		chevronTap.Tapped += async (s, e) =>
		{
			if (!TryGetBoundNode(s, out var tappedNode) || !tappedNode.IsDirectory)
			{
				return;
			}

			SelectNode(tappedNode);
			if (IsEmptyModelDirectory(tappedNode))
			{
				return;
			}

			await ToggleNodeExpansionAsync(tappedNode);
		};
		chevron.GestureRecognizers.Add(chevronTap);

		var pseudoDrag = new PanGestureRecognizer();
		AssetOpenRequest? pendingPseudoRequest = null;
		pseudoDrag.PanUpdated += (s, e) =>
		{
			if (!TryGetBoundNode(s, out var draggedNode))
			{
				return;
			}

			var dragRequest = CreateDragRequest(draggedNode.FullPath);
			if (!ShouldUsePseudoIntentDrag(dragRequest))
			{
				return;
			}

			switch (e.StatusType)
			{
				case GestureStatus.Started:
					if (!ShouldAllowAssetDrag(dragRequest, draggedNode.IsDirectory))
					{
						pendingPseudoRequest = null;
						return;
					}

					if (!_selection.Contains(draggedNode.FullPath))
					{
						SelectNode(draggedNode);
					}

					pendingPseudoRequest = dragRequest;
					BeginPseudoIntentDrag(pendingPseudoRequest);
					break;

				case GestureStatus.Running:
					break;

				case GestureStatus.Completed:
					pendingPseudoRequest = null;
					break;

				case GestureStatus.Canceled:
					CancelPseudoIntentDrag();
					pendingPseudoRequest = null;
					break;
			}
		};
		row.GestureRecognizers.Add(pseudoDrag);

		var drag = new DragGestureRecognizer();
		drag.DragStarting += async (s, e) =>
		{
			if (!TryGetBoundNode(s, out var draggedNode))
			{
				return;
			}

			var dragRequest = CreateDragRequest(draggedNode.FullPath);
			if (ShouldUsePseudoIntentDrag(dragRequest))
			{
				e.Cancel = true;
				return;
			}

			if (!_selection.Contains(draggedNode.FullPath))
			{
				SelectNode(draggedNode);
			}

			var selectedPaths = GetSelectedExistingPaths();
			if (selectedPaths.Count == 0) return;

			e.Data.Properties["name"] = draggedNode.Name;
			e.Data.Properties["kind"] = draggedNode.IsDirectory ? "directory" : "file";
			e.Data.Properties["root"] = _rootPath;
			if (!ShouldAllowSelectedAssetDrag(dragRequest, selectedPaths))
			{
				e.Cancel = true;
				return;
			}

			_activeDragRequest = dragRequest;
			SetActiveNativeDrag(selectedPaths);
			if (ShouldPublishPathDragProperties(dragRequest))
			{
				e.Data.Properties["path"] = draggedNode.FullPath;
				e.Data.Properties["paths"] = selectedPaths;
			}
			else
			{
				e.Data.Properties["assetPath"] = draggedNode.FullPath;
			}
			e.Data.Properties["assetMode"] = dragRequest.Mode.ToString();
			e.Data.Properties["assetAction"] = dragRequest.Action.ToString();
			e.Data.Properties["assetKind"] = dragRequest.Kind.ToString();
			e.Data.Properties["sourceRoot"] = dragRequest.SourceRoot;
			if (!string.IsNullOrWhiteSpace(dragRequest.DragId))
			{
				e.Data.Properties["dragId"] = dragRequest.DragId;
			}
			if (!string.IsNullOrWhiteSpace(dragRequest.ModelDirectory))
			{
				e.Data.Properties["modelDirectory"] = dragRequest.ModelDirectory;
			}
			if (!string.IsNullOrWhiteSpace(dragRequest.NodeType))
			{
				e.Data.Properties["nodeType"] = dragRequest.NodeType;
			}
			if (dragRequest.Mode is AssetInteractionMode.Model or AssetInteractionMode.Node or AssetInteractionMode.Image)
			{
				AssetInteractionRequested?.Invoke(this, dragRequest);
			}

			if (ShouldPublishNativeFileDragPayload(dragRequest))
			{
				await PlatformManager.Current.DragDrop.SetDragStartingPathsAsync(e, selectedPaths);
			}
			else
			{
				await PlatformManager.Current.DragDrop.SetDragStartingTextAsync(e, CreateAssetDragIntentText(dragRequest));
			}
		};
		drag.DropCompleted += (s, e) =>
		{
			if (_activeDragRequest is { } completedRequest)
			{
				AssetInteractionRequested?.Invoke(this, completedRequest with { Action = AssetInteractionAction.Drop });
			}

			_activeDragRequest = null;
			ClearActiveNativeDrag();
		};
		row.GestureRecognizers.Add(drag);

	}

	private void UpdateNodeRow(Grid row, RailTreeNode node)
	{
		row.BindingContext = node;
		row.Padding = new Thickness(10 + (node.Depth * 14), 0, 10, 0);
		row.BackgroundColor = IsSelected(node) ? RowSelectedColor : Colors.Transparent;
		row.Opacity = _clipboard.ShouldDim(node.FullPath) ? 0.46 : 1;
		AttachContextMenu(row, node);

		if (row.Children.Count < 3)
		{
			return;
		}

		bool isEmptyModelDirectory = node.IsDirectory && node.ModelFileCount == 0;
		row.Opacity = isEmptyModelDirectory ? 0.52 : (_clipboard.ShouldDim(node.FullPath) ? 0.46 : 1);

		if (row.Children[0] is Label chevron)
		{
			chevron.Text = node.IsDirectory && !isEmptyModelDirectory
				? (node.IsExpanded ? "v" : ">")
				: " ";
			chevron.Opacity = isEmptyModelDirectory ? 0.35 : 1;
		}

		if (row.Children[1] is Grid iconHost)
		{
			UpdateNodeIcon(iconHost, node);
		}

		if (row.Children[2] is Label label)
		{
			label.Text = node.Name;
			label.TextColor = isEmptyModelDirectory
				? Color.FromArgb("#6b7885")
				: node.IsDirectory ? Color.FromArgb("#e1e8f0") : Color.FromArgb("#c3cfdb");
			ApplyAssetRowTooltip(label, node.Name, node.FullPath, node.IsDirectory);
		}

		if (row.Children.Count > 3 && row.Children[3] is Label countLabel)
		{
			countLabel.IsVisible = node.IsDirectory && node.ModelFileCount.HasValue;
			countLabel.Text = node.ModelFileCount?.ToString() ?? string.Empty;
			countLabel.TextColor = isEmptyModelDirectory
				? Color.FromArgb("#4d5b68")
				: Color.FromArgb("#6f879f");
		}
	}

	private bool TryGetNode(string fullPath, out RailTreeNode node)
	{
		foreach (var root in _rootNodes)
		{
			foreach (var candidate in EnumerateNodes([root]))
			{
				if (string.Equals(candidate.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
				{
					node = candidate;
					return true;
				}
			}
		}

		node = null!;
		return false;
	}

	private void BeginModelThumbnailHover(string fullPath, string displayName, bool isDirectory, VisualElement anchor)
	{
		if (!ShouldConsiderThumbnailPreview(fullPath, isDirectory))
		{
			HideModelThumbnailPreview();
			return;
		}

		int version = ++_modelThumbnailHoverVersion;
		_latestOperations.RequestLatest("model-thumbnail-hover", async lease =>
		{
			if (!await lease.WaitForAsync(TimeSpan.FromMilliseconds(ModelThumbnailHoverDelayMs)))
			{
				return;
			}

			ModelAssetThumbnail? thumbnail = ResolveThumbnailPreview(fullPath);
			if (thumbnail is null || !lease.IsCurrent)
			{
				return;
			}

			UiThread.TryBeginInvoke(() =>
			{
				if (version == _modelThumbnailHoverVersion && lease.IsCurrent)
				{
					ShowModelThumbnailPreview(anchor, thumbnail);
				}
			}, "ASSET_MODEL_THUMBNAIL");
		});
	}

	private void EndModelThumbnailHover(string fullPath, bool isDirectory)
	{
		if (!ShouldConsiderThumbnailPreview(fullPath, isDirectory))
		{
			return;
		}

		HideModelThumbnailPreview();
	}

	private ModelAssetThumbnail? ResolveThumbnailPreview(string path)
	{
		if (_modelThumbnailPathCache.TryGetValue(path, out ModelAssetThumbnail? cached))
		{
			if (cached == null || File.Exists(cached.Path))
			{
				return cached;
			}

			_modelThumbnailPathCache.Remove(path);
		}

		ModelAssetThumbnail? thumbnail = ResolveDirectImageThumbnail(path);
		if (thumbnail is null && ShouldConsiderModelThumbnail(path, isDirectory: false))
		{
			var actualModelPath = ResolveModelAssetPathMatches(path).FirstOrDefault()?.FullPath;
			if (!string.IsNullOrWhiteSpace(actualModelPath))
			{
				thumbnail = ModelAssetThumbnailResolver.ResolveThumbnail(actualModelPath);
			}
		}

		_modelThumbnailPathCache[path] = thumbnail;
		return thumbnail;
	}

	private static ModelAssetThumbnail? ResolveDirectImageThumbnail(string path)
		=> File.Exists(path)
			? ModelAssetThumbnailResolver.ResolveImageFileThumbnail(path)
			: null;

	private void ShowModelThumbnailPreview(VisualElement anchor, ModelAssetThumbnail thumbnail)
	{
		if (!File.Exists(thumbnail.Path))
		{
			return;
		}

		var size = MeasureModelThumbnailPreview(thumbnail);
		ModelThumbnailPreviewRequested?.Invoke(this, new ModelAssetThumbnailPreviewRequest(thumbnail.Path, size.Width, size.Height));
	}

	private void HideModelThumbnailPreview(bool clearCache = false)
	{
		_latestOperations.Invalidate("model-thumbnail-hover");
		_modelThumbnailHoverVersion++;

		ModelThumbnailPreviewDismissed?.Invoke(this, EventArgs.Empty);

		if (clearCache)
		{
			_modelThumbnailPathCache.Clear();
		}
	}

	private bool ShouldConsiderModelThumbnail(string fullPath, bool isDirectory)
		=> !isDirectory &&
		   CurrentTreeSource == AssetTreeSource.ModelApi &&
		   _assetHubService.IsModelFile(fullPath);

	private static bool ShouldConsiderImageFileThumbnail(string fullPath, bool isDirectory)
		=> !isDirectory &&
		   File.Exists(fullPath) &&
		   ModelAssetThumbnailResolver.IsSupportedImageFile(fullPath);

	private bool ShouldConsiderThumbnailPreview(string fullPath, bool isDirectory)
		=> ShouldConsiderModelThumbnail(fullPath, isDirectory) ||
		   ShouldConsiderImageFileThumbnail(fullPath, isDirectory);

	private string? GetAssetRowTooltip(string name, string fullPath, bool isDirectory)
		=> $"{name}\n{fullPath}";

	private void ApplyAssetRowTooltip(BindableObject target, string name, string fullPath, bool isDirectory)
	{
		string? tooltip = GetAssetRowTooltip(name, fullPath, isDirectory);
		if (string.IsNullOrWhiteSpace(tooltip))
		{
			target.ClearValue(ToolTipProperties.TextProperty);
			return;
		}

		ToolTipProperties.SetText(target, tooltip);
	}

	private static Size MeasureModelThumbnailPreview(ModelAssetThumbnail thumbnail)
	{
		double width = Math.Max(1, thumbnail.Width);
		double height = Math.Max(1, thumbnail.Height);
		double scale = Math.Min(ModelThumbnailPreviewMaxWidth / width, ModelThumbnailPreviewMaxHeight / height);
		scale = Math.Min(1, scale);

		double scaledWidth = Math.Max(28, Math.Round(width * scale));
		double scaledHeight = Math.Max(28, Math.Round(height * scale));
		return new Size(scaledWidth, scaledHeight);
	}

	private static bool TryGetBoundNode(object? source, out RailTreeNode node)
	{
		if (source is BindableObject bindable &&
			bindable.BindingContext is RailTreeNode boundNode)
		{
			node = boundNode;
			return true;
		}

		node = null!;
		return false;
	}

	private static IEnumerable<RailTreeNode> EnumerateNodes(IEnumerable<RailTreeNode> nodes)
	{
		var pending = new Stack<RailTreeNode>(nodes.Reverse());
		while (pending.Count > 0)
		{
			RailTreeNode node = pending.Pop();
			yield return node;
			for (int index = node.Children.Count - 1; index >= 0; index--)
			{
				pending.Push(node.Children[index]);
			}
		}
	}

	private async Task ToggleNodeExpansionAsync(RailTreeNode node)
	{
		if (!node.IsDirectory || _isAnimating || IsEmptyModelDirectory(node))
		{
			return;
		}

		_isAnimating = true;

		if (_expandedPaths.Contains(node.FullPath))
		{
			var descendants = EnumerateVisibleNodes(node.Children).ToList();
			_expandedPaths.Remove(node.FullPath);
			node.IsExpanded = false;
			UpdateCachedChevron(node);
			await CollapseRowsAsync(descendants);
		}
		else
		{
			_expandedPaths.Add(node.FullPath);
			if (!node.ChildrenLoaded)
			{
				node.Children.Clear();
				node.Children.AddRange(CreateNodesForDirectory(node.FullPath, node.Depth + 1, node));
				node.ChildrenLoaded = true;
			}

			node.IsExpanded = true;
			UpdateCachedChevron(node);
			var descendants = EnumerateVisibleNodes(node.Children).ToList();
			await ExpandRowsAsync(node, descendants);
		}

		_isAnimating = false;
	}

	private View CreateEmptyState(string message)
	{
		return new Label
		{
			Text = message,
			TextColor = Color.FromArgb("#607688"),
			FontSize = 11,
			Margin = new Thickness(12, 12, 12, 0),
		};
	}

	private async void OnOpenRootClicked(object? sender, EventArgs e)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(_rootPath))
			{
				return;
			}

			if (_currentProfile?.TreeSource == AssetTreeSource.ModelApi)
			{
				ShowModelRootsFlyout();
				return;
			}

			await OpenInOsAsync(_rootPath);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "Failed to open root explorer");
		}
	}

	private void OnRefreshRootClicked(object? sender, EventArgs e)
	{
		try
		{
			RefreshTree();
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "Failed to refresh asset root");
		}
	}

	private void ShowModelRootsFlyout()
	{
		_ = ShowModelRootsPickerAsync();
	}

	private async Task ShowModelRootsPickerAsync()
	{
		var roots = GetModelRootShortcuts()
			.Where(root => Directory.Exists(root.Path))
			.ToList();
		if (roots.Count == 0 || GetHostPage() is not { } hostPage)
		{
			return;
		}

		string[] options = roots
			.Select(root => $"{root.Label}  //  {root.Path}")
			.ToArray();
		string? selected = await hostPage.DisplayActionSheetAsync(
			LocalizationManager.Text("context_menu.open_model_root"),
			LocalizationManager.Text("common.cancel"),
			null,
			options);
		int selectedIndex = Array.IndexOf(options, selected);
		if (selectedIndex < 0 || selectedIndex >= roots.Count)
		{
			return;
		}

		await OpenInOsAsync(roots[selectedIndex].Path);
	}

	private IReadOnlyList<ModelRootShortcut> GetModelRootShortcuts()
	{
		var roots = new List<ModelRootShortcut>();
		string modelsRoot = GetModelsRootPath();
		if (!string.IsNullOrWhiteSpace(modelsRoot))
		{
			roots.Add(new ModelRootShortcut(0, "Internal models", modelsRoot));
		}

		int externalIndex = 1;
		foreach (string root in SetupSettingsService.Instance.Settings.ModelLibraryRoots)
		{
			if (string.IsNullOrWhiteSpace(root))
			{
				continue;
			}

			roots.Add(new ModelRootShortcut(externalIndex, $"External library {externalIndex}", root));
			externalIndex++;
		}

		return roots;
	}

	private void OnOpenRootPointerEntered(object? sender, PointerEventArgs e)
	{
		RailRootToolButton.BackgroundColor = Color.FromArgb("#18283a");
	}

	private void OnOpenRootPointerExited(object? sender, PointerEventArgs e)
	{
		RailRootToolButton.BackgroundColor = Color.FromArgb("#101a26");
	}

	private void OnRefreshRootPointerEntered(object? sender, PointerEventArgs e)
	{
		RailRefreshToolButton.BackgroundColor = Color.FromArgb("#18283a");
	}

	private void OnRefreshRootPointerExited(object? sender, PointerEventArgs e)
	{
		RailRefreshToolButton.BackgroundColor = Color.FromArgb("#101a26");
	}

	private void OnSearchPointerEntered(object? sender, PointerEventArgs e)
	{
		_searchVisuals?.SetHovered(true);
	}

	private void OnSearchPointerExited(object? sender, PointerEventArgs e)
	{
		_searchVisuals?.SetHovered(false);
	}

	private void OnSearchEntryFocused(object? sender, FocusEventArgs e)
	{
		_searchVisuals?.SetFocused(true);
		_searchTextController?.RefreshNativeSelectionColors();
	}

	private void OnSearchEntryUnfocused(object? sender, FocusEventArgs e)
	{
		_searchVisuals?.SetFocused(false);
	}

	private async Task OpenInOsAsync(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return;

		var result = await PlatformManager.Current.Shell.OpenPathAsync(path);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			NexusLog.Warning($"Failed to open in OS: {result.Message}");
		}
	}

	private Page? GetHostPage()
	{
		Element? current = this;
		while (current is not null)
		{
			if (current is Page page)
			{
				return page;
			}

			current = current.Parent;
		}

		return Application.Current?.Windows.FirstOrDefault()?.Page;
	}

	private void UpdateNodeIcon(Grid iconHost, RailTreeNode node)
	{
		iconHost.Children.Clear();
		iconHost.Add(CreateNodeIcon(node));
	}

	private View CreateNodeIcon(RailTreeNode node)
	{
		string iconSource = node.IconKey switch
		{
			"folder" => "assets_folder.png",
			"image" => "assets_image.png",
			"video" => "assets_video.png",
			"json" => "assets_json.png",
			"model" => "assets_model.png",
			"workflow" => "assets_workflow.png",
			_ => "assets_file.png",
		};

		return new Image
		{
			Source = iconSource,
			HorizontalOptions = LayoutOptions.Center,
			VerticalOptions = LayoutOptions.Center,
			WidthRequest = 16,
			HeightRequest = 16,
			Opacity = 0.9,
		};
	}
}
