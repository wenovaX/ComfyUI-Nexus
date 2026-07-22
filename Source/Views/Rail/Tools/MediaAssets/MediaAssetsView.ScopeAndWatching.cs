using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Views.Rail.Tools.Assets;

namespace ComfyUI_Nexus.Views.Rail.Tools.MediaAssets;

public partial class MediaAssetsView
{	private string GetActiveRootPath()
		=> GetRootPath(_activeScope);

	private string GetRootPath(MediaAssetScope scope)
	{
		string directoryName = scope == MediaAssetScope.Output
			? ComfyPathOptions.OutputDirectoryName
			: ComfyPathOptions.InputDirectoryName;

		return string.IsNullOrWhiteSpace(_comfyRootPath)
			? string.Empty
			: Path.Combine(_comfyRootPath, directoryName);
	}

	private void ConfigureMediaWatcher()
	{
		string rootPath = GetActiveRootPath();
		_directoryWatcher.ConfigureRoot(rootPath, MediaWatcherOptions);
	}

	private async Task RefreshActiveScopeAfterRootChangeAsync()
	{
		try
		{
			await RefreshAsync(CancellationToken.None, showLoadingOverlay: false, forceRefresh: true);
			if (_isRailActive && _isReady && !_isRenderDeferred)
			{
				_directoryWatcher.MarkReconciled();
				_directoryWatcher.SetActive(true);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Warning($"Media root refresh failed: {ex.Message}");
		}
	}

	private bool CanApplyMediaWatcherBatch(RailDirectoryWatchBatch batch)
	{
		return _isRailActive
			&& _isReady
			&& !_isRenderDeferred
			&& IsVisible
			&& Handler is not null
			&& string.Equals(batch.RootPath, GetActiveRootPath(), StringComparison.OrdinalIgnoreCase)
			&& _directoryWatcher.IsCurrent(batch.RootPath, batch.Generation);
	}

	private bool ApplyMediaWatcherBatch(RailDirectoryWatchBatch batch)
	{
		if (!CanApplyMediaWatcherBatch(batch))
		{
			return false;
		}

		MarkCacheDirty(batch.RootPath);
		_ = RefreshScopeAsync(
			_activeScope,
			CancellationToken.None,
			showLoadingOverlay: false,
			forceRefresh: true);
		return true;
	}

	private void OnOutputTabTapped(object? sender, TappedEventArgs e)
	{
		SwitchScope(MediaAssetScope.Output);
	}

	private void OnInputTabTapped(object? sender, TappedEventArgs e)
	{
		SwitchScope(MediaAssetScope.Input);
	}

	private void SwitchScope(MediaAssetScope scope)
	{
		_ = SwitchScopeAsync(scope);
	}

	private async Task SwitchScopeAsync(MediaAssetScope scope)
	{
		if (_activeScope == scope)
		{
			return;
		}

		ClearSelection();
		_directoryWatcher.SetActive(false);
		_activeScope = scope;
		ResetSearchText();
		ApplyProjectionToCurrentSurface();
		UpdateTabState();
		ConfigureMediaWatcher();
		var surface = GetActiveSurface();
		surface.VirtualLayoutSignature = string.Empty;
		_ = surface.ScrollView.ScrollToAsync(0, 0, animated: false);
		UpdateVirtualGrid(surface);
		try
		{
			// A scope change is an explicit user refresh. Reconcile the selected directory
			// before re-enabling its watcher, even when no filesystem event was raised.
			await RefreshAsync(CancellationToken.None, showLoadingOverlay: true, forceRefresh: true);
			if (_isRailActive && _isReady && !_isRenderDeferred)
			{
				_directoryWatcher.MarkReconciled();
				_directoryWatcher.SetActive(true);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Warning($"Media scope refresh failed: {ex.Message}");
		}
	}

}
