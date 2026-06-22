using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

public partial class AssetsBrowserView
{
	private bool? _currentDragIsFolder = null;
	private bool _isValidatingDrag = false;
	private bool _dropHandledByRow = false;
	private IReadOnlyList<string>? _currentDragPaths = null;

	private void InitializeChromeState()
	{
		RenderFixedCards();
		UpdateCurrentSectionState();
		RenderBookmarks();
	}

	private void RenderFixedCards()
	{
		RailFixedCardsGrid.Children.Clear();
		RailFixedCardsGrid.RowDefinitions.Clear();

		var cards = GetFixedProfiles().ToList();
		if (cards.Count == 0)
		{
			RailFixedCardsSection.IsVisible = false;
			return;
		}

		RailFixedCardsSection.IsVisible = true;

		int columns = 2;
		int rows = (int)Math.Ceiling(cards.Count / (double)columns);
		for (int rowIndex = 0; rowIndex < rows; rowIndex++)
		{
			RailFixedCardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		}

		for (int index = 0; index < cards.Count; index++)
		{
			var card = cards[index];
			int row = index / columns;
			int column = index % columns;
			RailFixedCardsGrid.Add(CreateFixedCardView(card), column, row);
		}
	}

	private View CreateFixedCardView(AssetRootProfile card)
	{
		bool isActive = string.Equals(_rootPath, card.Path, StringComparison.OrdinalIgnoreCase);
		var border = new Border
		{
			BackgroundColor = isActive ? FixedCardActiveBackgroundColor : FixedCardInactiveBackgroundColor,
			Stroke = isActive ? Color.FromArgb(card.AccentColor) : FixedCardInactiveStrokeColor,
			StrokeThickness = isActive ? 1.5 : 1,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
			Padding = new Thickness(8, 7),
			Margin = Thickness.Zero,
			MinimumHeightRequest = 38,
		};

		var layout = new HorizontalStackLayout
		{
			Spacing = 7,
			VerticalOptions = LayoutOptions.Center,
		};

		layout.Children.Add(new Image
		{
			Source = card.IconSource,
			WidthRequest = 15,
			HeightRequest = 15,
			VerticalOptions = LayoutOptions.Center,
		});

		layout.Children.Add(new Label
		{
			Text = card.Label,
			TextColor = isActive ? NexusColors.White : FixedCardInactiveTextColor,
			FontSize = 11,
			FontAttributes = FontAttributes.Bold,
			LineBreakMode = LineBreakMode.TailTruncation,
			VerticalOptions = LayoutOptions.Center,
		});

		border.Content = layout;
		ToolTipProperties.SetText(border, $"{card.Subtitle}\n{card.Path}");

		var tap = new TapGestureRecognizer();
		tap.Tapped += (s, e) => SetRootProfile(card);
		border.GestureRecognizers.Add(tap);

		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (s, e) =>
		{
			if (!string.Equals(_rootPath, card.Path, StringComparison.OrdinalIgnoreCase))
			{
				border.BackgroundColor = FixedCardHoverBackgroundColor;
				border.Stroke = Color.FromArgb(card.AccentColor);
			}
		};
		pointer.PointerExited += (s, e) =>
		{
			bool stillActive = string.Equals(_rootPath, card.Path, StringComparison.OrdinalIgnoreCase);
			border.BackgroundColor = stillActive ? FixedCardActiveBackgroundColor : FixedCardInactiveBackgroundColor;
			border.Stroke = stillActive ? Color.FromArgb(card.AccentColor) : FixedCardInactiveStrokeColor;
		};
		border.GestureRecognizers.Add(pointer);

		return border;
	}

	private IEnumerable<AssetRootProfile> GetFixedProfiles()
	{
		string comfyRoot = ComfyPathResolver.ResolveConfiguredComfyPath();
		return AssetRootProfileProvider.GetFixedProfiles(comfyRoot, _fixedWorkflowsPath);
	}

	private AssetRootProfile ResolveProfileForPath(string? path)
	{
		string comfyRoot = ComfyPathResolver.ResolveConfiguredComfyPath();
		return AssetRootProfileProvider.ResolveForPath(path, comfyRoot, _fixedWorkflowsPath);
	}

	private void UpdateCurrentSectionState()
	{
		RailCurrentBodyStack.IsVisible = _isCurrentSectionExpanded;
	}

	private void OnAddBookmarkPointerEntered(object? sender, PointerEventArgs e)
	{
		RailAddBookmarkLabel.TextColor = NexusColors.White;
		RailAddBookmarkLabel.Opacity = 1.0;
	}

	private void OnAddBookmarkPointerExited(object? sender, PointerEventArgs e)
	{
		RailAddBookmarkLabel.TextColor = BookmarkActionColor;
		RailAddBookmarkLabel.Opacity = 0.8;
	}

	private async void OnAddBookmarkClicked(object? sender, EventArgs e)
	{
		try
		{
			var result = await PlatformManager.Current.FilePicker.PickFolderAsync(LocalizationManager.Text("asset_bookmark.add_asset_bookmark"));
			if (!result.IsSupported)
			{
				if (GetHostPage() is { } hostPage)
				{
					await hostPage.DisplayAlertAsync(
						LocalizationManager.Text("core_link.platform_not_supported_title"),
						result.Message ?? LocalizationManager.Text("core_link.folder_selection_not_supported"),
						LocalizationManager.Text("common.ok"));
				}

				return;
			}

			if (!result.IsSuccess)
			{
				if (!string.IsNullOrWhiteSpace(result.Message))
				{
					NexusLog.Warning($"Failed to pick folder: {result.Message}");
				}

				return;
			}

			string? path = result.Value;
			if (!string.IsNullOrWhiteSpace(path))
			{
				if (!IsProtectedBookmark(path))
				{
					if (!_bookmarkedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
					{
						_bookmarkedPaths.Add(path);
						_bookmarkedPaths.Sort(StringComparer.OrdinalIgnoreCase);
						_bookmarksDirty = true;
						await PersistBookmarksAsync();
					}

					SetRootPath(path);
				}
			}
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "Failed to pick folder");
		}
	}

	private void RenderBookmarks()
	{
		_isInternalPickerUpdate = true;
		try
		{
			// Find if current root matches any bookmark
			bool hasSelection = _bookmarkedPaths.Any(p => string.Equals(p, _rootPath, StringComparison.OrdinalIgnoreCase));

			// Check current state of the picker to avoid redundant rebuilds
			bool currentHasPlaceholder = _activeBookmarkPaths.Count > 0 && string.IsNullOrEmpty(_activeBookmarkPaths[0]);
			bool needsPlaceholder = !hasSelection;

			if (_bookmarksDirty || currentHasPlaceholder != needsPlaceholder)
			{
				RailBookmarksPicker.Items.Clear();
				_activeBookmarkPaths.Clear();

				var bookmarks = _bookmarkedPaths
					.Where(Directory.Exists)
					.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
					.ToList();

				if (bookmarks.Count == 0 || needsPlaceholder)
				{
					RailBookmarksPicker.Items.Add("Select a bookmark...");
					_activeBookmarkPaths.Add(string.Empty);
				}

				foreach (var bookmark in bookmarks)
				{
					_activeBookmarkPaths.Add(bookmark);
					string name = Path.GetFileName(bookmark.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
					if (string.IsNullOrWhiteSpace(name))
					{
						name = bookmark;
					}
					RailBookmarksPicker.Items.Add(name);
				}

				_bookmarksDirty = false;
			}

			// Always try to sync selection
			int selectedIndex = -1;
			for (int i = 0; i < _activeBookmarkPaths.Count; i++)
			{
				if (string.Equals(_activeBookmarkPaths[i], _rootPath, StringComparison.OrdinalIgnoreCase))
				{
					selectedIndex = i;
					break;
				}
			}

			// If no match but we have a placeholder at 0, use it
			if (selectedIndex == -1 && _activeBookmarkPaths.Count > 0 && string.IsNullOrEmpty(_activeBookmarkPaths[0]))
			{
				selectedIndex = 0;
			}

			RailBookmarksPicker.SelectedIndex = selectedIndex;
			RailRemoveBookmarkLabel.IsVisible = selectedIndex >= 0 && !string.IsNullOrEmpty(_activeBookmarkPaths[selectedIndex]);

			// Highlight container and icon when a bookmark is active
			if (hasSelection)
			{
				RailBookmarksContainerBorder.BackgroundColor = BookmarkActiveBackgroundColor;
				RailBookmarksContainerBorder.Stroke = BookmarkActiveStrokeColor;
				RailBookmarksContainerBorder.StrokeThickness = 1;
				RailBookmarksIcon.Opacity = 1.0;
			}
			else
			{
				RailBookmarksContainerBorder.BackgroundColor = BookmarkInactiveBackgroundColor;
				RailBookmarksContainerBorder.Stroke = Colors.Transparent;
				RailBookmarksContainerBorder.StrokeThickness = 0;
				RailBookmarksIcon.Opacity = 0.6;
			}
		}
		finally
		{
			_isInternalPickerUpdate = false;
		}
	}

	private async void OnBookmarksSectionDrop(object? sender, DropEventArgs e)
	{
		if (!CurrentAllowsBookmarkDrop || IsAssetIntentOnlyDrag(e.Data))
		{
			return;
		}

		await HandleDroppedPathsAsync(e, addToBookmarks: true, openDroppedFolder: false);
	}

	private async void OnBookmarksSectionDragOver(object? sender, DragEventArgs e)
	{
		if (!CurrentAllowsBookmarkDrop || IsAssetIntentOnlyDrag(e.Data))
		{
			_currentDragPaths = null;
			_currentDragIsFolder = null;
			ApplyDragOverState(e, false);
			RailBookmarksHighlightBorder.Stroke = Colors.Transparent;
			return;
		}

		if (_currentDragIsFolder.HasValue)
		{
			ApplyDragOverState(e, _currentDragIsFolder.Value);

			if (_currentDragIsFolder.Value)
			{
				RailBookmarksHighlightBorder.Stroke = BookmarkActionColor;
			}
			return;
		}

		if (!_isValidatingDrag)
		{
			_isValidatingDrag = true;
			bool isFolder = await PlatformManager.Current.DragDrop.ContainsFolderAsync(e);
			_currentDragIsFolder = isFolder;
			_isValidatingDrag = false;

			ApplyDragOverState(e, isFolder);

			if (isFolder)
			{
				RailBookmarksHighlightBorder.Stroke = BookmarkActionColor;
			}
		}
	}

	private void ApplyDragOverState(DragEventArgs e, bool isFolder)
	{
		if (isFolder)
		{
			e.AcceptedOperation = Microsoft.Maui.Controls.DataPackageOperation.Copy;
			PlatformManager.Current.Cursor.SetCursor(NexusCursorShape.Arrow);
		}
		else
		{
			e.AcceptedOperation = Microsoft.Maui.Controls.DataPackageOperation.None;
			PlatformManager.Current.Cursor.SetCursor(NexusCursorShape.Forbidden);
		}
	}

	private void OnBookmarksSectionDragLeave(object? sender, EventArgs e)
	{
		_currentDragIsFolder = null;
		PlatformManager.Current.Cursor.SetCursor(NexusCursorShape.Arrow);
		RailBookmarksHighlightBorder.Stroke = Colors.Transparent;
		_currentDragPaths = null;
	}

	private async void OnCurrentSectionDrop(object? sender, DropEventArgs e)
	{
		_currentDragIsFolder = null;

		if (IsAssetIntentOnlyDrag(e.Data))
		{
			_currentDragPaths = null;
			_dropHandledByRow = false;
			return;
		}
		if (!CanAcceptFileDrop(e.Data))
		{
			_currentDragPaths = null;
			_dropHandledByRow = false;
			return;
		}

		if (_dropHandledByRow)
		{
			_dropHandledByRow = false;
			return;
		}

		var droppedPaths = await TryGetDroppedPathsWithActiveFallbackAsync(e);
		if (droppedPaths.Count > 0)
		{
			if (IsCurrentRootDrag(e.Data))
			{
				if (IsDuplicateDragRequested())
				{
					await DuplicatePathsAsync(droppedPaths, _rootPath);
				}
				else
				{
					await MovePathsAsync(droppedPaths, _rootPath);
				}
			}
			else
			{
				await ImportDroppedPathsAsync(droppedPaths);
			}
		}
	}

	private void OnCurrentSectionDragOver(object? sender, DragEventArgs e)
	{
		if (IsAssetIntentOnlyDrag(e.Data))
		{
			_currentDragPaths = null;
			RailCurrentLocationHighlightBorder.Stroke = Colors.Transparent;
			e.AcceptedOperation = Microsoft.Maui.Controls.DataPackageOperation.None;
			return;
		}
		if (!CanAcceptFileDrop(e.Data))
		{
			_currentDragPaths = null;
			RailCurrentLocationHighlightBorder.Stroke = Colors.Transparent;
			e.AcceptedOperation = Microsoft.Maui.Controls.DataPackageOperation.None;
			return;
		}

		if (_dropHandledByRow)
		{
			RailCurrentLocationHighlightBorder.Stroke = Colors.Transparent;
			return;
		}

		if (_currentDragPaths != null)
		{
			bool isValid = IsDropValid(_currentDragPaths, _rootPath);
			ApplyCurrentLocationDragState(e, isValid);
			return;
		}

		if (!_isValidatingDrag)
		{
			_isValidatingDrag = true;
			_ = ValidateDragAsync(e);
		}

		e.AcceptedOperation = GetAcceptedFileDropOperation(e.Data);
	}

	private async Task ValidateDragAsync(DragEventArgs e)
	{
		var paths = await TryGetDraggedPathsWithActiveFallbackAsync(e);
		_currentDragPaths = paths;
		_isValidatingDrag = false;
	}

	private bool IsDropValid(IReadOnlyList<string> paths, string targetDirectory)
	{
		if (paths.Count == 0 || string.IsNullOrEmpty(targetDirectory)) return false;
		if (IsDuplicateDragRequested())
		{
			return _currentProfile?.AllowDuplicate == true && paths.Any(File.Exists);
		}

		foreach (var path in paths)
		{
			string? parent = Path.GetDirectoryName(path);
			if (parent == null || !string.Equals(parent, targetDirectory, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private void ApplyCurrentLocationDragState(DragEventArgs e, bool isValid)
	{
		if (isValid)
		{
			e.AcceptedOperation = GetAcceptedFileDropOperation(e.Data);
			RailCurrentLocationHighlightBorder.Stroke = AssetDropHighlightColor;
		}
		else
		{
			e.AcceptedOperation = Microsoft.Maui.Controls.DataPackageOperation.None;
			RailCurrentLocationHighlightBorder.Stroke = Colors.Transparent;
		}
	}

	private void OnCurrentSectionDragLeave(object? sender, EventArgs e)
	{
		RailCurrentLocationHighlightBorder.Stroke = Colors.Transparent;
		_currentDragPaths = null;
	}

	private async Task HandleDroppedPathsAsync(DropEventArgs e, bool addToBookmarks, bool openDroppedFolder)
	{
		var droppedPaths = await TryGetDroppedPathsAsync(e);
		if (droppedPaths.Count == 0)
		{
			return;
		}

		string? lastAddedPath = null;
		foreach (string path in droppedPaths)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				continue;
			}
			if (addToBookmarks)
			{
				if (!Directory.Exists(path))
				{
					continue;
				}

				if (!IsProtectedBookmark(path))
				{
					if (!_bookmarkedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
					{
						_bookmarkedPaths.Add(path);
						_bookmarksDirty = true;
					}

					lastAddedPath = path;
				}
			}
			else if (openDroppedFolder)
			{
				string targetDirectory = Directory.Exists(path)
					? path
					: (Path.GetDirectoryName(path) ?? string.Empty);

				if (!string.IsNullOrWhiteSpace(targetDirectory) && Directory.Exists(targetDirectory))
				{
					SetRootPath(targetDirectory);
				}
			}
		}

		_bookmarkedPaths.Sort(StringComparer.OrdinalIgnoreCase);
		_bookmarksDirty = true;

		if (!string.IsNullOrEmpty(lastAddedPath))
		{
			SetRootPath(lastAddedPath);
		}
		else
		{
			RenderBookmarks();
		}

		await PersistBookmarksAsync();
		OnBookmarksSectionDragLeave(null, EventArgs.Empty);
		OnCurrentSectionDragLeave(null, EventArgs.Empty);
		RailBookmarksHighlightBorder.Stroke = Colors.Transparent;
	}

	private static async Task<IReadOnlyList<string>> TryGetDroppedPathsAsync(DragEventArgs e)
	{
		var results = new List<string>();
		try
		{
			if (e.Data.Properties.TryGetValue("paths", out object? rawPaths) && rawPaths is IReadOnlyList<string> paths)
			{
				results.AddRange(paths);
			}
			else if (e.Data.Properties.TryGetValue("path", out object? rawPath) && rawPath is string path && !string.IsNullOrWhiteSpace(path))
			{
				results.Add(path);
			}
		}
		catch { }

		if (results.Count > 0) return results;

		return await PlatformManager.Current.DragDrop.GetDroppedPathsAsync(e);
	}

	private static async Task<IReadOnlyList<string>> TryGetDroppedPathsAsync(DropEventArgs e)
	{
		var results = new List<string>();
		try
		{
			if (e.Data.Properties.TryGetValue("paths", out object? rawPaths) && rawPaths is IReadOnlyList<string> paths)
			{
				results.AddRange(paths);
			}
			else if (e.Data.Properties.TryGetValue("path", out object? rawPath) && rawPath is string path && !string.IsNullOrWhiteSpace(path))
			{
				results.Add(path);
			}
		}
		catch { }

		if (results.Count > 0) return results;

		return await PlatformManager.Current.DragDrop.GetDroppedPathsAsync(e);
	}

	private IEnumerable<string> GetProtectedBookmarks()
	{
		string comfyRoot = ComfyPathResolver.ResolveConfiguredComfyPath();
		return AssetRootProfileProvider.GetProtectedBookmarkPaths(comfyRoot, _fixedWorkflowsPath);
	}

	private bool IsProtectedBookmark(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		return GetProtectedBookmarks().Any(bookmark => string.Equals(bookmark, path, StringComparison.OrdinalIgnoreCase));
	}
}
