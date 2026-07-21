using ComfyUI_Nexus.AssetHub;
using ComfyUI_Nexus.Boot;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Input;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Ui.Popups;
using ComfyUI_Nexus.Views.Rail.Tools.Assets;
using ComfyUI_Nexus.Views.Rail.Tools.NodeLibrary;

namespace ComfyUI_Nexus;

public partial class MainPage : ContentPage
{
	private readonly WorkflowTabController _tabController;
	private readonly LoadingOverlayController _loadingOverlayController;
	private readonly NexusServerLifecycleCoordinator _serverLifecycle;
	private readonly HeaderGpuStatusController _gpuStatusController;
	private readonly NexusWebViewBridge _webViewBridge;
	private readonly NexusControlDeckWindowService _controlDeckWindow = new();
	private readonly NexusUiSurfaceManager _uiSurfaceManager = new();
	private NexusPopupManager _popupManager = null!;
	private readonly NexusInputRouter _inputRouter;
	private readonly NexusOperationController _latestOperations = new("main-page");
	private readonly NexusOperationController _bridgeOperations = new("main-page-bridge");
	private readonly NodeLibraryService _nodeLibraryService = new();
	private readonly ShellLayoutSignals _shellLayoutSignals = new();
	private readonly TaskCompletionSource<bool> _webViewPlatformReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly LoginSequenceOrchestrator _loginSequence = new();
	internal NodeLibraryRoot? _nodeLibrary;
	internal NodeLibraryRoot? NodeLibrary => _nodeLibrary;
	private string _nodeLibraryManifestSignature = string.Empty;
	private List<BlueprintItem>? _latestBlueprints;
	private bool _isBooted = false;
	private bool _startupSequenceStarted;
	private bool _startupSequenceDispatchPending;
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
	private System.Diagnostics.Stopwatch? _serverBootSetupRouteStopwatch;
	private int _serverBootSetupRouteGeneration;
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
#endif

	private double _lastMeasuredHeaderHeight = 0;
	private double _expandedRailWidth = ShellLayoutOptions.DefaultExpandedRailWidth;
	private double _railResizeStartWidth = ShellLayoutOptions.DefaultExpandedRailWidth;
	private double _pendingRailWidth = ShellLayoutOptions.DefaultExpandedRailWidth;
	private bool _isRailResizeHovering = false;
	private IDispatcherTimer? _toastHoldTimer;
	private EventHandler? _toastHoldTimerTick;
	private int _toastVersion;

	partial void InitializeNativeSystemTelemetry();
	partial void StartNativeSystemTelemetry();
	partial void StopNativeSystemTelemetry();

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
			_serverLifecycle = (Application.Current as App)?.ServerLifecycle
				?? throw new InvalidOperationException("Nexus server lifecycle coordinator is unavailable.");
			_inputRouter = new NexusInputRouter(_uiSurfaceManager);
			_webViewBridge = new NexusWebViewBridge(() => WorkspaceControl?.BrowserSurface);
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
			_loadingOverlayController = new LoadingOverlayController(LoadingOverlayControl, HeaderControl);
			_loadingOverlayController.SetNexusAppEntry(new DelegateNexusAppEntry(LaunchNexusAppEntryAsync));
			_loadingOverlayController.SetServerLifecycleRunner(RunServerLifecycleFromLoadingAsync);
			_serverLifecycle.AttachShell(this, new ServerLifecycleShellHooks(
				PrepareShellForServerInterruptionAsync,
				QuiesceShellRuntimeServicesAsync,
				StartShellRuntimeServicesAsync));
			_serverLifecycle.StateChanged += OnServerLifecycleStateChanged;
			_serverLifecycle.LogEmitted += OnServerLifecycleLogEmitted;
			_gpuStatusController = new HeaderGpuStatusController(this, HeaderControl);
			HeaderControl.Loaded += OnHeaderControlLoaded;
			HeaderControl.GpuVisualSurfaceLoaded += OnGpuVisualSurfaceLoaded;
			HeaderControl.Unloaded += OnHeaderControlUnloaded;
			_shellLayoutSignals.LayoutInvalidated += OnShellLayoutInvalidated;
			InitializeComfyPaths();
			NexusDialogService.Register(NexusDialogOverlayControl);
			NexusDialogService.Closed += OnNexusDialogClosed;
			InitializePopupManager();
			WireSubviewEvents();
			InitializeKeyboardSurfaces();
			RefreshBridgeDiagnosticsButtonState();

			Log("Nexus Shell starting...");

			InitializePlatformHooks();
			InitializeNativeSystemTelemetry();
			ApplyTabUiTuning();

			ApplyRailState();
			InitializeRailRoot();
			Loaded += OnMainPageLoaded;
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
				TryStartStartupSequenceAfterLayout();
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
		_controlDeckWindow.Close();
		_loadingOverlayController.Stop();
		RailControl?.StopPrewarmForShutdown();
		StopShellRuntimeServicesForUnload();
		StopToastHoldTimer();
		_serverLifecycle.StateChanged -= OnServerLifecycleStateChanged;
		_serverLifecycle.LogEmitted -= OnServerLifecycleLogEmitted;
		_serverLifecycle.DetachShell(this);
		HeaderControl.Unloaded -= OnHeaderControlUnloaded;
		HeaderControl.Loaded -= OnHeaderControlLoaded;
		HeaderControl.GpuVisualSurfaceLoaded -= OnGpuVisualSurfaceLoaded;
		_loginSequence.Cancel();
		_latestOperations.StopAll();
		_bridgeOperations.StopAll();
		NexusDialogService.Closed -= OnNexusDialogClosed;
		NexusDialogService.Unregister(NexusDialogOverlayControl);
	}

	private void OnMainPageLoaded(object? sender, EventArgs e)
	{
		TryStartStartupSequenceAfterLayout();
	}

	private void TryStartStartupSequenceAfterLayout()
	{
		if (_startupSequenceStarted || _startupSequenceDispatchPending || Handler == null || Width <= 0 || Height <= 0)
		{
			return;
		}

		_startupSequenceDispatchPending = true;
		Dispatcher.Dispatch(() =>
		{
			_startupSequenceDispatchPending = false;
			if (_startupSequenceStarted || _isShuttingDown || Handler == null || Width <= 0 || Height <= 0)
			{
				return;
			}

			_startupSequenceStarted = true;
			NexusLog.Info($"[STARTUP] MainPage layout ready. size={Width:0}x{Height:0}");
			StartOverlayAnimations();
			StartStartupLoginSequence();
		});
	}

	private void OnHeaderControlUnloaded(object? sender, EventArgs e)
	{
		_gpuStatusController.Stop();
	}

	private async void OnHeaderControlLoaded(object? sender, EventArgs e)
	{
		await PrepareHeaderGpuVisualsAsync();
	}

	private async void OnGpuVisualSurfaceLoaded(object? sender, EventArgs e)
	{
		await PrepareHeaderGpuVisualsAsync();
	}

	private async Task PrepareHeaderGpuVisualsAsync()
	{
		await _gpuStatusController.PrepareSurfacesAsync();
		_gpuStatusController.RestoreAfterSurfaceAttach();
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
		WireToolbarEvents();
	}

	private void WireHeaderEvents()
	{
		HeaderControl.LogoClicked += OnLogoClicked;
		HeaderControl.LogoFiveClicked += OnLogoFiveClicked;
		HeaderControl.MainActionCommand = new Command(async () => await ExecuteHeaderMainActionAsync());
		HeaderControl.StopActionCommand = new Command(async () => await ExecuteHeaderStopActionAsync());
		HeaderControl.RunModeCommand = new Command<string>(async mode => await ExecuteHeaderRunModeAsync(mode ?? RunModeOptions.Default));
		HeaderControl.ViewQueueCommand = new Command(async () => await ExecuteHeaderViewQueueAsync());
		HeaderControl.TogglePropertiesCommand = new Command(async () => await ExecuteHeaderTogglePropertiesAsync());
		HeaderControl.ShowManagerCommand = new Command(async () => await ExecuteHeaderShowManagerAsync());
		HeaderControl.ShowFavoritesCommand = new Command(async () => await ExecuteHeaderShowFavoritesAsync());
		HeaderControl.UnloadModelsCommand = new Command(async () => await ExecuteHeaderUnloadModelsAsync());
		HeaderControl.FreeCacheCommand = new Command(async () => await ExecuteHeaderFreeCacheAsync());
		HeaderControl.ShareCommand = new Command(async () => await ExecuteHeaderShareAsync());
		HeaderControl.EnterAppModeCommand = new Command(async () => await ExecuteHeaderEnterAppModeAsync());
		HeaderControl.MainMenuCommand = new Command(async () => await ExecuteHeaderMainMenuAsync());
		HeaderControl.WorkflowActionsCommand = new Command(async () => await ExecuteHeaderWorkflowActionsAsync());
	}

	private void WireWorkspaceEvents()
	{
		WorkspaceControl.WebViewNavigated += OnWebViewNavigated;
	}

	private void WireOverlayEvents()
	{
		LoadingOverlayControl.SelectCoreRequested += OnSelectCoreClicked;
		LoadingOverlayControl.RetryRequested += OnLoadingRetryRequested;
		LoadingOverlayControl.ServerBootSetupRequested += OnServerBootSetupRequested;
		LoadingOverlayControl.GetComfyRequested += OnGetComfyClicked;
		LoadingOverlayControl.GetGitHubRequested += OnGetGitHubClicked;

		BookmarkHudControl.CloseRequested += OnCloseBookmarkHUDClicked;
		BookmarkHudControl.SearchChanged += OnBookmarkSearchChanged;
		CommandMenuControl.RestartServerCommand = new Command(async () => await RestartServerFromCommandMenuAsync());
		CommandMenuControl.SettingsCommand = new Command(async () => await ExecuteCommandMenuSettingsAsync());
		CommandMenuControl.HelpCommand = new Command(async () => await ExecuteCommandMenuHelpAsync());
		CommandMenuControl.AboutCommand = new Command(async () => await ExecuteCommandMenuAboutAsync());
		CommandMenuControl.ExitCommand = new Command(async () => await ExecuteCommandMenuExitAsync());
		SettingsOverlayControl.CloseRequested += OnSettingsOverlayCloseRequested;
		SettingsOverlayControl.RestartServerRequested += OnSettingsOverlayRestartServerRequested;
		SettingsOverlayControl.RuntimePurgeRequested += OnSettingsOverlayRuntimePurgeRequested;
		SettingsOverlayControl.RuntimeRestoreRequested += OnSettingsOverlayRuntimeRestoreRequested;
		HelpOverlayControl.CloseRequested += OnHelpOverlayCloseRequested;
		AboutOverlayControl.CloseRequested += OnAboutOverlayCloseRequested;
		WorkflowActionsMenuControl.ActionRequested += OnWorkflowActionsMenuActionRequested;
		WorkflowActionsMenuControl.DismissRequested += OnWorkflowActionsMenuDismissRequested;
		CanvasModeMenuControl.SelectCommand = new Command(async () => await ExecuteCanvasModeMenuModeAsync(CanvasModeOptions.Select));
		CanvasModeMenuControl.HandCommand = new Command(async () => await ExecuteCanvasModeMenuModeAsync(CanvasModeOptions.Hand));
		CanvasModeMenuControl.DismissCommand = new Command(async () => await SetCanvasModeMenuVisible(false));
		CommandInputControl.Completed += OnCommandInputCompleted;
		CommandInputControl.BackdropTapped += OnCommandInputBackdropTapped;
	}

	private INexusControlDeck? CurrentControlDeck => _controlDeckWindow.CurrentDeck;

	internal void CloseControlDeckWindow()
		=> _controlDeckWindow.Close();

	private void ConfigureControlDeck(INexusControlDeck deck)
	{
		deck.ManualRebootCommand = new Command(async () => await ExecuteControlDeckManualRebootAsync());
		deck.BootServerCommand = new Command(async () => await ExecuteControlDeckBootServerAsync());
		deck.ShutdownServerCommand = new Command(async () => await ExecuteControlDeckShutdownServerAsync());
		deck.ToggleBridgeDiagnosticsCommand = new Command(ExecuteControlDeckToggleBridgeDiagnostics);
		deck.ToggleWebLogsCommand = new Command(async () => await ExecuteControlDeckToggleWebLogsAsync());
		deck.ToggleDevToolsCommand = new Command(ExecuteControlDeckToggleDevTools);
		deck.ToggleUiIsolationCommand = new Command(async () => await ExecuteControlDeckToggleUiIsolationAsync());
		deck.PatchLocalHudCommand = new Command(async () => await ExecuteControlDeckPatchLocalHudAsync());
		deck.PatchNexusBridgeCommand = new Command(async () => await ExecuteControlDeckPatchNexusBridgeAsync());
		deck.OpenFullLogCommand = new Command(async () => await ExecuteControlDeckOpenFullLogAsync());
		deck.ClearLogCommand = new Command(ExecuteControlDeckClearLog);
		deck.SetLogFileRelativePath(GetControlDeckLogRelativePath());
		deck.SetBridgeDiagnosticsState(_bridgeDiagnosticsEnabled);
		deck.SetWebLogsState(_webLogsEnabled);
		deck.SetDevToolsState(_devToolsController.IsEnabled);
		deck.SetUiIsolationState(_uiIsolationEnabled);
		deck.SetPulseRun(_pulseIsRunning, _pulseInstantStop);
		deck.SetPulseWeb(
			isBridgeLive: _bridgeSession.Snapshot.State == NexusBridgeSessionState.Live,
			serverStatus: _controlDeckServerStatus,
			errorCount: _webPulseErrorCount,
			bridgeTraceEnabled: _bridgeDiagnosticsEnabled,
			webLogsEnabled: _webLogsEnabled,
			devToolsEnabled: _devToolsController.IsEnabled);
		deck.SetLogText(CreateLogText(_allLogs));
		deck.LogSearchChanged += OnLogSearchChanged;
	}

	private void WireToolbarEvents()
	{
		ToolbarControl.ModeCommand = new Command(async () => await ExecuteToolbarModeAsync());
		ToolbarControl.FitViewCommand = new Command(async () => await ExecuteToolbarFitViewAsync());
		ToolbarControl.ZoomCommand = new Command(async () => await ExecuteToolbarZoomAsync());
		ToolbarControl.MinimapCommand = new Command(async () => await ExecuteToolbarMinimapAsync());
		ToolbarControl.LinksCommand = new Command(async () => await ExecuteToolbarLinksAsync());
		ToolbarControl.HelpCommand = new Command(async () => await ExecuteToolbarHelpAsync());
		ToolbarControl.TerminalCommand = new Command(async () => await ExecuteToolbarTerminalAsync());
		ToolbarControl.ShortcutsCommand = new Command(async () => await ExecuteToolbarShortcutsAsync());
	}

	private void WireRailEvents()
	{
		RailControl.RailOpenRequestedAsync += ExpandRailAsync;
		RailControl.RailCloseRequested += CollapseRailImmediately;
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
			double railRight = GetTargetRailWidth();
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
					WorkspaceControl.BrowserSurface,
						request.FullPath,
						WorkflowTabController.StripWorkflowPrefix(relativePath));
					return;
				}
			}

			await ComfyUI_Nexus.Ui.AssetActionDispatcher.DispatchAsync(_webViewBridge, WorkspaceControl.BrowserSurface, request);
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
			await ComfyUI_Nexus.Ui.AssetActionDispatcher.DispatchAsync(_webViewBridge, WorkspaceControl.BrowserSurface, request);
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
			PlatformManager.Current.Cursor.SetCursor(WorkspaceControl.BrowserSurface.InputElement, NexusCursorShape.Grabbing);
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
			PlatformManager.Current.Cursor.SetCursor(WorkspaceControl.BrowserSurface.InputElement, NexusCursorShape.Arrow);
			Log($"Asset drag intent failed: {ex.GetType().Name} - {ex.Message}");
		}
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
			0
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
			PlatformManager.Current.Cursor.SetCursor(WorkspaceControl.BrowserSurface.InputElement, NexusCursorShape.Arrow);
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
		var point = PlatformManager.Current.Cursor.GetPointerPositionRelativeTo(WorkspaceControl.BrowserSurface.InputElement);
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
			0
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
