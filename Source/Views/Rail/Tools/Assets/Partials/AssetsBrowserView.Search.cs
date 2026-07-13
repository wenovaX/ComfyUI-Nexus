using ComfyUI_Nexus.AssetHub;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

public partial class AssetsBrowserView
{
	private string _activeSearchQuery = string.Empty;

	private bool IsSearchActive => !string.IsNullOrWhiteSpace(_activeSearchQuery);
	private bool HasSearchText => !string.IsNullOrWhiteSpace(NormalizeSearchQuery(RailSearchEntry?.Text));

	private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
	{
		RailSearchClearButton.IsVisible = !string.IsNullOrWhiteSpace(e.NewTextValue);
		_ = RefreshWorkflowBookmarksSectionAsync();
		_ = RefreshSearchResultsAsync();
	}

	private void OnClearSearchTapped(object? sender, TappedEventArgs e)
	{
		RailSearchEntry.Text = string.Empty;
		_ = RefreshWorkflowBookmarksSectionAsync();
		ExitSearchMode();
	}

	private Task RefreshSearchResultsAsync(bool immediate = false)
	{
		string query = NormalizeSearchQuery(RailSearchEntry.Text);
		if (string.IsNullOrWhiteSpace(query))
		{
			ExitSearchMode();
			return Task.CompletedTask;
		}

		if (string.IsNullOrWhiteSpace(_rootPath) || !Directory.Exists(_rootPath))
		{
			ExitSearchMode();
			return Task.CompletedTask;
		}

		RailSearchStatusLabel.IsVisible = true;
		RailSearchStatusLabel.Text = LocalizationManager.Text("views.rail.tools.assets.assets_browser_view.searching_current_location");
		string rootPath = _rootPath;
		bool recursive = CurrentUsesRecursiveSearch;
		bool filterModelFilesOnly = CurrentFiltersModelFilesOnly;
		bool includeDirectories = CurrentSearchIncludesDirectories;
		_latestOperations.Request("search", async lease =>
		{
			try
			{
				if (!immediate && !await lease.WaitForAsync(AssetBrowserOptions.SearchDebounceDelay))
				{
					return;
				}

				var results = await _assetHubService.SearchAsync(
					rootPath,
					query,
					recursive,
					filterModelFilesOnly,
					includeDirectories,
					lease.LifecycleToken);

				if (!lease.IsCurrent)
				{
					return;
				}

				UiThread.TryBeginInvoke(() =>
				{
					if (lease.IsCurrent)
					{
						ApplySearchResults(query, results);
					}
				}, "ASSET_SEARCH");
			}
			catch (OperationCanceledException) when (lease.LifecycleToken.IsCancellationRequested)
			{
			}
			catch (Exception ex)
			{
				UiThread.TryBeginInvoke(() =>
				{
					if (!lease.IsCurrent)
					{
						return;
					}

					System.Threading.Interlocked.Increment(ref _searchRenderVersion);
					_activeSearchQuery = query;
					_searchResults.Clear();
					RailTreeVirtualList.IsVisible = false;
					RailTreeEmptyLabel.IsVisible = false;
					RailSearchVirtualList.IsVisible = false;
					ReturnAllSearchResultRows();
					RailSearchEmptyLabel.Text = LocalizationManager.Format("views.rail.tools.assets.assets_browser_view.search_failed_message", ex.Message);
					RailSearchEmptyLabel.IsVisible = true;
					RailSearchStatusLabel.IsVisible = true;
					RailSearchStatusLabel.Text = LocalizationManager.Text("views.rail.tools.assets.assets_browser_view.search_failed");
				}, "ASSET_SEARCH");
			}
		});

		return Task.CompletedTask;
	}

	private void ApplySearchResults(string query, IReadOnlyList<AssetHubItem> results)
	{
		_activeSearchQuery = query;
		RailSearchVirtualList.BeginBatchUpdate();
		try
		{
			_searchResults.Clear();
			foreach (AssetHubItem result in results)
			{
				_searchResults.Add(result);
			}
		}
		finally
		{
			RailSearchVirtualList.EndBatchUpdate();
		}

		RailTreeVirtualList.IsVisible = false;
		RailTreeEmptyLabel.IsVisible = false;
		RailSearchVirtualList.IsVisible = results.Count > 0;
		RailSearchStatusLabel.IsVisible = true;
		RailSearchStatusLabel.Text = LocalizationManager.Format("views.rail.tools.assets.assets_browser_view.search_results_count", results.Count);

		RenderSearchResults();
	}

	private void ExitSearchMode()
	{
		System.Threading.Interlocked.Increment(ref _searchRenderVersion);
		_latestOperations.Invalidate("search");
		_activeSearchQuery = string.Empty;
		_searchResults.Clear();
		ReturnAllSearchResultRows();
		RailSearchVirtualList.IsVisible = false;
		RailSearchEmptyLabel.IsVisible = false;
		RailSearchEmptyLabel.Text = LocalizationManager.Text("views.rail.tools.assets.assets_browser_view.no_matching_assets_found");
		RailSearchStatusLabel.IsVisible = false;
		RailSearchStatusLabel.Text = string.Empty;
		RailTreeVirtualList.IsVisible = !RailTreeEmptyLabel.IsVisible;
	}

	private void RenderSearchResults()
	{
		int renderVersion = System.Threading.Interlocked.Increment(ref _searchRenderVersion);
		_ = RenderSearchResultsAsync(renderVersion);
	}

	private async Task RenderSearchResultsAsync(int renderVersion)
	{
		try
		{
			ReturnAllSearchResultRows();

			if (_searchResults.Count == 0)
			{
				RailSearchEmptyLabel.IsVisible = true;
				return;
			}

			RailSearchEmptyLabel.IsVisible = false;
			RailSearchVirtualList.IsVisible = true;
			await RailSearchVirtualList.ScrollToTopAsync(animated: false);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[ASSETS] Failed to render search results");
			if (renderVersion == _searchRenderVersion)
			{
				ReturnAllSearchResultRows();
				RailSearchEmptyLabel.Text = $"Search render failed: {ex.Message}";
				RailSearchEmptyLabel.IsVisible = true;
				RailSearchStatusLabel.IsVisible = true;
				RailSearchStatusLabel.Text = "Search render failed";
			}
		}
	}

	private void RefreshSearchResultsSelectionState()
	{
		if (!IsSearchActive)
		{
			return;
		}

		RenderSearchResults();
	}

	private void ReturnAllSearchResultRows()
	{
		_searchRowMap.Clear();
	}

	private Grid CreateSearchResultRow()
	{
		var row = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = GridLength.Star },
			},
			Padding = new Thickness(10, 6, 10, 6),
			BackgroundColor = Colors.Transparent,
			RowDefinitions =
			{
				new RowDefinition { Height = GridLength.Auto },
				new RowDefinition { Height = GridLength.Auto },
			},
			ColumnSpacing = 10,
		};

		var iconHost = new Grid
		{
			WidthRequest = 18,
			HeightRequest = 18,
			VerticalOptions = LayoutOptions.Start,
			Margin = new Thickness(4, 1, 0, 0),
			InputTransparent = true,
		};

		var nameLabel = new Label
		{
			FontSize = 12,
			FontAttributes = FontAttributes.Bold,
			LineBreakMode = LineBreakMode.TailTruncation,
			InputTransparent = true,
		};

		var pathLabel = new Label
		{
			TextColor = Color.FromArgb("#5f7892"),
			FontSize = 10,
			LineBreakMode = LineBreakMode.TailTruncation,
			InputTransparent = true,
		};

		row.Add(iconHost, 0, 0);
		Grid.SetRowSpan(iconHost, 2);
		row.Add(nameLabel, 1, 0);
		row.Add(pathLabel, 1, 1);

		WireSearchResultRow(row);
		return row;
	}

	private void WireSearchResultRow(Grid row)
	{
		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (s, e) =>
		{
			if (TryGetBoundSearchItem(s, out var item) &&
				_searchRowMap.TryGetValue(item.FullPath, out var searchRow))
			{
				searchRow.BackgroundColor = _selection.Contains(item.FullPath)
					? RowSelectedColor
					: RowHoverColor;
				BeginModelThumbnailHover(item.FullPath, item.Name, item.Type == AssetHubItemType.Directory, searchRow);
			}
		};
		pointer.PointerExited += (s, e) =>
		{
			if (TryGetBoundSearchItem(s, out var item) &&
				_searchRowMap.TryGetValue(item.FullPath, out var searchRow))
			{
				searchRow.BackgroundColor = _selection.Contains(item.FullPath)
					? RowSelectedColor
					: Colors.Transparent;
				EndModelThumbnailHover(item.FullPath, item.Type == AssetHubItemType.Directory);
			}
		};
		row.GestureRecognizers.Add(pointer);

		var selectTap = new TapGestureRecognizer { NumberOfTapsRequired = 1 };
		selectTap.Tapped += (s, e) =>
		{
			_suppressNextAssetListSurfaceSelectionClear = true;
			if (TryGetBoundSearchItem(s, out var item))
			{
				SelectSearchItemFromInput(item);
			}
		};
		row.GestureRecognizers.Add(selectTap);

		PlatformManager.Current.Interactions.AttachDoubleTap(row, async () =>
		{
			if (!TryGetBoundSearchItem(row, out var item))
			{
				return;
			}

			SelectPath(item.FullPath);
			await OpenSearchResultAsync(item);
		});

		var pseudoDrag = new PanGestureRecognizer();
		AssetOpenRequest? pendingPseudoRequest = null;
		pseudoDrag.PanUpdated += (s, e) =>
		{
			if (!TryGetBoundSearchItem(s, out var item))
			{
				return;
			}

			var dragRequest = CreateDragRequest(item.FullPath);
			if (!ShouldUsePseudoIntentDrag(dragRequest))
			{
				return;
			}

			switch (e.StatusType)
			{
				case GestureStatus.Started:
					if (!ShouldAllowAssetDrag(dragRequest, item.Type == AssetHubItemType.Directory))
					{
						pendingPseudoRequest = null;
						return;
					}

					if (!_selection.Contains(item.FullPath))
					{
						SelectPath(item.FullPath);
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
			if (!TryGetBoundSearchItem(s, out var item))
			{
				return;
			}

			var dragRequest = CreateDragRequest(item.FullPath);
			if (ShouldUsePseudoIntentDrag(dragRequest))
			{
				e.Cancel = true;
				return;
			}

			if (!_selection.Contains(item.FullPath))
			{
				SelectPath(item.FullPath);
			}

			var selectedPaths = GetSelectedExistingPaths();
			if (selectedPaths.Count == 0)
			{
				return;
			}

			e.Data.Properties["name"] = item.Name;
			e.Data.Properties["kind"] = item.Type == AssetHubItemType.Directory ? "directory" : "file";
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
				e.Data.Properties["path"] = item.FullPath;
				e.Data.Properties["paths"] = selectedPaths;
			}
			else
			{
				e.Data.Properties["assetPath"] = item.FullPath;
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

		var dropGesture = new DropGestureRecognizer { AllowDrop = true };
		dropGesture.DragOver += (s, e) =>
		{
			if (!TryGetBoundSearchItem(s, out var item))
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
			if (!CanAcceptFileDrop(e.Data))
			{
				e.AcceptedOperation = Microsoft.Maui.Controls.DataPackageOperation.None;
				return;
			}

			string targetDirectory = ResolveSearchDropTarget(item);
			if (_currentDragPaths != null && !IsDropValid(_currentDragPaths, targetDirectory))
			{
				e.AcceptedOperation = Microsoft.Maui.Controls.DataPackageOperation.None;
				return;
			}

			e.AcceptedOperation = GetAcceptedFileDropOperation(e.Data);
			if (_searchRowMap.TryGetValue(item.FullPath, out var searchRow))
			{
				searchRow.BackgroundColor = RowHoverColor;
			}
		};
		dropGesture.DragLeave += (s, e) =>
		{
			if (TryGetBoundSearchItem(s, out var item) &&
				_searchRowMap.TryGetValue(item.FullPath, out var searchRow))
			{
				searchRow.BackgroundColor = _selection.Contains(item.FullPath)
					? RowSelectedColor
					: Colors.Transparent;
			}
		};
		dropGesture.Drop += async (s, e) =>
		{
			if (!TryGetBoundSearchItem(s, out var item))
			{
				return;
			}

			if (IsAssetIntentOnlyDrag(e.Data))
			{
				return;
			}
			if (!CanAcceptFileDrop(e.Data))
			{
				return;
			}

			string destination = ResolveSearchDropTarget(item);
			var droppedPaths = await TryGetDroppedPathsWithActiveFallbackAsync(e);
			if (droppedPaths.Count > 0)
			{
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

			if (_searchRowMap.TryGetValue(item.FullPath, out var searchRow))
			{
				searchRow.BackgroundColor = _selection.Contains(item.FullPath)
					? RowSelectedColor
					: Colors.Transparent;
			}
		};
		row.GestureRecognizers.Add(dropGesture);
	}

	private void UpdateSearchResultRow(Grid row, AssetHubItem item)
	{
		row.BindingContext = item;
		row.BackgroundColor = _selection.Contains(item.FullPath) ? RowSelectedColor : Colors.Transparent;
		row.Opacity = _clipboard.ShouldDim(item.FullPath) ? 0.46 : 1;
		ApplyAssetRowTooltip(row, item.Name, item.FullPath, item.Type == AssetHubItemType.Directory);

		if (row.Children[0] is Grid iconHost)
		{
			iconHost.Children.Clear();
			iconHost.Add(CreateNodeIcon(CreatePseudoNode(item)));
		}

		if (row.Children[1] is Label nameLabel)
		{
			nameLabel.Text = item.Name;
			nameLabel.TextColor = item.Type == AssetHubItemType.Directory
				? Color.FromArgb("#e1e8f0")
				: Color.FromArgb("#d0dae4");
		}

		if (row.Children[2] is Label pathLabel)
		{
			pathLabel.Text = GetSearchResultSubtitle(item);
		}

		AttachSearchResultContextMenu(row, item);
	}

	private static void ResetSearchResultRow(Grid row)
	{
		row.BindingContext = null;
		row.Opacity = 1;
		row.TranslationX = 0;
		row.TranslationY = 0;
		row.BackgroundColor = Colors.Transparent;
		row.ClearValue(ToolTipProperties.TextProperty);
		FlyoutBase.SetContextFlyout(row, null);
	}

	private static bool TryGetBoundSearchItem(object? source, out AssetHubItem item)
	{
		if (source is BindableObject bindable &&
			bindable.BindingContext is AssetHubItem boundItem)
		{
			item = boundItem;
			return true;
		}

		item = null!;
		return false;
	}

	private RailTreeNode CreatePseudoNode(AssetHubItem item)
	{
		return new RailTreeNode
		{
			Name = item.Name,
			FullPath = item.FullPath,
			IsDirectory = item.Type == AssetHubItemType.Directory,
			Depth = 0,
			IconKey = item.Type == AssetHubItemType.Directory
				? "folder"
				: GetIconForEntry(new RailTreeEntry(item.Name, item.FullPath, false)),
		};
	}

	private string GetSearchResultSubtitle(AssetHubItem item)
	{
		if (string.IsNullOrWhiteSpace(item.RelativePath))
		{
			return item.Type == AssetHubItemType.Directory ? "Folder" : "Current location";
		}

		string relativeDirectory = item.Type == AssetHubItemType.Directory
			? item.RelativePath
			: (Path.GetDirectoryName(item.RelativePath) ?? ".");

		return relativeDirectory
			.Replace(Path.DirectorySeparatorChar, '/')
			.Replace(Path.AltDirectorySeparatorChar, '/');
	}

	private void SelectSearchItemFromInput(AssetHubItem item)
	{
		bool ctrl = PlatformManager.Current.Keyboard.IsCtrlPressed();
		bool shift = PlatformManager.Current.Keyboard.IsShiftPressed();

		if (shift)
		{
			SelectPathRange(item.FullPath, _searchResults.Select(result => result.FullPath).ToList());
			return;
		}

		if (ctrl)
		{
			TogglePathSelection(item.FullPath);
			return;
		}

		SelectPath(item.FullPath);
	}

	private async Task OpenSearchResultAsync(AssetHubItem item)
	{
		if (item.Type == AssetHubItemType.Directory)
		{
			RailSearchEntry.Text = string.Empty;
			SetRootPath(item.FullPath);
			return;
		}

		var request = CreateOpenRequest(item.FullPath);
		if (request.Kind is AssetOpenKind.WorkflowJson or AssetOpenKind.ModelFile)
		{
			FileOpenRequested?.Invoke(this, request);
			return;
		}

		await OpenInOsAsync(item.FullPath);
	}

	private string ResolveSearchDropTarget(AssetHubItem item)
	{
		if (item.Type == AssetHubItemType.Directory)
		{
			return item.FullPath;
		}

		return Path.GetDirectoryName(item.FullPath) ?? _rootPath;
	}

	private string NormalizeSearchQuery(string? text)
		=> text?.Trim() ?? string.Empty;

	private static string GetModelsRootPath()
		=> ComfyPathResolver.ResolveModelsRootPath();
}
