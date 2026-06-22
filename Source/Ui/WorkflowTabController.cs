using System.Text.Json;
using Microsoft.Maui.Controls.Shapes;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Views;
using ComfyUI_Nexus.Views.Overlays;

namespace ComfyUI_Nexus.Ui;

internal sealed class WorkflowTabController
{
	internal sealed record WorkflowTabInfo(
		int OriginalIndex,
		string Name,
		string DisplayName,
		bool Active,
		bool Modified,
		bool HasFile,
		string Path,
		string RelativePath)
	{
		internal bool IsRootWorkflowFile
		{
			get
			{
				string normalized = NormalizeWorkflowRelativePath(RelativePath);
				if (string.IsNullOrWhiteSpace(normalized))
				{
					return false;
				}

				string withoutPrefix = StripWorkflowPrefix(normalized);
				return !withoutPrefix.Contains('/');
			}
		}
	}

	private readonly HeaderView _header;
	private readonly WorkflowDropdownView _dropdown;
	private readonly Func<bool> _isDropdownVisible;
	private readonly Action _hideDropdown;
	private readonly Action<bool> _showDropdown;
	private readonly Func<string, Task> _toggleBookmarkAsync;
	private readonly Func<WorkflowTabInfo, WorkflowActionKind, Task> _executeWorkflowActionAsync;
	private readonly Func<string, Task> _executeScriptAsync;
	private readonly Action _onWorkflowContextChanged;

	private const double LogoWidth = 60;
	private const double RightMargin = 20;
	private const double AddButtonWidth = 42;      // Optimized for [+] icon
	private const double OverflowButtonWidth = 42; // Optimized for [...] icon
	private const double MinTabWidth = 110;
	private const double MaxTabWidth = 220;
	private const double OverflowHoverScale = 1.03;
	private const double DropdownHoverScale = 1.01;
	private const double ModifiedGlyphSize = 7;
	private const double ModifiedGlyphRadius = 3.5;
	private const double ModifiedGlyphHiddenScale = 0.86;
	private const double CloseGlyphHostWidth = 14;
	private const double CloseButtonMinWidth = 24;
	private const uint HoverAnimationLength = 90;
	private const uint CloseGlyphRevealAnimationLength = 110;
	private static readonly Color TabChromeBackgroundColor = NexusColors.SurfaceDark;
	private static readonly Color TabChromeStrokeColor = NexusColors.Stroke;
	private static readonly Color TabActiveBackgroundColor = NexusColors.Surface;
	private static readonly Color TabActiveStrokeColor = NexusColors.AccentDeep;
	private static readonly Color TabInactiveStrokeColor = NexusColors.StrokeSubtle;
	private static readonly Color TabInactiveHoverBackgroundColor = NexusColors.SurfaceHover;
	private static readonly Color TabInactiveTextColor = NexusColors.TextFaint;
	private static readonly Color TabActiveTextColor = NexusColors.White;
	private static readonly Color OverflowHoverBackgroundColor = Color.FromArgb("#1b2b42");
	private static readonly Color OverflowHoverStrokeColor = NexusColors.StrokeHover;
	private static readonly Color OverflowHoverTextColor = Color.FromArgb("#e6f6ff");
	private static readonly Color AddTabBackgroundColor = Color.FromArgb("#0c1526");
	private static readonly Color AddTabHoverBackgroundColor = Color.FromArgb("#16233a");
	private static readonly Color AddTabTextColor = NexusColors.AccentText;
	private static readonly Color AddTabHoverStrokeColor = Color.FromArgb("#42dfff");
	private static readonly Color ModifiedGlyphColor = Color.FromArgb("#ffaa44");
	private static readonly Color CloseGlyphTextColor = Color.FromArgb("#7f92ad");
	private static readonly Color CloseGlyphHoverTextColor = NexusColors.TextPrimary;
	private static readonly Color BookmarkStarColor = Color.FromArgb("#ffd700");
	private static readonly Color DropdownActiveTextColor = NexusColors.AccentDeep;
	private static readonly Color DropdownInactiveTextColor = Color.FromArgb("#a0b0c0");
	private static readonly Color DropdownStrokeColor = Color.FromArgb("#1a2a3e");
	private static readonly Color DropdownHoverBackgroundColor = Color.FromArgb("#1f3148");
	private static readonly Color DropdownHoverStrokeColor = NexusColors.StrokeHover;
	private static readonly Color DropdownActiveHoverTextColor = NexusColors.AccentHover;
	private static readonly Color DropdownInactiveHoverTextColor = Color.FromArgb("#e0f4ff");

	private string _lastSyncData = string.Empty;
	private int _activeTabIndex = -1;
	private readonly List<int> _visualOrder = [];
	private readonly Dictionary<string, int> _overflowMap = new();
	private readonly List<WorkflowTabInfo> _workflows = [];
	private readonly Border _btnAddTab;
	private readonly Border _btnOverflow;
	private double _availableWidth = 800;
	private bool _isBulkProcessing;
	private readonly HashSet<string> _bookmarkedWorkflows = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _knownWorkflowFiles = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _pendingClosedWorkflowPaths = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _trackedWorkflowPathsByName = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _workflowDisplayNameOverridesByName = new(StringComparer.OrdinalIgnoreCase);
	private WorkflowTabInfo? _activeWorkflowState;
	private DateTime _lastTabRebuildLogUtc = DateTime.MinValue;

	internal WorkflowTabController(
		HeaderView header,
		WorkflowDropdownView dropdown,
		Func<bool> isDropdownVisible,
		Action hideDropdown,
		Action<bool> showDropdown,
		Func<string, Task> toggleBookmarkAsync,
		Func<WorkflowTabInfo, WorkflowActionKind, Task> executeWorkflowActionAsync,
		Func<string, Task> executeScriptAsync,
		Action onWorkflowContextChanged)
	{
		_header = header;
		_dropdown = dropdown;
		_isDropdownVisible = isDropdownVisible;
		_hideDropdown = hideDropdown;
		_showDropdown = showDropdown;
		_toggleBookmarkAsync = toggleBookmarkAsync;
		_executeWorkflowActionAsync = executeWorkflowActionAsync;
		_executeScriptAsync = executeScriptAsync;
		_onWorkflowContextChanged = onWorkflowContextChanged;

		_btnAddTab = CreateAddTabButton();
		_btnOverflow = CreateOverflowButton();
	}

	internal int ActiveTabIndex => _activeTabIndex;
	internal WorkflowTabInfo? ActiveWorkflow => _activeWorkflowState;
	internal IReadOnlyCollection<string> BookmarkedWorkflows => _bookmarkedWorkflows;

	internal IReadOnlyList<WorkflowTabInfo> GetWorkflowsForPath(string relativePath, bool isDirectory)
	{
		string normalized = isDirectory
			? NormalizeWorkflowDirectoryRelativePath(relativePath)
			: NormalizeWorkflowRelativePath(relativePath);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return [];
		}

		string directoryPrefix = $"{normalized.TrimEnd('/')}/";
		return _workflows
			.Where(workflow =>
			{
				string workflowPath = NormalizeWorkflowRelativePath(workflow.RelativePath);
				return isDirectory
					? workflowPath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase)
					: string.Equals(workflowPath, normalized, StringComparison.OrdinalIgnoreCase);
			})
			.ToArray();
	}

	internal async Task<bool> ActivateWorkflowAndWaitAsync(WorkflowTabInfo workflow, TimeSpan timeout)
	{
		string relativePath = NormalizeWorkflowRelativePath(workflow.RelativePath);
		var current = GetWorkflowsForPath(relativePath, isDirectory: false).FirstOrDefault();
		if (current == null)
		{
			return false;
		}

		if (!current.Active)
		{
			await SendSwitchWorkflowAsync(current.OriginalIndex);
		}

		return await WaitForWorkflowStateAsync(
			() => GetWorkflowsForPath(relativePath, isDirectory: false).Any(candidate => candidate.Active),
			timeout);
	}

	internal Task<bool> WaitForWorkflowSavedAsync(string relativePath, TimeSpan timeout)
	{
		string normalized = NormalizeWorkflowRelativePath(relativePath);
		return WaitForWorkflowStateAsync(
			() => GetWorkflowsForPath(normalized, isDirectory: false).Any(workflow => !workflow.Modified),
			timeout);
	}

	internal async Task<bool> CloseWorkflowsAndWaitAsync(IEnumerable<string> relativePaths, TimeSpan timeout)
	{
		var normalizedPaths = relativePaths
			.Select(NormalizeWorkflowRelativePath)
			.Where(path => !string.IsNullOrWhiteSpace(path))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var indices = _workflows
			.Where(workflow => normalizedPaths.Contains(NormalizeWorkflowRelativePath(workflow.RelativePath)))
			.Select(workflow => workflow.OriginalIndex)
			.Distinct()
			.ToList();

		await CloseWorkflowsAsync(indices);
		return await WaitForWorkflowStateAsync(
			() => _workflows.All(workflow => !normalizedPaths.Contains(NormalizeWorkflowRelativePath(workflow.RelativePath))),
			timeout);
	}

	private static async Task<bool> WaitForWorkflowStateAsync(Func<bool> predicate, TimeSpan timeout)
	{
		DateTime deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			if (predicate())
			{
				return true;
			}

			await Task.Delay(50);
		}

		return predicate();
	}

	internal void SetBookmarkedWorkflows(IEnumerable<string> bookmarks)
	{
		_bookmarkedWorkflows.Clear();
		foreach (string bookmark in bookmarks)
		{
			string normalized = NormalizeWorkflowRelativePath(bookmark);
			if (!string.IsNullOrWhiteSpace(normalized))
			{
				_bookmarkedWorkflows.Add(normalized);
			}
		}

		_onWorkflowContextChanged();
		RefreshFromLastSync();
	}

	internal void SetKnownWorkflowFiles(IEnumerable<string> fileNames)
	{
		_knownWorkflowFiles.Clear();
		foreach (string fileName in fileNames)
		{
			string normalized = NormalizeWorkflowRelativePath(fileName);
			if (!string.IsNullOrWhiteSpace(normalized))
			{
				_knownWorkflowFiles.Add(normalized);
			}
		}

		RefreshFromLastSync();
	}

	internal void TrackOpenedWorkflow(string workflowName, string relativePath)
	{
		string normalized = NormalizeWorkflowRelativePath(relativePath);
		if (string.IsNullOrWhiteSpace(workflowName) || string.IsNullOrWhiteSpace(normalized))
		{
			return;
		}

		_trackedWorkflowPathsByName[workflowName] = normalized;
		_pendingClosedWorkflowPaths.Remove(normalized);
		_workflowDisplayNameOverridesByName.Remove(workflowName);
		RefreshFromLastSync();
	}

	internal void UpdateTrackedWorkflowPath(WorkflowTabInfo workflow, string relativePath)
	{
		string normalized = NormalizeWorkflowRelativePath(relativePath);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return;
		}

		_trackedWorkflowPathsByName[workflow.Name] = normalized;
		SetDisplayNameOverride(workflow.Name, normalized);
		RefreshFromLastSync();
	}

	internal async Task<bool> TryActivateTrackedWorkflowAsync(string relativePath)
	{
		string normalized = NormalizeWorkflowRelativePath(relativePath);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		if (_pendingClosedWorkflowPaths.Contains(normalized))
		{
			return false;
		}

		var workflow = _workflows.FirstOrDefault(wf =>
			string.Equals(NormalizeWorkflowRelativePath(wf.RelativePath), normalized, StringComparison.OrdinalIgnoreCase));
		if (workflow == null)
		{
			return false;
		}

		if (workflow.Active)
		{
			return true;
		}

		_activeTabIndex = workflow.OriginalIndex;
		await SendSwitchWorkflowAsync(workflow.OriginalIndex);
		return true;
	}

	internal bool RemapTrackedWorkflowPaths(string oldRelativePath, string newRelativePath, bool isDirectory)
	{
		bool changed = false;
		string newWorkflowName = isDirectory
			? string.Empty
			: System.IO.Path.GetFileNameWithoutExtension(StripWorkflowPrefix(newRelativePath));
		foreach (var entry in _trackedWorkflowPathsByName.ToArray())
		{
			if (TryRemapWorkflowRelativePath(entry.Value, oldRelativePath, newRelativePath, isDirectory, out string remapped))
			{
				_trackedWorkflowPathsByName[entry.Key] = remapped;
				if (!isDirectory)
				{
					SetDisplayNameOverride(entry.Key, remapped);
					if (!string.IsNullOrWhiteSpace(newWorkflowName))
					{
						_trackedWorkflowPathsByName[newWorkflowName] = remapped;
					}
				}

				changed = true;
			}
		}

		if (changed)
		{
			RefreshFromLastSync();
		}

		return changed;
	}

	internal bool IsBookmarked(string name) => ContainsBookmark(CreateRootRelativePath(name));

	internal bool IsBookmarked(WorkflowTabInfo workflow)
		=> ContainsBookmark(workflow.RelativePath);

	internal bool IsBookmarkedPath(string relativePath) => ContainsBookmark(relativePath);

	private bool ContainsBookmark(string relativePath)
	{
		string normalized = NormalizeWorkflowRelativePath(relativePath);
		return !string.IsNullOrWhiteSpace(normalized) && _bookmarkedWorkflows.Contains(normalized);
	}

	internal WorkflowActionState GetActiveWorkflowActionState()
	{
		bool hasFile = _activeWorkflowState?.HasFile == true;
		bool isModified = _activeWorkflowState?.Modified == true;
		bool isBookmarked = _activeWorkflowState != null && IsBookmarked(_activeWorkflowState);
		return WorkflowActionCatalog.CreateState(hasFile, isModified, isBookmarked);
	}

	internal bool TryApplyWorkflowSync(JsonElement data)
	{
		_isBulkProcessing = false;

		string currentData = data.ToString();
		if (_lastSyncData == currentData)
		{
			return false;
		}

		_lastSyncData = currentData;
		if (data.ValueKind == JsonValueKind.Array)
		{
			RebuildTabs(data);
		}

		return true;
	}

	internal void RefreshFromLastSync()
	{
		if (string.IsNullOrEmpty(_lastSyncData))
		{
			return;
		}

		using var doc = JsonDocument.Parse(_lastSyncData);
		RebuildTabs(doc.RootElement);
	}

	internal void RefreshLayout(double actualTabWidth)
	{
		if (Math.Abs(_availableWidth - actualTabWidth) < 0.1)
		{
			return;
		}

		_availableWidth = actualTabWidth;
		RefreshFromLastSync();
	}

	internal async Task CloseWorkflowsAsync(List<int> logicalIndices)
	{
		if (logicalIndices.Count == 0)
		{
			return;
		}

		if (logicalIndices.Count > 1)
		{
			_isBulkProcessing = true;
			NexusLog.Info("Bulk processing started... UI locked.");
		}

		logicalIndices.Sort((a, b) => b.CompareTo(a));
		foreach (int idx in logicalIndices)
		{
			await SendCloseWorkflowAsync(idx);
		}
	}

	private async Task SendTabActionAsync(string action, int logicalIndex)
	{
		NexusLog.Info($"Workflow action: {action} [{logicalIndex}]");
		await SendNexusActionAsync(BridgeActions.TabAction, $"{{ action: '{action}', index: {logicalIndex} }}");
	}

	private void RebuildTabs(JsonElement data)
	{
		_header.ResetTabSurface();
		_btnOverflow.IsVisible = false;
		_overflowMap.Clear();

		var workflows = new List<WorkflowTabInfo>();
		int index = 0;
		int currentActiveIndex = -1;
		string activeName = "Nexus Shell";

		foreach (var wf in data.EnumerateArray())
		{
			string name = wf.TryGetProperty("name", out var n) ? n.GetString() ?? "Untitled" : "Untitled";
			bool active = wf.TryGetProperty("active", out var a) && a.GetBoolean();
			bool modified = wf.TryGetProperty("modified", out var m) && m.GetBoolean();
			string path = wf.TryGetProperty("path", out var p) ? p.GetString() ?? string.Empty : string.Empty;
			string relativePath = ResolveWorkflowRelativePath(path, out bool hasFile);
			string displayName = hasFile
				? System.IO.Path.GetFileNameWithoutExtension(StripWorkflowPrefix(relativePath))
				: ResolveWorkflowDisplayName(name);

			workflows.Add(new WorkflowTabInfo(index, name, displayName, active, modified, hasFile, path, relativePath));
			if (active)
			{
				currentActiveIndex = index;
				activeName = displayName;
			}

			index++;
		}

		if (Application.Current?.Windows is { Count: > 0 })
		{
			var window = Application.Current.Windows[0];
			if (window is not null)
			{
				window.Title = $"ComfyUI Nexus - [{activeName}]";
			}
		}

		_activeTabIndex = currentActiveIndex;
		_activeWorkflowState = workflows.FirstOrDefault(wf => wf.Active);
		_workflows.Clear();
		_workflows.AddRange(workflows);
		SynchronizeTrackedWorkflowAliases(workflows);
		ClearCompletedPendingClosedWorkflows(workflows);
		_onWorkflowContextChanged();

		_visualOrder.RemoveAll(x => x >= workflows.Count);
		for (int i = 0; i < workflows.Count; i++)
		{
			if (!_visualOrder.Contains(i))
			{
				_visualOrder.Add(i);
			}
		}

		double logoWidth = ShellLayoutScale.H(LogoWidth, 44);
		double rightMargin = ShellLayoutScale.H(RightMargin, 12);
		double addBtnWidth = ShellLayoutScale.H(AddButtonWidth, 32);
		double overflowBtnWidth = ShellLayoutScale.H(OverflowButtonWidth, 32);
		double minTabW = ShellLayoutScale.H(MinTabWidth, 80);
		double maxTabW = ShellLayoutScale.H(MaxTabWidth);

		double innerWidth = _availableWidth - logoWidth - rightMargin;
		int totalTabs = workflows.Count;
		if (totalTabs == 0)
		{
			return;
		}

		_header.RemoveTabSurfaceChild(_btnAddTab);
		_header.RemoveTabSurfaceChild(_btnOverflow);

		// Flexible policy: allow up to 2 extra tabs by shrinking before switching to overflow
		int maxWithAdd = Math.Max(1, (int)((innerWidth - addBtnWidth + 2) / (minTabW + 2)));

		int row1Count = 0;
		int overflowCount = 0;
		double unifiedTabWidth = MaxTabWidth;

		if (totalTabs <= maxWithAdd + 2)
		{
			// All tabs fit (possibly squeezed up to +2 limit)
			row1Count = totalTabs;
			overflowCount = 0;
			unifiedTabWidth = Math.Min(maxTabW, (innerWidth - addBtnWidth - ((row1Count - 1) * 2)) / row1Count);
		}
		else
		{
			// Threshold exceeded: switch to overflow mode with both buttons
			int maxWithBoth = Math.Max(1, (int)((innerWidth - addBtnWidth - overflowBtnWidth + 2) / (minTabW + 2)));
			row1Count = maxWithBoth;
			overflowCount = totalTabs - row1Count;
			unifiedTabWidth = Math.Min(maxTabW, (innerWidth - addBtnWidth - overflowBtnWidth - ((row1Count - 1) * 2)) / row1Count);
		}
		unifiedTabWidth = NormalizeUnifiedTabWidth(unifiedTabWidth, totalTabs, ref row1Count, ref overflowCount);

		EnsureActiveTabRemainsVisible(workflows.Count, currentActiveIndex, row1Count);

		AddPrimaryTabColumns(row1Count, unifiedTabWidth, overflowCount);

		_dropdown.ClearItems();
		for (int i = 0; i < totalTabs; i++)
		{
			int logicalIndex = _visualOrder[i];
			var workflow = workflows[logicalIndex];

			if (i < row1Count)
			{
				var tab = CreateStyledTab(workflow.DisplayName, workflow.Active, workflow.Modified, workflow.HasFile, logicalIndex, unifiedTabWidth);
				Grid.SetColumn(tab, i);
				_header.AddTabSurfaceChild(tab, i);
			}
			else
			{
				_btnOverflow.IsVisible = true;
				_overflowMap[workflow.DisplayName] = logicalIndex;
				_dropdown.AddItem(CreateDropdownItem(workflow.DisplayName, workflow.Active, workflow.Modified, logicalIndex));
			}
		}

		PlaceTabUtilityButtons(row1Count, overflowCount);
		InvalidateTabLayouts();
		LogTabRebuild(totalTabs, row1Count, overflowCount);
	}

	private Border CreateOverflowButton()
	{
		double tabHeight = NexusUiTuning.TabButtonHeight;
		var label = new Label
		{
			Text = "•••",
			TextColor = TabInactiveTextColor,
			FontSize = 16,
			FontAttributes = FontAttributes.Bold,
			HeightRequest = tabHeight,
			VerticalOptions = LayoutOptions.Fill,
			HorizontalOptions = LayoutOptions.Center,
			VerticalTextAlignment = TextAlignment.Center,
			HorizontalTextAlignment = TextAlignment.Center,
			Margin = Thickness.Zero
		};

		var border = new Border
		{
			BackgroundColor = TabChromeBackgroundColor,
			Stroke = TabChromeStrokeColor,
			StrokeThickness = 1,
			StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
			Padding = Thickness.Zero,
			VerticalOptions = LayoutOptions.Center,
			HorizontalOptions = LayoutOptions.Start,
			Margin = new Thickness(4, 0, 0, 0),
			WidthRequest = 38,
			HeightRequest = tabHeight,
			Content = label
		};

		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (s, e) =>
		{
			border.BackgroundColor = OverflowHoverBackgroundColor;
			border.Stroke = OverflowHoverStrokeColor;
			label.TextColor = OverflowHoverTextColor;
			_ = border.ScaleToAsync(OverflowHoverScale, HoverAnimationLength, Easing.CubicOut);
		};
		pointer.PointerExited += (s, e) =>
		{
			border.BackgroundColor = TabChromeBackgroundColor;
			border.Stroke = TabChromeStrokeColor;
			label.TextColor = TabInactiveTextColor;
			_ = border.ScaleToAsync(1, HoverAnimationLength, Easing.CubicIn);
		};
		border.GestureRecognizers.Add(pointer);

		var tap = new TapGestureRecognizer();
		tap.Tapped += (s, e) =>
		{
			if (_isDropdownVisible())
			{
				_hideDropdown();
			}
			else
			{
				_showDropdown(false);
			}
		};
		border.GestureRecognizers.Add(tap);

		return border;
	}

	private Border CreateAddTabButton()
	{
		double tabHeight = NexusUiTuning.TabButtonHeight;
		var border = new Border
		{
			BackgroundColor = AddTabBackgroundColor,
			Stroke = TabChromeStrokeColor,
			StrokeThickness = 1,
			StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8, 8, 0, 0) },
			Padding = Thickness.Zero,
			WidthRequest = tabHeight,
			HeightRequest = tabHeight,
			VerticalOptions = LayoutOptions.End,
			HorizontalOptions = LayoutOptions.Start,
			Margin = new Thickness(0, 0, 4, 0),
			Content = new Label
			{
				Text = "+",
			TextColor = AddTabTextColor,
			FontSize = 14,
			FontAttributes = FontAttributes.Bold,
			HeightRequest = tabHeight,
			VerticalOptions = LayoutOptions.Fill,
			HorizontalOptions = LayoutOptions.Center,
			VerticalTextAlignment = TextAlignment.Center,
			HorizontalTextAlignment = TextAlignment.Center,
			Margin = Thickness.Zero
			}
		};

		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (s, e) =>
		{
			border.BackgroundColor = AddTabHoverBackgroundColor;
			border.Stroke = AddTabHoverStrokeColor;
		};
		pointer.PointerExited += (s, e) =>
		{
			border.BackgroundColor = AddTabBackgroundColor;
			border.Stroke = TabChromeStrokeColor;
		};
		border.GestureRecognizers.Add(pointer);

		var tap = new TapGestureRecognizer();
		tap.Tapped += async (s, e) =>
		{
			await SendNexusActionAsync(BridgeActions.NewWorkflow);
		};
		border.GestureRecognizers.Add(tap);

		return border;
	}

	private Border CreateStyledTab(string name, bool active, bool modified, bool hasFile, int index, double exactWidth)
	{
		double tabHeight = NexusUiTuning.TabButtonHeight;
		var border = new Border
		{
			BackgroundColor = active ? TabActiveBackgroundColor : TabChromeBackgroundColor,
			Stroke = active ? TabActiveStrokeColor : TabInactiveStrokeColor,
			StrokeThickness = 1,
			StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8, 8, 0, 0) },
			Padding = Thickness.Zero,
			HeightRequest = tabHeight,
			VerticalOptions = LayoutOptions.End,
			MinimumWidthRequest = 90,
			MaximumWidthRequest = exactWidth
		};

		ToolTipProperties.SetText(border, name);

		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (s, e) =>
		{
			if (!active)
			{
				border.BackgroundColor = TabInactiveHoverBackgroundColor;
			}
		};
		pointer.PointerExited += (s, e) =>
		{
			if (!active)
			{
				border.BackgroundColor = TabChromeBackgroundColor;
			}
		};
		border.GestureRecognizers.Add(pointer);

		var label = new Label
		{
			Text = name,
			TextColor = active ? TabActiveTextColor : TabInactiveTextColor,
			FontSize = NexusUiTuning.TabFontSize + 1,
			FontAttributes = active ? FontAttributes.Bold : FontAttributes.None,
			HeightRequest = tabHeight,
			VerticalOptions = LayoutOptions.Fill,
			HorizontalOptions = LayoutOptions.Fill,
			Padding = Thickness.Zero,
			VerticalTextAlignment = TextAlignment.Center,
			HorizontalTextAlignment = TextAlignment.Start,
			LineBreakMode = LineBreakMode.TailTruncation,
			MaxLines = 1,
			Margin = Thickness.Zero
		};

		bool bookmarked = _workflows.FirstOrDefault(wf => wf.OriginalIndex == index) is { } workflow && IsBookmarked(workflow);
		var bookmarkStar = new Label
		{
			Text = "*",
			TextColor = BookmarkStarColor,
			FontSize = 12,
			HeightRequest = tabHeight,
			VerticalOptions = LayoutOptions.Fill,
			VerticalTextAlignment = TextAlignment.Center,
			IsVisible = bookmarked,
			Margin = new Thickness(0, 0, 8, 0)
		};

		var nameAreaGrid = new Grid
		{
			ColumnDefinitions =
			[
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = GridLength.Star },
			],
			Padding = new Thickness(12, 0, 4, 0),
			HeightRequest = tabHeight,
			VerticalOptions = LayoutOptions.Fill,
			HorizontalOptions = LayoutOptions.Fill,
			RowDefinitions =
			{
				new RowDefinition { Height = GridLength.Star }
			}
		};
		nameAreaGrid.Add(bookmarkStar, 0, 0);
		nameAreaGrid.Add(label, 1, 0);

		var switchTap = new TapGestureRecognizer();
		switchTap.Tapped += async (s, e) =>
		{
			if (_isBulkProcessing || active)
			{
				return;
			}

			_activeTabIndex = index;
			await SendSwitchWorkflowAsync(index);
		};
		nameAreaGrid.GestureRecognizers.Add(switchTap);

		var closeGlyphLabel = new Label
		{
			Text = "×",
			Opacity = modified ? 0 : 1,
			TextColor = CloseGlyphTextColor,
			FontSize = 14,
			HeightRequest = tabHeight,
			VerticalOptions = LayoutOptions.Fill,
			HorizontalOptions = LayoutOptions.Center,
			HorizontalTextAlignment = TextAlignment.Center,
			VerticalTextAlignment = TextAlignment.Center,
			Margin = Thickness.Zero
		};

		var modifiedGlyphDot = new BoxView
		{
			Opacity = modified ? 1 : 0,
			Color = ModifiedGlyphColor,
			WidthRequest = ModifiedGlyphSize,
			HeightRequest = ModifiedGlyphSize,
			CornerRadius = ModifiedGlyphRadius,
			VerticalOptions = LayoutOptions.Center,
			HorizontalOptions = LayoutOptions.Center,
			Margin = Thickness.Zero,
			Scale = modified ? 1 : ModifiedGlyphHiddenScale
		};

		var closeGlyphHost = new Grid
		{
			WidthRequest = CloseGlyphHostWidth,
			HeightRequest = tabHeight,
			Padding = 0,
			HorizontalOptions = LayoutOptions.Center,
			VerticalOptions = LayoutOptions.Fill,
			Margin = Thickness.Zero
		};
		closeGlyphHost.Add(modifiedGlyphDot);
		closeGlyphHost.Add(closeGlyphLabel);

		var closeBtn = new Border
		{
			BackgroundColor = Colors.Transparent,
			StrokeThickness = 0,
			Padding = new Thickness(8, 0, 10, 0),
			HeightRequest = tabHeight,
			VerticalOptions = LayoutOptions.Fill,
			MinimumWidthRequest = CloseButtonMinWidth,
			Content = closeGlyphHost
		};

		var closePointer = new PointerGestureRecognizer();
		closePointer.PointerEntered += async (s, e) =>
		{
			closeGlyphLabel.TextColor = CloseGlyphHoverTextColor;
			if (!modified)
			{
				return;
			}

			var fadeOutDot = modifiedGlyphDot.FadeToAsync(0, HoverAnimationLength, Easing.CubicOut);
			var shrinkDot = modifiedGlyphDot.ScaleToAsync(ModifiedGlyphHiddenScale, HoverAnimationLength, Easing.CubicOut);
			closeGlyphLabel.Scale = ModifiedGlyphHiddenScale;
			var fadeInX = closeGlyphLabel.FadeToAsync(1, CloseGlyphRevealAnimationLength, Easing.CubicIn);
			var growX = closeGlyphLabel.ScaleToAsync(1, CloseGlyphRevealAnimationLength, Easing.CubicOut);
			await Task.WhenAll(fadeOutDot, shrinkDot, fadeInX, growX);
		};
		closePointer.PointerExited += async (s, e) =>
		{
			closeGlyphLabel.TextColor = CloseGlyphTextColor;
			if (!modified)
			{
				return;
			}

			var fadeOutX = closeGlyphLabel.FadeToAsync(0, HoverAnimationLength, Easing.CubicOut);
			var shrinkX = closeGlyphLabel.ScaleToAsync(ModifiedGlyphHiddenScale, HoverAnimationLength, Easing.CubicOut);
			modifiedGlyphDot.Scale = ModifiedGlyphHiddenScale;
			var fadeInDot = modifiedGlyphDot.FadeToAsync(1, CloseGlyphRevealAnimationLength, Easing.CubicIn);
			var growDot = modifiedGlyphDot.ScaleToAsync(1, CloseGlyphRevealAnimationLength, Easing.CubicOut);
			await Task.WhenAll(fadeOutX, shrinkX, fadeInDot, growDot);
		};
		closeBtn.GestureRecognizers.Add(closePointer);

		var closeTap = new TapGestureRecognizer();
		closeTap.Tapped += async (s, e) =>
		{
			await SendCloseWorkflowAsync(index);
		};
		closeBtn.GestureRecognizers.Add(closeTap);

		var row = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = GridLength.Star },
				new ColumnDefinition { Width = GridLength.Auto }
			},
			HeightRequest = tabHeight,
			VerticalOptions = LayoutOptions.Fill,
			HorizontalOptions = LayoutOptions.Fill,
			TranslationY = 1,
			RowDefinitions =
			{
				new RowDefinition { Height = GridLength.Star }
			}
		};
		row.Add(nameAreaGrid, 0, 0);
		row.Add(closeBtn, 1, 0);
		border.Content = row;

		if (!_isBulkProcessing)
		{
			var flyout = new MenuFlyout();
			var actionState = WorkflowActionCatalog.CreateState(hasFile, modified, bookmarked);
			foreach (var item in WorkflowActionCatalog.BuildTabContextMenuItems(actionState))
			{
				if (item.StartsNewSection)
				{
					flyout.Add(new MenuFlyoutSeparator());
				}

				var menuItem = new MenuFlyoutItem { Text = item.Label, IsEnabled = item.IsEnabled };
				menuItem.Clicked += async (s, e) => await ExecuteWorkflowActionAsync(item.Kind, index, name);
				flyout.Add(menuItem);
			}

			var closeItem = new MenuFlyoutItem { Text = "Close Tab" };
			closeItem.Clicked += async (s, e) => await CloseWorkflowsAsync([index]);
			flyout.Add(closeItem);

			int visualIndexAtCreation = _visualOrder.IndexOf(index);
			int totalVisualTabs = _visualOrder.Count;

			var closeLeft = new MenuFlyoutItem { Text = "Close Tabs to Left", IsEnabled = visualIndexAtCreation > 0 };
			closeLeft.Clicked += async (s, e) =>
			{
				int currentVisualIndex = _visualOrder.IndexOf(index);
				var toClose = new List<int>();
				for (int i = 0; i < currentVisualIndex; i++)
				{
					toClose.Add(_visualOrder[i]);
				}

				await CloseWorkflowsAsync(toClose);
			};
			flyout.Add(closeLeft);

			var closeRight = new MenuFlyoutItem { Text = "Close Tabs to Right", IsEnabled = visualIndexAtCreation < totalVisualTabs - 1 };
			closeRight.Clicked += async (s, e) =>
			{
				int currentVisualIndex = _visualOrder.IndexOf(index);
				var toClose = new List<int>();
				for (int i = currentVisualIndex + 1; i < _visualOrder.Count; i++)
				{
					toClose.Add(_visualOrder[i]);
				}

				await CloseWorkflowsAsync(toClose);
			};
			flyout.Add(closeRight);

			var closeOthers = new MenuFlyoutItem { Text = "Close Other Tabs", IsEnabled = totalVisualTabs > 1 };
			closeOthers.Clicked += async (s, e) =>
			{
				var toClose = new List<int>();
				foreach (int visualIndex in _visualOrder)
				{
					if (visualIndex != index)
					{
						toClose.Add(visualIndex);
					}
				}

				await CloseWorkflowsAsync(toClose);
			};
			flyout.Add(closeOthers);

			FlyoutBase.SetContextFlyout(border, flyout);
		}

		return border;
	}

	private async Task ExecuteWorkflowActionAsync(WorkflowActionKind kind, int index, string workflowName)
	{
		var workflow = _workflows.FirstOrDefault(wf => wf.OriginalIndex == index);
		if (workflow != null)
		{
			await _executeWorkflowActionAsync(workflow, kind);
			return;
		}

		if (kind == WorkflowActionKind.Bookmark)
		{
			await _toggleBookmarkAsync(workflowName);
			return;
		}

		string? action = GetWorkflowMenuAction(kind);
		if (!string.IsNullOrWhiteSpace(action))
		{
			await SendTabActionAsync(action, index);
		}
	}

	private void SynchronizeTrackedWorkflowAliases(IReadOnlyList<WorkflowTabInfo> workflows)
	{
		foreach (var workflow in workflows)
		{
			string relativePath = NormalizeWorkflowRelativePath(workflow.RelativePath);
			if (!string.IsNullOrWhiteSpace(workflow.Name) && !string.IsNullOrWhiteSpace(relativePath))
			{
				_trackedWorkflowPathsByName[workflow.Name] = relativePath;
			}
		}

		var canonicalNamesByPath = workflows
			.Select(workflow => new
			{
				workflow.Name,
				Path = NormalizeWorkflowRelativePath(workflow.Path)
			})
			.Where(entry => !string.IsNullOrWhiteSpace(entry.Name) && _knownWorkflowFiles.Contains(entry.Path))
			.GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(
				group => group.Key,
				group => group.Select(entry => entry.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
				StringComparer.OrdinalIgnoreCase);
		foreach (var entry in _trackedWorkflowPathsByName.ToArray())
		{
			string normalizedPath = NormalizeWorkflowRelativePath(entry.Value);
			if (!canonicalNamesByPath.TryGetValue(normalizedPath, out var canonicalNames) ||
				canonicalNames.Contains(entry.Key))
			{
				continue;
			}

			_trackedWorkflowPathsByName.Remove(entry.Key);
			_workflowDisplayNameOverridesByName.Remove(entry.Key);
		}
	}

	private static string? GetWorkflowMenuAction(WorkflowActionKind kind)
	{
		return kind switch
		{
			WorkflowActionKind.Rename => WorkflowMenuActions.Rename,
			WorkflowActionKind.Duplicate => WorkflowMenuActions.Duplicate,
			WorkflowActionKind.Save => WorkflowMenuActions.Save,
			WorkflowActionKind.SaveAs => WorkflowMenuActions.SaveAs,
			WorkflowActionKind.Export => WorkflowMenuActions.Export,
			WorkflowActionKind.ExportApi => WorkflowMenuActions.ExportApi,
			WorkflowActionKind.Clear => WorkflowMenuActions.Clear,
			WorkflowActionKind.Delete => WorkflowMenuActions.Delete,
			_ => null,
		};
	}

	private Task SendCloseWorkflowAsync(int index)
	{
		MarkPendingClosedWorkflow(index);
		return SendNexusActionAsync(BridgeActions.CloseWorkflow, $"{{index: {index}}}");
	}

	private Task SendSwitchWorkflowAsync(int index)
		=> SendNexusActionAsync(BridgeActions.SwitchWorkflow, $"{{index: {index}}}");

	private void MarkPendingClosedWorkflow(int index)
	{
		var workflow = _workflows.FirstOrDefault(wf => wf.OriginalIndex == index);
		if (workflow == null)
		{
			return;
		}

		string normalized = NormalizeWorkflowRelativePath(workflow.RelativePath);
		if (!string.IsNullOrWhiteSpace(normalized))
		{
			_pendingClosedWorkflowPaths.Add(normalized);
		}
	}

	private void ClearCompletedPendingClosedWorkflows(IReadOnlyList<WorkflowTabInfo> workflows)
	{
		if (_pendingClosedWorkflowPaths.Count == 0)
		{
			return;
		}

		var openPaths = workflows
			.Select(workflow => NormalizeWorkflowRelativePath(workflow.RelativePath))
			.Where(path => !string.IsNullOrWhiteSpace(path))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		_pendingClosedWorkflowPaths.RemoveWhere(path => !openPaths.Contains(path));
	}

	private Task SendNexusActionAsync(string action, string payloadJson = "{}")
		=> _executeScriptAsync($"if(window.NexusAction) window.NexusAction('{action}', {payloadJson});");

	internal static string NormalizeWorkflowRelativePath(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		string normalized = value.Trim().Replace('\\', '/');
		if (normalized.StartsWith("/"))
		{
			normalized = normalized.TrimStart('/');
		}

		if (normalized.StartsWith("user/default/workflows/", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized["user/default/".Length..];
		}

		if (!normalized.StartsWith("workflows/", StringComparison.OrdinalIgnoreCase))
		{
			normalized = $"workflows/{normalized}";
		}

		if (!normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
		{
			normalized += ".json";
		}

		return normalized;
	}

	internal static string StripWorkflowPrefix(string relativePath)
	{
		string normalized = NormalizeWorkflowRelativePath(relativePath);
		return normalized.StartsWith("workflows/", StringComparison.OrdinalIgnoreCase)
			? normalized["workflows/".Length..]
			: normalized;
	}

	internal static bool TryRemapWorkflowRelativePath(
		string currentRelativePath,
		string oldRelativePath,
		string newRelativePath,
		bool isDirectory,
		out string remappedRelativePath)
	{
		remappedRelativePath = NormalizeWorkflowRelativePath(currentRelativePath);
		if (string.IsNullOrWhiteSpace(remappedRelativePath))
		{
			return false;
		}

		if (!isDirectory)
		{
			string oldFilePath = NormalizeWorkflowRelativePath(oldRelativePath);
			string newFilePath = NormalizeWorkflowRelativePath(newRelativePath);
			if (!string.Equals(remappedRelativePath, oldFilePath, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			remappedRelativePath = newFilePath;
			return true;
		}

		string oldDirectoryPath = NormalizeWorkflowDirectoryRelativePath(oldRelativePath);
		string newDirectoryPath = NormalizeWorkflowDirectoryRelativePath(newRelativePath);
		string oldPrefix = $"{oldDirectoryPath}/";
		if (!remappedRelativePath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		remappedRelativePath = $"{newDirectoryPath}{remappedRelativePath[oldDirectoryPath.Length..]}";
		return true;
	}

	internal static string NormalizeWorkflowDirectoryRelativePath(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		string normalized = value.Trim().Replace('\\', '/').Trim('/');
		if (normalized.StartsWith("user/default/workflows/", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized["user/default/".Length..];
		}

		if (!normalized.StartsWith("workflows/", StringComparison.OrdinalIgnoreCase))
		{
			normalized = $"workflows/{normalized}";
		}

		return normalized.TrimEnd('/');
	}

	internal static string CreateRootRelativePath(string workflowName)
		=> NormalizeWorkflowRelativePath($"{workflowName}.json");

	private string ResolveWorkflowRelativePath(string path, out bool hasFile)
	{
		string normalizedPath = NormalizeWorkflowRelativePath(path);
		if (!string.IsNullOrWhiteSpace(normalizedPath) && _knownWorkflowFiles.Contains(normalizedPath))
		{
			hasFile = true;
			return normalizedPath;
		}

		hasFile = false;
		return string.Empty;
	}

	private string ResolveWorkflowDisplayName(string name)
	{
		return _workflowDisplayNameOverridesByName.TryGetValue(name, out string? displayName) &&
			!string.IsNullOrWhiteSpace(displayName)
				? displayName
				: name;
	}

	private void SetDisplayNameOverride(string workflowName, string relativePath)
	{
		string displayName = System.IO.Path.GetFileNameWithoutExtension(StripWorkflowPrefix(relativePath));
		if (string.IsNullOrWhiteSpace(displayName) ||
			string.Equals(displayName, workflowName, StringComparison.Ordinal))
		{
			_workflowDisplayNameOverridesByName.Remove(workflowName);
			return;
		}

		_workflowDisplayNameOverridesByName[workflowName] = displayName;
	}

	private Border CreateDropdownItem(string name, bool active, bool modified, int index)
	{
		Color normalText = active ? DropdownActiveTextColor : DropdownInactiveTextColor;
		var border = new Border
		{
			BackgroundColor = Colors.Transparent,
			Stroke = DropdownStrokeColor,
			StrokeThickness = 0.5,
			StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
			Padding = new Thickness(12, 10),
			Content = new Label
			{
				Text = modified ? $"{name} *" : name,
				TextColor = normalText,
				FontSize = 13,
				FontAttributes = active ? FontAttributes.Bold : FontAttributes.None
			}
		};

		if (border.Content is not Label label)
		{
			return border;
		}

		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (s, e) =>
		{
			border.BackgroundColor = DropdownHoverBackgroundColor;
			border.Stroke = DropdownHoverStrokeColor;
			label.TextColor = active ? DropdownActiveHoverTextColor : DropdownInactiveHoverTextColor;
			_ = border.ScaleToAsync(DropdownHoverScale, HoverAnimationLength, Easing.CubicOut);
		};
		pointer.PointerExited += (s, e) =>
		{
			border.BackgroundColor = Colors.Transparent;
			border.Stroke = DropdownStrokeColor;
			label.TextColor = normalText;
			_ = border.ScaleToAsync(1, HoverAnimationLength, Easing.CubicIn);
		};
		border.GestureRecognizers.Add(pointer);

		var tap = new TapGestureRecognizer();
		tap.Tapped += async (s, e) =>
		{
			_hideDropdown();

			if (_visualOrder.Contains(index))
			{
				_visualOrder.Remove(index);
				_visualOrder.Insert(0, index);
			}

			await SendSwitchWorkflowAsync(index);
		};
		border.GestureRecognizers.Add(tap);

		return border;
	}

	private void EnsureActiveTabRemainsVisible(int workflowCount, int currentActiveIndex, int visibleCapacity)
	{
		if (currentActiveIndex < 0)
		{
			return;
		}

		int currentVisualIndex = _visualOrder.IndexOf(currentActiveIndex);

		if (currentVisualIndex < visibleCapacity)
		{
			return;
		}

		_visualOrder.Remove(currentActiveIndex);
		int targetPos = Math.Max(0, visibleCapacity - 1);
		_visualOrder.Insert(targetPos, currentActiveIndex);
	}

	private static double NormalizeUnifiedTabWidth(double unifiedTabWidth, int totalTabs, ref int row1Count, ref int overflowCount)
	{
		if (double.IsNaN(unifiedTabWidth) || unifiedTabWidth <= 0)
		{
			row1Count = 0;
			overflowCount = totalTabs;
			return 0;
		}

		return Math.Floor(Math.Max(0, unifiedTabWidth));
	}

	private void AddPrimaryTabColumns(int row1Count, double unifiedTabWidth, int overflowCount)
	{
		for (int i = 0; i < row1Count; i++)
		{
			double columnWidth = Math.Floor(unifiedTabWidth);
			if (double.IsNaN(columnWidth) || columnWidth < 0)
			{
				columnWidth = 0;
			}

			_header.AddTabColumn(new GridLength(columnWidth));
		}

		if (overflowCount > 0)
		{
			_header.AddTabColumn(GridLength.Star);
			_header.AddTabColumn(GridLength.Auto); // [+]
			_header.AddTabColumn(GridLength.Auto); // [...]
		}
		else
		{
			_header.AddTabColumn(GridLength.Auto); // [+]
		}
	}

	private void PlaceTabUtilityButtons(int row1Count, int overflowCount)
	{
		if (overflowCount > 0)
		{
			// Order: [Tabs] [Spacer] [+] [...]
			_header.AddTabSurfaceChild(_btnAddTab, row1Count + 1);
			_btnOverflow.IsVisible = true;
			_header.AddTabSurfaceChild(_btnOverflow, row1Count + 2);
		}
		else
		{
			// Order: [Tabs] [+]
			_header.AddTabSurfaceChild(_btnAddTab, row1Count);
		}
	}

	private void InvalidateTabLayouts()
	{
		_header.InvalidateTabSurface();
	}

	private void LogTabRebuild(int totalTabs, int row1Count, int overflowCount)
	{
		var now = DateTime.UtcNow;
		if ((now - _lastTabRebuildLogUtc).TotalSeconds < 2)
		{
			return;
		}

		_lastTabRebuildLogUtc = now;
		NexusLog.Info($"Tabs rebuilt: {totalTabs} tab(s) [Visible:{row1Count}, Menu:{overflowCount}]");
	}
}
