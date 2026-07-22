using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Views.Rail.Tools.Assets;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace ComfyUI_Nexus.Views.Rail.Tools.MediaAssets;

public partial class MediaAssetsView
{	private static void ResetMediaAssetCell(MediaAssetCell cell) => cell.Clear();

	private static MenuFlyout CreateMediaAssetFlyout(MediaAssetCell cell)
	{
		var flyout = new MenuFlyout();
		flyout.Add(CreateFlyoutItem(LocalizationManager.Text("common.open"), (_, _) => cell.Open()));
		flyout.Add(CreateFlyoutItem(LocalizationManager.Text("context_menu.reveal_in_explorer"), (_, _) => cell.Reveal()));
		flyout.Add(new MenuFlyoutSeparator());
		flyout.Add(CreateFlyoutItem(LocalizationManager.Text("common.copy"), (_, _) => cell.Copy()));
		flyout.Add(CreateFlyoutItem(LocalizationManager.Text("views.rail.tools.media_assets.media_assets_view.move_to"), (_, _) => cell.Move()));
		flyout.Add(CreateFlyoutItem(LocalizationManager.Text("context_menu.copy_path"), (_, _) => cell.CopyPath()));
		flyout.Add(new MenuFlyoutSeparator());
		flyout.Add(CreateFlyoutItem(LocalizationManager.Text("common.rename"), (_, _) => cell.Rename()));
		flyout.Add(CreateFlyoutItem(LocalizationManager.Text("common.delete"), (_, _) => cell.Delete()));
		return flyout;
	}

	private static MenuFlyoutItem CreateFlyoutItem(string text, EventHandler handler)
	{
		var item = new MenuFlyoutItem { Text = text };
		item.Clicked += handler;
		return item;
	}

	private void OnPooledAssetTapped(MediaAssetCell cell)
	{
		if (cell.Entry == null)
		{
			return;
		}

		SelectFromPointer(cell.Entry.FullPath);
	}

	private static void ApplyPooledAssetHover(MediaAssetCell cell, bool isHovered)
	{
		if (cell.Entry?.IsSelected == true)
		{
			return;
		}

		cell.Root.BackgroundColor = isHovered ? MediaCellHoverBackgroundColor : MediaCellBackgroundColor;
		cell.Root.Stroke = isHovered ? MediaCellHoverStrokeColor : MediaCellStrokeColor;
	}

	private async Task RenderItemsAsync(MediaAssetScopeSurface surface, IReadOnlyList<MediaAssetEntry> entries, int renderVersion)
	{
		try
		{
			if (renderVersion != surface.RenderVersion)
			{
				return;
			}

			string nextSignature = BuildRenderSignature(entries);
			surface.SourceItems.Clear();
			surface.SourceItems.AddRange(entries);
			await RenderProjectedItemsAsync(surface, renderVersion, nextSignature);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[MEDIA_ASSETS] Failed to render media assets");
			if (renderVersion == surface.RenderVersion)
			{
				surface.SourceItems.Clear();
				surface.Items.Clear();
				surface.RenderedSignature = string.Empty;
				UpdateVirtualGrid(surface);
			}
		}
	}

	private async Task RenderProjectedItemsAsync(MediaAssetScopeSurface surface, int renderVersion, string? sourceSignature = null)
	{
		if (renderVersion != surface.RenderVersion)
		{
			return;
		}

		var entries = ProjectEntries(surface.SourceItems);
		string nextSignature = $"{sourceSignature ?? BuildRenderSignature(surface.SourceItems)}\nFILTER:{_searchText}\n{BuildRenderSignature(entries)}";
		if (string.Equals(surface.RenderedSignature, nextSignature, StringComparison.Ordinal))
		{
			SyncVisibleSelectionState();
			UpdateVirtualGrid(surface);
			return;
		}

		try
		{
			surface.Items.Clear();
			for (int index = 0; index < entries.Count; index++)
			{
				if (renderVersion != surface.RenderVersion)
				{
					return;
				}

				surface.Items.Add(entries[index]);
				if ((index + 1) % MediaCachedThumbnailBatchSize == 0)
				{
					await Task.Yield();
				}
			}

			surface.RenderedSignature = nextSignature;
			_selection.Normalize();
			SyncVisibleSelectionState();
			UpdateVirtualGrid(surface);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[MEDIA_ASSETS] Failed to render projected media assets");
			if (renderVersion == surface.RenderVersion)
			{
				surface.Items.Clear();
				surface.RenderedSignature = string.Empty;
				UpdateVirtualGrid(surface);
			}
		}
	}

	private static string BuildRenderSignature(IReadOnlyList<MediaAssetEntry> entries)
		=> string.Join(
			"\n",
			entries.Select(entry => $"{entry.FullPath}|{entry.CreatedAt.Ticks}|{entry.ModifiedAt.Ticks}|{entry.SizeBytes}"));

	private IReadOnlyList<MediaAssetEntry> ProjectEntries(IReadOnlyList<MediaAssetEntry> entries)
	{
		IEnumerable<MediaAssetEntry> projected = entries;
		if (!string.IsNullOrWhiteSpace(_searchText))
		{
			projected = projected.Where(entry => entry.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
		}

		return SortEntries(projected.ToList());
	}

	private MediaAssetScopeSurface GetActiveSurface() => GetSurface(_activeScope);

	private MediaAssetScopeSurface GetSurface(MediaAssetScope scope)
		=> scope == MediaAssetScope.Output
			? _outputSurface ?? throw new InvalidOperationException("Output media surface is not initialized.")
			: _inputSurface ?? throw new InvalidOperationException("Input media surface is not initialized.");

	private void UpdateVirtualGrid(MediaAssetScopeSurface surface)
	{
		UpdateActiveSurfaceVisibility();
		if (surface.Items.Count == 0)
		{
			surface.AttachVersion++;
			ReturnAllVisibleCells(surface);
			surface.Spacer.HeightRequest = 0;
			surface.Canvas.HeightRequest = 0;
			surface.Canvas.TranslationY = 0;
			surface.Surface.ClearValue(HeightRequestProperty);
			surface.VirtualLayoutSignature = string.Empty;
			return;
		}

		int columnCount = Math.Clamp(_mediaGridColumns, 1, 3);
		double viewportHeight = Math.Max(1, surface.ScrollView.Height);
		double viewportWidth = Math.Max(1, surface.ScrollView.Width - MediaScrollbarGutter);
		double cellWidth = Math.Max(58, (viewportWidth - MediaCardGap * (columnCount - 1)) / columnCount);
		double cardHeight = cellWidth;
		int rowCount = (int)Math.Ceiling(surface.Items.Count / (double)columnCount);
		double stride = cardHeight + MediaCardGap;
		double totalHeight = Math.Max(0, rowCount * cardHeight + Math.Max(0, rowCount - 1) * MediaCardGap);
		surface.Spacer.WidthRequest = viewportWidth;
		surface.Canvas.WidthRequest = viewportWidth;
		surface.Surface.WidthRequest = viewportWidth;
		surface.Spacer.HeightRequest = totalHeight;

		double maxScrollY = Math.Max(0, totalHeight - viewportHeight);
		double scrollY = Math.Clamp(Math.Max(0, surface.ScrollView.ScrollY), 0, maxScrollY);
		int firstRow = Math.Min(rowCount - 1, Math.Max(0, (int)Math.Floor(scrollY / stride) - MediaGridBufferRows));
		int lastRow = Math.Min(rowCount - 1, Math.Max(firstRow, (int)Math.Ceiling((scrollY + viewportHeight) / stride) + MediaGridBufferRows));
		double renderTop = firstRow * stride;
		double renderHeight = Math.Max(cardHeight, (lastRow - firstRow) * stride + cardHeight);
		surface.Canvas.HeightRequest = renderHeight;
		surface.Canvas.TranslationY = renderTop;
		surface.Surface.ClearValue(HeightRequestProperty);

		string layoutSignature = $"{surface.Items.Count}|{columnCount}|{firstRow}|{lastRow}|{viewportWidth:0.###}|{viewportHeight:0.###}|{cellWidth:0.###}|{cardHeight:0.###}|{renderTop:0.###}|{renderHeight:0.###}";
		if (string.Equals(surface.VirtualLayoutSignature, layoutSignature, StringComparison.Ordinal))
		{
			return;
		}

		surface.VirtualLayoutSignature = layoutSignature;
		int attachVersion = ++surface.AttachVersion;

		var requiredIndices = new HashSet<int>();
		for (int row = firstRow; row <= lastRow; row++)
		{
			for (int column = 0; column < columnCount; column++)
			{
				int itemIndex = row * columnCount + column;
				if (itemIndex < surface.Items.Count)
				{
					requiredIndices.Add(itemIndex);
				}
			}
		}

		foreach (int index in surface.VisibleCells.Keys.Where(index => !requiredIndices.Contains(index)).ToList())
		{
			var cell = surface.VisibleCells[index];
			surface.Canvas.Children.Remove(cell.Root);
			cell.Root.CancelAnimations();
			surface.CellPool!.Return(cell);
			surface.VisibleCells.Remove(index);
		}

		var missingIndices = new List<int>();
		foreach (int index in requiredIndices.OrderBy(index => index))
		{
			if (surface.VisibleCells.TryGetValue(index, out var cell))
			{
				BindMediaAssetCell(surface, cell, index, columnCount, cellWidth, cardHeight, renderTop);
				continue;
			}

			missingIndices.Add(index);
		}

		// Cells are attached in their resting state. Deferred reveal animations used to
		// outlive rail transitions and could write to recycled native views.
		if (missingIndices.Count > 0)
		{
			AttachMissingMediaCellsImmediately(
				surface,
				missingIndices,
				columnCount,
				cellWidth,
				cardHeight,
				renderTop,
				attachVersion);
		}
	}

	private void AttachMissingMediaCellsImmediately(
		MediaAssetScopeSurface surface,
		IReadOnlyList<int> indices,
		int columnCount,
		double cellWidth,
		double cardHeight,
		double renderTop,
		int attachVersion)
	{
		foreach (int index in indices)
		{
			if (attachVersion != surface.AttachVersion || index < 0 || index >= surface.Items.Count)
			{
				return;
			}

			if (surface.VisibleCells.ContainsKey(index))
			{
				continue;
			}

			var cell = surface.CellPool!.Rent();
			cell.Root.CancelAnimations();
			cell.Root.Opacity = 1;
			cell.Root.Scale = 1;
			surface.VisibleCells[index] = cell;
			BindMediaAssetCell(surface, cell, index, columnCount, cellWidth, cardHeight, renderTop);
			surface.Canvas.Children.Add(cell.Root);
			EnsureMediaAssetFlyout(cell);
		}
	}

	private static void BindMediaAssetCell(
		MediaAssetScopeSurface surface,
		MediaAssetCell cell,
		int index,
		int columnCount,
		double cellWidth,
		double cardHeight,
		double renderTop)
	{
		int row = index / columnCount;
		int column = index % columnCount;
		cell.Bind(surface.Items[index]);
		double x = column * (cellWidth + MediaCardGap);
		double y = row * (cardHeight + MediaCardGap) - renderTop;
		cell.Resize(cellWidth, cardHeight, showMetadataOverlay: columnCount < 3);
		AbsoluteLayout.SetLayoutBounds(cell.Root, new Rect(x, y, cellWidth, cardHeight));
		AbsoluteLayout.SetLayoutFlags(cell.Root, AbsoluteLayoutFlags.None);
	}

	private static void EnsureMediaAssetFlyout(MediaAssetCell cell)
	{
		if (FlyoutBase.GetContextFlyout(cell.Root) == null)
		{
			FlyoutBase.SetContextFlyout(cell.Root, CreateMediaAssetFlyout(cell));
		}
	}

	private void ReturnAllVisibleCells(MediaAssetScopeSurface surface)
	{
		surface.AttachVersion++;
		foreach (var cell in surface.VisibleCells.Values)
		{
			surface.Canvas.Children.Remove(cell.Root);
			cell.Root.CancelAnimations();
			surface.CellPool!.Return(cell);
		}

		surface.VisibleCells.Clear();
	}

	private async Task LoadThumbnailsAsync(
		MediaAssetScopeSurface surface,
		string cacheKey,
		IReadOnlyList<MediaAssetSourceEntry> sourceEntries,
		IReadOnlyList<MediaAssetEntry> entries,
		CancellationToken cancellationToken)
	{
		if (sourceEntries.Count == 0 || entries.Count == 0)
		{
			return;
		}

		var entryByPath = entries.ToDictionary(entry => entry.FullPath, StringComparer.OrdinalIgnoreCase);

		var missingThumbnailSources = new List<MediaAssetSourceEntry>();
		var cachedThumbnailSources = OrderSourcesByViewport(cacheKey, sourceEntries, entries);
		for (int index = 0; index < cachedThumbnailSources.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!IsSurfaceCacheKey(surface, cacheKey))
			{
				surface.RefreshRequestedAfterCurrent = true;
				return;
			}

			var sourceEntry = cachedThumbnailSources[index];
			if (!entryByPath.TryGetValue(sourceEntry.FullPath, out var entry) || !File.Exists(sourceEntry.FullPath))
			{
				continue;
			}

			string? existingThumbnailPath = _thumbnailCache.GetExistingThumbnailPath(sourceEntry);
			if (!string.IsNullOrWhiteSpace(existingThumbnailPath))
			{
				await MainThread.InvokeOnMainThreadAsync(() => entry.SetThumbnailPath(existingThumbnailPath));

				if ((index + 1) % MediaCachedThumbnailBatchSize == 0)
				{
					await Task.Yield();
				}

				continue;
			}

			missingThumbnailSources.Add(sourceEntry);
		}

		int createdCount = 0;
		while (missingThumbnailSources.Count > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!IsSurfaceCacheKey(surface, cacheKey))
			{
				surface.RefreshRequestedAfterCurrent = true;
				return;
			}

			if (createdCount % MediaThumbnailReprioritizeInterval == 0)
			{
				missingThumbnailSources = OrderSourcesByViewport(cacheKey, missingThumbnailSources, entries);
			}

			var sourceEntry = missingThumbnailSources[0];
			missingThumbnailSources.RemoveAt(0);
			if (!entryByPath.TryGetValue(sourceEntry.FullPath, out var entry) || !File.Exists(sourceEntry.FullPath))
			{
				continue;
			}

			string? thumbnailPath = await Task.Run(
				() => _thumbnailCache.EnsureThumbnailAsync(sourceEntry, cancellationToken),
				cancellationToken);

			if (!string.IsNullOrWhiteSpace(thumbnailPath))
			{
				await MainThread.InvokeOnMainThreadAsync(() => entry.SetThumbnailPath(thumbnailPath));
			}

			createdCount++;
			if (createdCount % MediaCreateThumbnailBatchSize == 0)
			{
				await Task.Yield();
			}
		}

	}

}
