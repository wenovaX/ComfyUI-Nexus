using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Views.Overlays;
using ComfyUI_Nexus.Views.Controls.Buttons;
using ComfyUI_Nexus.Views.Rail.Contracts;
using ComfyUI_Nexus.Views.Rail.Tools.Assets;
using ComfyUI_Nexus.Views.Rail.Tools.MediaAssets;
using ComfyUI_Nexus.Views.Rail.Tools.NodeLibrary;

namespace ComfyUI_Nexus.Views.Rail;

public partial class RailView : ContentView
{
	private const int RailRevealFrameRate = 16;
	private const double RailRevealMaxOffset = 34;
	private const double RailRevealStartOpacity = 0.36;
	private const double RailRevealEndOpacity = 0.82;
	private const string RailRevealAnimationName = "RailRevealAnim";
	private const uint RailContentRevealLength = 120;
	private const string RailContentRevealAnimationName = "RailContentRevealAnim";
	private static readonly Brush CollapseFlagIdleFill = new SolidColorBrush(Color.FromArgb("#7A31D8FF"));
	private static readonly Brush CollapseFlagHoverFill = new SolidColorBrush(Color.FromArgb("#D031D8FF"));
	private static readonly Brush CollapseFlagIdleGlowFill = new SolidColorBrush(Color.FromArgb("#2431D8FF"));
	private static readonly Brush CollapseFlagHoverGlowFill = new SolidColorBrush(Color.FromArgb("#6631D8FF"));

	private sealed record RailToolRegistration(
		RailToolKind Kind,
		RailToolButton Button);

	private enum RailAuxiliarySurface
	{
		None,
		Apps,
		Templates,
		Settings,
	}

	private enum RailState
	{
		Ready,
		Transitioning,
	}

	private bool _isToolPanelOpen;
	private readonly NexusAppManager _appManager;
	private RailToolKind _activeTool = RailToolKind.Assets;
	private RailAuxiliarySurface _activeAuxiliarySurface = RailAuxiliarySurface.None;
	// Native clicks can beat the web-side state observer; keep the button active until web confirms the opened surface.
	private RailAuxiliarySurface _awaitingWebConfirmedAuxiliarySurface = RailAuxiliarySurface.None;
	private RailState _railState = RailState.Ready;
	private readonly List<RailToolRegistration> _toolRegistrations = [];
	private CancellationTokenSource? _prewarmCts;
	private readonly NexusOperationController _latestOperations;
	private readonly Color _contentCardSurfaceColor;
	private bool _prewarmStoppedForShutdown;
	private Task? _activeTransitionTask;
	private string? _activeTransitionKey;
	private int _transitionVersion;
	private int _railRequestVersion;
	private bool _isUserTransitionLocked;

	internal event Func<CancellationToken, Task<bool>>? RailOpenRequestedAsync;
	internal event Action? RailCloseRequested;
	internal event EventHandler? MainMenuDismissRequested;
	internal event EventHandler? AppsToggled;
	internal event EventHandler? SettingsRequested;
	internal event EventHandler? TemplatesRequested;
	internal event EventHandler<AssetOpenRequest>? FileOpenRequested;
	internal event EventHandler<AssetOpenRequest>? AssetInteractionRequested;
	internal event EventHandler<MediaAssetViewerRequest>? MediaAssetViewerRequested;
	internal event EventHandler? WorkflowBookmarksChanged;
	internal event EventHandler<ModelAssetThumbnailPreviewRequest>? ModelThumbnailPreviewRequested;
	internal event EventHandler? ModelThumbnailPreviewDismissed;

	public RailView()
	{
		_appManager = NexusAppManager.Instance;
		_latestOperations = new NexusOperationController("rail-view", _appManager.BackgroundWorkers);
		InitializeComponent();
		_contentCardSurfaceColor = RailContentCard.BackgroundColor ?? Color.FromArgb("#0b1119");
		Unloaded += OnRailUnloaded;

		RegisterTools();
		WirePermanentToolEvents();

		StageToolView(RailToolKind.Assets, showContentHost: false);
		AppsToggleButton.Clicked += (s, e) => ToggleAuxiliarySurface(RailAuxiliarySurface.Apps);
		SettingsButton.Clicked += (s, e) => ToggleAuxiliarySurface(RailAuxiliarySurface.Settings);
		TemplatesButton.Clicked += (s, e) => ToggleAuxiliarySurface(RailAuxiliarySurface.Templates);
		SetCollapseFlagHover(false);
	}

	// Tool panel
	internal Task RequestToggleAsync()
		=> RunUserTransitionAsync(async () =>
		{
			MainMenuDismissRequested?.Invoke(this, EventArgs.Empty);
			if (_isToolPanelOpen)
			{
				await CloseAsync();
				return;
			}

			await OpenToolAsync(_activeTool);
		});

	private Task CloseAsync()
	{
		if (!_isToolPanelOpen &&
			_activeAuxiliarySurface == RailAuxiliarySurface.None &&
			_activeTransitionTask == null)
		{
			return Task.CompletedTask;
		}

		CancelTransition();
		ResetToolPresentations();
		ApplyClosedPanel();
		XamlLifetimeDiagnostics.RemoveSurface("rail");
		return Task.CompletedTask;
	}

	private void OnCollapseFlagTapped(object? sender, TappedEventArgs e)
		=> _ = RunUserTransitionAsync(CloseAsync);

	private void OnCollapseFlagPointerEntered(object? sender, PointerEventArgs e)
		=> SetCollapseFlagHover(true);

	private void OnCollapseFlagPointerExited(object? sender, PointerEventArgs e)
		=> SetCollapseFlagHover(false);

	private void SetCollapseFlagHover(bool isHovered)
	{
		RailCollapseFlagShape.Fill = isHovered ? CollapseFlagHoverFill : CollapseFlagIdleFill;
		RailCollapseFlagGlow.Fill = isHovered ? CollapseFlagHoverGlowFill : CollapseFlagIdleGlowFill;
		RailCollapseFlagGlow.Opacity = isHovered ? 0.58 : 0.32;
		RailCollapseFlagChevron.Opacity = isHovered ? 1 : 0.82;
	}

	private void ClosePanelKeepAux()
	{
		if (!_isToolPanelOpen &&
			_activeTransitionTask == null)
		{
			return;
		}

		CancelTransition();
		ResetToolPresentations();
		ApplyClosedPanel(clearAuxiliarySurface: false);
	}

	// Event wiring
	private void WirePermanentToolEvents()
	{
		AssetsToolView.FileOpenRequested += OnFileOpenRequested;
		AssetsToolView.AssetInteractionRequested += OnAssetInteractionRequested;
		AssetsToolView.WorkflowBookmarksChanged += OnWorkflowBookmarksChanged;
		AssetsToolView.ModelThumbnailPreviewRequested += OnModelThumbnailPreviewRequested;
		AssetsToolView.ModelThumbnailPreviewDismissed += OnModelThumbnailPreviewDismissed;
		MediaAssetsToolView.ViewerRequested += OnMediaAssetViewerRequested;
		NodeLibraryToolView.AssetInteractionRequested += OnAssetInteractionRequested;
	}

	private void RegisterTools()
	{
		_toolRegistrations.Add(new RailToolRegistration(
			RailToolKind.Assets,
			AssetsToolButton));

		_toolRegistrations.Add(new RailToolRegistration(
			RailToolKind.MediaAssets,
			MediaAssetsToolButton));

		_toolRegistrations.Add(new RailToolRegistration(
			RailToolKind.NodeLibrary,
			NodeLibraryToolButton));

		foreach (var tool in _toolRegistrations)
		{
			tool.Button.Clicked += (s, e) => HandleToolButtonClick(tool.Kind);
		}
	}

	private async void HandleToolButtonClick(RailToolKind toolKind)
		=> await RunUserTransitionAsync(async () =>
		{
			MainMenuDismissRequested?.Invoke(this, EventArgs.Empty);
			await OpenToolAsync(toolKind);
		});

	private async Task OpenToolAsync(RailToolKind toolKind)
	{
		var perf = RailPerformanceDiagnostics.Start();
		int requestVersion = Interlocked.Increment(ref _railRequestVersion);
		if (_isToolPanelOpen && _activeTool == toolKind)
		{
			RailPerformanceDiagnostics.Mark("ToggleCloseRequested", perf, toolKind.ToString());
			await CloseAsync();
			return;
		}

		RailPerformanceDiagnostics.Mark("OpenRequested", perf, $"tool={toolKind}, expanded={_isToolPanelOpen}");
		await RunTransitionAsync(
			$"tool:{toolKind}",
			async cancellationToken =>
			{
				RailPerformanceDiagnostics.Mark("TransitionEntered", perf, toolKind.ToString());
				ClearAuxiliarySurface();

				bool isAlreadyActive = _activeTool == toolKind;
				bool isExpanded = _isToolPanelOpen;
				if (!isAlreadyActive && !isExpanded)
				{
					ResetActiveToolPresentation();
				}

				StageToolView(toolKind, showContentHost: isExpanded);
				RailPerformanceDiagnostics.Mark("ToolStaged", perf, $"tool={toolKind}, expanded={isExpanded}");
				if (!isExpanded)
				{
					bool animationCompleted = await RequestRailOpenAsync(cancellationToken);
					RailPerformanceDiagnostics.Mark("RailOpenAnimationCompleted", perf, $"tool={toolKind}, completed={animationCompleted}");
					if (!animationCompleted ||
						cancellationToken.IsCancellationRequested ||
						requestVersion != _railRequestVersion)
					{
						return;
					}
				}

				RailPerformanceDiagnostics.Mark("ActivateStagedToolStart", perf, $"tool={toolKind}");
				await ActivateStagedToolAsync(toolKind, requestVersion, cancellationToken, perf);
			});
	}

	private void ApplyClosedPanel(bool clearAuxiliarySurface = true)
	{
		if (clearAuxiliarySurface)
		{
			ClearAuxiliarySurface();
		}

		_isToolPanelOpen = false;
		SetContentCardVisible(false);
		ClearTransitionOverlay();
		ApplyToolButtonSelectionState();
		RailCloseRequested?.Invoke();
	}

	// Web surfaces
	private async void ToggleAuxiliarySurface(RailAuxiliarySurface targetSurface)
		=> await RunUserTransitionAsync(() => ToggleAuxiliarySurfaceCoreAsync(targetSurface));

	private async Task ToggleAuxiliarySurfaceCoreAsync(RailAuxiliarySurface targetSurface)
	{
		bool isAlreadyActive = _activeAuxiliarySurface == targetSurface;
		await CloseAsync();

		if (isAlreadyActive)
		{
			return;
		}

		_activeAuxiliarySurface = targetSurface;
		_awaitingWebConfirmedAuxiliarySurface = targetSurface;
		SyncAuxiliaryButtons();
		RaiseAuxiliarySurfaceEvent(targetSurface);
	}

	private async Task RunUserTransitionAsync(Func<Task> transition)
	{
		if (_isUserTransitionLocked)
		{
			return;
		}

		_isUserTransitionLocked = true;
		InputTransparent = true;
		try
		{
			await transition();
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[RAIL_VIEW] User transition failed");
		}
		finally
		{
			InputTransparent = false;
			_isUserTransitionLocked = false;
		}
	}

	private void ClearAuxiliarySurface()
	{
		if (_activeAuxiliarySurface == RailAuxiliarySurface.None)
		{
			SyncAuxiliaryButtons();
			return;
		}

		RailAuxiliarySurface previousSurface = _activeAuxiliarySurface;
		_activeAuxiliarySurface = RailAuxiliarySurface.None;
		_awaitingWebConfirmedAuxiliarySurface = RailAuxiliarySurface.None;
		SyncAuxiliaryButtons();
		RaiseAuxiliarySurfaceEvent(previousSurface);
	}

	private void RaiseAuxiliarySurfaceEvent(RailAuxiliarySurface surface)
	{
		switch (surface)
		{
			case RailAuxiliarySurface.Apps:
				AppsToggled?.Invoke(this, EventArgs.Empty);
				break;
			case RailAuxiliarySurface.Templates:
				TemplatesRequested?.Invoke(this, EventArgs.Empty);
				break;
			case RailAuxiliarySurface.Settings:
				SettingsRequested?.Invoke(this, EventArgs.Empty);
				break;
		}
	}

	private void SyncAuxiliaryButtons()
	{
		AppsToggleButton.IsSelected = _activeAuxiliarySurface == RailAuxiliarySurface.Apps;
		TemplatesButton.IsSelected = _activeAuxiliarySurface == RailAuxiliarySurface.Templates;
		SettingsButton.IsSelected = _activeAuxiliarySurface == RailAuxiliarySurface.Settings;
		ApplyToolButtonSelectionState();
	}

	// Tool content
	private void StageToolView(RailToolKind toolKind, bool showContentHost)
	{
		RailToolKind previousTool = _activeTool;
		if (previousTool != toolKind && GetToolView(previousTool) is IRailToolView previousToolView)
		{
			previousToolView.ResetPresentation();
		}

		bool keepStagedToolLoaded = !showContentHost && RailContentCard.IsVisible && previousTool == toolKind;
		_activeTool = toolKind;
		if (toolKind != RailToolKind.MediaAssets || !showContentHost)
		{
			MediaAssetsToolView.SetRenderDeferred(true);
		}

		if (showContentHost)
		{
			SetContentCardVisible(true);
		}
		else
		{
			RailContentCard.InputTransparent = true;
		}

		if (GetToolView(toolKind) is View view)
		{
			if (showContentHost || keepStagedToolLoaded)
			{
				ActivateToolLayer(view, isActive: showContentHost || keepStagedToolLoaded);
			}
			else
			{
				HideToolLayers();
			}
		}
		else
		{
			NexusLog.Warning($"[RAIL_VIEW_DIAGNOSTICS] Error: GetToolView returned null for {toolKind}");
		}

		ApplyToolButtonSelectionState();
	}

	private void ActivateToolLayer(View activeView, bool isActive)
	{
		ApplyToolLayerState(AssetsToolView, activeView, isActive);
		ApplyToolLayerState(MediaAssetsToolView, activeView, isActive);
		ApplyToolLayerState(NodeLibraryToolView, activeView, isActive);
	}

	private void HideToolLayers()
	{
		HideToolLayer(AssetsToolView);
		HideToolLayer(MediaAssetsToolView);
		HideToolLayer(NodeLibraryToolView);
	}

	private static void ApplyToolLayerState(View view, View activeView, bool isActive)
	{
		bool isTarget = ReferenceEquals(view, activeView);
		view.IsVisible = isTarget;
		view.Opacity = isTarget && isActive ? 1 : 0;
		view.InputTransparent = !isTarget || !isActive;
	}

	private static void HideToolLayer(View view)
	{
		view.IsVisible = false;
		view.Opacity = 0;
		view.InputTransparent = true;
	}

	private async Task ActivateStagedToolAsync(
		RailToolKind toolKind,
		int requestVersion,
		CancellationToken cancellationToken,
		long perf)
	{
		if (requestVersion != _railRequestVersion ||
			cancellationToken.IsCancellationRequested)
		{
			return;
		}

		bool hasTransitionOverlay = RailTransitionOverlay.IsVisible;
		SetContentCardVisible(true);
		if (!hasTransitionOverlay)
		{
			RestoreContentCardSurface();
		}
		_isToolPanelOpen = true;
		XamlLifetimeDiagnostics.RecordSurface("rail", toolKind.ToString());
		if (GetToolView(toolKind) is not IRailToolView activeTool)
		{
			return;
		}

		activeTool.PrepareOpenShell();
		// The tool shell, including its loading blocker, is ready behind the rail sweep.
		// Only the sweep fades away so the blocker cannot flash in after content is revealed.
		ActivateToolLayer(activeTool.View, isActive: true);
		ApplyToolButtonSelectionState();
		RailPerformanceDiagnostics.Mark("ContentHostActivated", perf, $"tool={toolKind}");
		if (hasTransitionOverlay &&
			!await RevealContentAsync(activeTool.View, cancellationToken))
		{
			return;
		}

		// Content opens after the rail layout is committed, with its own loading shell already visible.
		await OpenToolContentAsync(toolKind, activeTool, requestVersion, cancellationToken, perf);
	}

	private async Task OpenToolContentAsync(
		RailToolKind toolKind,
		IRailToolView activeTool,
		int requestVersion,
		CancellationToken cancellationToken,
		long perf)
	{
		try
		{
			RailPerformanceDiagnostics.Mark("ToolOpenStart", perf, $"tool={toolKind}");
			await activeTool.OpenAsync(cancellationToken);
			RailPerformanceDiagnostics.Mark("ToolOpenCompleted", perf, $"tool={toolKind}, ready={activeTool.IsReady}");
		}
		catch (OperationCanceledException)
		{
			// A newer rail transition superseded this content open.
			RailPerformanceDiagnostics.Mark("ToolOpenCanceled", perf, $"tool={toolKind}");
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[RAIL_VIEW] Rail tool open failed");
		}
	}

	private void ResetActiveToolPresentation()
	{
		if (GetToolView(_activeTool) is IRailToolView activeTool)
		{
			activeTool.ResetPresentation();
		}

		MediaAssetsToolView.SetRenderDeferred(true);
	}

	private void ResetToolPresentations()
	{
		((IRailToolView)AssetsToolView).ResetPresentation();
		((IRailToolView)MediaAssetsToolView).ResetPresentation();
		((IRailToolView)NodeLibraryToolView).ResetPresentation();
	}

	private async Task<bool> RequestRailOpenAsync(CancellationToken cancellationToken)
	{
		if (RailOpenRequestedAsync != null)
		{
			return await RailOpenRequestedAsync(cancellationToken);
		}

		NexusLog.Warning("[RAIL_VIEW] RailOpenRequestedAsync has no subscriber.");
		return false;
	}

	private View? GetToolView(RailToolKind kind) => kind switch
	{
		RailToolKind.Assets => AssetsToolView,
		RailToolKind.MediaAssets => MediaAssetsToolView,
		RailToolKind.NodeLibrary => NodeLibraryToolView,
		_ => null
	};

	private void ApplyToolButtonSelectionState()
	{
		bool isExpanded = _isToolPanelOpen;
		bool hasActiveAuxiliarySurface = _activeAuxiliarySurface != RailAuxiliarySurface.None;
		foreach (var tool in _toolRegistrations)
		{
			tool.Button.IsSelected = isExpanded && !hasActiveAuxiliarySurface && tool.Kind == _activeTool;
		}
	}

	private void OnFileOpenRequested(object? sender, AssetOpenRequest request) => FileOpenRequested?.Invoke(this, request);

	private void OnAssetInteractionRequested(object? sender, AssetOpenRequest request) => AssetInteractionRequested?.Invoke(this, request);

	private void OnWorkflowBookmarksChanged(object? sender, EventArgs e) => WorkflowBookmarksChanged?.Invoke(this, EventArgs.Empty);

	private void OnModelThumbnailPreviewRequested(object? sender, ModelAssetThumbnailPreviewRequest request)
		=> ModelThumbnailPreviewRequested?.Invoke(this, request);

	private void OnModelThumbnailPreviewDismissed(object? sender, EventArgs e)
		=> ModelThumbnailPreviewDismissed?.Invoke(this, EventArgs.Empty);

	private void OnMediaAssetViewerRequested(object? sender, MediaAssetViewerRequest request)
		=> MediaAssetViewerRequested?.Invoke(this, request);

	// External API
	internal void SetRootPath(string path)
	{
		AssetsToolView?.SetRootPath(path);

		string comfyRootPath = _appManager.Paths.ConfiguredComfyPath;
		MediaAssetsToolView?.SetComfyRootPath(
			!string.IsNullOrWhiteSpace(comfyRootPath) && Directory.Exists(comfyRootPath)
				? comfyRootPath
				: path);
	}

	internal void RefreshConfiguredComfyRoots()
	{
		string comfyRootPath = _appManager.Paths.ConfiguredComfyPath;
		AssetsToolView?.RefreshConfiguredRoots();
		MediaAssetsToolView?.SetComfyRootPath(comfyRootPath);
	}

	internal void RefreshTree()
	{
		AssetsToolView?.RefreshTree();
		MediaAssetsToolView?.RefreshAssets();
	}

	internal void RefreshMediaAssets()
	{
		MediaAssetsToolView?.RefreshAssets();
	}

	internal void SyncMediaAssetsFromJobs(IReadOnlyList<MediaAssetJobPreview> jobs)
	{
		MediaAssetsToolView?.SyncOutputJobs(jobs);
	}

	internal void SetMediaAssetDeleteHandler(Func<IReadOnlyList<MediaViewerItem>, Task<bool>> deleteHandler)
	{
		MediaAssetsToolView?.SetDeleteHandler(deleteHandler);
	}

	internal bool TryCreateMediaAssetViewerRequest(string fullPath, out MediaAssetViewerRequest request)
	{
		if (MediaAssetsToolView != null && MediaAssetsToolView.TryCreateViewerRequest(fullPath, out request))
		{
			return true;
		}

		request = new MediaAssetViewerRequest([], -1);
		return false;
	}

	internal void SetMediaAssetOutputRefreshHandler(Func<Task> outputRefreshHandler)
	{
		MediaAssetsToolView?.SetOutputRefreshHandler(outputRefreshHandler);
	}

	internal void SetMediaAssetStaleOutputJobCleanupHandler(Func<IReadOnlyList<string>, Task> staleOutputJobCleanupHandler)
	{
		MediaAssetsToolView?.SetStaleOutputJobCleanupHandler(staleOutputJobCleanupHandler);
	}

	internal void SetAssetPathMutationHandlers(
		Func<AssetPathMutation, Task<AssetMutationPreparationResult>> prepareMutationAsync,
		Func<AssetPathMutation, bool, Task> completeMutationAsync,
		Func<int, Task> beginBatchOperationAsync,
		Func<Task> endBatchOperationAsync)
	{
		AssetsToolView?.SetPathMutationHandlers(
			prepareMutationAsync,
			completeMutationAsync,
			beginBatchOperationAsync,
			endBatchOperationAsync);
	}

	internal void SyncNodeLibraryTree(bool forceRebuild = false)
	{
		NodeLibraryToolView?.RefreshTree(forceRebuild);
	}

	internal Task SyncNodeLibraryTreeAsync(bool forceRebuild = false)
	{
		return NodeLibraryToolView?.RefreshTreeAndWaitAsync(forceRebuild) ?? Task.CompletedTask;
	}

	internal string FixedWorkflowsPath
	{
		get => AssetsToolView?.FixedWorkflowsPath ?? string.Empty;
		set
		{
			if (AssetsToolView != null)
			{
				AssetsToolView.FixedWorkflowsPath = value;
			}
		}
	}

	internal void SetAppsSelected(bool isSelected)
	{
		SyncAuxiliarySurface(RailAuxiliarySurface.Apps, isSelected, collapseToolPanel: isSelected);
	}

	internal void SetAppModeSurfaceActive(bool isActive)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			AppsToggleButton.IsVisible = !isActive;
			AppsToggleButton.IsEnabled = !isActive;
			AppsToggleButton.InputTransparent = isActive;

			if (isActive && _activeAuxiliarySurface == RailAuxiliarySurface.Apps)
			{
				_activeAuxiliarySurface = RailAuxiliarySurface.None;
				_awaitingWebConfirmedAuxiliarySurface = RailAuxiliarySurface.None;
				SyncAuxiliaryButtons();
			}
		});
	}

	internal void SetSettingsSelected(bool isSelected)
	{
		SyncAuxiliarySurface(RailAuxiliarySurface.Settings, isSelected);
	}

	internal void SetTemplatesSelected(bool isSelected)
	{
		SyncAuxiliarySurface(RailAuxiliarySurface.Templates, isSelected);
	}

	internal void DismissAuxiliarySurface()
	{
		_activeAuxiliarySurface = RailAuxiliarySurface.None;
		_awaitingWebConfirmedAuxiliarySurface = RailAuxiliarySurface.None;
		SyncAuxiliaryButtons();
	}

	private void SyncAuxiliarySurface(RailAuxiliarySurface surface, bool isSelected, bool collapseToolPanel = false)
	{
		if (isSelected)
		{
			if (collapseToolPanel)
			{
				ClosePanelKeepAux();
			}

			_activeAuxiliarySurface = surface;
			_awaitingWebConfirmedAuxiliarySurface = RailAuxiliarySurface.None;
		}
		else if (_activeAuxiliarySurface == surface)
		{
			if (_awaitingWebConfirmedAuxiliarySurface == surface)
			{
				return;
			}

			_activeAuxiliarySurface = RailAuxiliarySurface.None;
		}

		SyncAuxiliaryButtons();
	}

	// Prewarm
	internal void SyncVisualState()
	{
		SyncTransitionOverlay();
		ApplyToolButtonSelectionState();
	}

	internal void PrepareForDisplay(bool isExpanded)
	{
		_isToolPanelOpen = isExpanded;
		RailToolsBorder.Opacity = 1;
		RailToolsBorder.TranslationX = 0;
		SetContentCardVisible(isExpanded);
		ApplyToolButtonSelectionState();
	}

	internal void PrepareRevealAnimation(double targetWidth)
	{
		AbortRevealAnimation();
		double revealOffset = GetRevealOffset(targetWidth);
		HideContentCardKeepingLayout();
		RailTransitionOverlay.IsVisible = true;
		RailTransitionOverlay.Opacity = RailRevealStartOpacity;
		RailTransitionOverlay.InputTransparent = false;
		RailTransitionOverlay.ScaleX = 0;
		RailTransitionOverlay.TranslationX = -revealOffset;
	}

	internal async Task<bool> AnimateRevealAsync(uint length, Easing easing, CancellationToken cancellationToken)
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("Rail.Reveal.Animate");
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}

		await NexusUiFrame.AwaitShellReadyAsync(RailContentCard, "RAIL:RevealShell");
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}

		double startOffset = RailTransitionOverlay.TranslationX;
		using var cancelRegistration = cancellationToken.Register(() => UiThread.TryBeginInvoke(AbortRevealAnimation, "Rail.Reveal.Cancel"));
		return (await SafeAnimation.TweenAsync(
			this,
			RailRevealAnimationName,
			progress =>
			{
				RailTransitionOverlay.ScaleX = progress;
				RailTransitionOverlay.TranslationX = startOffset * (1 - progress);
				RailTransitionOverlay.Opacity = RailRevealStartOpacity + ((RailRevealEndOpacity - RailRevealStartOpacity) * progress);
			},
			0,
			1,
			RailRevealFrameRate,
			length,
			easing,
			source: "Rail.Reveal"))
			&& !cancellationToken.IsCancellationRequested;
	}

	internal void CompleteRevealAnimation()
	{
		RailTransitionOverlay.Opacity = RailRevealEndOpacity;
		RailTransitionOverlay.ScaleX = 1;
		RailTransitionOverlay.TranslationX = 0;
	}

	internal void AbortRevealAnimation()
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("Rail.Reveal.Abort");
		SafeAnimation.AbortAnimation(this, RailRevealAnimationName, "Rail.Reveal");
		SafeAnimation.AbortAnimation(this, RailContentRevealAnimationName, "Rail.ContentReveal");
		SafeAnimation.CancelAnimations(RailTransitionOverlay, "Rail.Reveal");
		SafeAnimation.CancelAnimations(RailContentCard, "Rail.ContentReveal");
	}

	private async Task<bool> RevealContentAsync(View content, CancellationToken cancellationToken)
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("Rail.ContentReveal.Animate");
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}

		await NexusUiFrame.AwaitShellReadyAsync(content, "RAIL:ContentReveal");
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}

		double overlayOpacity = RailTransitionOverlay.Opacity;
		RestoreContentCardSurface();
		using var cancelRegistration = cancellationToken.Register(() => UiThread.TryBeginInvoke(AbortRevealAnimation, "Rail.ContentReveal.Cancel"));
		bool completed = (await SafeAnimation.TweenAsync(
			this,
			RailContentRevealAnimationName,
			progress =>
			{
				RailTransitionOverlay.Opacity = overlayOpacity * (1 - progress);
			},
			0,
			1,
			RailRevealFrameRate,
			RailContentRevealLength,
			Easing.CubicOut,
			source: "Rail.ContentReveal"))
			&& !cancellationToken.IsCancellationRequested;

		if (!completed)
		{
			return false;
		}

		content.Opacity = 1;
		content.InputTransparent = false;
		ClearTransitionOverlay();
		return true;
	}

	internal async Task PrewarmContentAsync()
		=> await PrewarmContentAsync(ShellLayoutOptions.DefaultExpandedRailWidth);

	internal async Task PrewarmContentAsync(double targetWidth)
	{
		if (_prewarmStoppedForShutdown)
		{
			return;
		}

		if (_prewarmCts is not null)
		{
			return;
		}

		var prewarmCts = new CancellationTokenSource();
		_prewarmCts = prewarmCts;
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("Rail.PrewarmContent");
		var snapshot = CapturePrewarmVisualState();

		try
		{
			NexusLog.Trace($"[RAIL_VIEW] Prewarm rail data start. targetWidth={targetWidth:0.##}");

			foreach (RailToolKind toolKind in GetStartupPrewarmTools())
			{
				await PrewarmToolDataSequentiallyAsync(toolKind, prewarmCts.Token);
				await YieldBetweenStartupPrewarmStepsAsync(prewarmCts.Token, toolKind);
			}

			NexusLog.Trace("[RAIL_VIEW] Startup render prewarm skipped; data and view pools are ready.");
		}
		finally
		{
			if (ReferenceEquals(_prewarmCts, prewarmCts))
			{
				_prewarmCts = null;
			}

			if (!prewarmCts.IsCancellationRequested)
			{
				RestorePrewarmVisualState(snapshot);
				NexusLog.Trace("[RAIL_VIEW] Prewarm rail data completed.");
			}

			prewarmCts.Dispose();
		}
	}

	internal void StopPrewarmForShutdown()
	{
		_prewarmStoppedForShutdown = true;
		_prewarmCts?.Cancel();
	}

	private async Task PrewarmToolDataSequentiallyAsync(RailToolKind toolKind, CancellationToken cancellationToken)
	{
		if (GetToolView(toolKind) is not IRailToolView tool)
		{
			return;
		}

		NexusLog.Trace($"[RAIL_VIEW] Prewarm data start: {toolKind}");
		NexusLog.Trace($"[RAIL_VIEW] Prewarm pool start: {toolKind}");
		await tool.PrewarmAsync(cancellationToken);
		NexusLog.Trace($"[RAIL_VIEW] Prewarm pool completed: {toolKind}");
		await NexusUiFrame.AwaitDispatcherTurnAsync(this, $"RAIL:Prewarm:{toolKind}");
		NexusLog.Trace($"[RAIL_VIEW] Prewarm data completed: {toolKind}");
	}

	private static IEnumerable<RailToolKind> GetStartupPrewarmTools()
	{
		yield return RailToolKind.Assets;
		yield return RailToolKind.MediaAssets;
		yield return RailToolKind.NodeLibrary;
	}

	private Task YieldBetweenStartupPrewarmStepsAsync(CancellationToken cancellationToken, RailToolKind toolKind)
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return Task.CompletedTask;
		}

		return NexusUiFrame.AwaitDispatcherTurnAsync(this, $"RAIL:Prewarm:{toolKind}:Settled");
	}

	private PrewarmVisualState CapturePrewarmVisualState()
		=> new(
			_activeTool,
			_isToolPanelOpen,
			RailContentCard.IsVisible,
			RailContentCard.Opacity,
			RailContentCard.InputTransparent,
			WidthRequest);

	private void RestorePrewarmVisualState(PrewarmVisualState snapshot)
	{
		CancelTransition();
		_activeTool = snapshot.ActiveTool;
		_isToolPanelOpen = snapshot.IsToolPanelOpen;
		RailContentCard.IsVisible = snapshot.IsContentVisible;
		RailContentCard.Opacity = snapshot.ContentOpacity;
		RailContentCard.InputTransparent = snapshot.IsContentInputTransparent;
		RailContentCard.TranslationX = 0;
		RailContentHostGrid.TranslationX = 0;
		WidthRequest = snapshot.WidthRequest;
		RestoreContentCardSurface();
		ClearTransitionOverlay();
		if (GetToolView(snapshot.ActiveTool) is View activeView)
		{
			ActivateToolLayer(activeView, snapshot.IsToolPanelOpen);
		}
		ApplyToolButtonSelectionState();
	}

	private sealed record PrewarmVisualState(
		RailToolKind ActiveTool,
		bool IsToolPanelOpen,
		bool IsContentVisible,
		double ContentOpacity,
		bool IsContentInputTransparent,
		double WidthRequest);

	// Transition
	private void CancelTransition()
	{
		// A close or replacement only invalidates the result. The in-flight transition
		// is allowed to finish and is prevented from applying by the request version.
		_latestOperations.Invalidate("transition");
		Interlocked.Increment(ref _railRequestVersion);
		_activeTransitionTask = null;
		_activeTransitionKey = null;
		SetRailState(RailState.Ready);
	}

	private void OnRailUnloaded(object? sender, EventArgs e)
	{
		_latestOperations.StopAll();
	}

	private void BeginTransition()
	{
		SetRailState(RailState.Transitioning);
	}

	private Task RunTransitionAsync(string requestKey, Func<CancellationToken, Task> transition)
	{
		BeginTransition();
		int version = _transitionVersion;
		_activeTransitionKey = requestKey;
		Task transitionTask = _latestOperations.RequestLatestAsync(
			"transition",
			lease => RunTransitionCoreAsync(requestKey, version, lease.LifecycleToken, transition));
		_activeTransitionTask = transitionTask;
		return transitionTask;
	}

	private async Task RunTransitionCoreAsync(
		string requestKey,
		int version,
		CancellationToken cancellationToken,
		Func<CancellationToken, Task> transition)
	{
		try
		{
			await transition(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			// A newer rail transition superseded this one.
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[RAIL_VIEW] Rail transition failed");
		}
		finally
		{
			if (version == _transitionVersion &&
				string.Equals(_activeTransitionKey, requestKey, StringComparison.Ordinal))
			{
				_activeTransitionTask = null;
				_activeTransitionKey = null;
				SetRailState(RailState.Ready);
			}
		}
	}

	private void SetRailState(RailState state)
	{
		_railState = state;
		_transitionVersion++;
		SyncTransitionOverlay();
	}

	private void SyncTransitionOverlay()
	{
		if (_railState == RailState.Ready)
		{
			ClearTransitionOverlay();
		}
	}

	private void ClearTransitionOverlay()
	{
		RailTransitionOverlay.IsVisible = false;
		RailTransitionOverlay.Opacity = 0;
		RailTransitionOverlay.InputTransparent = true;
		RailTransitionOverlay.ScaleX = 0;
		RailTransitionOverlay.TranslationX = 0;
	}

	private static double GetRevealOffset(double targetWidth)
	{
		double panelWidth = Math.Max(0, targetWidth - ShellLayoutOptions.CollapsedRailWidth);
		return Math.Min(RailRevealMaxOffset, panelWidth);
	}

	private void SetContentCardVisible(bool isVisible)
	{
		RailContentCard.IsVisible = isVisible;
		RailContentCard.Opacity = isVisible ? 1 : 0;
		RailContentCard.InputTransparent = !isVisible;
		RailContentCard.TranslationX = 0;
		RailContentHostGrid.TranslationX = 0;
		if (!isVisible)
		{
			HideToolLayers();
		}
	}

	// Keep the card shell alive for the gradient reveal while deferring heavy tool content until it completes.
	private void HideContentCardKeepingLayout()
	{
		RailContentCard.IsVisible = true;
		RailContentCard.Opacity = 1;
		RailContentCard.InputTransparent = true;
		RailContentCard.TranslationX = 0;
		RailContentHostGrid.TranslationX = 0;
		RailContentCard.BackgroundColor = Colors.Transparent;
		HideToolLayers();
	}

	private void RestoreContentCardSurface()
		=> RailContentCard.BackgroundColor = _contentCardSurfaceColor;

	// Keyboard
	internal bool TryHandleKeyboardShortcut(NexusKey key, bool ctrl, bool shift)
	{
		if (!_isToolPanelOpen)
		{
			return false;
		}

		return _activeTool switch
		{
			RailToolKind.Assets => AssetsToolView?.TryHandleKeyboardShortcut(key, ctrl, shift) ?? false,
			RailToolKind.MediaAssets => MediaAssetsToolView?.TryHandleKeyboardShortcut(key, ctrl, shift) ?? false,
			_ => false,
		};
	}

	internal bool CanHandleKeyboardShortcut(NexusKey key, bool ctrl, bool shift)
	{
		if (!_isToolPanelOpen)
		{
			return false;
		}

		return _activeTool switch
		{
			RailToolKind.Assets => AssetsToolView?.CanHandleKeyboardShortcut(key, ctrl, shift) ?? false,
			RailToolKind.MediaAssets => MediaAssetsToolView?.CanHandleKeyboardShortcut(key, ctrl, shift) ?? false,
			_ => false,
		};
	}

	internal void UpdateBlueprints(List<BlueprintItem> items)
	{
		NodeLibraryToolView?.UpdateBlueprints(items);
	}

	internal Task UpdateBlueprintsAsync(List<BlueprintItem> items)
	{
		return NodeLibraryToolView?.UpdateBlueprintsAndWaitAsync(items) ?? Task.CompletedTask;
	}
}
