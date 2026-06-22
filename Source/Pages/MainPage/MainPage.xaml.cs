using System.Text.Json;
using Microsoft.Maui.Controls;
using ComfyUI_Nexus.AssetHub;
using ComfyUI_Nexus.Boot;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Input;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Views;
using ComfyUI_Nexus.Views.Rail;
using ComfyUI_Nexus.Views.Rail.Tools.Assets;
using ComfyUI_Nexus.Views.Rail.Tools.MediaAssets;
using ComfyUI_Nexus.Views.Rail.Tools.NodeLibrary;

namespace ComfyUI_Nexus;

public partial class MainPage : ContentPage
{
	private readonly WorkflowTabController _tabController;
	private readonly LoadingOverlayController _loadingOverlayController;
	private readonly HeaderGpuStatusController _gpuStatusController;
	private readonly NexusWebViewBridge _webViewBridge;
	private readonly NexusUiSurfaceManager _uiSurfaceManager = new();
	private readonly NexusInputRouter _inputRouter;
	private readonly NodeLibraryService _nodeLibraryService = new();
	private readonly ShellLayoutSignals _shellLayoutSignals = new();
	private readonly TaskCompletionSource<bool> _webViewPlatformReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly LoginSequenceOrchestrator _loginSequence = new();
	internal NodeLibraryRoot? _nodeLibrary;
	internal NodeLibraryRoot? NodeLibrary => _nodeLibrary;
	private string _nodeLibraryManifestSignature = string.Empty;
	private List<BlueprintItem>? _latestBlueprints;
	private bool _isBooted = false;
	private bool _isRebooting = false;
	private bool _isShuttingDown = false;
	private bool _bridgeDiagnosticsEnabled;
	private bool _uiIsolationEnabled = true;
	private bool _bootReadyHandled = false;
	private bool _stabilizedVisualStateApplied = false;
	private bool _isFileRailExpanded = false;
	private readonly AssetHubNativeService _assetHubService = new();
	private bool _isRailAnimating = false;
	private bool _isSystemLoading = true;
	private bool _startupSplashHiding = false;
	private bool _isControlDeckVisible = false;
	private bool _isAssetDragActive = false;
	private bool _isAssetDragCompleting = false;
	private bool _assetDragSawPrimaryPressed = false;
	private DateTime _assetDragStartedUtc = DateTime.MinValue;
	private AssetOpenRequest? _activeAssetDragRequest;
	private IDispatcherTimer? _assetDragGhostTimer;
	private string? _activeModelThumbnailPreviewPath;
	private int _modelThumbnailPreviewVersion;
	private string _currentRunMode = RunModeOptions.Default;
#if WINDOWS
	private double _lastSystemCpuPercent;
	private double _lastSystemGpuPercent;
#endif

	private double _lastMeasuredHeaderHeight = 0;
	private double _expandedRailWidth = ShellLayoutOptions.DefaultExpandedRailWidth;
	private double _railResizeStartWidth = ShellLayoutOptions.DefaultExpandedRailWidth;
	private double _pendingRailWidth = ShellLayoutOptions.DefaultExpandedRailWidth;
	private bool _isRailResizeHovering = false;
	private CancellationTokenSource? _toastCts;
	private CancellationTokenSource? _mediaAssetSnapshotBurstCts;

	partial void InitializeNativeSystemTelemetry();

	public MainPage()
	{
		try
		{
			InitializeComponent();
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[STARTUP] MainPage InitializeComponent failed");
			throw;
		}

		NexusLog.SetSink(AppendLogEntry);
		try
		{
			_inputRouter = new NexusInputRouter(_uiSurfaceManager);
			_webViewBridge = new NexusWebViewBridge(() => WorkspaceControl?.BrowserView);
			_tabController = new WorkflowTabController(
				HeaderControl,
				WorkflowDropdownControl,
				() => WorkflowDropdownControl.IsOpen,
				HideDropdown,
				ShowDropdown,
				ToggleBookmarkAsync,
				HandleWorkflowTabActionAsync,
				_webViewBridge.ExecuteRawScriptAsync,
				UpdateWorkflowContextBar);
			_loadingOverlayController = new LoadingOverlayController(this, LoadingOverlayControl, HeaderControl);
			_loadingOverlayController.SetNexusAppEntry(new DelegateNexusAppEntry(LaunchNexusAppEntryAsync));
			_gpuStatusController = new HeaderGpuStatusController(this, HeaderControl);
			_shellLayoutSignals.LayoutInvalidated += OnShellLayoutInvalidated;
			InitializeComfyPaths();
			NexusDialogService.Register(NexusDialogOverlayControl);
			NexusDialogService.Closed += OnNexusDialogClosed;
			WireSubviewEvents();
			InitializeKeyboardSurfaces();
			RefreshBridgeDiagnosticsButtonState();

			Log("Nexus Shell starting...");

			InitializePlatformHooks();
			InitializeNativeSystemTelemetry();
			ApplyTabUiTuning();

			ControlDeckControl.SetDisplayState(isVisible: false, opacity: 0);
			ApplyRailState();
			InitializeRailRoot();
			Unloaded += OnMainPageUnloaded;

			HeaderControl.SizeChanged += (s, e) =>
			{
				if (HeaderControl.Height > 0)
				{
					_lastMeasuredHeaderHeight = HeaderControl.Height;
				}
				if (_isSystemLoading) return;
			};

			this.SizeChanged += (s, e) =>
			{
				ShellLayoutScale.Update(this.Width);
				RefreshAvailableWidthAndTabs(ShellLayoutInvalidationReason.WindowSizeChanged);
			};

			ShellLayoutScale.ScaleChanged += OnLayoutScaleChanged;

			var closeTap = new TapGestureRecognizer();
			closeTap.Tapped += (s, e) =>
			{
				HideDropdown();
				_ = SetCommandMenuVisible(false);
				_ = SetSettingsOverlayVisible(false);
				_ = SetHelpOverlayVisible(false);
				_ = SetWorkflowActionsMenuVisible(false);
			};
			WorkspaceControl.GestureRecognizers.Add(closeTap);

			StartStartupLoginSequence();
			StartOverlayAnimations();
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[STARTUP] MainPage constructor failed");
			throw;
		}
	}

	private void OnMainPageUnloaded(object? sender, EventArgs e)
	{
		_isShuttingDown = true;
		_loginSequence.Cancel();
		_mediaAssetSnapshotBurstCts?.Cancel();
		_mediaAssetSnapshotBurstCts?.Dispose();
		_mediaAssetSnapshotBurstCts = null;
		NexusDialogService.Closed -= OnNexusDialogClosed;
		NexusDialogService.Unregister(NexusDialogOverlayControl);
	}

	private void OnShellLayoutInvalidated(object? sender, ShellLayoutInvalidatedEventArgs e)
	{
		HeaderControl.InvalidateShellLayout();
	}

	private void WireSubviewEvents()
	{
		WireRailEvents();
		WireHeaderEvents();
		WireWorkspaceEvents();
		WireOverlayEvents();
		WireControlDeckEvents();
		WireToolbarEvents();
	}

	private void WireHeaderEvents()
	{
		HeaderControl.LogoClicked += OnLogoClicked;
		HeaderControl.LogoFiveClicked += OnLogoFiveClicked;
		HeaderControl.MainActionRequested += OnHeaderMainActionRequested;
		HeaderControl.StopActionRequested += OnHeaderStopActionRequested;
		HeaderControl.RunModeRequested += OnHeaderRunModeRequested;
		HeaderControl.ViewQueueRequested += OnHeaderViewQueueRequested;
		HeaderControl.TogglePropertiesRequested += OnHeaderTogglePropertiesRequested;
		HeaderControl.ShowManagerRequested += OnHeaderShowManagerRequested;
		HeaderControl.ShowFavoritesRequested += OnHeaderShowFavoritesRequested;
		HeaderControl.UnloadModelsRequested += OnHeaderUnloadModelsRequested;
		HeaderControl.FreeCacheRequested += OnHeaderFreeCacheRequested;
		HeaderControl.ShareRequested += OnHeaderShareRequested;
		HeaderControl.EnterAppModeRequested += OnHeaderEnterAppModeRequested;
		HeaderControl.WorkflowActionsRequested += OnHeaderWorkflowActionsRequested;
	}

	private void WireWorkspaceEvents()
	{
		WorkspaceControl.WebViewNavigated += OnWebViewNavigated;
	}

	private void WireOverlayEvents()
	{
		LoadingOverlayControl.SelectCoreRequested += OnSelectCoreClicked;
		LoadingOverlayControl.GetComfyRequested += OnGetComfyClicked;
		LoadingOverlayControl.GetGitHubRequested += OnGetGitHubClicked;

		BookmarkHudControl.CloseRequested += OnCloseBookmarkHUDClicked;
		BookmarkHudControl.SearchChanged += OnBookmarkSearchChanged;
		CommandMenuControl.RestartServerRequested += OnCommandMenuRestartServerRequested;
		CommandMenuControl.SettingsRequested += OnCommandMenuSettingsRequested;
		CommandMenuControl.HelpRequested += OnCommandMenuHelpRequested;
		CommandMenuControl.AboutRequested += OnCommandMenuAboutRequested;
		SettingsOverlayControl.CloseRequested += OnSettingsOverlayCloseRequested;
		SettingsOverlayControl.RestartServerRequested += OnCommandMenuRestartServerRequested;
		SettingsOverlayControl.RuntimePurgeRequested += OnSettingsOverlayRuntimePurgeRequested;
		SettingsOverlayControl.RuntimeRestoreRequested += OnSettingsOverlayRuntimeRestoreRequested;
		HelpOverlayControl.CloseRequested += OnHelpOverlayCloseRequested;
		AboutOverlayControl.CloseRequested += OnAboutOverlayCloseRequested;
		WorkflowActionsMenuControl.ActionRequested += OnWorkflowActionsMenuActionRequested;
		WorkflowActionsMenuControl.DismissRequested += OnWorkflowActionsMenuDismissRequested;
		CanvasModeMenuControl.ModeRequested += OnCanvasModeMenuModeRequested;
		CanvasModeMenuControl.DismissRequested += OnCanvasModeMenuDismissRequested;
		CommandInputControl.Completed += OnCommandInputCompleted;
		CommandInputControl.BackdropTapped += OnCommandInputBackdropTapped;
	}

	private void WireControlDeckEvents()
	{
		ControlDeckControl.ManualRebootRequested += OnManualRebootClicked;
		ControlDeckControl.ToggleBridgeDiagnosticsRequested += OnToggleBridgeDiagnosticsClicked;
		ControlDeckControl.ToggleWebLogsRequested += OnToggleWebLogsClicked;
		ControlDeckControl.ToggleDevToolsRequested += OnToggleDevToolsClicked;
		ControlDeckControl.ToggleUiIsolationRequested += OnToggleUiIsolationClicked;
		ControlDeckControl.PatchLocalHudRequested += OnPatchLocalHudClicked;
		ControlDeckControl.PatchNexusBridgeRequested += OnPatchNexusBridgeClicked;
		ControlDeckControl.OpenFullLogRequested += OnOpenFullLogClicked;
		ControlDeckControl.CopyAllRequested += OnCopyAllClicked;
		ControlDeckControl.ClearLogRequested += OnClearLogClicked;
		ControlDeckControl.LogSearchChanged += OnLogSearchChanged;
	}

	private void WireToolbarEvents()
	{
		ToolbarControl.ModeToggled += OnToolbarModeToggled;
		ToolbarControl.FitViewRequested += OnToolbarFitViewRequested;
		ToolbarControl.ZoomRequested += OnToolbarZoomRequested;
		ToolbarControl.MinimapToggled += OnToolbarMinimapToggled;
		ToolbarControl.LinksToggled += OnToolbarLinksToggled;
		ToolbarControl.HelpRequested += (s, e) =>
		{
			_webViewBridge.OpenHelpCenterAsync(GetRailContentPanelWidth());
		};
		ToolbarControl.TerminalRequested += (s, e) => _webViewBridge.ToggleBottomPanelAsync();
		ToolbarControl.ShortcutsRequested += (s, e) => _webViewBridge.ToggleShortcutsAsync();
	}

	private void WireRailEvents()
	{
		RailControl.RailOpenRequestedAsync += ExpandRailAsync;
		RailControl.RailCloseRequested += CollapseRailImmediately;
		RailControl.ComfyMenuRequested += OnRailComfyMenuRequested;
		RailControl.MainMenuDismissRequested += OnRailMainMenuDismissRequested;
		RailControl.AppsToggled += OnRailAppsToggled;
		RailControl.SettingsRequested += OnRailSettingsRequested;
		RailControl.TemplatesRequested += OnRailTemplatesRequested;
		RailControl.MediaAssetViewerRequested += OnMediaAssetViewerRequested;
		RailControl.SetMediaAssetDeleteHandler(DeleteMediaAssetItemsAsync);
		RailControl.SetMediaAssetOutputRefreshHandler(() => _webViewBridge.RequestMediaAssetJobSnapshotAsync());
		RailControl.SetMediaAssetStaleOutputJobCleanupHandler(_webViewBridge.DeleteMediaAssetJobIdsAsync);
		RailControl.SetAssetPathMutationHandlers(
			PrepareWorkflowAssetMutationAsync,
			CompleteWorkflowAssetMutationAsync,
			BeginAssetBatchOperationAsync,
			EndAssetBatchOperationAsync);
		RailControl.FileOpenRequested += OnRailFileOpenRequested;
		RailControl.AssetInteractionRequested += OnRailAssetInteractionRequested;
		RailControl.WorkflowBookmarksChanged += OnRailWorkflowBookmarksChanged;
		RailControl.ModelThumbnailPreviewRequested += OnRailModelThumbnailPreviewRequested;
		RailControl.ModelThumbnailPreviewDismissed += OnRailModelThumbnailPreviewDismissed;
		RailResizeHandleControl.PanUpdated += OnRailResizePanUpdated;
	}

	private async void OnRailModelThumbnailPreviewRequested(object? sender, ModelAssetThumbnailPreviewRequest request)
	{
		int version = ++_modelThumbnailPreviewVersion;
		if (!File.Exists(request.ThumbnailPath))
		{
			HideModelThumbnailPreview();
			return;
		}

		try
		{
			ModelThumbnailPreview.WidthRequest = request.Width;
			ModelThumbnailPreview.HeightRequest = request.Height;
			ModelThumbnailPreviewImage.WidthRequest = request.Width;
			ModelThumbnailPreviewImage.HeightRequest = request.Height;

			bool sourceChanged = !string.Equals(_activeModelThumbnailPreviewPath, request.ThumbnailPath, StringComparison.OrdinalIgnoreCase);
			if (!string.Equals(_activeModelThumbnailPreviewPath, request.ThumbnailPath, StringComparison.OrdinalIgnoreCase))
			{
				ModelThumbnailPreview.Opacity = 0;
				_activeModelThumbnailPreviewPath = request.ThumbnailPath;
				ModelThumbnailPreviewImage.Source = ImageSource.FromFile(request.ThumbnailPath);
			}

			var pointer = PlatformManager.Current.Cursor.GetPointerPositionRelativeTo(RootLayoutGrid);
			double railRight = GetControlDeckWidth() + GetTargetRailWidth();
			double x = railRight + 10;
			double y = pointer?.Y + 14 ?? HeaderControl.Height + 24;
			double maxX = Math.Max(4, RootLayoutGrid.Width - request.Width - 8);
			double maxY = Math.Max(4, RootLayoutGrid.Height - request.Height - 8);

			if (x > maxX)
			{
				x = Math.Max(4, railRight - request.Width - 10);
			}

			ModelThumbnailPreview.TranslationX = Math.Min(x, maxX);
			ModelThumbnailPreview.TranslationY = Math.Min(y, maxY);
			ModelThumbnailPreview.IsVisible = true;

			if (sourceChanged)
			{
				await Task.Delay(35);
				if (version != _modelThumbnailPreviewVersion)
				{
					return;
				}
			}

			ModelThumbnailPreview.Opacity = 1;
		}
		catch (Exception ex)
		{
			NexusLog.Trace($"[MODEL ASSETS] Failed to assign thumbnail preview image '{request.ThumbnailPath}': {ex.Message}");
			HideModelThumbnailPreview();
			return;
		}
	}

	private void OnRailModelThumbnailPreviewDismissed(object? sender, EventArgs e)
		=> HideModelThumbnailPreview();

	private void HideModelThumbnailPreview()
	{
		_modelThumbnailPreviewVersion++;
		ModelThumbnailPreview.IsVisible = false;
		ModelThumbnailPreview.Opacity = 0;
	}

	private double GetControlDeckWidth()
		=> ControlDeckColumn.Width.IsAbsolute ? ControlDeckColumn.Width.Value : 0;

	private async void OnRailWorkflowBookmarksChanged(object? sender, EventArgs e)
	{
		try
		{
			await LoadBookmarks();
			await _webViewBridge.RefreshBookmarksAsync();
			RenderBookmarkHUDList();
			RefreshTabsFromLastSync();
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "Failed to refresh workflow bookmarks after an asset action");
		}
	}

	private async void OnRailFileOpenRequested(object? sender, AssetOpenRequest request)
	{
		try
		{
			request = AttachRailWidth(request);
			if (await TryOpenAssetMediaViewerAsync(request))
			{
				return;
			}

			if (request.Mode == AssetInteractionMode.Workflow && string.Equals(request.SourceRoot, "workflows", StringComparison.OrdinalIgnoreCase))
			{
				string relativePath = GetWorkflowRelativePath(request.FullPath);
				if (!string.IsNullOrWhiteSpace(relativePath))
				{
					if (await _tabController.TryActivateTrackedWorkflowAsync(relativePath))
					{
						return;
					}

					_tabController.TrackOpenedWorkflow(Path.GetFileNameWithoutExtension(request.FullPath), relativePath);
					await WebViewUtility.SimulateFileDropAsync(
						WorkspaceControl.BrowserView,
						request.FullPath,
						WorkflowTabController.StripWorkflowPrefix(relativePath));
					return;
				}
			}

			await ComfyUI_Nexus.Ui.AssetActionDispatcher.DispatchAsync(_webViewBridge, WorkspaceControl.BrowserView, request);
		}
		catch (Exception ex)
		{
			Log($"Asset dispatch failed: {ex.GetType().Name} - {ex.Message}");
		}
	}

	private async void OnRailAssetInteractionRequested(object? sender, AssetOpenRequest request)
	{
		bool isWorkflowInsert = request.Mode == AssetInteractionMode.Workflow &&
			request.Action == AssetInteractionAction.Insert;
		if (!isWorkflowInsert &&
			request.Mode is not (AssetInteractionMode.Model or AssetInteractionMode.Node or AssetInteractionMode.Image or AssetInteractionMode.Video))
		{
			return;
		}

		// WebView is not resized by the native rail; JS uses this only as a visual origin hint.
		request = AttachRailWidth(request);

		if (request.Action is AssetInteractionAction.Open or AssetInteractionAction.Insert)
		{
			await ComfyUI_Nexus.Ui.AssetActionDispatcher.DispatchAsync(_webViewBridge, WorkspaceControl.BrowserView, request);
			return;
		}

		if (request.Action == AssetInteractionAction.Drop)
		{
			await CompleteAssetDragAsync(request);
			return;
		}

		if (request.Action != AssetInteractionAction.DragStart)
		{
			return;
		}

		await StartRailAssetDragAsync(request);
	}

	private async Task StartRailAssetDragAsync(AssetOpenRequest request)
	{
		try
		{
			_isAssetDragActive = true;
			_activeAssetDragRequest = request;
			_assetDragSawPrimaryPressed = PlatformManager.Current.Cursor.IsPrimaryPointerPressed();
			_assetDragStartedUtc = DateTime.UtcNow;
			_lastAppliedCursor = CssCursorNames.Grabbing;
			ShowAssetDragGhost(request);
			PlatformManager.Current.Cursor.SetCursor(WorkspaceControl.BrowserView, NexusCursorShape.Grabbing);
			if (request.Mode == AssetInteractionMode.Image)
			{
				await _webViewBridge.NotifyAssetDropFeedbackSourceAsync(request);
			}
			else
			{
				await _webViewBridge.NotifyAssetDragStartAsync(request);
			}
		}
		catch (Exception ex)
		{
			_isAssetDragActive = false;
			_assetDragSawPrimaryPressed = false;
			_activeAssetDragRequest = null;
			HideAssetDragGhost();
			PlatformManager.Current.Cursor.SetCursor(WorkspaceControl.BrowserView, NexusCursorShape.Arrow);
			Log($"Asset drag intent failed: {ex.GetType().Name} - {ex.Message}");
		}
	}

	private void OnRailComfyMenuRequested(object? sender, EventArgs e)
	{
		_ = _webViewBridge.ToggleMainMenuAsync(GetRailContentPanelWidth());
	}

	private async void OnRailMainMenuDismissRequested(object? sender, EventArgs e)
	{
		await CloseMainMenuThenAsync();
	}

	private double GetRailContentPanelWidth()
		=> _isFileRailExpanded
			? Math.Max(0, _expandedRailWidth - ShellLayoutOptions.CollapsedRailWidth)
			: 0;

	private async void OnRailAppsToggled(object? sender, EventArgs e)
	{
		await CloseMainMenuThenAsync(_webViewBridge.ToggleAppsAsync);
	}

	private async void OnRailSettingsRequested(object? sender, EventArgs e)
	{
		await CloseMainMenuThenAsync(_webViewBridge.OpenSettingsAsync);
	}

	private async void OnRailTemplatesRequested(object? sender, EventArgs e)
	{
		await CloseMainMenuThenAsync(() => _webViewBridge.InvokeActionAsync(BridgeActions.ToggleTemplates));
	}

	private async Task CloseMainMenuThenAsync(Func<Task>? nextAction = null)
	{
		try
		{
			await _webViewBridge.CloseMainMenuAsync();
			if (nextAction != null)
			{
				await nextAction();
			}
		}
		catch (Exception ex)
		{
			Log($"Main menu action failed: {ex.GetType().Name} - {ex.Message}");
		}
	}

	private bool IsPointerOverAssetDropSurface()
	{
		var rootPoint = PlatformManager.Current.Cursor.GetPointerPositionRelativeTo(RootLayoutGrid);
		if (rootPoint is null)
		{
			return false;
		}

		double blockedLeftWidth =
			(_isControlDeckVisible ? ShellLayoutScale.H(ShellLayoutOptions.ControlDeckExpandedWidth, 200) : 0)
			+ (_isFileRailExpanded ? _expandedRailWidth : ShellLayoutScale.H(ShellLayoutOptions.CollapsedRailWidth, ShellLayoutOptions.CollapsedRailWidth));
		double topLimit = HeaderControl.Height;
		double bottomLimit = Math.Max(topLimit, RootLayoutGrid.Height - ToolbarControl.Height);
		bool insideWorkspaceBand = rootPoint.Value.Y >= topLimit && rootPoint.Value.Y <= bottomLimit;
		bool outsideLeftChrome = rootPoint.Value.X > blockedLeftWidth;

		return outsideLeftChrome && insideWorkspaceBand;
	}

	private void ShowAssetDragGhost(AssetOpenRequest request)
	{
		AssetDragGhostIcon.Text = request.Mode switch
		{
			AssetInteractionMode.Node => "NODE",
			AssetInteractionMode.Image => "IMAGE",
			AssetInteractionMode.Video => "VIDEO",
			_ => "MODEL",
		};
		AssetDragGhostLabel.Text = request.DisplayName ?? request.Name;
		AssetDragGhost.IsVisible = true;
		AssetDragGhost.Opacity = 0.92;
		AssetDragGhost.Scale = 1;
		UpdateAssetDragGhostPosition();

		_assetDragGhostTimer ??= Dispatcher.CreateTimer();
		_assetDragGhostTimer.Interval = TimeSpan.FromMilliseconds(16);
		_assetDragGhostTimer.Tick -= OnAssetDragGhostTimerTick;
		_assetDragGhostTimer.Tick += OnAssetDragGhostTimerTick;
		_assetDragGhostTimer.Start();
	}

	private async Task CompleteAssetDragAsync(AssetOpenRequest request)
	{
		if (_isAssetDragCompleting)
		{
			return;
		}

		_isAssetDragCompleting = true;
		try
		{
			if (request.Mode == AssetInteractionMode.Image)
			{
				return;
			}

			bool dropSurface = IsPointerOverAssetDropSurface();
			if (dropSurface)
			{
				await _webViewBridge.NotifyAssetOpenAsync(AttachAssetDropClientPosition(request));
			}
		}
		catch (Exception ex)
		{
			Log($"Asset drag fallback failed: {ex.GetType().Name} - {ex.Message}");
		}
		finally
		{
			_activeAssetDragRequest = null;
			_isAssetDragActive = false;
			_assetDragSawPrimaryPressed = false;
			_lastAppliedCursor = CssCursorNames.Default;
			HideAssetDragGhost();
			PlatformManager.Current.Cursor.SetCursor(WorkspaceControl.BrowserView, NexusCursorShape.Arrow);
			_isAssetDragCompleting = false;
		}
	}

	private void HideAssetDragGhost()
	{
		if (_assetDragGhostTimer is not null)
		{
			_assetDragGhostTimer.Stop();
			_assetDragGhostTimer.Tick -= OnAssetDragGhostTimerTick;
		}

		AssetDragGhost.IsVisible = false;
		AssetDragGhost.Opacity = 0;
		AssetDragGhost.Scale = 0.96;
	}

	private void OnAssetDragGhostTimerTick(object? sender, EventArgs e)
	{
		if (!_isAssetDragActive)
		{
			HideAssetDragGhost();
			return;
		}

		UpdateAssetDragGhostPosition();

		if (_activeAssetDragRequest is not { } request)
		{
			return;
		}

		bool isPressed = PlatformManager.Current.Cursor.IsPrimaryPointerPressed();
		if (isPressed)
		{
			_assetDragSawPrimaryPressed = true;
			return;
		}

		if (_assetDragSawPrimaryPressed && (DateTime.UtcNow - _assetDragStartedUtc).TotalMilliseconds > 80)
		{
			if (request.Mode == AssetInteractionMode.Image)
			{
				return;
			}

			_ = CompleteAssetDragAsync(request);
		}
	}

	private void UpdateAssetDragGhostPosition()
	{
		var point = PlatformManager.Current.Cursor.GetPointerPositionRelativeTo(RootLayoutGrid);
		if (point is null)
		{
			return;
		}

		AssetDragGhost.TranslationX = point.Value.X + 14;
		AssetDragGhost.TranslationY = point.Value.Y + 16;
	}

	private Task CompleteActiveAssetDragFromPointerReleaseAsync()
	{
		if (!_isAssetDragActive || _activeAssetDragRequest is not { } request)
		{
			return Task.CompletedTask;
		}

		return CompleteAssetDragAsync(request);
	}

	private AssetOpenRequest AttachAssetDropClientPosition(AssetOpenRequest request)
	{
		var point = PlatformManager.Current.Cursor.GetPointerPositionRelativeTo(WorkspaceControl.BrowserView);
		if (point is null)
		{
			return request;
		}

		return request with
		{
			DropClientX = point.Value.X,
			DropClientY = point.Value.Y,
		};
	}

	private AssetOpenRequest AttachRailWidth(AssetOpenRequest request)
	{
		double railWidth =
			(_isControlDeckVisible ? ShellLayoutScale.H(ShellLayoutOptions.ControlDeckExpandedWidth, 200) : 0)
			+ (_isFileRailExpanded ? _expandedRailWidth : ShellLayoutScale.H(ShellLayoutOptions.CollapsedRailWidth, ShellLayoutOptions.CollapsedRailWidth));
		return request with { RailWidth = railWidth };
	}

	private void OnLayoutScaleChanged()
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			ApplyRailState();
			RefreshAvailableWidthAndTabs(ShellLayoutInvalidationReason.WindowSizeChanged);
		});
	}

	partial void InitializePlatformHooks();
}
