using System;
using System.Collections.Generic;
using System.Linq;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Views.Rail.Tools;
using ComfyUI_Nexus.Views.Rail.Contracts;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using ComfyUI_Nexus;

namespace ComfyUI_Nexus.Views.Rail.Tools.NodeLibrary;

public enum NodeItemType
{
	Header,
	Category,
	Node
}

public class NodeItemViewModel : System.ComponentModel.INotifyPropertyChanged
{
	private const string NodeDefaultAccentHex = "#8de7ff";
	private static readonly Color NexusFamilyAccentColor = Color.FromArgb("#00d2ff");
	private static readonly Color BookmarkedAccentColor = Color.FromArgb("#ffd700");
	private static readonly Color BlueprintsAccentColor = Color.FromArgb("#da70d6");
	private static readonly Color PartnerAccentColor = Color.FromArgb("#7be495");
	private static readonly Color ComfyAccentColor = Color.FromArgb("#8dbce0");
	private static readonly Color ExtensionsAccentColor = Color.FromArgb("#ff8c5a");
	private static readonly Color DefaultHeaderAccentColor = Color.FromArgb("#5c7a99");
	private static readonly Color BookmarkInactiveColor = Color.FromArgb("#6d8399");

	public NodeItemType ItemType { get; init; }
	public string DisplayName { get; init; } = "";
	public int Depth { get; init; }

	// Section header support
	public SectionKind Section { get; init; } = SectionKind.Comfy;
	public NodeCategoryNode? SectionNode { get; init; }

	public Color HeaderAccentColor => Section switch
	{
		SectionKind.NexusFamily => NexusFamilyAccentColor,
		SectionKind.Bookmarked => BookmarkedAccentColor,
		SectionKind.Blueprints => BlueprintsAccentColor,
		SectionKind.Partner => PartnerAccentColor,
		SectionKind.Comfy => ComfyAccentColor,
		SectionKind.Extensions => ExtensionsAccentColor,
		_ => DefaultHeaderAccentColor
	};

	public string HeaderIcon => Section switch
	{
		SectionKind.NexusFamily => "node_library_hud.png",
		_ => ""
	};

	public bool HasHeaderIcon => !string.IsNullOrEmpty(HeaderIcon);

	public NodeLibraryEntry? NodeEntry { get; init; }
	public string ColorHex => NodeEntry?.ColorHex ?? NodeDefaultAccentHex;

	public string NodeIcon
	{
		get
		{
			if (NodeEntry == null) return "node_library_node.png";

			if (NodeEntry.PythonModule != null && NodeEntry.PythonModule.Contains("ComfyUI-HUD", StringComparison.OrdinalIgnoreCase)) return "node_library_hud.png";

			string cat = NodeEntry.Category?.ToLowerInvariant() ?? string.Empty;
			string type = NodeEntry.Type?.ToLowerInvariant() ?? string.Empty;
			string name = NodeEntry.DisplayName?.ToLowerInvariant() ?? string.Empty;

			if (cat.Contains("image") || type.Contains("image") || name.Contains("image")) return "node_library_image.png";
			if (cat.Contains("video") || type.Contains("video") || type.Contains("vhs")) return "node_library_video.png";
			if (cat.Contains("model") || cat.Contains("loader") || type.Contains("checkpoint") || name.Contains("loader") || name.Contains("load")) return "node_library_model.png";
			if (type.Contains("input") || type.Contains("primitive")) return "node_library_input.png";
			if (type.Contains("output") || type.Contains("save")) return "node_library_output.png";
			if (cat.Contains("json") || type.Contains("json")) return "node_library_json.png";
			if (cat.Contains("workflow") || cat.Contains("logic") || cat.Contains("math") || cat.Contains("routing")) return "node_library_workflow.png";

			return "node_library_node.png";
		}
	}

	public NodeCategoryNode? CategoryNode { get; init; }

	private bool _isExpanded;
	public bool IsExpanded
	{
		get => _isExpanded;
		set
		{
			if (_isExpanded != value)
			{
				_isExpanded = value;
				PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExpanded)));
				PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ChevronIcon)));
				PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ChevronRotation)));
			}
		}
	}

	private bool _isBookmarked;
	public bool IsBookmarked
	{
		get => _isBookmarked;
		set
		{
			if (_isBookmarked != value)
			{
				_isBookmarked = value;
				PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsBookmarked)));
				PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(BookmarkToggleText)));
				PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(BookmarkToggleIcon)));
				PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(BookmarkToggleColor)));
			}
		}
	}

	public string BookmarkToggleText => IsBookmarked ? "Remove from Bookmarks" : "Add to Bookmarks";
	public string BookmarkToggleIcon => IsBookmarked ? "★" : "☆";
	public Color BookmarkToggleColor => IsBookmarked
		? BookmarkedAccentColor
		: BookmarkInactiveColor;

	public bool IsInsideBookmarkSection { get; init; } = false;
	public bool ShowBookmarkToggle => !IsInsideBookmarkSection;
	public bool ShowBookmarkRemoveButton => IsInsideBookmarkSection && Depth == 1;

	public string ChevronIcon => IsExpanded ? "▾" : "▸";
	public double ChevronRotation => IsExpanded ? 90 : 0;

	public Thickness Padding => ItemType switch
	{
		NodeItemType.Header => new Thickness(12, 16, 12, 4),
		NodeItemType.Category => new Thickness(10 + Depth * 14, 0, 10, 0),
		NodeItemType.Node => new Thickness(10 + Depth * 14, 0, 10, 0),
		_ => Thickness.Zero
	};

	public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

internal class NodeItemTemplateSelector : DataTemplateSelector
{
	public DataTemplate? HeaderTemplate { get; set; }
	public DataTemplate? CategoryTemplate { get; set; }
	public DataTemplate? NodeTemplate { get; set; }

	protected override DataTemplate? OnSelectTemplate(object item, BindableObject container)
	{
		if (item is NodeItemViewModel vm)
		{
			return vm.ItemType switch
			{
				NodeItemType.Header => HeaderTemplate,
				NodeItemType.Category => CategoryTemplate,
				NodeItemType.Node => NodeTemplate,
				_ => null
			};
		}
		return null;
	}
}

public partial class NodeLibraryView : ContentView, IRailToolView
{
	private const int TreeMergeBatchSize = 48;
	private const int TreePoolPrewarmPerTemplate = 48;
	private const int TreePoolPrewarmBatchSize = 6;
	private const string NodeSearchActiveIndicatorColor = "#7be495";
	private const string NodeSearchInactiveIndicatorColor = "#3d5266";
	private static readonly Color NodeSearchToggleActiveHoverBackgroundColor = Color.FromArgb("#15261d");
	private static readonly Color NodeSearchToggleActiveBackgroundColor = Color.FromArgb("#111e17");
	private static readonly Color NodeSearchToggleActiveStrokeColor = Color.FromArgb("#1a3824");
	private static readonly Color NodeSearchToggleActiveTextColor = Color.FromArgb("#a2eebb");
	private static readonly Color NodeSearchToggleInactiveHoverBackgroundColor = Color.FromArgb("#111b29");
	private static readonly Color NodeSearchToggleInactiveBackgroundColor = Color.FromArgb("#090e14");
	private static readonly Color NodeSearchToggleInactiveStrokeColor = Color.FromArgb("#1e2c3d");
	private static readonly Color NodeSearchToggleInactiveTextColor = Color.FromArgb("#5c7a99");

	private readonly IDispatcherTimer _searchTimer;
	private bool _isDescriptionSearchEnabled;
	private bool _isToggleHovered;
	private RailSearchVisualController? _searchVisuals;
	private NexusEntryTextController? _searchTextController;
	private string _currentSearchText = "";
	private bool _hasInitializedTreeDefaults;
	private readonly RailLoadingOverlayController _loadingOverlay;
	private bool _isReady;
	private bool _isBusy;
	private int _refreshTreeVersion;
	private Task? _refreshTreeTask;

	internal System.Collections.ObjectModel.ObservableCollection<NodeItemViewModel> FlattenedNodes { get; } = new();

	public Command<NodeItemViewModel> ToggleCategoryCommand { get; }
	public Command<NodeItemViewModel> ToggleSectionCommand { get; }
	public Command<NodeItemViewModel> DoubleTapNodeCommand { get; }

	public Command<NodeItemViewModel> ToggleBookmarkCommand { get; }
	public Command<NodeItemViewModel> ExpandAllCommand { get; }
	public Command<NodeItemViewModel> CollapseAllCommand { get; }
	public Command<NodeItemViewModel> AddToStageCommand { get; }

	View IRailToolView.View => this;
	bool IRailToolView.IsReady => _isReady;
	bool IRailToolView.IsBusy => _isBusy;
	internal event EventHandler<AssetOpenRequest>? AssetInteractionRequested;

	public NodeLibraryView()
	{
		InitializeComponent();
		_loadingOverlay = new RailLoadingOverlayController(RailLoadingOverlay);
		_searchVisuals = new RailSearchVisualController(RailSearchBorder, RailSearchEntry);
		_searchTextController = new NexusEntryTextController(RailSearchEntry, RailSearchBorder);
		new RailSearchClearButtonController(RailSearchClearButton, RailSearchClearLabel);

		ToggleCategoryCommand = new Command<NodeItemViewModel>(OnToggleCategory);
		ToggleSectionCommand = new Command<NodeItemViewModel>(OnToggleSection);
		DoubleTapNodeCommand = new Command<NodeItemViewModel>(OnDoubleTapNode);

		ToggleBookmarkCommand = new Command<NodeItemViewModel>(OnToggleBookmark);
		ExpandAllCommand = new Command<NodeItemViewModel>(OnExpandAll);
		CollapseAllCommand = new Command<NodeItemViewModel>(OnCollapseAll);
		AddToStageCommand = new Command<NodeItemViewModel>(OnDoubleTapNode);

		LibraryVirtualList.ItemTemplateSelector = (DataTemplateSelector)Resources["NodeSelector"];
		LibraryVirtualList.ItemHeightSelector = GetNodeItemHeight;
		LibraryVirtualList.ItemsSource = FlattenedNodes;

		_searchTimer = Dispatcher.CreateTimer();
		_searchTimer.Interval = NodeLibraryOptions.SearchDebounceDelay;
		_searchTimer.Tick += (s, e) =>
		{
			_searchTimer.Stop();
			RefreshTree();
		};

	}
	private MainPage? GetMainPage()
	{
		Element? parent = this.Parent;
		while (parent != null)
		{
			if (parent is MainPage mp) return mp;
			parent = parent.Parent;
		}

		var rootPage = GetApplicationRootPage();
		if (rootPage is Shell shell)
		{
			return shell.CurrentPage as MainPage;
		}

		return rootPage as MainPage;
	}

	private static Page? GetApplicationRootPage()
	{
		var app = Application.Current;
		if (app == null)
		{
			return null;
		}

		return app.Windows.FirstOrDefault(static w => w?.Page != null)?.Page;
	}

	private void OnToggleBookmark(NodeItemViewModel? vm)
	{
		var root = GetMainPage()?.NodeLibrary;
		if (vm == null || root == null) return;

		if (vm.ItemType == NodeItemType.Node && vm.NodeEntry != null)
		{
			string type = vm.NodeEntry.Type;
			if (root.BookmarkedTypes.Contains(type))
			{
				root.BookmarkedTypes.Remove(type);
				var itemToRemove = root.Bookmarked.FirstOrDefault(x => x.Type == type);
				if (itemToRemove != null) root.Bookmarked.Remove(itemToRemove);
			}
			else
			{
				root.BookmarkedTypes.Add(type);
				root.Bookmarked.Add(vm.NodeEntry);
			}
		}
		else if (vm.ItemType == NodeItemType.Category && vm.CategoryNode != null)
		{
			string path = vm.CategoryNode.FullPath;
			if (root.BookmarkedCategoryPaths.Contains(path))
			{
				root.BookmarkedCategoryPaths.Remove(path);
				var itemToRemove = root.BookmarkedCategories.FirstOrDefault(x => x.FullPath == path);
				if (itemToRemove != null) root.BookmarkedCategories.Remove(itemToRemove);
			}
			else
			{
				root.BookmarkedCategoryPaths.Add(path);
				root.BookmarkedCategories.Add(vm.CategoryNode);
			}
		}
		else
		{
			return;
		}

		new NodeLibraryService().SaveBookmarks(root);

		// Refresh tree to re-render Bookmarked section and update icons
		RefreshTree();
	}

	private void OnExpandAll(NodeItemViewModel? vm)
	{
		var root = GetMainPage()?.NodeLibrary;
		if (root == null) return;

		root.NexusFamilyRoot.IsExpanded = true;
		root.BlueprintRoot.IsExpanded = true;
		root.PartnerRoot.IsExpanded = true;
		root.ComfyRoot.IsExpanded = true;
		root.ExtensionRoot.IsExpanded = true;
		root.IsBookmarkedExpanded = true;

		RefreshTree();
	}

	private void OnCollapseAll(NodeItemViewModel? vm)
	{
		var root = GetMainPage()?.NodeLibrary;
		if (root == null) return;

		root.NexusFamilyRoot.IsExpanded = false;
		root.BlueprintRoot.IsExpanded = false;
		root.PartnerRoot.IsExpanded = false;
		root.ComfyRoot.IsExpanded = false;
		root.ExtensionRoot.IsExpanded = false;
		root.IsBookmarkedExpanded = false;

		RefreshTree();
	}

	private void OnToggleCategory(NodeItemViewModel? vm)
	{
		if (vm == null || vm.ItemType != NodeItemType.Category || vm.CategoryNode == null)
		{
			return;
		}

		bool isSearching = !string.IsNullOrWhiteSpace(_currentSearchText);
		if (isSearching)
		{
			return;
		}

		vm.IsExpanded = !vm.IsExpanded;
		if (vm.IsInsideBookmarkSection)
		{
			vm.CategoryNode.IsBookmarkExpanded = vm.IsExpanded;
		}
		else
		{
			vm.CategoryNode.IsExpanded = vm.IsExpanded;
		}

		int startIndex = FlattenedNodes.IndexOf(vm) + 1;
		if (startIndex == 0) return;

		LibraryVirtualList.BeginBatchUpdate();
		try
		{
			if (vm.IsExpanded)
			{
				var descendants = GetVisibleDescendants(vm.CategoryNode, vm.Depth, vm.IsInsideBookmarkSection);
				for (int i = 0; i < descendants.Count; i++)
				{
					FlattenedNodes.Insert(startIndex + i, descendants[i]);
				}
			}
			else
			{
				while (startIndex < FlattenedNodes.Count && FlattenedNodes[startIndex].Depth > vm.Depth)
				{
					FlattenedNodes.RemoveAt(startIndex);
				}
			}
		}
		finally
		{
			LibraryVirtualList.EndBatchUpdate();
		}
	}

	private void OnToggleSection(NodeItemViewModel? vm)
	{
		if (vm == null || vm.ItemType != NodeItemType.Header)
		{
			return;
		}

		bool isSearching = !string.IsNullOrWhiteSpace(_currentSearchText);
		if (isSearching)
		{
			return;
		}

		vm.IsExpanded = !vm.IsExpanded;

		var root = GetMainPage()?.NodeLibrary;

		if (vm.Section == SectionKind.Bookmarked && root != null)
		{
			root.IsBookmarkedExpanded = vm.IsExpanded;
			RefreshTree();
			return;
		}

		if (vm.SectionNode != null)
		{
			vm.SectionNode.IsExpanded = vm.IsExpanded;
		}

		int startIndex = FlattenedNodes.IndexOf(vm) + 1;
		if (startIndex == 0) return;

		LibraryVirtualList.BeginBatchUpdate();
		try
		{
			if (vm.IsExpanded && vm.SectionNode != null)
			{
				var descendants = GetVisibleDescendants(vm.SectionNode, vm.Depth, vm.IsInsideBookmarkSection);
				for (int i = 0; i < descendants.Count; i++)
				{
					FlattenedNodes.Insert(startIndex + i, descendants[i]);
				}
			}
			else
			{
				// Remove everything until we hit another Header or run out
				while (startIndex < FlattenedNodes.Count && FlattenedNodes[startIndex].ItemType != NodeItemType.Header)
				{
					FlattenedNodes.RemoveAt(startIndex);
				}
			}
		}
		finally
		{
			LibraryVirtualList.EndBatchUpdate();
		}
	}

	private List<NodeItemViewModel> GetVisibleDescendants(NodeCategoryNode node, int baseDepth, bool isInsideBookmarkSection = false)
	{
		var result = new List<NodeItemViewModel>();
		var root = GetMainPage()?.NodeLibrary;
		foreach (var sub in node.SubCategories)
		{
			bool expanded = isInsideBookmarkSection ? sub.IsBookmarkExpanded : sub.IsExpanded;
			var subVm = new NodeItemViewModel
			{
				ItemType = NodeItemType.Category,
				DisplayName = sub.Name,
				Depth = baseDepth + 1,
				CategoryNode = sub,
				IsExpanded = expanded,
				IsBookmarked = root?.BookmarkedCategoryPaths.Contains(sub.FullPath) ?? false,
				IsInsideBookmarkSection = isInsideBookmarkSection
			};
			result.Add(subVm);

			if (expanded)
			{
				result.AddRange(GetVisibleDescendants(sub, baseDepth + 1, isInsideBookmarkSection));
			}
		}
		foreach (var entry in node.Nodes)
		{
			result.Add(new NodeItemViewModel
			{
				ItemType = NodeItemType.Node,
				DisplayName = entry.DisplayName,
				Depth = baseDepth + 1,
				NodeEntry = entry,
				IsBookmarked = root?.BookmarkedTypes.Contains(entry.Type) ?? false,
				IsInsideBookmarkSection = isInsideBookmarkSection
			});
		}
		return result;
	}

	private void OnDoubleTapNode(NodeItemViewModel? vm)
	{
		if (vm == null || vm.ItemType != NodeItemType.Node || vm.NodeEntry == null)
		{
			return;
		}

		RaiseInteraction(vm.NodeEntry, AssetInteractionAction.Open);
	}

	private void OnNodeDragStarting(object? sender, DragStartingEventArgs e)
	{
		// Cancel native OLE drag - our ghost+timer system handles the full drag lifecycle.
		// Without this, MAUI's DragGestureRecognizer captures the pointer and causes
		// GetAsyncKeyState(VK_LBUTTON) to momentarily report false, triggering an
		// instant drop as soon as the cursor enters the WebView area.
		e.Cancel = true;

		if (sender is BindableObject bindable && bindable.BindingContext is NodeItemViewModel vm && vm.ItemType == NodeItemType.Node && vm.NodeEntry != null)
		{
			RaiseInteraction(vm.NodeEntry, AssetInteractionAction.DragStart);
		}
	}

	/// <summary>
	/// Refreshes the node library tree from cached or freshly fetched node metadata.
	/// </summary>
	/// <param name="forceRebuild">True to ignore cached tree defaults and rebuild the visible tree.</param>
	internal void RefreshTree(bool forceRebuild = false)
	{
		_ = QueueRefreshTreeAsync(forceRebuild, showLoading: false);
	}

	async Task IRailToolView.PrewarmAsync(CancellationToken cancellationToken)
	{
		// Keep startup prewarm data-only. The rail host calls OpenAsync after transition,
		// where the visible rows are materialized safely.
		if (FlattenedNodes.Count == 0)
		{
			await QueueRefreshTreeAsync(forceRebuild: false, showLoading: false);
		}

		if (cancellationToken.IsCancellationRequested)
		{
			return;
		}
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
		if (FlattenedNodes.Count == 0)
		{
			RailPerformanceDiagnostics.Mark("NodeOpenRefreshStart", perf);
			await RefreshTreeAsync(forceRebuild: false, showLoading: true);
			RailPerformanceDiagnostics.Mark("NodeOpenRefreshCompleted", perf, $"items={FlattenedNodes.Count}");
		}
		else
		{
			RailPerformanceDiagnostics.Mark("NodeOpenResetPresentationStart", perf, $"items={FlattenedNodes.Count}");
			ResetOpenPresentation(cancellationToken);
			RailPerformanceDiagnostics.Mark("NodeOpenResetPresentationCompleted", perf);
		}

		RailPerformanceDiagnostics.Mark("NodeMaterializeStart", perf);
		await LibraryVirtualList.MaterializeVisibleViewsAsync(cancellationToken);
		RailPerformanceDiagnostics.Mark("NodeMaterializeCompleted", perf);
		await _loadingOverlay.HideAsync();

		if (cancellationToken.IsCancellationRequested)
		{
			return;
		}

		_isReady = true;
		RailPerformanceDiagnostics.Mark("NodeOpenReady", perf);
	}

	void IRailToolView.ResetPresentation()
	{
		_isReady = false;
	}

	private void ResetOpenPresentation(CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return;
		}

		_ = ResetOpenPresentationAsync();
	}

	private async Task ResetOpenPresentationAsync()
	{
		try
		{
			await LibraryVirtualList.ScrollToTopAsync(animated: false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Warning($"Node library scroll reset skipped: {ex.Message}");
		}
	}

	private void RefreshTreeWithLoading(bool forceRebuild = false)
	{
		_ = QueueRefreshTreeAsync(forceRebuild, showLoading: true);
	}

	internal Task RefreshTreeAndWaitAsync(bool forceRebuild = false)
	{
		return QueueRefreshTreeAsync(forceRebuild, showLoading: false);
	}

	private Task QueueRefreshTreeAsync(bool forceRebuild, bool showLoading)
	{
		if (_refreshTreeTask is { IsCompleted: false })
		{
			return _refreshTreeTask;
		}

		_refreshTreeTask = RefreshTreeAsync(forceRebuild, showLoading);
		return _refreshTreeTask;
	}

	private async Task RefreshTreeAsync(bool forceRebuild = false, bool showLoading = false)
	{
		var perf = RailPerformanceDiagnostics.Start();
		bool loadingShown = false;
		try
		{
			_isBusy = true;
			int refreshVersion = System.Threading.Interlocked.Increment(ref _refreshTreeVersion);
			RailPerformanceDiagnostics.Mark("NodeRefreshStart", perf, $"force={forceRebuild}, loading={showLoading}");
			if (showLoading)
			{
				loadingShown = true;
				await _loadingOverlay.ShowAsync();
				RailPerformanceDiagnostics.Mark("NodeRefreshOverlayShown", perf);
			}

			var mainPage = GetMainPage();
			var root = mainPage?.NodeLibrary;

			if (root == null)
			{
				LibraryVirtualList.IsVisible = false;
				return;
			}

			LibraryVirtualList.IsVisible = true;

			if (!_hasInitializedTreeDefaults)
			{
				_hasInitializedTreeDefaults = true;

				// Only force-expand on first load
				root.NexusFamilyRoot.IsExpanded = true;
				root.IsBookmarkedExpanded = true;
				root.ComfyRoot.IsExpanded = true;
				root.ExtensionRoot.IsExpanded = true;

				root.BlueprintRoot.IsExpanded = false;
				root.PartnerRoot.IsExpanded = false;
			}

			var buildContext = new NodeTreeBuildContext(
				root,
				_currentSearchText,
				_isDescriptionSearchEnabled,
				root.BookmarkedTypes.ToHashSet(StringComparer.OrdinalIgnoreCase),
				root.BookmarkedCategoryPaths.ToHashSet(StringComparer.OrdinalIgnoreCase));
			var newNodes = await Task.Run(() => BuildFlattenedNodes(buildContext));
			RailPerformanceDiagnostics.Mark("NodeRefreshBuildCompleted", perf, $"items={newNodes.Count}");
			if (refreshVersion != _refreshTreeVersion)
			{
				return;
			}

			await MergeFlattenedNodesAsync(newNodes, CancellationToken.None);
			RailPerformanceDiagnostics.Mark("NodeRefreshMergeCompleted", perf, $"items={FlattenedNodes.Count}");
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[NODE_LIB] FATAL ERROR in RefreshTree");
		}
		finally
		{
			_isBusy = false;
			if (loadingShown)
			{
				await _loadingOverlay.HideAsync();
				RailPerformanceDiagnostics.Mark("NodeRefreshOverlayHidden", perf);
			}
		}
	}

	private async Task MergeFlattenedNodesAsync(IReadOnlyList<NodeItemViewModel> newNodes, CancellationToken cancellationToken)
	{
		LibraryVirtualList.BeginBatchUpdate();
		try
		{
			int operationsSinceYield = 0;

			// In-place update preserves scroll position while yielding between chunks
			// so loading animations and pointer feedback can keep breathing.
			for (int i = 0; i < newNodes.Count; i++)
			{
				if (i < FlattenedNodes.Count)
				{
					SyncFlattenedNode(i, newNodes[i]);
				}
				else
				{
					FlattenedNodes.Add(newNodes[i]);
				}

				operationsSinceYield++;
				if (operationsSinceYield >= TreeMergeBatchSize)
				{
					operationsSinceYield = 0;
					await Task.Yield();
				}
			}

			while (FlattenedNodes.Count > newNodes.Count)
			{
				FlattenedNodes.RemoveAt(FlattenedNodes.Count - 1);

				operationsSinceYield++;
				if (operationsSinceYield >= TreeMergeBatchSize)
				{
					operationsSinceYield = 0;
					await Task.Yield();
				}
			}

			await LibraryVirtualList.PrewarmViewPoolAsync(
				GetTemplatePrewarmSamples(newNodes),
				TreePoolPrewarmPerTemplate,
				TreePoolPrewarmBatchSize,
				cancellationToken);
		}
		finally
		{
			LibraryVirtualList.EndBatchUpdate();
		}
	}

	private static double GetNodeItemHeight(object item)
		=> item is NodeItemViewModel { ItemType: NodeItemType.Header } ? 36 : 28;

	private static IEnumerable<object> GetTemplatePrewarmSamples(IReadOnlyList<NodeItemViewModel> nodes)
	{
		bool hasHeader = false;
		bool hasCategory = false;
		bool hasNode = false;

		foreach (NodeItemViewModel node in nodes)
		{
			switch (node.ItemType)
			{
				case NodeItemType.Header when !hasHeader:
					hasHeader = true;
					yield return node;
					break;
				case NodeItemType.Category when !hasCategory:
					hasCategory = true;
					yield return node;
					break;
				case NodeItemType.Node when !hasNode:
					hasNode = true;
					yield return node;
					break;
			}

			if (hasHeader && hasCategory && hasNode)
			{
				yield break;
			}
		}
	}

	private void SyncFlattenedNode(int index, NodeItemViewModel newNode)
	{
		var current = FlattenedNodes[index];
		if (current.DisplayName != newNode.DisplayName || current.ItemType != newNode.ItemType)
		{
			FlattenedNodes[index] = newNode;
			return;
		}

		if (current.IsExpanded != newNode.IsExpanded)
		{
			current.IsExpanded = newNode.IsExpanded;
		}

		if (current.IsBookmarked != newNode.IsBookmarked)
		{
			current.IsBookmarked = newNode.IsBookmarked;
		}
	}

	private List<NodeItemViewModel> BuildFlattenedNodes(NodeTreeBuildContext context)
	{
		var root = context.Root;
		var newNodes = new List<NodeItemViewModel>();

		// 0. Nexus Suite / ComfyUI-HUD (Top Pinned)
		if (root.NexusFamilyRoot.SubCategories.Any() || root.NexusFamilyRoot.Nodes.Any())
		{
			newNodes.Add(new NodeItemViewModel { ItemType = NodeItemType.Header, DisplayName = "COMFYUI HUD", Section = SectionKind.NexusFamily, SectionNode = root.NexusFamilyRoot, IsExpanded = root.NexusFamilyRoot.IsExpanded });
			FlattenCategoryTree(root.NexusFamilyRoot, 0, newNodes, context);
		}

		// 1. Bookmarked section (always visible header)
		newNodes.Add(new NodeItemViewModel { ItemType = NodeItemType.Header, DisplayName = "BOOKMARKED", Section = SectionKind.Bookmarked, IsExpanded = root.IsBookmarkedExpanded });

		if (root.IsBookmarkedExpanded)
		{
			if (root.BookmarkedCategories.Any() || root.Bookmarked.Any())
			{
				foreach (var cat in root.BookmarkedCategories)
				{
					FlattenCategoryTree(cat, 1, newNodes, context, true);
				}
				foreach (var node in root.Bookmarked)
				{
					newNodes.Add(new NodeItemViewModel { ItemType = NodeItemType.Node, DisplayName = node.DisplayName, Depth = 1, NodeEntry = node, IsBookmarked = true, IsInsideBookmarkSection = true });
				}
			}
			else
			{
				newNodes.Add(new NodeItemViewModel { ItemType = NodeItemType.Node, DisplayName = "No bookmarks yet", Depth = 1 });
			}
		}

		// 2. Subgraph Blueprints (currently empty but keep for future)
		newNodes.Add(new NodeItemViewModel { ItemType = NodeItemType.Header, DisplayName = "SUBGRAPH BLUEPRINTS", Section = SectionKind.Blueprints, SectionNode = root.BlueprintRoot, IsExpanded = root.BlueprintRoot.IsExpanded });
		FlattenCategoryTree(root.BlueprintRoot, 0, newNodes, context);

		// 3. Partner Nodes
		newNodes.Add(new NodeItemViewModel { ItemType = NodeItemType.Header, DisplayName = "PARTNER NODES", Section = SectionKind.Partner, SectionNode = root.PartnerRoot, IsExpanded = root.PartnerRoot.IsExpanded });
		FlattenCategoryTree(root.PartnerRoot, 0, newNodes, context);

		// 4. Comfy Nodes
		newNodes.Add(new NodeItemViewModel { ItemType = NodeItemType.Header, DisplayName = "COMFY NODES", Section = SectionKind.Comfy, SectionNode = root.ComfyRoot, IsExpanded = root.ComfyRoot.IsExpanded });
		FlattenCategoryTree(root.ComfyRoot, 0, newNodes, context);

		// 5. Extensions
		newNodes.Add(new NodeItemViewModel { ItemType = NodeItemType.Header, DisplayName = "EXTENSIONS", Section = SectionKind.Extensions, SectionNode = root.ExtensionRoot, IsExpanded = root.ExtensionRoot.IsExpanded });
		FlattenCategoryTree(root.ExtensionRoot, 0, newNodes, context);

		return newNodes;
	}

	private bool FlattenCategoryTree(NodeCategoryNode node, int depth, List<NodeItemViewModel> result, NodeTreeBuildContext context, bool isInsideBookmarkSection = false)
	{
		bool isSearching = !string.IsNullOrWhiteSpace(context.SearchText);
		bool expanded = isInsideBookmarkSection ? node.IsBookmarkExpanded : node.IsExpanded;

		int startIndex = result.Count;

		// At depth 0, the node is a section root - its header is added externally.
		// Only emit a Category item for sub-folders (depth > 0).
		if (depth > 0)
		{
			result.Add(new NodeItemViewModel
			{
				ItemType = NodeItemType.Category,
				DisplayName = node.Name,
				Depth = depth,
				CategoryNode = node,
				IsExpanded = expanded,
				IsBookmarked = context.BookmarkedCategoryPaths.Contains(node.FullPath),
				IsInsideBookmarkSection = isInsideBookmarkSection
			});
		}

		bool hasAnyMatch = false;

		if (isSearching || expanded)
		{
			foreach (var sub in node.SubCategories)
			{
				bool subMatched = FlattenCategoryTree(sub, depth + 1, result, context, isInsideBookmarkSection);
				if (subMatched) hasAnyMatch = true;
			}

			foreach (var nodeEntry in node.Nodes)
			{
				if (isSearching && !IsMatch(nodeEntry, context.SearchText, context.IsDescriptionSearchEnabled)) continue;

				hasAnyMatch = true;
				result.Add(new NodeItemViewModel
				{
					ItemType = NodeItemType.Node,
					DisplayName = nodeEntry.DisplayName,
					Depth = depth + 1,
					NodeEntry = nodeEntry,
					IsBookmarked = context.BookmarkedTypes.Contains(nodeEntry.Type),
					IsInsideBookmarkSection = isInsideBookmarkSection
				});
			}
		}

		if (isSearching && depth > 0 && !hasAnyMatch)
		{
			// Remove the category item if it has no matches
			result.RemoveAt(startIndex);
			return false;
		}

		return !isSearching || hasAnyMatch;
	}

	private static bool IsMatch(NodeLibraryEntry entry, string searchText, bool isDescriptionSearchEnabled)
	{
		if (string.IsNullOrWhiteSpace(searchText)) return true;
		if (entry.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)) return true;
		if (entry.Type.Contains(searchText, StringComparison.OrdinalIgnoreCase)) return true;
		if (isDescriptionSearchEnabled && entry.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	private sealed record NodeTreeBuildContext(
		NodeLibraryRoot Root,
		string SearchText,
		bool IsDescriptionSearchEnabled,
		HashSet<string> BookmarkedTypes,
		HashSet<string> BookmarkedCategoryPaths);

	private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
	{
		_currentSearchText = e.NewTextValue ?? string.Empty;
		RailSearchClearButton.IsVisible = !string.IsNullOrEmpty(_currentSearchText);
		_searchTimer.Stop();
		_searchTimer.Start();
	}

	private void OnClearSearchTapped(object? sender, EventArgs e)
	{
		RailSearchEntry.Text = string.Empty;
	}

	private void OnDescSearchTapped(object? sender, EventArgs e)
	{
		_isDescriptionSearchEnabled = !_isDescriptionSearchEnabled;
		UpdateToggleVisualState();
		RefreshTree();
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

	private void OnTogglePointerEntered(object? sender, PointerEventArgs e)
	{
		_isToggleHovered = true;
		UpdateToggleVisualState();
	}

	private void OnTogglePointerExited(object? sender, PointerEventArgs e)
	{
		_isToggleHovered = false;
		UpdateToggleVisualState();
	}

	private void UpdateToggleVisualState()
	{
		if (_isDescriptionSearchEnabled)
		{
			DescSearchIndicator.Color = Color.FromArgb(NodeSearchActiveIndicatorColor);
			DescSearchToggleBorder.BackgroundColor = _isToggleHovered ? NodeSearchToggleActiveHoverBackgroundColor : NodeSearchToggleActiveBackgroundColor;
			DescSearchToggleBorder.Stroke = NodeSearchToggleActiveStrokeColor;
			DescSearchLabel.TextColor = NodeSearchToggleActiveTextColor;
		}
		else
		{
			DescSearchIndicator.Color = Color.FromArgb(NodeSearchInactiveIndicatorColor);
			DescSearchToggleBorder.BackgroundColor = _isToggleHovered ? NodeSearchToggleInactiveHoverBackgroundColor : NodeSearchToggleInactiveBackgroundColor;
			DescSearchToggleBorder.Stroke = NodeSearchToggleInactiveStrokeColor;
			DescSearchLabel.TextColor = NodeSearchToggleInactiveTextColor;
		}
	}

	private void RaiseInteraction(NodeLibraryEntry node, AssetInteractionAction action)
	{
		var request = new AssetOpenRequest(
			FullPath: "",
			Kind: AssetOpenKind.GenericFile,
			Name: node.Type,
			Extension: "",
			SourceRoot: "",
			DisplayName: node.DisplayName,
			Mode: AssetInteractionMode.Node,
			Action: action,
			NodeType: node.Type,
			DragId: action == AssetInteractionAction.DragStart
				? Guid.NewGuid().ToString("N")
				: null
		);

		AssetInteractionRequested?.Invoke(this, request);
	}

	private void SortCategoryRecursively(NodeCategoryNode node)
	{
		node.SubCategories.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
		node.Nodes.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
		foreach (var sub in node.SubCategories)
		{
			SortCategoryRecursively(sub);
		}
	}

	/// <summary>
	/// Replaces the blueprint section and merges it into the currently visible node tree.
	/// </summary>
	/// <param name="items">Blueprints received from the WebView bridge.</param>
	internal void UpdateBlueprints(List<BlueprintItem> items)
	{
		_ = UpdateBlueprintsAndWaitAsync(items);
	}

	internal Task UpdateBlueprintsAndWaitAsync(List<BlueprintItem> items)
	{
		var mainPage = GetMainPage();
		var root = mainPage?.NodeLibrary;
		if (root == null)
		{
			NexusLog.Warning("[BLUEPRINT] NodeLibrary root is null in UpdateBlueprints");
			return Task.CompletedTask;
		}

		root.BlueprintsById.Clear();
		root.BlueprintRoot.SubCategories.Clear();
		root.BlueprintRoot.Nodes.Clear();

		foreach (var item in items)
		{
			root.BlueprintsById[item.Id] = item;

			string category = string.IsNullOrWhiteSpace(item.Category) ? "Uncategorized" : item.Category;
			var parts = category.Split('/', StringSplitOptions.RemoveEmptyEntries);

			NodeCategoryNode current = root.BlueprintRoot;
			foreach (var part in parts)
			{
				var trimmed = part.Trim();
				var next = current.SubCategories.FirstOrDefault(x => x.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
				if (next == null)
				{
					next = new NodeCategoryNode(trimmed, current);
					current.SubCategories.Add(next);
				}
				current = next;
			}

			string description = "";
			if (item.Info.HasValue)
			{
				var infoVal = item.Info.Value;
				if (infoVal.ValueKind == System.Text.Json.JsonValueKind.String)
				{
					description = infoVal.GetString() ?? string.Empty;
				}
				else
				{
					description = infoVal.ToString();
				}
			}

			current.Nodes.Add(new NodeLibraryEntry(
				Type: $"SubgraphBlueprint.{item.Id}",
				DisplayName: string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name,
				Description: description,
				Category: item.Category ?? string.Empty,
				PythonModule: "Blueprint",
				GroupKind: NodeGroupKind.Blueprint,
				ColorHex: "#ffcc33"
			));
		}

		SortCategoryRecursively(root.BlueprintRoot);
		return RefreshTreeAndWaitAsync();
	}
}
