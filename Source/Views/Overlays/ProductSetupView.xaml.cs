using System.Collections.ObjectModel;
using System.Linq;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup;
using ComfyUI_Nexus.Setup.Diagnostics;
using ComfyUI_Nexus.Setup.Diagnostics.Nodes;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Services;
using Microsoft.Maui.Controls.Shapes;
#if WINDOWS
using Microsoft.UI.Xaml.Input;
#endif
namespace ComfyUI_Nexus.Views.Overlays;

public partial class ProductSetupView : ContentView
{
	private const int LayoutReadyPollDelayMs = 50;
	private const int LayoutReadyMaxWaitMs = 1000;
	private const double HeaderInitialOffsetY = 30;
	private const double WelcomeInitialOffsetY = 50;
	private const double PanelRevealOffsetY = 20;
	private const double ActionBarHideOffsetY = 50;
	private const double BackgroundRevealOpacity = 0.5;
	private const double BackgroundDimmedOpacity = 0.15;
	private const double GlassInitialScale = 0.95;
	private const int SystemBootKernelDelayMs = 800;
	private const int SystemBootModulesDelayMs = 600;
	private const int SystemBootValidationDelayMs = 700;
	private const int SystemBootLinkDelayMs = 500;
	private const string DiagnosticActionCustom = "custom";
	private const string DiagnosticActionSystem = "system";
	private const string DiagnosticActionKeep = "keep";
	private const string DiagnosticActionBaseModelDownload = "download";
	private const string DiagnosticActionBaseModelBrowser = "browser";
	private const string DiagnosticNodeGitCore = "git-core";
	private const string DiagnosticNodePythonEngine = "python-engine";
	private const string DiagnosticNodeComfyCore = "comfy-core";
	private const string DiagnosticNodeBaseResources = "base-resources";
	private const string ConsoleStateAccentHex = "#31d8ff";
	private const string ConsoleStateInitializingHex = "#4D31d8ff";
	private const string ConsoleStateWarningHex = "#ffaa00";
	private const string ConsoleStateFailedHex = "#ff4d4d";
	private const uint BackgroundRevealLength = 1200;
	private const uint BackgroundDimLength = 800;
	private const uint CrossroadsBackgroundRevealLength = 600;
	private const uint CrossroadsWelcomeRevealLength = 400;
	private const uint ArchitectInitiationShowLength = 600;
	private const uint ConsoleShowLength = 600;
	private const uint FinalizeGlassFadeLength = 500;
	private const uint FinalizeBackgroundFadeLength = 600;
	private const uint GlassRevealLength = 1000;
	private const uint HeaderRevealLength = 800;
	private const uint WelcomeRevealLength = 1000;
	private const uint PanelQuickAnimationLength = 300;
	private const uint PanelShowLength = 500;
	private const double ConsoleConfigDisabledOpacity = 0.45;
	private const double ConsoleBootActionsExpandedRowSpacing = 8;
	private const double ConsoleButtonPreparingOpacity = 0.4;
	private const double ConsoleButtonBootingOpacity = 0.5;
	private const double GpuCardCornerRadius = 8;
	private const double GpuCardTitleFontSize = 10;
	private const double GpuCardNameFontSize = 9;
	private const double GpuCardMemoryFontSize = 8;
	private const double InitiationOverscrollResistance = 0.28;
	private const double InitiationOverscrollMaxOffset = 42;
	private const uint InitiationOverscrollReturnLength = 220;
	private const string CrossroadsAmbientWebpAnimationName = "ProductSetup.CrossroadsAmbientWebp";
	private const string WelcomeTitleWebpAnimationName = "ProductSetup.WelcomeTitleWebp";
	private const string VanguardIconWebpAnimationName = "ProductSetup.VanguardIconWebp";
	private const string ArchitectIconWebpAnimationName = "ProductSetup.ArchitectIconWebp";
	private const string ConsoleReadyPulseWebpAnimationName = "ProductSetup.ConsoleReadyPulseWebp";
	private const string ConsoleBootingPulseWebpAnimationName = "ProductSetup.ConsoleBootingPulseWebp";
	private const string ConsoleStatusBootingPulseWebpAnimationName = "ProductSetup.ConsoleStatusBootingPulseWebp";
	private const string PrimaryActionReadyPulseWebpAnimationName = "ProductSetup.PrimaryActionReadyPulseWebp";
	private const string DiagnosticLoadingRingWebpAnimationName = "ProductSetup.DiagnosticLoadingRingWebp";
	private static readonly Color GpuCardTitleColor = Color.FromArgb("#e8fbff");
	private static readonly Color GpuCardNameColor = NexusColors.TextDim;
	private static readonly Color GpuCardMemoryColor = NexusColors.AccentGlow;
	private static readonly Color GpuCardSelectedBackgroundColor = NexusColors.AccentSoft;
	private static readonly Color GpuCardNormalBackgroundColor = Color.FromArgb("#08000000");
	private static readonly Color GpuCardSelectedStrokeColor = NexusColors.AccentStrokeStrong;
	private static readonly Color GpuCardNormalStrokeColor = NexusColors.AccentStroke;
	private static readonly Color ConsoleAccentColor = NexusColors.Accent;
	private static readonly Color ConsoleBootNormalColor = NexusColors.AccentSoft;
	private static readonly Color ConsoleBootHoverColor = NexusColors.AccentHoverSoft;
	private static readonly Color ConsoleRetryNormalColor = Color.FromArgb("#1Aff4d4d");
	private static readonly Color ConsoleRetryHoverColor = Color.FromArgb("#33ff4d4d");
	private static readonly Color ConsoleRecoverNormalColor = NexusColors.WarningSoft;
	private static readonly Color ConsoleRecoverHoverColor = NexusColors.WarningHover;
	private static readonly Color ServerStateCardBackgroundColor = NexusColors.SurfaceDarkTranslucent;
	private static readonly Color DiagnosticActionDeleteNormalColor = NexusColors.DeleteSoft;
	private static readonly Color DiagnosticActionDeleteHoverColor = NexusColors.DeleteHover;
	private static readonly Color DiagnosticActionDeleteTextColor = NexusColors.DeleteText;
	private static readonly Color DiagnosticActionDefaultNormalColor = NexusColors.SurfaceSubtle;
	private static readonly Color DiagnosticActionDefaultHoverColor = NexusColors.SurfaceSubtleHover;
	private static readonly Color DiagnosticActionDefaultTextColor = NexusColors.White;
	private static readonly Color DiagnosticRequiredReadyColor = NexusColors.Accent;
	private static readonly Color DiagnosticRequiredPendingColor = NexusColors.WarningText;
	private static readonly Color ServerPythonVenvColor = NexusColors.Accent;
	private static readonly Color ServerPythonDirectColor = Color.FromArgb("#c78bff");
	private static readonly Color ServerPythonVenvPillColor = NexusColors.AccentWash;
	private static readonly Color ServerPythonDirectPillColor = Color.FromArgb("#10c78bff");
	private static readonly Color ServerPythonVenvTrackColor = NexusColors.AccentStroke;
	private static readonly Color ServerPythonDirectTrackColor = Color.FromArgb("#33c78bff");
	private static readonly Color ConsoleConfigPillHoverColor = Color.FromArgb("#1831d8ff");
	private static readonly Color ConsoleConfigPillNormalColor = NexusColors.AccentWash;

	public event EventHandler? SetupFinalized;

	private bool _introPlayed = false;
	private bool _isTransitioning = false;
	private string _selectedInstallMode = SetupInstallModes.LocalRuntime;
	private readonly SetupSequenceOrchestrator _setupSequence = new();
	private readonly InitiationSequenceRunner _initiationSequence;
	private readonly List<SetupDiagnosticStep> _vanguardRequiredSteps = new();
	private readonly List<SetupDiagnosticStep> _vanguardOptionalSteps = new();
	private readonly List<SetupDiagnosticStep> _architectRequiredSteps = new();
	private readonly List<SetupDiagnosticStep> _architectOptionalSteps = new();
	private readonly List<GpuDeviceInfo> _gpuDevices = new();
	private readonly List<Border> _gpuOptionCards = new();
	private readonly NexusMotionController _motion;
	private readonly SetupBackgroundController _background;
	private readonly NexusAnimatedWebpClip _crossroadsAmbientClip;
	private readonly NexusAnimatedWebpClip _welcomeTitleClip;
	private readonly NexusAnimatedWebpClip _vanguardIconClip;
	private readonly NexusAnimatedWebpClip _architectIconClip;
	private readonly NexusAnimatedWebpClip _consoleReadyPulseClip;
	private readonly NexusAnimatedWebpClip _consoleBootingPulseClip;
	private readonly NexusAnimatedWebpClip _consoleStatusBootingPulseClip;
	private readonly NexusAnimatedWebpClip _primaryActionReadyPulseClip;
	private readonly Dictionary<Image, NexusAnimatedWebpClip> _diagnosticLoadingRingClips = new();
	private NexusAnimatedWebpCacheLease? _animationCacheLease;
	private Task<NexusAnimatedWebpCacheLease>? _animationCacheAcquireTask;
	private readonly NexusOperationController _latestOperations = new("product-setup");
	private CancellationTokenSource? _repairCts;
	private readonly SemaphoreSlim _vanguardOptionalRefreshGate = new(1, 1);
	private readonly SemaphoreSlim _architectOptionalRefreshGate = new(1, 1);
	private bool _isDisposed;
	private bool _isGpuSelectorExpanded;
	private bool _isUpdatingServerPythonMode;
	private bool _isDiagnosticActionRunning;
	private bool _isInitiationSequenceRunning;
	private bool _isInitiationUserScrollBlocked;
	private bool _consoleRepairBeforeBoot;
	private string _architectCandidateComfyPath = string.Empty;
	private SetupDiagnosticStep? _focusedRequiredStep;
	private DiagnosticNodeViewModel? _focusedDiagnosticNode;
#if WINDOWS
	private bool _nativeScrollDragAttached;
	private bool _isNativeInitiationScrollDragging;
	private double _nativeInitiationDragStartY;
	private double _nativeInitiationDragStartOffsetY;
	private Microsoft.UI.Xaml.Controls.ScrollViewer? _nativeDraggedInitiationScrollViewer;
	private View? _nativeDraggedInitiationScrollContent;
#endif

	private enum ViewState { StartAction, Ready, EndAction }
	private enum SetupScrollPivot { Top, Bottom }
	private enum ViewContext { Intro, Crossroads, Vanguard, Architect, Repairing }
	private enum ConsoleBootActionState { Preparing, Standby, Booting, Failed, Online }
	private enum SetupSceneMotionScope { Backdrop, WelcomeTitle, CrossroadsIcons }

	private ViewContext _currentContext = ViewContext.Intro;
	private SetupSceneMotionState _sceneMotionState = SetupSceneMotionState.Hidden;
	private ConsoleBootActionState _consoleBootActionState = ConsoleBootActionState.Preparing;
	private ViewState _currentStateField = ViewState.StartAction;
	private ViewState _currentState
	{
		get => _currentStateField;
		set
		{
			_currentStateField = value;
			if (CinematicOverlay != null)
			{
				CinematicOverlay.IsVisible = (value != ViewState.Ready);
			}
		}
	}
	private Point _lastGlobalMousePos;

	public ProductSetupView()
	{
		InitializeComponent();
		_motion = new NexusMotionController("product-setup", "SETUP:UI", Dispatcher);
		_crossroadsAmbientClip = new NexusAnimatedWebpClip(_motion, CrossroadsAmbientLoop, CrossroadsAmbientWebpAnimationName, NexusAnimatedWebpCacheCatalog.SetupCrossroadsAmbient);
		_welcomeTitleClip = new NexusAnimatedWebpClip(_motion, WelcomeTitleAnimation, WelcomeTitleWebpAnimationName, NexusAnimatedWebpCacheCatalog.SetupWelcomeTitle);
		_vanguardIconClip = new NexusAnimatedWebpClip(_motion, VanguardModeIcon, VanguardIconWebpAnimationName, NexusAnimatedWebpCacheCatalog.SetupVanguardIcon);
		_architectIconClip = new NexusAnimatedWebpClip(_motion, ArchitectModeIcon, ArchitectIconWebpAnimationName, NexusAnimatedWebpCacheCatalog.SetupArchitectIcon);
		_consoleReadyPulseClip = new NexusAnimatedWebpClip(_motion, ConsoleBootPulseSurface, ConsoleReadyPulseWebpAnimationName, NexusAnimatedWebpCacheCatalog.SetupConsoleReadyPulse);
		_consoleBootingPulseClip = new NexusAnimatedWebpClip(_motion, ConsoleBootPulseSurface, ConsoleBootingPulseWebpAnimationName, NexusAnimatedWebpCacheCatalog.SetupConsoleBootingPulse);
		_consoleStatusBootingPulseClip = new NexusAnimatedWebpClip(_motion, ConsoleStatusPulseSurface, ConsoleStatusBootingPulseWebpAnimationName, NexusAnimatedWebpCacheCatalog.SetupConsoleStatusBootingPulse);
		_primaryActionReadyPulseClip = new NexusAnimatedWebpClip(_motion, PrimaryActionPulseSurface, PrimaryActionReadyPulseWebpAnimationName, NexusAnimatedWebpCacheCatalog.SetupPrimaryActionReadyPulse);
		_background = new SetupBackgroundController(
			_motion,
			BackgroundLayer,
			CrossroadsAmbientLoop,
			VanguardGlow,
			ArchitectGlow,
			VanguardSelectionBurst,
			ArchitectSelectionBurst,
			_vanguardIconClip,
			_architectIconClip);
		_initiationSequence = new InitiationSequenceRunner(
			PopulateInlineActions,
			EnableDiagnosticNodeInteraction,
			SetInitiationSequenceInteractionBlocked,
			WaitForStepCompleteAsync,
			RequestDiagnosticScrollAsync,
			EvaluateCurrentInitiationReadiness,
			UpdateDiagnosticProgress);

		BindingContext = this;
		BindableLayout.SetItemsSource(DiagnosticNodesList, VanguardNodes);
		BindableLayout.SetItemsSource(VanguardOptionalNodesList, VanguardOptionalNodes);
		_setupSequence.OnMessage += AddConsoleLog;
		_setupSequence.OnProgress += (progress, message) => AddConsoleLog($"[PROGRESS] {message} ({progress:P0})");

		// Initial State
		GlassPanel.Opacity = 0;
		HeaderBar.Opacity = 0;
		ActionBottomBar.Opacity = 0;
		ActionBottomBar.InputTransparent = true;

		this.SizeChanged += OnViewSizeChanged;

		// Use a one-time event handler to prevent double-initialization
		EventHandler? loadedHandler = null;
		loadedHandler = (s, e) =>
		{
			this.Loaded -= loadedHandler;
			_isDisposed = false;
		};
		this.Loaded += loadedHandler;
		this.Unloaded += OnProductSetupUnloaded;
	}

	internal Task ActivateLifecycleAsync()
	{
		_isDisposed = false;
		AttachNativeInitiationScrollDragHandlers();
		if (MainThread.IsMainThread)
		{
			return InitializeLifecycleAsync();
		}

		return UiThread.InvokeAsync(InitializeLifecycleAsync, "SETUP:UI:ACTIVATE_LIFECYCLE");
	}

	internal Task PrepareAnimationCacheAsync()
		=> EnsureAnimationCacheAsync();

	internal void PrepareForLifecycleHandoff()
	{
		if (_introPlayed) return;

		StopCrossroadsAmbientPulse();
		_currentContext = ViewContext.Intro;
		_currentState = ViewState.StartAction;
		ScaleContainer.InputTransparent = true;

		_background.SetBaseOpacity(0);
		GlassPanel.Opacity = 0;
		GlassPanel.Scale = GlassInitialScale;

		HeaderBar.IsVisible = true;
		HeaderBar.Opacity = 0;
		HeaderBar.TranslationY = HeaderInitialOffsetY;

		WelcomeContainer.IsVisible = true;
		WelcomeContainer.Opacity = 0;
		WelcomeContainer.TranslationY = WelcomeInitialOffsetY;
	}

	private void OnProductSetupUnloaded(object? sender, EventArgs e)
	{
		SetSceneMotionState(SetupSceneMotionState.Hidden);
		DetachNativeInitiationScrollDragHandlers();
		StopCrossroadsAmbientPulse();
		CancelTransientAnimationLoops();
		_crossroadsAmbientClip.Dispose();
		_welcomeTitleClip.Dispose();
		_vanguardIconClip.Dispose();
		_architectIconClip.Dispose();
		_consoleReadyPulseClip.Dispose();
		_consoleBootingPulseClip.Dispose();
		_consoleStatusBootingPulseClip.Dispose();
		_primaryActionReadyPulseClip.Dispose();
		DisposeDiagnosticLoadingRingClips();
		_background.Dispose();
		ReleaseAnimationCache();
		_isDisposed = true;
	}

#if WINDOWS
	private void AttachNativeInitiationScrollDragHandlers()
	{
		if (_nativeScrollDragAttached) return;

		AttachNativeInitiationScrollDragHandler(VanguardInitiationScrollView);
		AttachNativeInitiationScrollDragHandler(ArchitectInitiationScrollView);
		_nativeScrollDragAttached = true;
	}

	private void AttachNativeInitiationScrollDragHandler(ScrollView scrollView)
	{
		if (scrollView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.ScrollViewer nativeScrollViewer)
		{
			scrollView.HandlerChanged += OnInitiationScrollViewHandlerChanged;
			return;
		}

		nativeScrollViewer.PointerPressed -= OnNativeInitiationScrollPointerPressed;
		nativeScrollViewer.PointerMoved -= OnNativeInitiationScrollPointerMoved;
		nativeScrollViewer.PointerReleased -= OnNativeInitiationScrollPointerReleased;
		nativeScrollViewer.PointerCanceled -= OnNativeInitiationScrollPointerReleased;

		nativeScrollViewer.PointerPressed += OnNativeInitiationScrollPointerPressed;
		nativeScrollViewer.PointerMoved += OnNativeInitiationScrollPointerMoved;
		nativeScrollViewer.PointerReleased += OnNativeInitiationScrollPointerReleased;
		nativeScrollViewer.PointerCanceled += OnNativeInitiationScrollPointerReleased;
		nativeScrollViewer.VerticalScrollMode = _isInitiationUserScrollBlocked
			? Microsoft.UI.Xaml.Controls.ScrollMode.Disabled
			: Microsoft.UI.Xaml.Controls.ScrollMode.Enabled;
	}

	private void OnInitiationScrollViewHandlerChanged(object? sender, EventArgs e)
	{
		if (sender is not ScrollView scrollView) return;

		scrollView.HandlerChanged -= OnInitiationScrollViewHandlerChanged;
		AttachNativeInitiationScrollDragHandler(scrollView);
	}

	private void DetachNativeInitiationScrollDragHandlers()
	{
		DetachNativeInitiationScrollDragHandler(VanguardInitiationScrollView);
		DetachNativeInitiationScrollDragHandler(ArchitectInitiationScrollView);
		_nativeScrollDragAttached = false;
		_isNativeInitiationScrollDragging = false;
		_nativeDraggedInitiationScrollViewer = null;
	}

	private void DetachNativeInitiationScrollDragHandler(ScrollView scrollView)
	{
		scrollView.HandlerChanged -= OnInitiationScrollViewHandlerChanged;

		if (scrollView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.ScrollViewer nativeScrollViewer)
		{
			return;
		}

		nativeScrollViewer.PointerPressed -= OnNativeInitiationScrollPointerPressed;
		nativeScrollViewer.PointerMoved -= OnNativeInitiationScrollPointerMoved;
		nativeScrollViewer.PointerReleased -= OnNativeInitiationScrollPointerReleased;
		nativeScrollViewer.PointerCanceled -= OnNativeInitiationScrollPointerReleased;
	}

	private void OnNativeInitiationScrollPointerPressed(object sender, PointerRoutedEventArgs e)
	{
		if (_isInitiationUserScrollBlocked)
		{
			e.Handled = true;
			return;
		}

		if (sender is not Microsoft.UI.Xaml.Controls.ScrollViewer scrollViewer) return;

		var point = e.GetCurrentPoint(scrollViewer);
		if (!point.Properties.IsLeftButtonPressed) return;

		_isNativeInitiationScrollDragging = true;
		_nativeDraggedInitiationScrollViewer = scrollViewer;
		_nativeDraggedInitiationScrollContent = GetNativeInitiationScrollContent(scrollViewer);
		_nativeDraggedInitiationScrollContent?.CancelAnimations();
		_nativeInitiationDragStartY = point.Position.Y;
		_nativeInitiationDragStartOffsetY = scrollViewer.VerticalOffset;
		scrollViewer.CapturePointer(e.Pointer);
		e.Handled = true;
	}

	private void OnNativeInitiationScrollPointerMoved(object sender, PointerRoutedEventArgs e)
	{
		if (!_isNativeInitiationScrollDragging) return;
		if (sender is not Microsoft.UI.Xaml.Controls.ScrollViewer scrollViewer) return;
		if (!ReferenceEquals(_nativeDraggedInitiationScrollViewer, scrollViewer)) return;

		var point = e.GetCurrentPoint(scrollViewer);
		if (!point.Properties.IsLeftButtonPressed)
		{
			EndNativeInitiationScrollDrag(scrollViewer, e);
			return;
		}

		double dragDeltaY = point.Position.Y - _nativeInitiationDragStartY;
		double rawTargetOffsetY = _nativeInitiationDragStartOffsetY - dragDeltaY;
		double targetOffsetY = Math.Clamp(
			rawTargetOffsetY,
			0,
			Math.Max(0, scrollViewer.ScrollableHeight));

		scrollViewer.ChangeView(null, targetOffsetY, null, disableAnimation: true);
		ApplyNativeInitiationOverscroll(rawTargetOffsetY, scrollViewer.ScrollableHeight);
		e.Handled = true;
	}

	private void OnNativeInitiationScrollPointerReleased(object sender, PointerRoutedEventArgs e)
	{
		if (sender is Microsoft.UI.Xaml.Controls.ScrollViewer scrollViewer)
		{
			EndNativeInitiationScrollDrag(scrollViewer, e);
		}
	}

	private void EndNativeInitiationScrollDrag(Microsoft.UI.Xaml.Controls.ScrollViewer scrollViewer, PointerRoutedEventArgs e)
	{
		_isNativeInitiationScrollDragging = false;
		_nativeDraggedInitiationScrollViewer = null;
		_ = ReturnNativeInitiationOverscrollAsync(_nativeDraggedInitiationScrollContent);
		_nativeDraggedInitiationScrollContent = null;
		scrollViewer.ReleasePointerCapture(e.Pointer);
		e.Handled = true;
	}

	private View? GetNativeInitiationScrollContent(Microsoft.UI.Xaml.Controls.ScrollViewer nativeScrollViewer)
	{
		if (ReferenceEquals(VanguardInitiationScrollView.Handler?.PlatformView, nativeScrollViewer))
		{
			return VanguardInitiationScrollView.Content;
		}

		if (ReferenceEquals(ArchitectInitiationScrollView.Handler?.PlatformView, nativeScrollViewer))
		{
			return ArchitectInitiationScrollView.Content;
		}

		return null;
	}

	private void ApplyNativeInitiationOverscroll(double rawTargetOffsetY, double maxOffsetY)
	{
		if (_nativeDraggedInitiationScrollContent == null) return;

		double overscroll = 0;
		if (rawTargetOffsetY < 0)
		{
			overscroll = GetRubberBandOffset(-rawTargetOffsetY);
		}
		else if (rawTargetOffsetY > maxOffsetY)
		{
			overscroll = -GetRubberBandOffset(rawTargetOffsetY - maxOffsetY);
		}

		_nativeDraggedInitiationScrollContent.TranslationY = overscroll;
	}

	private static double GetRubberBandOffset(double overscroll)
		=> Math.Min(InitiationOverscrollMaxOffset, overscroll * InitiationOverscrollResistance);

	private static async Task ReturnNativeInitiationOverscrollAsync(View? content)
	{
		if (content == null || Math.Abs(content.TranslationY) < 0.5)
		{
			return;
		}

		try
		{
			await SafeAnimation.TranslateToAsync(content, 0, 0, InitiationOverscrollReturnLength, Easing.SpringOut, "Setup.InitiationScroll");
		}
		catch
		{
			content.TranslationY = 0;
		}
	}
#else
	private void AttachNativeInitiationScrollDragHandlers()
	{
	}

	private void DetachNativeInitiationScrollDragHandlers()
	{
	}
#endif

	private void StartCrossroadsAmbientPulse()
	{
		StartSetupBackdropMotion();
		StartCrossroadsIconFloatLoop();
	}

	private void SetSceneMotionState(SetupSceneMotionState state)
	{
		_sceneMotionState = state;
		_background.ApplySceneState(state);
	}

	private void StartSetupBackdropMotion()
	{
		if (_isDisposed)
		{
			return;
		}

		_background.SetCrossroadsAmbientVisible(true);
		_crossroadsAmbientClip.PlayLoop(() => CanRepeatSetupSceneMotion(SetupSceneMotionScope.Backdrop));
		_welcomeTitleClip.PlayLoop(() => CanRepeatSetupSceneMotion(SetupSceneMotionScope.WelcomeTitle));
	}

	private void StopCrossroadsAmbientPulse(bool keepWelcomeTitle = false)
	{
		_crossroadsAmbientClip.Stop();
		if (!keepWelcomeTitle)
		{
			_welcomeTitleClip.Stop();
		}

		_background.SetMode(SetupBackgroundMode.Crossroads);
		_background.SetCrossroadsAmbientVisible(false);
		StopCrossroadsIconFloatLoop();
	}

	private bool CanPlayCrossroadsExitMotion()
		=> !_isDisposed
			&& IsVisible
			&& Handler is not null
			&& WelcomeContainer.IsVisible;

	private bool CanRepeatSetupSceneMotion(SetupSceneMotionScope scope)
	{
		bool isSetupSurfaceAvailable = !_isDisposed && IsVisible && Handler is not null;
		return scope switch
		{
			SetupSceneMotionScope.Backdrop => isSetupSurfaceAvailable && !ServerConsolePanel.IsVisible,
			SetupSceneMotionScope.WelcomeTitle => isSetupSurfaceAvailable && WelcomeTitleAnimation.IsVisible,
			SetupSceneMotionScope.CrossroadsIcons => isSetupSurfaceAvailable
				&& WelcomeContainer.IsVisible
				&& _sceneMotionState is SetupSceneMotionState.Crossroads or SetupSceneMotionState.SelectionExit,
			_ => false,
		};
	}

	private void StartCrossroadsIconFloatLoop()
	{
		_vanguardIconClip.Rewind();
		_architectIconClip.Rewind();
		_vanguardIconClip.PlayLoop(() => CanRepeatSetupSceneMotion(SetupSceneMotionScope.CrossroadsIcons));
		_architectIconClip.PlayLoop(() => CanRepeatSetupSceneMotion(SetupSceneMotionScope.CrossroadsIcons));
	}

	private void StopCrossroadsIconFloatLoop()
	{
		_vanguardIconClip.Stop();
		_architectIconClip.Stop();
	}

	private void CancelTransientAnimationLoops()
	{
		_repairCts?.Cancel();
		_latestOperations.StopAll();
		StopCrossroadsAmbientPulse();
		StopConsoleReadyPulse();
		StopConsoleBootingPulse();
		StopConsoleStatusBootingPulse();
		StopPrimaryActionReadyPulse();
		StopDiagnosticLoadingRingAnimations();
	}

	private async Task InitializeLifecycleAsync()
	{
		if (_introPlayed) return;
		_introPlayed = true;

		try
		{
			NexusLog.Info("[SETUP:UI] Product setup lifecycle starting.");
			_currentState = ViewState.StartAction;
			_currentContext = ViewContext.Intro;

			// Ensure install service is ready
			if (ComfyInstallService.Instance == null) _ = new ComfyInstallService();
			await WaitForProductSetupLayoutAsync();
			await EnsureAnimationCacheAsync();

			HeaderBar.IsVisible = true;
			HeaderBar.Opacity = 0;
			HeaderBar.TranslationY = HeaderInitialOffsetY;

			WelcomeContainer.IsVisible = true;
			WelcomeContainer.Opacity = 0;
			WelcomeContainer.TranslationY = WelcomeInitialOffsetY;

			GlassPanel.Opacity = 0;
			GlassPanel.Scale = GlassInitialScale;

			// Yield only the frames needed for the newly visible setup surface to attach.
			await UiThread.YieldDispatcherFramesAsync(2, "Setup.Reveal");

			await Task.WhenAll(
				_background.FadeBaseToAsync(BackgroundRevealOpacity, BackgroundRevealLength, Easing.CubicOut),

				SafeAnimation.FadeToAsync(GlassPanel, 1, GlassRevealLength, Easing.CubicOut, "Setup.Reveal"),
				SafeAnimation.ScaleToAsync(GlassPanel, 1.0, GlassRevealLength, Easing.SpringOut, "Setup.Reveal"),
				SafeAnimation.FadeToAsync(HeaderBar, 1, HeaderRevealLength, Easing.CubicOut, "Setup.Reveal"),
				SafeAnimation.TranslateToAsync(HeaderBar, 0, 0, HeaderRevealLength, Easing.CubicOut, "Setup.Reveal"),
				SafeAnimation.FadeToAsync(WelcomeContainer, 1, WelcomeRevealLength, Easing.CubicOut, "Setup.Reveal"),
				SafeAnimation.TranslateToAsync(WelcomeContainer, 0, 0, WelcomeRevealLength, Easing.CubicOut, "Setup.Reveal")
			);

			// Let the final reveal values reach the native surface before accepting input.
			await UiThread.YieldDispatcherFramesAsync(2, "Setup.Reveal");
			NexusLog.Info("[SETUP:UI] Product setup lifecycle reveal completed.");
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETUP:UI] Product setup lifecycle failed");
		}
		finally
		{
			try
			{
				NexusLog.Info("[SETUP:UI] Product setup lifecycle unlock starting.");
				_currentContext = ViewContext.Crossroads;
				ScaleContainer.InputTransparent = false;
				_currentState = ViewState.Ready;

				SetSceneMotionState(SetupSceneMotionState.Crossroads);
				StartCrossroadsAmbientPulse();
				NexusLog.Info("[SETUP:UI] Product setup lifecycle unlock completed.");
			}
			catch (Exception ex)
			{
				NexusLog.Exception(ex, "[SETUP:UI] Product setup lifecycle unlock failed");
			}
		}
	}

	private async Task WaitForProductSetupLayoutAsync()
	{
		int waited = 0;
		while (!_isDisposed
			&& waited < LayoutReadyMaxWaitMs
			&& (Width <= 1 || Height <= 1 || ProductSetupRoot.Width <= 1 || ProductSetupRoot.Height <= 1 || CinematicOverlay.Width <= 1 || CinematicOverlay.Height <= 1))
		{
			await Task.Delay(LayoutReadyPollDelayMs);
			waited += LayoutReadyPollDelayMs;
		}

		NexusLog.Info($"[SETUP:UI] Layout ready. view={Width:0}x{Height:0}, root={ProductSetupRoot.Width:0}x{ProductSetupRoot.Height:0}, overlay={CinematicOverlay.Width:0}x{CinematicOverlay.Height:0}");
	}

	internal void ResetFlow()
	{
		try
		{
			NexusLog.Info("[SETUP:UI] ResetFlow starting.");
			StopCrossroadsAmbientPulse();
			CancelTransientAnimationLoops();
			StopConsoleReadyPulse();
			StopConsoleBootingPulse();
			StopConsoleStatusBootingPulse();
			StopPrimaryActionReadyPulse();

			_currentContext = ViewContext.Crossroads;
			_currentState = ViewState.Ready;
			_isTransitioning = false;
			InputTransparent = false;
			ScaleContainer.InputTransparent = false;

			_background.SetBaseOpacity(BackgroundRevealOpacity);
			GlassPanel.Opacity = 1;
			GlassPanel.Scale = 1;
			HeaderBar.IsVisible = true;
			HeaderBar.Opacity = 1;
			HeaderBar.TranslationY = 0;

			ResetHiddenPanel(VanguardPanel);
			ResetHiddenPanel(ArchitectInitiationPanel);
			ResetHiddenPanel(ArchitectWorkspacePanel);
			ResetHiddenPanel(ServerConsolePanel);
			WelcomeContainer.IsVisible = true;
			WelcomeContainer.Opacity = 1;
			WelcomeContainer.TranslationY = 0;

			ArchitectNodes.Clear();
			ArchitectOptionalNodes.Clear();
			VanguardNodes.Clear();
			VanguardOptionalNodes.Clear();
			UpdateVanguardOptionalSectionVisibility();
			UpdateArchitectOptionalSectionVisibility();

			VanguardContainer.CancelAnimations();
			ArchitectContainer.CancelAnimations();
			VanguardContainer.Scale = 1;
			ArchitectContainer.Scale = 1;
			VanguardContainer.TranslationY = 0;
			ArchitectContainer.TranslationY = 0;
			SetSceneMotionState(SetupSceneMotionState.Crossroads);
			StartCrossroadsAmbientPulse();

			ResetActionBottomBar();
			NexusLog.Info("[SETUP:UI] ResetFlow completed.");
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETUP:UI] ResetFlow failed");
			throw;
		}
	}

	private static void ResetHiddenPanel(VisualElement panel)
	{
		panel.IsVisible = false;
		panel.Opacity = 0;
		panel.TranslationY = 0;
	}

	private void ResetActionBottomBar()
	{
		ActionBottomBar.IsVisible = false;
		ActionBottomBar.Opacity = 0;
		ActionBottomBar.TranslationY = 0;
		ActionBottomBar.InputTransparent = true;
	}

	// ------------------------------------------------------------

	// ------------------------------------------------------------
	// CARD INTERACTIONS (No Scale, Just Glow/Opacity)
	// ------------------------------------------------------------
	private async void OnVanguardSelected(object? sender, TappedEventArgs e)
	{
		if (_currentState != ViewState.Ready || _currentContext != ViewContext.Crossroads) return;

		_currentState = ViewState.StartAction;
		SetSceneMotionState(SetupSceneMotionState.SelectionExit);
		_selectedInstallMode = SetupInstallModes.LocalRuntime;
		SetupSettingsService.Instance.UseLocalRuntime();

		// Initiation Phase
		PrepareVanguardChecklist();
		_ = _background.FadeBaseToAsync(BackgroundDimmedOpacity, BackgroundDimLength, Easing.CubicOut);
		if (!await _background.PlaySelectionAsync(SetupBackgroundMode.VanguardSelected, VanguardContainer, CanPlayCrossroadsExitMotion))
		{
			return;
		}
		await TransitionToPanel(VanguardPanel, LocalizationManager.Text("setup.status.pending"));
		ResetInitiationScrollPosition(VanguardInitiationScrollView);
		_ = RefreshVanguardOptionalNodesSafeAsync();

		_ = RunRepairSequenceAsync();
	}

	private async void OnArchitectSelected(object? sender, TappedEventArgs e)
	{
		if (_currentState != ViewState.Ready || _currentContext != ViewContext.Crossroads) return;

		_currentState = ViewState.StartAction;
		SetSceneMotionState(SetupSceneMotionState.SelectionExit);
		_selectedInstallMode = SetupInstallModes.ExistingComfyPath;
		ArchitectNodes.Clear();

		var settings = SetupSettingsService.Instance.Settings;
		_architectCandidateComfyPath = settings.ComfyPath ?? string.Empty;
		ArchitectFolderBrowser.InitializePath(string.IsNullOrWhiteSpace(_architectCandidateComfyPath) ? "C:\\" : _architectCandidateComfyPath);

		_ = _background.FadeBaseToAsync(BackgroundDimmedOpacity, BackgroundDimLength, Easing.CubicOut);
		if (!await _background.PlaySelectionAsync(SetupBackgroundMode.ArchitectSelected, ArchitectContainer, CanPlayCrossroadsExitMotion))
		{
			return;
		}
		await TransitionToPanel(ArchitectWorkspacePanel, LocalizationManager.Text("common.next"));

		UpdateArchitectWorkspaceReadiness();
		_isTransitioning = false;
		_currentState = ViewState.Ready;
	}

	private DiagnosticNodeViewModel CreateDiagnosticVM(IRuntimeDiagnosticNode node)
	{
		var vm = new DiagnosticNodeViewModel(node);
		vm.PreferStatusEditAction = node is PythonEnvironmentDiagnosticNode or PipCacheDiagnosticNode;
		vm.UpdateState(HealthState.Pending);
		return vm;
	}

	private SetupDiagnosticStep CreateDiagnosticStep(IRuntimeDiagnosticNode node, bool isRequired)
	{
		var vm = CreateDiagnosticVM(node);
		var step = new SetupDiagnosticStep(vm, isRequired);
		step.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(SetupDiagnosticStep.State))
			{
				EvaluateCurrentInitiationReadiness();
				UpdateInitiationUserScrollBlock();
			}
		};

		vm.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(DiagnosticNodeViewModel.CurrentHealth))
			{
				EvaluateCurrentInitiationReadiness();
			}
		};

		return step;
	}

	private T GetDiagnosticNode<T>() where T : class, IRuntimeDiagnosticNode
	{
		IRuntimeDiagnosticNode node = typeof(T) switch
		{
			Type type when type == typeof(GitDiagnosticNode) => new GitDiagnosticNode(),
			Type type when type == typeof(PythonDiagnosticNode) => new PythonDiagnosticNode(),
			Type type when type == typeof(ComfyCoreDiagnosticNode) => new ComfyCoreDiagnosticNode(),
			Type type when type == typeof(BaseResourceDiagnosticNode) => new BaseResourceDiagnosticNode(),
			Type type when type == typeof(ManagerExtensionDiagnosticNode) => new ManagerExtensionDiagnosticNode(),
			Type type when type == typeof(PythonEnvironmentDiagnosticNode) => new PythonEnvironmentDiagnosticNode(),
			Type type when type == typeof(PipCacheDiagnosticNode) => new PipCacheDiagnosticNode(),
			Type type when type == typeof(ModelLibraryDiagnosticNode) => new ModelLibraryDiagnosticNode(allowMultipleRoots: true),
			_ => throw new InvalidOperationException($"Diagnostic node not registered: {typeof(T).Name}")
		};

		return (T)node;
	}

	private void PrepareArchitectChecklist()
	{
		ArchitectNodes.Clear();
		ArchitectOptionalNodes.Clear();
		_architectRequiredSteps.Clear();
		_architectOptionalSteps.Clear();
		BindableLayout.SetItemsSource(ArchitectNodesList, ArchitectNodes);
		BindableLayout.SetItemsSource(ArchitectOptionalNodesList, ArchitectOptionalNodes);

		AddRequiredArchitectStep(GetDiagnosticNode<GitDiagnosticNode>());
		AddRequiredArchitectStep(GetDiagnosticNode<PythonDiagnosticNode>());
		AddRequiredArchitectStep(GetDiagnosticNode<ManagerExtensionDiagnosticNode>());

		AddOptionalArchitectStep(GetDiagnosticNode<PythonEnvironmentDiagnosticNode>());
		AddOptionalArchitectStep(GetDiagnosticNode<PipCacheDiagnosticNode>());
		AddOptionalArchitectStep(GetDiagnosticNode<ModelLibraryDiagnosticNode>());
		UpdateArchitectOptionalSectionVisibility();

		UpdateArchitectRequiredStatus();
	}

	private void AddRequiredArchitectStep(IRuntimeDiagnosticNode node)
	{
		var step = CreateDiagnosticStep(node, isRequired: true);
		_architectRequiredSteps.Add(step);
		ArchitectNodes.Add(step.ViewModel);
	}

	private void AddOptionalArchitectStep(IRuntimeDiagnosticNode node)
	{
		var step = CreateDiagnosticStep(node, isRequired: false);
		_architectOptionalSteps.Add(step);
		ArchitectOptionalNodes.Add(step.ViewModel);
		if (node is PythonEnvironmentDiagnosticNode)
		{
			step.ViewModel.EnvironmentDetails = LocalizationManager.Text("setup.venv.requires_python");
			step.ViewModel.EnvironmentPath = ComfyInstallService.ComfyVenvPath;
		}
	}

	private async Task RefreshArchitectOptionalNodesSafeAsync()
	{
		if (_isDisposed) return;

		try
		{
			await RefreshArchitectOptionalNodesAsync();
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETUP:UI] Optional initiation refresh failed");
			AddConsoleLog($"[SETUP:UI] Optional initiation refresh failed: {ex.Message}");
		}
	}

	private async Task RefreshVanguardOptionalNodesSafeAsync()
	{
		if (_isDisposed) return;

		try
		{
			await _vanguardOptionalRefreshGate.WaitAsync();
			try
			{
				await RefreshOptionalNodesAsync(VanguardOptionalNodes, _vanguardOptionalSteps);
			}
			finally
			{
				_vanguardOptionalRefreshGate.Release();
			}
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETUP:UI] Optional initiation refresh failed");
			AddConsoleLog($"[SETUP:UI] Optional initiation refresh failed: {ex.Message}");
		}
	}

	private async Task RefreshArchitectOptionalNodesAsync()
	{
		await _architectOptionalRefreshGate.WaitAsync();
		try
		{
			await InvokeOnMainThreadSafeAsync(() =>
			{
				UpdateArchitectOptionalSectionVisibility();
				return Task.CompletedTask;
			});

			await RefreshOptionalNodesAsync(ArchitectOptionalNodes, _architectOptionalSteps);
		}
		finally
		{
			_architectOptionalRefreshGate.Release();
		}
	}

	private async Task RefreshOptionalNodesAsync(
		IReadOnlyCollection<DiagnosticNodeViewModel> optionalNodes,
		IReadOnlyCollection<SetupDiagnosticStep> optionalSteps)
	{
		foreach (var vm in optionalNodes)
		{
			if (vm.Node is not IConfigurableDiagnosticNode configurableNode) continue;
			SetupDiagnosticStep? step = optionalSteps.FirstOrDefault(candidate => ReferenceEquals(candidate.ViewModel, vm));

			await InvokeOnMainThreadSafeAsync(() =>
			{
				vm.Actions.Clear();
				vm.NotifyActionsChanged();
				return Task.CompletedTask;
			});

			await configurableNode.ProbeEnvironmentAsync(CancellationToken.None);
			var health = await vm.Node.CheckHealthAsync(CancellationToken.None);
			await InvokeOnMainThreadSafeAsync(() =>
			{
				vm.UpdateState(health);
				vm.EnvironmentDetails = configurableNode.EnvironmentDetails;
				vm.EnvironmentPath = configurableNode.EnvironmentPath;
				MarkStepFromHealth(step, vm);
				if (ShouldStartOptionalNodeCollapsed(vm))
				{
					vm.Actions.Clear();
					vm.NotifyActionsChanged();
					vm.ActionText = GetRestingActionText(vm);
					SetDiagnosticNodeInteraction(vm, false);
				}
				else
				{
					PopulateInlineActions(vm, configurableNode);
					vm.ActionText = GetInteractiveActionText(vm);
					SetDiagnosticNodeInteraction(vm, true);
				}

				return Task.CompletedTask;
			});
		}
	}

	private IReadOnlyCollection<SetupDiagnosticStep> GetRequiredStepsForCurrentContext()
		=> _currentContext == ViewContext.Architect
			? _architectRequiredSteps
			: _vanguardRequiredSteps;

	private async Task RunArchitectInitiationSequenceAsync()
	{
		_currentContext = ViewContext.Architect;

		try
		{
			await _initiationSequence.RunArchitectAsync(_architectRequiredSteps, CancellationToken.None);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETUP:UI] Architect initiation sequence failed");
			AddConsoleLog($"[SETUP:UI] Architect initiation sequence failed: {ex.Message}");
		}

		if (ArchitectOptionalNodes.Count > 0)
		{
			await RefreshArchitectOptionalNodesSafeAsync();
			await ScrollArchitectOptionalSectionIntoViewAsync();
		}
	}

	private void CheckArchitectInitiationStatus()
	{
		try
		{
			if (_isDisposed) return;

			UpdateArchitectRequiredStatus();
			EnsureActionBottomBarReady();

			bool allReady = IsArchitectInitiationReady();

			if (allReady)
			{
				PrimaryActionLabel.Text = LocalizationManager.Text("common.next");
				PrimaryActionButton.IsEnabled = !IsInitiationInteractionBlocked;
				PrimaryActionButton.InputTransparent = IsInitiationInteractionBlocked;
				PrimaryActionButton.Opacity = IsInitiationInteractionBlocked ? 0.35 : 1;
				if (IsInitiationInteractionBlocked) StopPrimaryActionReadyPulse();
				else StartPrimaryActionReadyPulse();
			}
			else
			{
				PrimaryActionLabel.Text = LocalizationManager.Text("setup.status.pending");
				PrimaryActionButton.IsEnabled = false;
				PrimaryActionButton.InputTransparent = true;
				PrimaryActionButton.Opacity = 0.3;
				StopPrimaryActionReadyPulse();
			}
		}
		catch (ObjectDisposedException)
		{
		}
		catch (InvalidOperationException)
		{
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETUP:UI] Architect readiness update failed");
		}
	}

	private bool IsArchitectInitiationReady()
	{
		var settings = SetupSettingsService.Instance.Settings;
		return IsValidComfyPath(settings.ComfyPath)
			&& _architectRequiredSteps.Count > 0
			&& _architectRequiredSteps.All(step => step.CountsAsReady);
	}

	private async Task TransitionToArchitectInitiationAsync()
	{
		if (ArchitectInitiationPanel.IsVisible) return;

		await FadeOutAndHideAsync(ArchitectWorkspacePanel, PanelQuickAnimationLength);

		PrepareArchitectChecklist();

		PreparePanelReveal(ArchitectInitiationPanel);

		PrimaryActionLabel.Text = LocalizationManager.Text("setup.status.pending");
		PrimaryActionButton.IsEnabled = false;
		PrimaryActionButton.Opacity = 0.3;
		StopPrimaryActionReadyPulse();

		await Task.WhenAll(
			SafeAnimation.FadeToAsync(ArchitectInitiationPanel, 1, ArchitectInitiationShowLength, Easing.CubicOut, "Setup.Architect"),
			SafeAnimation.TranslateToAsync(ArchitectInitiationPanel, 0, 0, ArchitectInitiationShowLength, Easing.CubicOut, "Setup.Architect")
		);
		ResetInitiationScrollPosition(ArchitectInitiationScrollView);
		_ = RefreshArchitectOptionalNodesSafeAsync();

		_ = RunArchitectInitiationSequenceAsync();
	}

	private async Task TransitionToConsoleAsync()
	{
		NexusLog.Info($"[SETUP:UI] Console transition requested from primary action. context={_currentContext}, vanguardVisible={VanguardPanel.IsVisible}, architectWorkspaceVisible={ArchitectWorkspacePanel.IsVisible}, architectInitiationVisible={ArchitectInitiationPanel.IsVisible}");
		VisualElement? currentPanel = null;
		if (VanguardPanel.IsVisible) currentPanel = VanguardPanel;
		else if (ArchitectWorkspacePanel.IsVisible) currentPanel = ArchitectWorkspacePanel;
		else if (ArchitectInitiationPanel.IsVisible) currentPanel = ArchitectInitiationPanel;

		if (currentPanel != null)
		{
			await FadeOutAndHideAsync(currentPanel, PanelQuickAnimationLength, Easing.CubicIn);
		}

		ConsoleLogTail.Clear(); // Clear existing logs

		StopCrossroadsAmbientPulse(keepWelcomeTitle: true);
		SetSceneMotionState(SetupSceneMotionState.Console);
		PreparePanelReveal(ServerConsolePanel);

		ShowActionBottomBar(LocalizationManager.Text("setup.action.launch_nexus"), false);
		ApplyConsoleBootActionState(ConsoleBootActionState.Preparing);

		// Set Back button to RETRY
		BackButton.IsVisible = true;
		var label = BackButton.Content as Label;
		if (label == null && BackButton.Content is Grid g)
		{
			label = g.Children.OfType<Label>().FirstOrDefault();
		}

		if (label != null) label.Text = "BACK";

		await Task.WhenAll(
			SafeAnimation.FadeToAsync(ServerConsolePanel, 1, ConsoleShowLength, Easing.CubicOut, "Setup.Console"),
			SafeAnimation.TranslateToAsync(ServerConsolePanel, 0, 0, ConsoleShowLength, Easing.CubicOut, "Setup.Console"),
			SafeAnimation.FadeToAsync(ActionBottomBar, 1, ConsoleShowLength, Easing.CubicOut, "Setup.Console"),
			SafeAnimation.TranslateToAsync(ActionBottomBar, 0, 0, ConsoleShowLength, Easing.CubicOut, "Setup.Console")
		);

		UpdateBootChannelInfo();
		AutoSelectServerPythonModeFromComfyVenv();
		UpdateServerPythonModeVisual();
		await InitializeGpuSelectorAsync();

		_ = RunSystemBootCheckAsync();
	}

	private async Task RunSystemBootCheckAsync()
	{
		NexusLog.Info("[SETUP:UI] Console system boot check started.");
		ApplyConsoleBootActionState(ConsoleBootActionState.Preparing);

		AddConsoleLog("[SYSTEM] Preparing Nexus services...");
		await Task.Delay(SystemBootKernelDelayMs);
		AddConsoleLog("[SYSTEM] Loading Nexus Kernel Modules...");
		await Task.Delay(SystemBootModulesDelayMs);
		AddConsoleLog("[SYSTEM] Validating Environment Integrity...");
		await Task.Delay(SystemBootValidationDelayMs);
		AddConsoleLog("[SYSTEM] Connecting workspace services...");
		await Task.Delay(SystemBootLinkDelayMs);
		AddConsoleLog("[SYSTEM] System Health: OPTIMAL.");

		ApplyConsoleBootActionState(ConsoleBootActionState.Standby);
		AddConsoleLog("[SYSTEM] Nexus Core is ready for engagement.");
	}

	private void UpdateBootChannelInfo()
	{
		var settings = SetupSettingsService.Instance.Settings;
		ConsoleHostLabel.Text = settings.ListenAddress;
		ConsolePortLabel.Text = settings.ServerPort.ToString();
		BootHostLabel.Text = settings.ListenAddress;
		BootPortLabel.Text = settings.ServerPort.ToString();
		BootProbeLabel.Text = GetProbeAddress(settings.ListenAddress);
		BootLogLabel.Text = $"Logs/{SessionLogPaths.ComfyServerLatestFileName}";
	}

	private async void OnBootLogOpenTapped(object? sender, TappedEventArgs e)
	{
		string logsPath = ComfyInstallService.GetLocalRuntimePath("Logs");
		string logPath = System.IO.Path.Combine(logsPath, SessionLogPaths.ComfyServerLatestFileName);
		string targetPath = File.Exists(logPath) ? logPath : logsPath;

		var result = await PlatformManager.Current.Shell.OpenPathAsync(targetPath);
		if (!result.IsSuccess)
		{
			NexusLog.Warning($"[SETUP] Failed to open server boot log path: {result.Message}");
		}
	}

	private void OnBootLogOpenHovered(object? sender, PointerEventArgs e)
	{
		BootLogOpenButton.BackgroundColor = ConsoleConfigPillHoverColor;
		BootLogOpenLabel.TextColor = DiagnosticActionDefaultTextColor;
	}

	private void OnBootLogOpenUnhovered(object? sender, PointerEventArgs e)
	{
		BootLogOpenButton.BackgroundColor = ConsoleConfigPillNormalColor;
		BootLogOpenLabel.TextColor = ConsoleAccentColor;
	}

	private void AutoSelectServerPythonModeFromComfyVenv()
	{
		var settings = SetupSettingsService.Instance.Settings;
		if (!string.Equals(settings.InstallMode, SetupInstallModes.ExistingComfyPath, StringComparison.Ordinal))
		{
			return;
		}

		if (settings.PendingVenvDelete
			|| settings.PendingBootTasks.Any(task => string.Equals(task.Id, PendingBootTaskIds.VenvDelete, StringComparison.Ordinal)))
		{
			AddConsoleLog("[CONFIG] Pending .venv delete detected. Keeping DIRECT Python mode.");
			return;
		}

		bool hasVenvPython = File.Exists(ComfyInstallService.ComfyVenvPythonExe);
		string detectedMode = hasVenvPython
			? PythonExecutionModes.Venv
			: PythonExecutionModes.ConfiguredPython;
		if (string.Equals(settings.ServerPythonMode, detectedMode, StringComparison.Ordinal))
		{
			AddConsoleLog(hasVenvPython
				? "[CONFIG] Existing ComfyUI .venv detected. VENV launch mode is active."
				: "[CONFIG] No ComfyUI .venv detected. DIRECT Python launch mode is active.");
			return;
		}

		settings.ServerPythonMode = detectedMode;
		SetupSettingsService.Instance.Save();
		AddConsoleLog(hasVenvPython
			? "[CONFIG] Existing ComfyUI .venv detected. Switching launch mode to VENV."
			: "[CONFIG] No ComfyUI .venv detected. Switching launch mode to DIRECT Python.");
	}

	private void UpdateServerPythonModeVisual()
	{
		_isUpdatingServerPythonMode = true;
		try
		{
			bool useVenv = RuntimePythonModePresenter.ShouldDisplayVenvMode(
				SetupSettingsService.Instance.Settings,
				includeActiveLaunchSnapshot: false);
			var colors = GetServerPythonModeColors(useVenv);
			ServerPythonModeLabel.Text = useVenv ? "VENV" : "DIRECT";
			ServerPythonModeLabel.TextColor = colors.Accent;
			ServerPythonModePill.BackgroundColor = colors.Pill;
			ServerPythonModeTrack.Color = colors.Track;
			ServerPythonModeKnob.BackgroundColor = colors.Accent;
			ServerPythonModeKnob.HorizontalOptions = useVenv ? LayoutOptions.End : LayoutOptions.Start;
		}
		finally
		{
			_isUpdatingServerPythonMode = false;
		}
	}

	private void OnServerPythonModeTapped(object? sender, TappedEventArgs e)
	{
		if (_isUpdatingServerPythonMode || !IsConsoleConfigEditable()) return;

		var settings = SetupSettingsService.Instance.Settings;
		bool useVenv = settings.ServerPythonMode != PythonExecutionModes.Venv;
		settings.ServerPythonMode = useVenv ? PythonExecutionModes.Venv : PythonExecutionModes.ConfiguredPython;
		SetupSettingsService.Instance.Save();
		UpdateServerPythonModeVisual();

		string modeText = useVenv ? ".venv isolated runtime" : "configured Python runtime";
		AddConsoleLog($"[CONFIG] Server Python mode set to {modeText}.");
	}

	private static (Color Accent, Color Pill, Color Track) GetServerPythonModeColors(bool useVenv)
	{
		return useVenv
			? (ServerPythonVenvColor, ServerPythonVenvPillColor, ServerPythonVenvTrackColor)
			: (ServerPythonDirectColor, ServerPythonDirectPillColor, ServerPythonDirectTrackColor);
	}

	private async void OnHostEditClicked(object? sender, TappedEventArgs e)
	{
		if (!IsConsoleConfigEditable()) return;

		var page = GetPromptPage();
		if (page == null) return;

		var settings = SetupSettingsService.Instance.Settings;
		string? result = await page.DisplayPromptAsync(
			LocalizationManager.Text("server_config.host_title"),
			LocalizationManager.Text("server_config.host_message"),
			LocalizationManager.Text("common.save"),
			LocalizationManager.Text("common.cancel"),
			"127.0.0.1",
			64,
			Keyboard.Text,
			settings.ListenAddress);

		if (result == null || !IsConsoleConfigEditable()) return;

		string host = result.Trim();
		if (!IsValidHostValue(host))
		{
			await page.DisplayAlertAsync(
				LocalizationManager.Text("server_config.invalid_host_title"),
				LocalizationManager.Text("server_config.invalid_host_message"),
				LocalizationManager.Text("common.ok"));
			return;
		}

		settings.ListenAddress = host;
		SetupSettingsService.Instance.Save();
		UpdateBootChannelInfo();
		AddConsoleLog($"[CONFIG] ComfyUI host set to {host}.");
	}

	private async void OnPortEditClicked(object? sender, TappedEventArgs e)
	{
		if (!IsConsoleConfigEditable()) return;

		var page = GetPromptPage();
		if (page == null) return;

		var settings = SetupSettingsService.Instance.Settings;
		string? result = await page.DisplayPromptAsync(
			LocalizationManager.Text("server_config.port_title"),
			LocalizationManager.Text("server_config.port_message"),
			LocalizationManager.Text("common.save"),
			LocalizationManager.Text("common.cancel"),
			"8188",
			5,
			Keyboard.Numeric,
			settings.ServerPort.ToString());

		if (result == null || !IsConsoleConfigEditable()) return;

		if (!int.TryParse(result.Trim(), out int port) || port < 1 || port > 65535)
		{
			await page.DisplayAlertAsync(
				LocalizationManager.Text("server_config.invalid_port_title"),
				LocalizationManager.Text("server_config.invalid_port_message"),
				LocalizationManager.Text("common.ok"));
			return;
		}

		settings.ServerPort = port;
		SetupSettingsService.Instance.Save();
		UpdateBootChannelInfo();
		AddConsoleLog($"[CONFIG] ComfyUI port set to {port}.");
	}

	private void OnConsoleConfigPillHovered(object? sender, PointerEventArgs e)
	{
		if (!IsConsoleConfigEditable() || sender is not Border border) return;

		border.BackgroundColor = ConsoleConfigPillHoverColor;
		_ = SafeAnimation.FadeToAsync(border, 1, 120, Easing.CubicOut, "Setup.ConsoleConfig");
	}

	private void OnConsoleConfigPillUnhovered(object? sender, PointerEventArgs e)
	{
		if (sender is not Border border) return;
		if (!IsConsoleConfigEditable())
		{
			UpdateConsoleConfigAvailability();
			return;
		}

		if (ReferenceEquals(border, ServerPythonModePill))
		{
			UpdateServerPythonModeVisual();
			return;
		}

		border.BackgroundColor = ConsoleConfigPillNormalColor;
	}

	private Page? GetPromptPage()
		=> Window?.Page ?? Application.Current?.Windows.FirstOrDefault()?.Page;

	private static bool IsValidHostValue(string host)
		=> !string.IsNullOrWhiteSpace(host)
			&& host.Length <= 64
			&& !host.Any(char.IsWhiteSpace);

	private async Task InitializeGpuSelectorAsync()
	{
		GpuDiscoveryService.StartResult gpuStart = await GpuDiscoveryService.StartAsync();
		PopulateGpuSelector(gpuStart.Devices);
		if (!gpuStart.IsSuccess)
		{
			AddConsoleLog($"[GPU] Discovery unavailable: {gpuStart.FailureMessage}");
		}
	}

	private void RefreshGpuSelectorFromKnownDevices()
		=> PopulateGpuSelector(GpuDiscoveryService.GetCachedDevicesOrFallback());

	private void PopulateGpuSelector(IReadOnlyList<GpuDeviceInfo> devices)
	{
		GpuSelectorStack.Children.Clear();
		GpuSelectorDropdownPanel.IsVisible = false;
		_gpuOptionCards.Clear();
		_gpuDevices.Clear();
		SetGpuSelectorExpanded(false);

		_gpuDevices.AddRange(devices);
		foreach (GpuDeviceInfo device in devices)
		{
			Border card = CreateGpuOptionCard(device);
			GpuSelectorStack.Children.Add(card);
			_gpuOptionCards.Add(card);
		}

		UpdateGpuSelectionVisuals();
		AddConsoleLog($"[GPU] {devices.Count} device option(s) ready. Selected GPU {SetupSettingsService.Instance.Settings.GpuId}.");
	}

	private Border CreateGpuOptionCard(GpuDeviceInfo device)
	{
		var title = new Label
		{
			Text = FormatGpuLabel(device.Id),
			TextColor = GpuCardTitleColor,
			FontSize = GpuCardTitleFontSize,
			FontAttributes = FontAttributes.Bold,
			VerticalOptions = LayoutOptions.Center
		};

		var name = new Label
		{
			Text = device.Name,
			TextColor = GpuCardNameColor,
			FontSize = GpuCardNameFontSize,
			LineBreakMode = LineBreakMode.TailTruncation
		};

		var memory = new Label
		{
			Text = device.MemoryTotalMb,
			TextColor = GpuCardMemoryColor,
			FontSize = GpuCardMemoryFontSize,
			LineBreakMode = LineBreakMode.TailTruncation
		};

		var stack = new VerticalStackLayout
		{
			Spacing = 1,
			InputTransparent = true,
			Children = { title, name, memory }
		};

		var card = new Border
		{
			BindingContext = device,
			Padding = new Thickness(9, 6),
			StrokeThickness = 1,
			StrokeShape = new RoundRectangle { CornerRadius = GpuCardCornerRadius },
			Content = stack
		};

		card.GestureRecognizers.Add(new TapGestureRecognizer
		{
			Command = new Command(() => SelectGpuDevice(device.Id))
		});

		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (s, e) => ApplyGpuCardVisual(card, true);
		pointer.PointerExited += (s, e) => UpdateGpuSelectionVisuals();
		card.GestureRecognizers.Add(pointer);

		return card;
	}

	private void OnGpuSelectorTapped(object? sender, EventArgs e)
	{
		if (_gpuDevices.Count <= 1) return;

		SetGpuSelectorExpanded(!_isGpuSelectorExpanded);
	}

	private void SelectGpuDevice(string gpuId)
	{
		SetupSettingsService.Instance.Settings.GpuId = gpuId;
		SetupSettingsService.Instance.Save();
		SelectedGpuLabel.Text = FormatGpuLabel(gpuId);
		SetGpuSelectorExpanded(false);
		UpdateGpuSelectionVisuals();
		AddConsoleLog($"[GPU] CUDA device set to GPU {gpuId}. The next boot will use --cuda-device {gpuId}.");
	}

	private void UpdateGpuSelectionVisuals()
	{
		string selectedGpuId = SetupSettingsService.Instance.Settings.GpuId;
		GpuDeviceInfo? selectedDevice = _gpuDevices.FirstOrDefault(device => string.Equals(device.Id, selectedGpuId, StringComparison.Ordinal));
		if (selectedDevice == null && _gpuDevices.Count > 0)
		{
			selectedDevice = _gpuDevices[0];
			SetupSettingsService.Instance.Settings.GpuId = selectedDevice.Id;
			SetupSettingsService.Instance.Save();
			selectedGpuId = selectedDevice.Id;
		}

		if (selectedDevice != null)
		{
			SelectedGpuLabel.Text = FormatGpuLabel(selectedDevice.Id);
			SelectedGpuNameLabel.Text = FormatGpuLabel(selectedDevice.Id);
			SelectedGpuDetailLabel.Text = $"{selectedDevice.Name} - {selectedDevice.MemoryTotalMb}";
			GpuDropdownGlyphLabel.IsVisible = _gpuDevices.Count > 1;
		}

		foreach (Border card in _gpuOptionCards)
		{
			if (card.BindingContext is not GpuDeviceInfo device) continue;

			bool isSelected = string.Equals(device.Id, selectedGpuId, StringComparison.Ordinal);
			ApplyGpuCardVisual(card, isSelected);
		}
	}

	private static void ApplyGpuCardVisual(Border card, bool isActive)
	{
		card.BackgroundColor = isActive ? GpuCardSelectedBackgroundColor : GpuCardNormalBackgroundColor;
		card.Stroke = isActive ? GpuCardSelectedStrokeColor : GpuCardNormalStrokeColor;
	}

	private void SetGpuSelectorExpanded(bool isExpanded)
	{
		_isGpuSelectorExpanded = isExpanded;
		GpuSelectorDropdownPanel.IsVisible = isExpanded;
		GpuDropdownGlyphLabel.Text = isExpanded ? "^" : "v";
	}

	private static string FormatGpuLabel(string gpuId)
		=> $"GPU {gpuId}";

	private async Task RunServerBootSequenceAsync(bool repairRuntimeBeforeBoot = false)
	{
		ApplyConsoleBootActionState(ConsoleBootActionState.Booting);

		SetupStepResult result;
		try
		{
			result = await _setupSequence.RunServerBootAsync(repairRuntimeBeforeBoot, AddConsoleLog);
		}
		catch (OperationCanceledException)
		{
			string message = LocalizationManager.Text("setup.console.boot_canceled");
			AddConsoleLog($"[ERROR] {message}");
			ApplyConsoleBootActionState(ConsoleBootActionState.Failed, message);
			return;
		}
		catch (Exception ex)
		{
			string message = LocalizationManager.Format("setup.console.boot_failed_with_message", ex.Message);
			AddConsoleLog($"[ERROR] {message}");
			ApplyConsoleBootActionState(ConsoleBootActionState.Failed, message);
			return;
		}

		if (!result.IsSuccess)
		{
			AddConsoleLog($"[ERROR] {result.Message}");
			ApplyConsoleBootActionState(ConsoleBootActionState.Failed, result.Message);
			return;
		}

		if (result.RequiresSetupHandoff)
		{
			AddConsoleLog($"[SYSTEM] {result.Message}");
			ApplyConsoleBootActionState(ConsoleBootActionState.Standby, LocalizationManager.Text("setup.console.maintenance_completed"));
			ResetFlow();
			return;
		}

		ApplyConsoleBootActionState(ConsoleBootActionState.Online);
		AddConsoleLog("[SYSTEM] Nexus Core Server detected on target port.");
		AddConsoleLog("[SYSTEM] Deployment ready.");
	}

	private static Task InvokeOnMainThreadSafeAsync(Func<Task> action)
	{
		return UiThread.InvokeAsync(action, "PRODUCT_SETUP:UI");
	}

	private void StartConsoleReadyPulse()
	{
		if (_isDisposed)
		{
			return;
		}

		_consoleBootingPulseClip.Stop();
		ConsoleBootPulseSurface.Opacity = 1;
		_consoleReadyPulseClip.PlayLoop(CanRepeatConsoleReadyPulse);
	}

	private void StopConsoleReadyPulse()
	{
		_consoleReadyPulseClip.Stop();
		ConsoleBootPulseSurface.Opacity = 0;
	}

	private void StartConsoleBootingPulse()
	{
		if (_isDisposed)
		{
			return;
		}

		_consoleReadyPulseClip.Stop();
		ConsoleBootPulseSurface.Opacity = 1;
		_consoleBootingPulseClip.PlayLoop(CanRepeatConsoleBootingPulse);
	}

	private void StopConsoleBootingPulse()
	{
		_consoleBootingPulseClip.Stop();
		ConsoleBootPulseSurface.Opacity = 0;
	}

	private void StartConsoleStatusBootingPulse()
	{
		if (_isDisposed)
		{
			return;
		}

		ConsoleStatusPulseSurface.Opacity = 1;
		_consoleStatusBootingPulseClip.PlayLoop(CanRepeatConsoleStatusBootingPulse);
	}

	private void StopConsoleStatusBootingPulse()
	{
		_consoleStatusBootingPulseClip.Stop();
		ConsoleStatusPulseSurface.Opacity = 0;
	}

	private bool CanRepeatConsoleReadyPulse()
		=> !_isDisposed
			&& IsVisible
			&& Handler is not null
			&& ServerConsolePanel.IsVisible
			&& _consoleBootActionState == ConsoleBootActionState.Standby;

	private bool CanRepeatConsoleBootingPulse()
		=> !_isDisposed
			&& IsVisible
			&& Handler is not null
			&& ServerConsolePanel.IsVisible
			&& _consoleBootActionState == ConsoleBootActionState.Booting;

	private bool CanRepeatConsoleStatusBootingPulse()
		=> !_isDisposed
			&& IsVisible
			&& Handler is not null
			&& ServerConsolePanel.IsVisible
			&& _consoleBootActionState == ConsoleBootActionState.Booting;

	private async void OnConsoleRetryClicked(object? sender, EventArgs e)
	{
		if (_consoleBootActionState != ConsoleBootActionState.Failed) return;

		await RunSystemBootCheckAsync();
		if (_consoleBootActionState == ConsoleBootActionState.Standby)
		{
			if (_consoleRepairBeforeBoot && !await ConfirmConsoleRepairBootAsync())
			{
				return;
			}

			await RunServerBootSequenceAsync(_consoleRepairBeforeBoot);
		}
	}

	private void OnConsoleRepairBeforeBootToggleClicked(object? sender, EventArgs e)
	{
		if (_consoleBootActionState is not (ConsoleBootActionState.Standby or ConsoleBootActionState.Failed)) return;

		_consoleRepairBeforeBoot = !_consoleRepairBeforeBoot;
		ApplyConsoleRepairBeforeBootToggleVisual(ShouldShowConsoleRecoverBoot(_consoleBootActionState));
	}

	private async Task<bool> ConfirmConsoleRepairBootAsync()
	{
		var page = GetPromptPage();
		if (page == null)
		{
			return true;
		}

		string repairTarget = RuntimeRepairTarget.GetDisplay();
		return await page.DisplayAlertAsync(
			LocalizationManager.Text("setup.console.recover_boot_title"),
			LocalizationManager.Format("setup.console.recover_boot_message", repairTarget),
			LocalizationManager.Text("setup.console.recover_boot"),
			LocalizationManager.Text("common.cancel"));
	}

	private void ApplyConsoleBootActionState(ConsoleBootActionState state, string? detail = null)
	{
		XamlLifetimeDiagnostics.RecordSurface("product-setup-console", state.ToString());
		_consoleBootActionState = state;
		if (_isDisposed) return;

		StopConsoleReadyPulse();
		StopConsoleBootingPulse();
		StopConsoleStatusBootingPulse();
		StopPrimaryActionReadyPulse();

		ConsoleBootButton.CancelAnimations();
		ConsoleRepairBeforeBootToggle.CancelAnimations();
		ConsoleRetryButton.CancelAnimations();
		ConsoleRetryButton.Scale = 1.0;
		ConsoleBootPulseSurface.Opacity = 0;

		bool showRepairToggle = ShouldShowConsoleRecoverBoot(state);
		if (!showRepairToggle)
		{
			_consoleRepairBeforeBoot = false;
		}

		ConsoleBootActionsGrid.Spacing = showRepairToggle ? ConsoleBootActionsExpandedRowSpacing : 0;
		SetConsoleButtonAvailability(
			ConsoleBootButton,
			isVisible: state is ConsoleBootActionState.Preparing or ConsoleBootActionState.Standby or ConsoleBootActionState.Booting,
			isEnabled: state == ConsoleBootActionState.Standby);
		SetConsoleButtonAvailability(
			ConsoleRepairBeforeBootToggle,
			isVisible: showRepairToggle,
			isEnabled: showRepairToggle);
		SetConsoleButtonAvailability(
			ConsoleRetryButton,
			isVisible: state == ConsoleBootActionState.Failed,
			isEnabled: state == ConsoleBootActionState.Failed);
		ConsoleBootButton.Opacity = state switch
		{
			ConsoleBootActionState.Preparing => ConsoleButtonPreparingOpacity,
			ConsoleBootActionState.Booting => ConsoleButtonBootingOpacity,
			_ => 1.0
		};
		ApplyConsoleRepairBeforeBootToggleVisual(showRepairToggle);
		ConsoleRetryButton.Opacity = state == ConsoleBootActionState.Failed ? 1.0 : 0.0;

		PrimaryActionButton.IsEnabled = state == ConsoleBootActionState.Online;
		PrimaryActionButton.InputTransparent = state != ConsoleBootActionState.Online;
		PrimaryActionButton.Opacity = state == ConsoleBootActionState.Online ? 1.0 : 0.4;
		UpdateBackButtonAvailability();
		UpdateConsoleConfigAvailability();

		switch (state)
		{
			case ConsoleBootActionState.Preparing:
				SetServerState(LocalizationManager.Text("setup.console.state_standby"), LocalizationManager.Text("setup.console.preparing_detail"), ConsoleStateAccentHex, LocalizationManager.Text("setup.console.badge_ready"));
				SetConsoleStatus(LocalizationManager.Text("setup.console.state_initializing"), ConsoleStateInitializingHex);
				break;
			case ConsoleBootActionState.Standby:
				SetServerState(LocalizationManager.Text("setup.console.state_standby"), LocalizationManager.Text("setup.console.standby_detail"), ConsoleStateAccentHex, LocalizationManager.Text("setup.console.badge_ready"));
				SetConsoleStatus(LocalizationManager.Text("setup.console.state_standby"), ConsoleStateAccentHex);
				StartConsoleReadyPulse();
				break;
			case ConsoleBootActionState.Booting:
				SetServerState(LocalizationManager.Text("setup.console.state_booting"), LocalizationManager.Text("setup.console.booting_detail"), ConsoleStateWarningHex, LocalizationManager.Text("setup.console.badge_boot"));
				SetConsoleStatus(LocalizationManager.Text("setup.console.state_booting"), ConsoleStateWarningHex);
				StartConsoleBootingPulse();
				StartConsoleStatusBootingPulse();
				break;
			case ConsoleBootActionState.Failed:
				SetServerState(LocalizationManager.Text("setup.console.state_failed"), detail ?? LocalizationManager.Text("setup.console.failed_detail"), ConsoleStateFailedHex, LocalizationManager.Text("setup.console.badge_fail"));
				SetConsoleStatus(LocalizationManager.Text("setup.console.state_failed"), ConsoleStateFailedHex);
				ConsoleRetryButton.BackgroundColor = ConsoleRetryNormalColor;
				break;
			case ConsoleBootActionState.Online:
				SetServerState(LocalizationManager.Text("setup.console.state_online"), LocalizationManager.Text("setup.console.online_detail"), ConsoleStateAccentHex, LocalizationManager.Text("setup.console.badge_live"));
				SetConsoleStatus(LocalizationManager.Text("setup.console.state_online"), ConsoleStateAccentHex);
				_currentState = ViewState.Ready;
				StartPrimaryActionReadyPulse();
				break;
		}

		XamlLifetimeDiagnostics.WriteSnapshot($"product-setup-console:{state}");
	}

	private bool IsConsoleConfigEditable()
		=> _consoleBootActionState is ConsoleBootActionState.Standby or ConsoleBootActionState.Failed;

	private void UpdateConsoleConfigAvailability()
	{
		bool isEditable = IsConsoleConfigEditable();
		SetConsoleConfigPillAvailability(ConsoleHostPill, isEditable);
		SetConsoleConfigPillAvailability(ConsolePortPill, isEditable);
		SetConsoleConfigPillAvailability(ServerPythonModePill, isEditable);

		if (!isEditable)
		{
			ConsoleHostPill.BackgroundColor = ConsoleConfigPillNormalColor;
			ConsolePortPill.BackgroundColor = ConsoleConfigPillNormalColor;
			UpdateServerPythonModeVisual();
		}
	}

	private static void SetConsoleConfigPillAvailability(Border pill, bool isEditable)
	{
		pill.IsEnabled = isEditable;
		pill.InputTransparent = !isEditable;
		pill.Opacity = isEditable ? 1.0 : ConsoleConfigDisabledOpacity;
	}

	private static void SetConsoleButtonAvailability(Border button, bool isVisible, bool isEnabled)
	{
		button.IsVisible = isVisible;
		button.IsEnabled = isEnabled;
		button.InputTransparent = !isEnabled;
	}

	private void ApplyConsoleRepairBeforeBootToggleVisual(bool isVisible)
	{
		ConsoleRepairBeforeBootToggle.Opacity = isVisible ? 1.0 : 0.0;
		ConsoleRepairBeforeBootGlyph.Text = _consoleRepairBeforeBoot ? "■" : "□";
		ConsoleRepairBeforeBootToggle.BackgroundColor = _consoleRepairBeforeBoot
			? ConsoleRecoverHoverColor
			: ConsoleRecoverNormalColor;
	}

	private void SetConsoleStatus(string text, string colorHex)
	{
		var color = Color.FromArgb(colorHex);
		ConsoleStatusLabel.Text = text;
		ConsoleStatusBorder.Stroke = color;
		ConsoleStatusLabel.TextColor = color;
	}

	private void SetServerState(string title, string detail, string colorHex, string stateTag)
	{
		var color = Color.FromArgb(colorHex);
		ServerStateTitleLabel.Text = title;
		ServerStateTitleLabel.TextColor = color;
		ServerStateDetailLabel.Text = detail;
		ServerStateGlyphLabel.Text = stateTag;
		ServerStateGlyphLabel.TextColor = color;
		ServerStateBadge.BackgroundColor = color.WithAlpha(0.16f);
		ServerStateAccentBar.BackgroundColor = color;
		ServerStateCardGlow.Color = color;
		ServerStateCardGlow.Opacity = title == "FAILED" ? 0.14 : 0.08;
		ServerStateCard.BackgroundColor = ServerStateCardBackgroundColor;
	}

	private static string GetProbeAddress(string listenAddress)
	{
		if (string.IsNullOrWhiteSpace(listenAddress)) return "127.0.0.1";

		string normalized = listenAddress.Trim();
		return normalized is "0.0.0.0" or "::" or "*" ? "127.0.0.1" : normalized;
	}

	private void AddConsoleLog(string message)
	{
		if (_isDisposed)
		{
			return;
		}

		ConsoleLogTail.AppendLine(message);
	}

	private static bool ShouldShowConsoleRecoverBoot(ConsoleBootActionState state)
	{
		if (state == ConsoleBootActionState.Failed)
		{
			return true;
		}

		return state == ConsoleBootActionState.Standby
			&& SetupSettingsService.Instance.Settings.LastLaunchSuccessful;
	}

	private async Task TransitionToPanel(VisualElement panel, string actionText)
	{
		await SafeAnimation.FadeToAsync(WelcomeContainer, 0, PanelQuickAnimationLength, Easing.CubicIn, "Setup.Panel");
		WelcomeContainer.IsVisible = false;
		SetSceneMotionState(SetupSceneMotionState.Panel);
		PreparePanelReveal(panel);

		ShowActionBottomBar(actionText, false);

		await Task.WhenAll(
			SafeAnimation.FadeToAsync(panel, 1, PanelShowLength, Easing.CubicOut, "Setup.Panel"),
			SafeAnimation.TranslateToAsync(panel, 0, 0, PanelShowLength, Easing.CubicOut, "Setup.Panel"),
			SafeAnimation.FadeToAsync(ActionBottomBar, 1, PanelShowLength, Easing.CubicOut, "Setup.Panel"),
			SafeAnimation.TranslateToAsync(ActionBottomBar, 0, 0, PanelShowLength, Easing.CubicOut, "Setup.Panel")
		);
	}

	private async void OnBackClicked(object? sender, EventArgs e)
	{
		if (_isDiagnosticActionRunning || _consoleBootActionState == ConsoleBootActionState.Booting) return;
		if (_currentState != ViewState.Ready || _isTransitioning) return;

		if (_currentContext == ViewContext.Repairing)
		{
			_repairCts?.Cancel();
		}

		if (ServerConsolePanel.IsVisible)
		{
			// RETRY ACTION: Go back to diagnostics
			await FadeOutAndHideAsync(ServerConsolePanel, PanelQuickAnimationLength, Easing.CubicIn);
			SetSceneMotionState(SetupSceneMotionState.Panel);
			StartSetupBackdropMotion();

			if (_selectedInstallMode == SetupInstallModes.LocalRuntime)
			{
				VanguardPanel.IsVisible = true;
				_currentContext = ViewContext.Vanguard;
				EvaluateOverallReadiness(); // This will call ShowActionBottomBar with correct state

				await ShowPanelAndActionBarQuickAsync(VanguardPanel);
			}
			else
			{
				ArchitectInitiationPanel.IsVisible = true;
				_currentContext = ViewContext.Architect;
				CheckArchitectInitiationStatus(); // This will call ShowActionBottomBar

				await ShowPanelAndActionBarQuickAsync(ArchitectInitiationPanel);
			}

			_isTransitioning = false;
			_currentState = ViewState.Ready;
			return;
		}

		_isTransitioning = true;
		_currentState = ViewState.EndAction;

		// 1. Restore the complete Crossroads backdrop before the welcome surface returns.
		SetSceneMotionState(SetupSceneMotionState.Crossroads);
		VanguardContainer.CancelAnimations();
		ArchitectContainer.CancelAnimations();
		VanguardContainer.Scale = 1;
		ArchitectContainer.Scale = 1;

		var panel = _currentContext == ViewContext.Vanguard || _currentContext == ViewContext.Repairing ?
					VanguardPanel :
					(ArchitectInitiationPanel.IsVisible ? ArchitectInitiationPanel : ArchitectWorkspacePanel);

		await Task.WhenAll(
			SafeAnimation.FadeToAsync(panel, 0, PanelQuickAnimationLength, Easing.CubicIn, "Setup.Panel"),
			SafeAnimation.TranslateToAsync(panel, 0, PanelRevealOffsetY, PanelQuickAnimationLength, Easing.CubicIn, "Setup.Panel"),
			SafeAnimation.FadeToAsync(ActionBottomBar, 0, PanelQuickAnimationLength, Easing.CubicIn, "Setup.Panel"),
			SafeAnimation.TranslateToAsync(ActionBottomBar, 0, ActionBarHideOffsetY, PanelQuickAnimationLength, Easing.CubicIn, "Setup.Panel")
		);

		panel.IsVisible = false;
		HideArchitectPanelsAndActionBar();

		// 2. Start Main Action (Return to Crossroads)
		_currentState = ViewState.StartAction;

		// Reset states for clean fade-in
		WelcomeContainer.Opacity = 0;
		WelcomeContainer.IsVisible = true;
		_background.SetBaseOpacity(BackgroundDimmedOpacity);

		// Let the restored crossroads surface attach before its reveal begins.
		await UiThread.YieldDispatcherFramesAsync(1, "Setup.Crossroads");

		_ = _background.FadeBaseToAsync(BackgroundRevealOpacity, CrossroadsBackgroundRevealLength, Easing.CubicOut);
		await SafeAnimation.FadeToAsync(WelcomeContainer, 1, CrossroadsWelcomeRevealLength, Easing.CubicOut, "Setup.Crossroads");

		// 3. Ready State
		_currentContext = ViewContext.Crossroads;
		_currentState = ViewState.Ready;
		_isTransitioning = false;
		SetSceneMotionState(SetupSceneMotionState.Crossroads);
		StartCrossroadsAmbientPulse();
	}

	private static void PreparePanelReveal(VisualElement panel)
	{
		panel.Opacity = 0;
		panel.IsVisible = true;
		panel.TranslationY = PanelRevealOffsetY;
	}

	private static async Task FadeOutAndHideAsync(VisualElement panel, uint length, Easing? easing = null)
	{
		await SafeAnimation.FadeToAsync(panel, 0, length, easing, "Setup.Panel");
		panel.IsVisible = false;
	}

	private Task ShowPanelAndActionBarQuickAsync(VisualElement panel)
	{
		panel.IsVisible = true;
		EnsureActionBottomBarReady(restoreOpacity: false);

		return Task.WhenAll(
			SafeAnimation.FadeToAsync(panel, 1, PanelQuickAnimationLength, source: "Setup.Panel"),
			SafeAnimation.FadeToAsync(ActionBottomBar, 1, PanelQuickAnimationLength, source: "Setup.Panel"));
	}

	private void HideArchitectPanelsAndActionBar()
	{
		ArchitectInitiationPanel.IsVisible = false;
		ArchitectWorkspacePanel.IsVisible = false;
		ActionBottomBar.IsVisible = false;
	}

	private void UpdateHoverStatesFromMouse()
	{
		if (_currentState != ViewState.Ready || _currentContext != ViewContext.Crossroads) return;

		// Precise Bounds-based Detection using Manual coordinate mapping
		// We calculate the absolute position relative to the main viewport
		bool isOverVanguard = IsMouseOverElement(VanguardContainer, _lastGlobalMousePos);
		bool isOverArchitect = IsMouseOverElement(ArchitectContainer, _lastGlobalMousePos);

		_background.SetMode(isOverVanguard
			? SetupBackgroundMode.VanguardHover
			: isOverArchitect
				? SetupBackgroundMode.ArchitectHover
				: SetupBackgroundMode.Crossroads);
	}

	private void OnArchitectPathConfirmed(object? sender, string path)
	{
		_architectCandidateComfyPath = path;
		bool isValid = IsValidComfyPath(path);

		if (isValid)
		{
			ComfyInstallService.EnsureComfyWorkspaceDirectories(path);
			SetupSettingsService.Instance.UseExistingComfyPath(path);
		}

		UpdateArchitectWorkspaceReadiness();
	}

	private void UpdateArchitectWorkspaceReadiness()
	{
		bool isValid = IsValidComfyPath(_architectCandidateComfyPath);

		PrimaryActionButton.IsEnabled = isValid;
		PrimaryActionButton.InputTransparent = !isValid;
		PrimaryActionButton.Opacity = isValid ? 1 : 0.4;
		PrimaryActionLabel.Text = LocalizationManager.Text("common.next");

		if (isValid) StartPrimaryActionReadyPulse();
		else StopPrimaryActionReadyPulse();
	}

	private async void OnPrimaryActionClicked(object? sender, EventArgs e)
	{
		if (IsInitiationInteractionBlocked) return;
		if (_currentState != ViewState.Ready) return;
		NexusLog.Info($"[SETUP:UI] Primary action tapped. context={_currentContext}, vanguardVisible={VanguardPanel.IsVisible}, architectWorkspaceVisible={ArchitectWorkspacePanel.IsVisible}, architectInitiationVisible={ArchitectInitiationPanel.IsVisible}, consoleVisible={ServerConsolePanel.IsVisible}, consoleState={_consoleBootActionState}");

		if (VanguardPanel.IsVisible)
		{
			bool allReady = _vanguardRequiredSteps.Count > 0
				&& _vanguardRequiredSteps.All(step => step.CountsAsReady);
			if (allReady)
			{
				await TransitionToConsoleAsync();
			}
		}
		else if (ArchitectInitiationPanel.IsVisible)
		{
			if (IsArchitectInitiationReady())
			{
				await TransitionToConsoleAsync();
			}
		}
		else if (ArchitectWorkspacePanel.IsVisible)
		{
			if (IsValidComfyPath(_architectCandidateComfyPath))
			{
				ComfyInstallService.EnsureComfyWorkspaceDirectories(_architectCandidateComfyPath);
				SetupSettingsService.Instance.UseExistingComfyPath(_architectCandidateComfyPath);
				await TransitionToArchitectInitiationAsync();
			}
		}
		else if (ServerConsolePanel.IsVisible)
		{
			if (_consoleBootActionState == ConsoleBootActionState.Standby)
			{
				if (_consoleRepairBeforeBoot && !await ConfirmConsoleRepairBootAsync())
				{
					return;
				}

				await RunServerBootSequenceAsync(_consoleRepairBeforeBoot);
			}
			else if (PrimaryActionButton.IsEnabled && !_isTransitioning)
			{
				await FinalizeSetupAndLaunchAsync();
			}
		}
	}

	private async Task FinalizeSetupAndLaunchAsync()
	{
		if (_isTransitioning) return;

		_isTransitioning = true;
		this.InputTransparent = true;
		_currentState = ViewState.EndAction;

		AddConsoleLog("[SYSTEM] Workspace ready. Opening Nexus...");

		await Task.WhenAll(
			SafeAnimation.FadeToAsync(GlassPanel, 0, FinalizeGlassFadeLength, Easing.CubicIn, "Setup.Finalize"),
			_background.FadeBaseToAsync(0, FinalizeBackgroundFadeLength, Easing.CubicIn),
			SafeAnimation.FadeToAsync(ActionBottomBar, 0, PanelQuickAnimationLength, Easing.CubicIn, "Setup.Finalize"),
			SafeAnimation.TranslateToAsync(ActionBottomBar, 0, ActionBarHideOffsetY, PanelQuickAnimationLength, Easing.CubicIn, "Setup.Panel")
		);

		CancelTransientAnimationLoops();
		ReleaseAnimationCache();
		this.IsVisible = false;
		SetupFinalized?.Invoke(this, EventArgs.Empty);
	}

	private async Task EnsureAnimationCacheAsync()
	{
		if (_animationCacheLease is not null)
		{
			return;
		}

		_animationCacheAcquireTask ??= NexusAnimatedWebpFrameCache.AcquireAsync(NexusAnimatedWebpCacheGroup.Setup);
		_animationCacheLease = await _animationCacheAcquireTask.ConfigureAwait(false);
	}

	private void ReleaseAnimationCache()
	{
		_animationCacheLease?.Dispose();
		_animationCacheLease = null;
		_animationCacheAcquireTask = null;
	}

	private async Task RunRepairSequenceAsync()
	{
		if (_repairCts is not null)
		{
			return;
		}

		_repairCts = new CancellationTokenSource();
		var token = _repairCts.Token;

		_currentContext = ViewContext.Repairing;

		if (ArchitectWorkspacePanel.IsVisible)
		{
			await SafeAnimation.FadeToAsync(ArchitectWorkspacePanel, 0, PanelQuickAnimationLength, source: "Setup.Repair");
			ArchitectWorkspacePanel.IsVisible = false;
			PrepareVanguardChecklist();
			VanguardPanel.IsVisible = true;
			await SafeAnimation.FadeToAsync(VanguardPanel, 1, PanelQuickAnimationLength, source: "Setup.Repair");
			ResetInitiationScrollPosition(VanguardInitiationScrollView);
		}

		_currentState = ViewState.Ready;

		try
		{
			await _initiationSequence.RunVanguardAsync(_vanguardRequiredSteps, token);
		}
		catch (OperationCanceledException)
		{
			// Sequence was cancelled by user going back
			return;
		}
		finally
		{
			_repairCts = null;
			// Ensure UI is interactive again
			DiagnosticNodesList.InputTransparent = false;
			VanguardOptionalNodesList.InputTransparent = false;
			EvaluateOverallReadiness();
			_currentState = ViewState.Ready;
			UpdateBackButtonAvailability();
		}

		if (VanguardOptionalNodes.Count > 0)
		{
			await RefreshVanguardOptionalNodesSafeAsync();
			await ScrollVanguardOptionalSectionIntoViewAsync();
		}

		// All done
		_currentState = ViewState.Ready;
		UpdateBackButtonAvailability();
	}

	private async Task WaitForStepCompleteAsync(SetupDiagnosticStep step, CancellationToken token)
	{
		if (step.CountsAsReady)
		{
			return;
		}

		var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var reg = token.Register(() => tcs.TrySetCanceled());

		void handler(object? sender, EventArgs e)
		{
			if (step.CountsAsReady)
			{
				tcs.TrySetResult();
			}
		}

		void stepHandler(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(SetupDiagnosticStep.State) && step.CountsAsReady)
			{
				tcs.TrySetResult();
			}
		}

		step.ViewModel.CompletionSignalChanged += handler;
		step.PropertyChanged += stepHandler;
		try
		{
			if (step.CountsAsReady)
			{
				return;
			}

			await tcs.Task;
		}
		finally
		{
			step.ViewModel.CompletionSignalChanged -= handler;
			step.PropertyChanged -= stepHandler;
		}
	}

	private Task RequestDiagnosticScrollAsync(SetupDiagnosticStep step, SetupScrollReason reason)
	{
		_focusedRequiredStep = step.IsRequired ? step : null;
		_focusedDiagnosticNode = step.ViewModel;
		return RequestSetupScrollAsync(step, SetupScrollPivot.Top, reason, animated: true);
	}

	private void EvaluateCurrentInitiationReadiness()
	{
		if (_currentContext == ViewContext.Architect)
		{
			CheckArchitectInitiationStatus();
			return;
		}

		EvaluateOverallReadiness();
	}

	private static void UpdateDiagnosticProgress(DiagnosticNodeViewModel vm, double progress, string message)
	{
		UiThread.TryBeginInvoke(() =>
		{
			vm.UpdateProgressDisplay(progress, message);
		}, "PRODUCT_SETUP:DIAGNOSTIC_PROGRESS");
	}

	internal ObservableCollection<DiagnosticNodeViewModel> ArchitectNodes { get; } = new();
	internal ObservableCollection<DiagnosticNodeViewModel> ArchitectOptionalNodes { get; } = new();
	internal ObservableCollection<DiagnosticNodeViewModel> VanguardNodes { get; } = new();
	internal ObservableCollection<DiagnosticNodeViewModel> VanguardOptionalNodes { get; } = new();

	private void UpdateVanguardOptionalSectionVisibility()
	{
		bool hasOptionalNodes = VanguardOptionalNodes.Count > 0;
		VanguardOptionalHeader.IsVisible = hasOptionalNodes;
		VanguardOptionalNodesList.IsVisible = hasOptionalNodes;
	}

	private void UpdateArchitectOptionalSectionVisibility()
	{
		bool hasOptionalNodes = ArchitectOptionalNodes.Count > 0;
		ArchitectOptionalHeader.IsVisible = hasOptionalNodes;
		ArchitectOptionalNodesList.IsVisible = hasOptionalNodes;
	}

	private void UpdateArchitectRequiredStatus()
	{
		UpdateRequiredStatus(ArchitectRequiredStatusLabel, _architectRequiredSteps);
	}

	private void UpdateVanguardRequiredStatus()
	{
		UpdateRequiredStatus(VanguardRequiredStatusLabel, _vanguardRequiredSteps);
	}

	private static void UpdateRequiredStatus(Label label, IReadOnlyCollection<SetupDiagnosticStep> steps)
	{
		int totalCount = steps.Count;
		int readyCount = steps.Count(step => step.CountsAsReady);
		label.Text = $"{readyCount}/{totalCount} READY";
		label.TextColor = readyCount == totalCount && totalCount > 0
			? DiagnosticRequiredReadyColor
			: DiagnosticRequiredPendingColor;
	}

	private void PrepareVanguardChecklist()
	{
		VanguardNodes.Clear();
		VanguardOptionalNodes.Clear();
		_vanguardRequiredSteps.Clear();
		_vanguardOptionalSteps.Clear();

		AddRequiredVanguardStep(GetDiagnosticNode<GitDiagnosticNode>());
		AddRequiredVanguardStep(GetDiagnosticNode<PythonDiagnosticNode>());
		AddRequiredVanguardStep(GetDiagnosticNode<ComfyCoreDiagnosticNode>());
		AddRequiredVanguardStep(GetDiagnosticNode<BaseResourceDiagnosticNode>());
		AddRequiredVanguardStep(GetDiagnosticNode<ManagerExtensionDiagnosticNode>());

		AddOptionalVanguardStep(GetDiagnosticNode<ModelLibraryDiagnosticNode>());

		BindableLayout.SetItemsSource(DiagnosticNodesList, VanguardNodes);
		BindableLayout.SetItemsSource(VanguardOptionalNodesList, VanguardOptionalNodes);
		UpdateVanguardOptionalSectionVisibility();
		UpdateVanguardRequiredStatus();
		EvaluateOverallReadiness();
	}

	private void AddRequiredVanguardStep(IRuntimeDiagnosticNode node)
	{
		var step = CreateDiagnosticStep(node, isRequired: true);
		_vanguardRequiredSteps.Add(step);
		VanguardNodes.Add(step.ViewModel);
	}

	private void AddOptionalVanguardStep(IRuntimeDiagnosticNode node)
	{
		var step = CreateDiagnosticStep(node, isRequired: false);
		_vanguardOptionalSteps.Add(step);
		VanguardOptionalNodes.Add(step.ViewModel);
		if (node is PythonEnvironmentDiagnosticNode)
		{
			step.ViewModel.EnvironmentDetails = LocalizationManager.Text("setup.venv.requires_python");
			step.ViewModel.EnvironmentPath = ComfyInstallService.ComfyVenvPath;
		}
	}

	private void PopulateInlineActions(DiagnosticNodeViewModel vm, IConfigurableDiagnosticNode confNode)
	{
		vm.Actions.Clear();
		foreach (var opt in confNode.AvailableOptions)
		{
			var actionColors = GetDiagnosticActionColors(opt.Id, opt.IsRecommended);
			vm.Actions.Add(new DiagnosticActionViewModel
			{
				Id = opt.Id,
				DisplayName = opt.DisplayName,
				Description = opt.Description,
				WorkingHint = opt.WorkingHint,
				NormalBackground = actionColors.Normal,
				HoverBackground = actionColors.Hover,
				TextColor = actionColors.Text,
				Command = new Command(async () => await ExecuteNodeActionAsync(vm, confNode, opt.Id))
			});
		}
		vm.NotifyActionsChanged();
	}

	private void PopulatePersistentInlineActionsIfNeeded(DiagnosticNodeViewModel vm)
	{
		if (!ShouldKeepInlineActionsVisible(vm)) return;
		if (vm.Node is not IConfigurableDiagnosticNode configurableNode) return;

		PopulateInlineActions(vm, configurableNode);
		vm.ActionText = GetInteractiveActionText(vm);
		SetDiagnosticNodeInteraction(vm, true);
	}

	private void EnableDiagnosticNodeInteraction(DiagnosticNodeViewModel vm)
	{
		SetDiagnosticNodeInteraction(vm, true);
		SetDiagnosticActionNavigationBlocked(false);
	}

	private void OnGlassItemHovered(object? sender, PointerEventArgs e)
	{
		if (sender is Border b && b.BindingContext is DiagnosticNodeViewModel nodeVm)
		{
			if (nodeVm.IsLoading) return;

			b.BackgroundColor = NexusColors.SurfaceSubtle;
		}
		else if (sender is Border b2 && b2.BindingContext is DiagnosticActionViewModel actionVm)
		{
			b2.BackgroundColor = actionVm.HoverBackground;
		}
	}

	private void OnDiagnosticLoadingRingLoaded(object? sender, EventArgs e)
	{
		if (sender is not Image ring || ring.BindingContext is not DiagnosticNodeViewModel vm)
		{
			return;
		}

		vm.PropertyChanged -= OnDiagnosticLoadingRingViewModelChanged;
		vm.PropertyChanged += OnDiagnosticLoadingRingViewModelChanged;
		UpdateDiagnosticLoadingRing(ring, vm.IsLoading);
	}

	private void OnDiagnosticLoadingRingUnloaded(object? sender, EventArgs e)
	{
		if (sender is not Image ring)
		{
			return;
		}

		if (ring.BindingContext is DiagnosticNodeViewModel vm)
		{
			vm.PropertyChanged -= OnDiagnosticLoadingRingViewModelChanged;
		}

		DisposeDiagnosticLoadingRingClip(ring);
	}

	private void OnDiagnosticLoadingRingViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName != nameof(DiagnosticNodeViewModel.IsLoading)) return;
		if (sender is not DiagnosticNodeViewModel vm) return;

		UiThread.TryBeginInvoke(() =>
		{
			UpdateLoadingRingInList(DiagnosticNodesList, vm);
			UpdateLoadingRingInList(VanguardOptionalNodesList, vm);
			UpdateLoadingRingInList(ArchitectNodesList, vm);
			UpdateLoadingRingInList(ArchitectOptionalNodesList, vm);
		}, "PRODUCT_SETUP:DIAGNOSTIC_RING");
	}

	private void UpdateLoadingRingInList(Layout list, DiagnosticNodeViewModel vm)
	{
		foreach (Image ring in FindVisualChildren<Image>(list))
		{
			if (ring.BindingContext == vm && ring.StyleId == "DiagnosticLoadingRing")
			{
				UpdateDiagnosticLoadingRing(ring, vm.IsLoading);
			}
		}
	}

	private void UpdateDiagnosticLoadingRing(Image ring, bool isLoading)
	{
		if (isLoading)
		{
			StartDiagnosticLoadingRing(ring);
			return;
		}

		StopDiagnosticLoadingRing(ring);
	}

	private void StartDiagnosticLoadingRing(Image ring)
	{
		if (_isDisposed || !IsVisible || !ring.IsVisible)
		{
			return;
		}

		ring.Opacity = 1;
		GetOrCreateDiagnosticLoadingRingClip(ring).PlayLoop(() => CanRunDiagnosticLoadingRing(ring));
	}

	private void StopDiagnosticLoadingRing(Image ring)
	{
		if (_diagnosticLoadingRingClips.TryGetValue(ring, out NexusAnimatedWebpClip? clip))
		{
			clip.Stop();
		}

		ring.Opacity = 0;
	}

	private NexusAnimatedWebpClip GetOrCreateDiagnosticLoadingRingClip(Image ring)
	{
		if (_diagnosticLoadingRingClips.TryGetValue(ring, out NexusAnimatedWebpClip? clip))
		{
			return clip;
		}

		string motionName = $"{DiagnosticLoadingRingWebpAnimationName}.{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ring):X8}";
		clip = new NexusAnimatedWebpClip(_motion, ring, motionName, NexusAnimatedWebpCacheCatalog.SetupDiagnosticLoadingRing);
		_diagnosticLoadingRingClips.Add(ring, clip);
		return clip;
	}

	private bool CanRunDiagnosticLoadingRing(Image ring)
		=> !_isDisposed
			&& IsVisible
			&& Handler is not null
			&& ring.IsVisible
			&& ring.Opacity > 0;

	private void DisposeDiagnosticLoadingRingClip(Image ring)
	{
		if (_diagnosticLoadingRingClips.Remove(ring, out NexusAnimatedWebpClip? clip))
		{
			clip.Dispose();
		}

		ring.Opacity = 0;
	}

	private void StopDiagnosticLoadingRingAnimations()
	{
		foreach ((Image ring, NexusAnimatedWebpClip clip) in _diagnosticLoadingRingClips)
		{
			clip.Stop();
			ring.Opacity = 0;
		}
	}

	private void DisposeDiagnosticLoadingRingClips()
	{
		foreach (NexusAnimatedWebpClip clip in _diagnosticLoadingRingClips.Values)
		{
			clip.Dispose();
		}

		_diagnosticLoadingRingClips.Clear();
	}

	private static IEnumerable<T> FindVisualChildren<T>(Element parent)
		where T : Element
	{
		if (parent is not IVisualTreeElement visualParent)
		{
			yield break;
		}

		foreach (IVisualTreeElement visualChild in visualParent.GetVisualChildren())
		{
			if (visualChild is not Element child)
			{
				continue;
			}

			if (child is T typed)
			{
				yield return typed;
			}

			foreach (T descendant in FindVisualChildren<T>(child))
			{
				yield return descendant;
			}
		}
	}

	private void OnGlassItemUnhovered(object? sender, PointerEventArgs e)
	{
		if (sender is Border b && b.BindingContext is DiagnosticNodeViewModel nodeVm)
		{
			if (nodeVm.IsLoading) return;

			b.BackgroundColor = Colors.Transparent;
		}
		else if (sender is Border b2 && b2.BindingContext is DiagnosticActionViewModel actionVm)
		{
			b2.BackgroundColor = actionVm.NormalBackground;
		}
	}

	private async void OnDiagnosticNodeTapped(object? sender, TappedEventArgs e)
	{
		if (_isDiagnosticActionRunning) return;
		if (sender is not View { BindingContext: DiagnosticNodeViewModel nodeVm } editTrigger) return;
		if (!CanReconfigureReadyNode(nodeVm)) return;
		if (nodeVm.Node is not IConfigurableDiagnosticNode configurableNode) return;

		if (ShouldKeepInlineActionsVisible(nodeVm))
		{
			await configurableNode.ProbeEnvironmentAsync(_repairCts?.Token ?? CancellationToken.None);
			nodeVm.EnvironmentDetails = configurableNode.EnvironmentDetails;
			nodeVm.EnvironmentPath = configurableNode.EnvironmentPath;
			PopulateInlineActions(nodeVm, configurableNode);
			nodeVm.ActionText = GetInteractiveActionText(nodeVm);
			SetDiagnosticNodeInteraction(nodeVm, true);
			return;
		}

		if (nodeVm.HasActions)
		{
			nodeVm.Actions.Clear();
			nodeVm.NotifyActionsChanged();
			nodeVm.ActionText = GetRestingActionText(nodeVm);
			SetDiagnosticNodeInteraction(nodeVm, false);
			return;
		}

		PopulateInlineActions(nodeVm, configurableNode);
		nodeVm.ActionText = GetInteractiveActionText(nodeVm);
		SetDiagnosticNodeInteraction(nodeVm, true);
	}

	private void OnDiagnosticEditHovered(object? sender, PointerEventArgs e)
	{
		if (sender is Label label)
		{
			label.TextColor = DiagnosticActionDefaultTextColor;
		}
	}

	private void OnDiagnosticEditUnhovered(object? sender, PointerEventArgs e)
	{
		if (sender is Label label)
		{
			label.TextColor = ConsoleAccentColor;
		}
	}

	private async void OnDiagnosticRetryTapped(object? sender, TappedEventArgs e)
	{
		if (_isDiagnosticActionRunning) return;
		if (sender is not View { BindingContext: DiagnosticNodeViewModel nodeVm }) return;
		if (!nodeVm.CanRetry) return;
		if (nodeVm.Node is not IConfigurableDiagnosticNode configurableNode) return;

		if (configurableNode.AvailableOptions.Count == 0)
		{
			await configurableNode.ProbeEnvironmentAsync(_repairCts?.Token ?? CancellationToken.None);
		}

		nodeVm.EnvironmentDetails = configurableNode.EnvironmentDetails;
		nodeVm.EnvironmentPath = configurableNode.EnvironmentPath;
		PopulateInlineActions(nodeVm, configurableNode);
		SetDiagnosticNodeInteraction(nodeVm, true);

	}

	private void OnDiagnosticRetryHovered(object? sender, PointerEventArgs e)
	{
		if (sender is Border border)
		{
			border.BackgroundColor = DiagnosticActionDefaultHoverColor;
		}
	}

	private void OnDiagnosticRetryUnhovered(object? sender, PointerEventArgs e)
	{
		if (sender is Border border)
		{
			border.BackgroundColor = DiagnosticActionDefaultNormalColor;
		}
	}

	private void OnDiagnosticCancelTapped(object? sender, TappedEventArgs e)
	{
		if (sender is not View { BindingContext: DiagnosticNodeViewModel nodeVm } || !nodeVm.CanCancel)
		{
			return;
		}

		nodeVm.CanCancel = false;
		nodeVm.WorkingHint = LocalizationManager.Text("setup.base_model.canceling_download");
		var cts = _repairCts;
		cts?.Cancel();
		if (ReferenceEquals(_repairCts, cts))
		{
			_repairCts = new CancellationTokenSource();
		}
	}

	private async Task ScrollArchitectOptionalSectionIntoViewAsync()
	{
		if (_currentContext != ViewContext.Architect || ArchitectOptionalNodes.Count == 0)
		{
			return;
		}

		await InvokeOnMainThreadSafeAsync(async () =>
		{
			if (!ArchitectOptionalHeader.IsVisible) return;

			_focusedRequiredStep = null;
			_focusedDiagnosticNode = null;
			await RequestSetupScrollAsync(ArchitectOptionalHeader, SetupScrollPivot.Top, SetupScrollReason.OptionalSectionFocused, animated: true);
		});
	}

	private async Task ScrollVanguardOptionalSectionIntoViewAsync()
	{
		if (_currentContext is not (ViewContext.Vanguard or ViewContext.Repairing)
			|| VanguardOptionalNodes.Count == 0)
		{
			return;
		}

		await InvokeOnMainThreadSafeAsync(async () =>
		{
			if (!VanguardOptionalHeader.IsVisible) return;

			_focusedRequiredStep = null;
			_focusedDiagnosticNode = null;
			await RequestSetupScrollAsync(VanguardOptionalHeader, SetupScrollPivot.Top, SetupScrollReason.OptionalSectionFocused, animated: true);
		});
	}

	private Task RequestSetupScrollAsync(
		SetupDiagnosticStep step,
		SetupScrollPivot pivot,
		SetupScrollReason reason,
		bool animated)
		=> RequestSetupScrollAsync(step.ViewModel, pivot, reason, animated);

	private Task RequestSetupScrollAsync(
		DiagnosticNodeViewModel nodeVm,
		SetupScrollPivot pivot,
		SetupScrollReason reason,
		bool animated)
	{
		if (FindDiagnosticNodeContainer(nodeVm) is not { } target)
		{
			return Task.CompletedTask;
		}

		return RequestSetupScrollAsync(target, pivot, reason, animated);
	}

	private async Task NotifyDiagnosticItemUpdatedAsync(DiagnosticNodeViewModel nodeVm)
	{
		if (!IsOptionalTailDiagnosticNode(nodeVm))
		{
			return;
		}

		await RequestActiveSetupScrollToBottomAsync(SetupScrollReason.ItemUpdated, animated: true);
	}

	private Task RequestSetupScrollAsync(
		View target,
		SetupScrollPivot pivot,
		SetupScrollReason reason,
		bool animated)
	{
		return InvokeOnMainThreadSafeAsync(async () =>
		{
			try
			{
				if (_isDisposed) return;

				ScrollView? scrollView = GetActiveInitiationScrollView();
				if (scrollView == null) return;

				await WaitForLayoutPassAsync();
				if (_isDisposed) return;

				await ScrollTargetByPivotAsync(scrollView, target, pivot, animated);
			}
			catch (ObjectDisposedException)
			{
			}
			catch (InvalidOperationException)
			{
			}
			catch (Exception ex)
			{
				NexusLog.Exception(ex, $"[SETUP:UI] Setup scroll failed: {reason}");
			}
		});
	}

	private ScrollView? GetActiveInitiationScrollView()
		=> _currentContext switch
		{
			ViewContext.Vanguard or ViewContext.Repairing => VanguardInitiationScrollView,
			ViewContext.Architect => ArchitectInitiationScrollView,
			_ => null
		};

	private Task RequestActiveSetupScrollToBottomAsync(SetupScrollReason reason, bool animated)
	{
		return InvokeOnMainThreadSafeAsync(async () =>
		{
			try
			{
				if (_isDisposed) return;

				ScrollView? scrollView = GetActiveInitiationScrollView();
				if (scrollView == null) return;

				await WaitForLayoutPassAsync();
				if (_isDisposed) return;

				double maxScrollY = Math.Max(0, scrollView.ContentSize.Height - scrollView.Height);
				await ScrollToYAndSettleAsync(scrollView, maxScrollY, animated);
			}
			catch (ObjectDisposedException)
			{
			}
			catch (InvalidOperationException)
			{
			}
			catch (Exception ex)
			{
				NexusLog.Exception(ex, $"[SETUP:UI] Setup scroll-to-bottom failed: {reason}");
			}
		});
	}

	private bool IsOptionalTailDiagnosticNode(DiagnosticNodeViewModel nodeVm)
	{
		return _currentContext switch
		{
			ViewContext.Vanguard or ViewContext.Repairing => VanguardOptionalNodes.Count > 0
				&& ReferenceEquals(VanguardOptionalNodes[^1], nodeVm),
			ViewContext.Architect => ArchitectOptionalNodes.Count > 0
				&& ReferenceEquals(ArchitectOptionalNodes[^1], nodeVm),
			_ => false
		};
	}

	private static async Task ScrollTargetByPivotAsync(
		ScrollView scrollView,
		View target,
		SetupScrollPivot pivot,
		bool animated)
	{
		double? targetY = GetElementYRelativeToScrollContent(target, scrollView);
		if (targetY == null || scrollView.Height <= 0)
		{
			await scrollView.ScrollToAsync(
				target,
				pivot == SetupScrollPivot.Top ? ScrollToPosition.Start : ScrollToPosition.MakeVisible,
				animated);
			return;
		}

		double maxScrollY = Math.Max(0, scrollView.ContentSize.Height - scrollView.Height);
		double desiredY = pivot == SetupScrollPivot.Top
			? targetY.Value
			: targetY.Value + target.Height - scrollView.Height;
		double settledY = Math.Min(maxScrollY, Math.Max(0, desiredY));
		await ScrollToYAndSettleAsync(scrollView, settledY, animated);
	}

	private static double? GetElementYRelativeToScrollContent(View element, ScrollView scrollView)
	{
		if (scrollView.Content is not View content)
		{
			return null;
		}

		double y = 0;
		Element? current = element;
		while (current != null)
		{
			if (ReferenceEquals(current, content))
			{
				return y;
			}

			if (current is VisualElement visual)
			{
				y += visual.Y;
			}

			current = current.Parent;
		}

		return null;
	}

	private static void ResetInitiationScrollPosition(ScrollView scrollView)
	{
		if (!scrollView.IsVisible)
		{
			return;
		}

		try
		{
			_ = scrollView.ScrollToAsync(0, 0, false);
		}
		catch
		{
		}
	}

	private static async Task WaitForLayoutPassAsync()
	{
		await Task.Yield();
		await Task.Yield();
	}

	private static async Task ScrollToYAndSettleAsync(ScrollView scrollView, double targetY, bool animated)
	{
		const double ScrollPositionTolerance = 1;

		double maxScrollY = Math.Max(0, scrollView.ContentSize.Height - scrollView.Height);
		double settledY = Math.Min(maxScrollY, Math.Max(0, targetY));

		if (Math.Abs(scrollView.ScrollY - settledY) <= ScrollPositionTolerance)
		{
			return;
		}

		await scrollView.ScrollToAsync(0, settledY, animated);

		if (Math.Abs(scrollView.ScrollY - settledY) > ScrollPositionTolerance)
		{
			await scrollView.ScrollToAsync(0, settledY, false);
		}
	}

	private View? FindDiagnosticNodeContainer(DiagnosticNodeViewModel nodeVm)
	{
		Layout? list = _currentContext switch
		{
			ViewContext.Vanguard or ViewContext.Repairing when VanguardNodes.Contains(nodeVm) => DiagnosticNodesList,
			ViewContext.Vanguard or ViewContext.Repairing when VanguardOptionalNodes.Contains(nodeVm) => VanguardOptionalNodesList,
			ViewContext.Architect when ArchitectNodes.Contains(nodeVm) => ArchitectNodesList,
			ViewContext.Architect when ArchitectOptionalNodes.Contains(nodeVm) => ArchitectOptionalNodesList,
			_ => null
		};

		if (list == null)
		{
			return null;
		}

		return FindVisualChildren<View>(list).FirstOrDefault(view => ReferenceEquals(view.BindingContext, nodeVm));
	}

	private SetupDiagnosticStep? FindDiagnosticStep(DiagnosticNodeViewModel nodeVm)
	{
		return _vanguardRequiredSteps
			.Concat(_vanguardOptionalSteps)
			.Concat(_architectRequiredSteps)
			.Concat(_architectOptionalSteps)
			.FirstOrDefault(step => ReferenceEquals(step.ViewModel, nodeVm));
	}

	private static void MarkStepFromHealth(SetupDiagnosticStep? step, DiagnosticNodeViewModel vm)
	{
		if (step == null) return;

		step.State = vm.CurrentHealth switch
		{
			HealthState.Healthy => SetupDiagnosticStepState.Verified,
			HealthState.OptionalMissing => SetupDiagnosticStepState.Skipped,
			_ => SetupDiagnosticStepState.Failed
		};
	}

	private static bool CanReconfigureReadyNode(DiagnosticNodeViewModel nodeVm)
		=> nodeVm.CurrentHealth is HealthState.Healthy or HealthState.OptionalMissing
			&& nodeVm.Node is IConfigurableDiagnosticNode;

	private static bool ShouldKeepInlineActionsVisible(DiagnosticNodeViewModel nodeVm)
		=> nodeVm.Node.NodeId == "model-library"
			&& nodeVm.CurrentHealth is (HealthState.Healthy or HealthState.OptionalMissing);

	private static bool ShouldStartOptionalNodeCollapsed(DiagnosticNodeViewModel nodeVm)
		=> nodeVm.Node is PythonEnvironmentDiagnosticNode;

	private static string GetRestingActionText(DiagnosticNodeViewModel nodeVm)
	{
		if (nodeVm.CurrentHealth == HealthState.OptionalMissing)
		{
			return nodeVm.Node is IOptionalConfigurableDiagnosticNode
				? LocalizationManager.Text("setup.status.setup")
				: LocalizationManager.Text("setup.status.optional");
		}

		return LocalizationManager.Text("setup.status.ready");
	}

	private static string GetInteractiveActionText(DiagnosticNodeViewModel nodeVm)
		=> nodeVm.Node is IOptionalConfigurableDiagnosticNode
			? GetRestingActionText(nodeVm)
			: nodeVm.HasActions
				? LocalizationManager.Text("setup.status.config")
				: LocalizationManager.Text("setup.status.edit");

	private static void SetDiagnosticNodeInteraction(DiagnosticNodeViewModel nodeVm, bool isActive)
	{
		nodeVm.HighlightBackground = Colors.Transparent;
		nodeVm.InteractionOverlayOpacity = isActive ? 1.0 : 0.0;
	}

	private void SetDiagnosticActionNavigationBlocked(bool isBlocked)
	{
		bool isInteractionBlocked = isBlocked || _isInitiationSequenceRunning;
		SetInitiationUserScrollBlocked(isInteractionBlocked || IsAnyDiagnosticStepWorking());
		UpdateBackButtonAvailability();

		if (isInteractionBlocked)
		{
			PrimaryActionButton.IsEnabled = false;
			PrimaryActionButton.InputTransparent = true;
			PrimaryActionButton.Opacity = 0.35;
			StopPrimaryActionReadyPulse();
			return;
		}

		EvaluateCurrentInitiationReadiness();
	}

	private void UpdateInitiationUserScrollBlock()
	{
		SetInitiationUserScrollBlocked(IsInitiationInteractionBlocked || IsAnyDiagnosticStepWorking());
	}

	private bool IsInitiationInteractionBlocked
		=> _isDiagnosticActionRunning || _isInitiationSequenceRunning;

	private void SetInitiationSequenceInteractionBlocked(bool isBlocked)
	{
		if (_isInitiationSequenceRunning == isBlocked)
		{
			return;
		}

		_isInitiationSequenceRunning = isBlocked;
		SetDiagnosticActionNavigationBlocked(_isDiagnosticActionRunning);
	}

	private bool IsAnyDiagnosticStepWorking()
	{
		return _vanguardRequiredSteps
			.Concat(_vanguardOptionalSteps)
			.Concat(_architectRequiredSteps)
			.Concat(_architectOptionalSteps)
			.Any(step => step.State == SetupDiagnosticStepState.Working);
	}

	private void SetInitiationUserScrollBlocked(bool isBlocked)
	{
		if (_isInitiationUserScrollBlocked == isBlocked)
		{
			return;
		}

		_isInitiationUserScrollBlocked = isBlocked;
#if WINDOWS
		if (isBlocked)
		{
			_isNativeInitiationScrollDragging = false;
			_nativeDraggedInitiationScrollViewer = null;
			_ = ReturnNativeInitiationOverscrollAsync(_nativeDraggedInitiationScrollContent);
			_nativeDraggedInitiationScrollContent = null;
		}

		SetNativeInitiationScrollMode(VanguardInitiationScrollView, isBlocked);
		SetNativeInitiationScrollMode(ArchitectInitiationScrollView, isBlocked);
#endif
	}

#if WINDOWS
	private static void SetNativeInitiationScrollMode(ScrollView scrollView, bool isBlocked)
	{
		if (scrollView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.ScrollViewer nativeScrollViewer)
		{
			return;
		}

		nativeScrollViewer.VerticalScrollMode = isBlocked
			? Microsoft.UI.Xaml.Controls.ScrollMode.Disabled
			: Microsoft.UI.Xaml.Controls.ScrollMode.Enabled;
	}
#endif

	private static (Color Normal, Color Hover, Color Text) GetDiagnosticActionColors(string actionId, bool isRecommended)
	{
		if (actionId.Contains("delete", StringComparison.OrdinalIgnoreCase)
			|| actionId.Contains("remove", StringComparison.OrdinalIgnoreCase))
		{
			return (DiagnosticActionDeleteNormalColor, DiagnosticActionDeleteHoverColor, DiagnosticActionDeleteTextColor);
		}

		return isRecommended
			? (ConsoleBootNormalColor, ConsoleBootHoverColor, ConsoleAccentColor)
			: (DiagnosticActionDefaultNormalColor, DiagnosticActionDefaultHoverColor, DiagnosticActionDefaultTextColor);
	}

	private void OnPathHovered(object? sender, PointerEventArgs e)
	{
		if (sender is Label label)
		{
			label.TextColor = DiagnosticActionDefaultTextColor;
		}
	}

	private void OnPathUnhovered(object? sender, PointerEventArgs e)
	{
		if (sender is Label label)
		{
			label.TextColor = ConsoleAccentColor;
		}
	}

	private async Task ExecuteNodeActionAsync(DiagnosticNodeViewModel vm, IConfigurableDiagnosticNode confNode, string actionId)
	{
		if (_isDiagnosticActionRunning) return;

		SetupDiagnosticStep? step = FindDiagnosticStep(vm);
		bool shouldNotifyItemUpdated = false;
		_isDiagnosticActionRunning = true;
		SetDiagnosticActionNavigationBlocked(true);
		try
		{
			bool canCancelAction = confNode.NodeId == DiagnosticNodeBaseResources
				&& string.Equals(actionId, DiagnosticActionBaseModelDownload, StringComparison.Ordinal);
			if (canCancelAction && !await ConfirmLargeModelDownloadAsync())
			{
				return;
			}

			vm.WorkingHint = confNode.AvailableOptions.FirstOrDefault(option => option.Id == actionId)?.WorkingHint ?? string.Empty;
			vm.IsLoading = true;
			vm.CanCancel = canCancelAction;
			if (step != null) step.State = SetupDiagnosticStepState.Working;
			SetDiagnosticNodeInteraction(vm, false);
			vm.Actions.Clear();
			vm.NotifyActionsChanged();

			await YieldDiagnosticWorkingStateAsync();

			if (confNode is IFolderSelectionDiagnosticNode folderNode
				&& folderNode.RequiresFolderSelection(actionId))
			{
				var folderResult = await PlatformManager.Current.FilePicker.PickFolderAsync(
					GetDiagnosticFolderPickerTitle(confNode));
				if (!folderResult.IsSupported || !folderResult.IsSuccess || string.IsNullOrWhiteSpace(folderResult.Value))
				{
					vm.IsLoading = false;
					PopulateInlineActions(vm, confNode);
					SetDiagnosticNodeInteraction(vm, true);
					if (step != null) step.State = SetupDiagnosticStepState.WaitingForUser;
					return;
				}

				RecoveryResult selectionResult = folderNode.ApplySelectedFolder(actionId, folderResult.Value);
				if (!selectionResult.IsSuccess)
				{
					vm.UpdateState(HealthState.OptionalMissing);
					vm.EnvironmentDetails = selectionResult.Message;
					PopulateInlineActions(vm, confNode);
					SetDiagnosticNodeInteraction(vm, true);
					if (step != null) step.State = SetupDiagnosticStepState.WaitingForUser;
					return;
				}

				shouldNotifyItemUpdated = true;
			}
			else
			{
				confNode.SelectOption(actionId);
				shouldNotifyItemUpdated = actionId != DiagnosticActionCustom;
			}

			vm.EnvironmentDetails = confNode.EnvironmentDetails;
			vm.EnvironmentPath = confNode.EnvironmentPath;

			if (actionId == DiagnosticActionCustom)
			{
#if WINDOWS
				var picker = new Windows.Storage.Pickers.FileOpenPicker();
				picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;

				if (confNode.NodeId == DiagnosticNodeGitCore)
				{
					picker.FileTypeFilter.Add(".exe");
					picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
				}
				else if (confNode.NodeId == DiagnosticNodePythonEngine)
				{
					picker.FileTypeFilter.Add(".exe");
					picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
				}

				var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler.PlatformView as MauiWinUIWindow;
				if (window != null)
				{
					var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
					WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

					var file = await picker.PickSingleFileAsync();
					if (file != null)
					{
						if (confNode.NodeId == DiagnosticNodeGitCore) SetupSettingsService.Instance.Settings.GitPath = file.Path;
						else if (confNode.NodeId == DiagnosticNodePythonEngine) SetupSettingsService.Instance.Settings.PythonPath = file.Path;
						shouldNotifyItemUpdated = true;
					}
					else
					{
						vm.IsLoading = false;
						PopulateInlineActions(vm, confNode);
						SetDiagnosticNodeInteraction(vm, true);
						if (step != null) step.State = SetupDiagnosticStepState.WaitingForUser;
						return;
					}
				}
#endif
			}

			if (confNode is not IFolderSelectionDiagnosticNode
				&& actionId != DiagnosticActionSystem
				&& actionId != DiagnosticActionKeep)
			{
				if (step != null)
				{
					await RequestDiagnosticScrollAsync(step, SetupScrollReason.WorkStarted);
				}

				vm.ShowProgress = true;
				vm.ProgressValue = 0;

				var originalOnProgress = ComfyInstallService.Instance.OnProgress;
				ComfyInstallService.Instance.OnProgress = (p, msg) =>
				{
					UiThread.TryBeginInvoke(() =>
					{
						vm.UpdateProgressDisplay(p, msg);
					}, "PRODUCT_SETUP:RECOVERY_PROGRESS");
				};

				var progress = new Progress<double>(p => vm.ProgressValue = p);
				try
				{
					RecoveryResult result = await confNode.RecoverAsync(progress, _repairCts?.Token ?? CancellationToken.None);
					if (!result.IsSuccess)
					{
						vm.ShowProgress = false;
						vm.UpdateState(HealthState.CriticalError);
						vm.EnvironmentDetails = result.Message;
						if (step != null) step.State = SetupDiagnosticStepState.Failed;
						ComfyInstallService.Instance.OnProgress = originalOnProgress;
						EvaluateCurrentInitiationReadiness();
						return;
					}

					shouldNotifyItemUpdated = true;
					if (IsBaseModelBrowserDownloadAction(confNode, actionId))
					{
						vm.EnvironmentDetails = confNode.EnvironmentDetails;
						vm.EnvironmentPath = confNode.EnvironmentPath;
						vm.UpdateState(HealthState.Healthy);
						MarkStepFromHealth(step, vm);
						vm.ActionText = LocalizationManager.Text("setup.status.ready");
						vm.Actions.Clear();
						vm.NotifyActionsChanged();
						SetDiagnosticNodeInteraction(vm, false);
						EvaluateCurrentInitiationReadiness();
						vm.SignalCompletionChanged();
						return;
					}
				}
				catch (OperationCanceledException)
				{
					vm.ShowProgress = false;
					vm.CanCancel = false;
					vm.UpdateState(HealthState.NeedsRecovery);
					vm.EnvironmentDetails = confNode.EnvironmentDetails;
					ComfyInstallService.Instance.OnProgress = originalOnProgress;
					PopulateInlineActions(vm, confNode);
					SetDiagnosticNodeInteraction(vm, true);
					if (step != null) step.State = SetupDiagnosticStepState.WaitingForUser;
					EvaluateCurrentInitiationReadiness();
					return;
				}
				finally
				{
					ComfyInstallService.Instance.OnProgress = originalOnProgress;
					vm.CanCancel = false;
					vm.ShowProgress = false;
				}
			}

			await CheckSingleNodeHealthAsync(vm);

			// Final sync after recovery
			vm.EnvironmentDetails = confNode.EnvironmentDetails;
			vm.EnvironmentPath = confNode.EnvironmentPath;
			MarkStepFromHealth(step, vm);
			vm.SignalCompletionChanged();

			DiagnosticNodesList.InputTransparent = false;
			VanguardOptionalNodesList.InputTransparent = false;
			EvaluateCurrentInitiationReadiness();
		}
		finally
		{
			vm.CanCancel = false;
			vm.WorkingHint = string.Empty;
			_isDiagnosticActionRunning = false;
			SetDiagnosticActionNavigationBlocked(false);
			if (shouldNotifyItemUpdated)
			{
				await NotifyDiagnosticItemUpdatedAsync(vm);
			}
		}
	}

	private static async Task YieldDiagnosticWorkingStateAsync()
	{
		await Task.Yield();
	}

	private async Task<bool> ConfirmLargeModelDownloadAsync()
	{
		var page = GetPromptPage();
		return page == null || await page.DisplayAlertAsync(
			LocalizationManager.Text("setup.base_model.download_confirm_title"),
			LocalizationManager.Text("setup.base_model.download_confirm_message"),
			LocalizationManager.Text("setup.base_model.download_confirm_accept"),
			LocalizationManager.Text("common.cancel"));
	}

	private static bool IsBaseModelBrowserDownloadAction(IConfigurableDiagnosticNode node, string actionId)
		=> node.NodeId == DiagnosticNodeBaseResources
			&& string.Equals(actionId, DiagnosticActionBaseModelBrowser, StringComparison.Ordinal);

	private static string GetDiagnosticFolderPickerTitle(IConfigurableDiagnosticNode node)
		=> node.NodeId == "pip-cache"
			? LocalizationManager.Text("settings.pip_cache.select_folder")
			: LocalizationManager.Text("setup.model_library.folder_picker_title");

	private async Task CheckSingleNodeHealthAsync(DiagnosticNodeViewModel vm)
	{
		vm.IsLoading = true;
		var state = await vm.Node.CheckHealthAsync(CancellationToken.None);
		vm.UpdateState(state);
		PopulatePersistentInlineActionsIfNeeded(vm);
		EvaluateCurrentInitiationReadiness();
	}

	private void EvaluateOverallReadiness()
	{
		if (_vanguardRequiredSteps.Count == 0) return;

		UpdateVanguardRequiredStatus();
		bool allReady = _vanguardRequiredSteps.All(step => step.CountsAsReady);

		if (_currentContext == ViewContext.Vanguard || _currentContext == ViewContext.Repairing)
		{
			EnsureActionBottomBarReady();
			bool primaryEnabled = allReady && !IsInitiationInteractionBlocked;
			PrimaryActionButton.IsEnabled = primaryEnabled;
			PrimaryActionButton.InputTransparent = !primaryEnabled;
			PrimaryActionButton.Opacity = primaryEnabled ? 1.0 : 0.3;
			PrimaryActionLabel.Text = allReady
				? LocalizationManager.Text("common.next")
				: LocalizationManager.Text("setup.status.requirements_pending");

			if (primaryEnabled) StartPrimaryActionReadyPulse();
			else StopPrimaryActionReadyPulse();
		}
	}

	private void ShowActionBottomBar(string primaryText, bool primaryEnabled)
	{
		EnsureActionBottomBarReady(restoreOpacity: false);
		ActionBottomBar.Opacity = 0;
		PrimaryActionLabel.Text = primaryText;
		PrimaryActionButton.IsEnabled = primaryEnabled;
		PrimaryActionButton.InputTransparent = !primaryEnabled;
		PrimaryActionButton.Opacity = primaryEnabled ? 1 : 0.4;

		if (primaryEnabled) StartPrimaryActionReadyPulse();
		else StopPrimaryActionReadyPulse();

		// Reset Back button text to default
		var label = BackButton.Content as Label;
		if (label == null && BackButton.Content is Grid g)
		{
			label = g.Children.OfType<Label>().FirstOrDefault();
		}
		if (label != null) label.Text = "BACK";
	}

	private void EnsureActionBottomBarReady(bool restoreOpacity = true)
	{
		ActionBottomBar.IsVisible = true;
		ActionBottomBar.InputTransparent = false;
		if (restoreOpacity && ActionBottomBar.Opacity <= 0)
		{
			ActionBottomBar.Opacity = 1;
		}

		BackButton.IsVisible = true;
		UpdateBackButtonAvailability();
	}

	private void UpdateBackButtonAvailability()
	{
		bool isBlocked = IsInitiationInteractionBlocked || _consoleBootActionState == ConsoleBootActionState.Booting;
		BackButton.IsEnabled = !isBlocked;
		BackButton.InputTransparent = isBlocked;
		BackButton.Opacity = isBlocked ? 0.35 : 1.0;
	}

	private void StartPrimaryActionReadyPulse()
	{
		if (_isDisposed)
		{
			return;
		}

		PrimaryActionPulseSurface.Opacity = 1;
		_primaryActionReadyPulseClip.PlayLoop(CanRepeatPrimaryActionPulse);
	}

	private void StopPrimaryActionReadyPulse()
	{
		_primaryActionReadyPulseClip.Stop();
		PrimaryActionPulseSurface.Opacity = 0;
	}

	private bool CanRepeatPrimaryActionPulse()
		=> !_isDisposed
			&& IsVisible
			&& Handler is not null
			&& ActionBottomBar.IsVisible
			&& PrimaryActionButton.IsEnabled
			&& !IsInitiationInteractionBlocked;

	private static bool IsValidComfyPath(string? path)
		=> !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && File.Exists(System.IO.Path.Combine(path, "main.py"));

	// ------------------------------------------------------------
	// POINTER & STATE ENGINE
	// ------------------------------------------------------------
	private void OnGlobalPointerMoved(object? sender, PointerEventArgs e)
	{
		// Use Window coordinates (null) for absolute precision across the entire viewport
		_lastGlobalMousePos = e.GetPosition(null) ?? new Point(0, 0);
	}

	private async void OnVanguardCardPointerEntered(object? sender, PointerEventArgs e)
	{
		if (_currentState != ViewState.Ready || _currentContext != ViewContext.Crossroads) return;
		_background.SetMode(SetupBackgroundMode.VanguardHover);
		await _background.PrepareSelectionBurstAsync(SetupBackgroundMode.VanguardHover);
	}

	private void OnVanguardCardPointerExited(object? sender, PointerEventArgs e)
	{
		if (_currentContext != ViewContext.Crossroads || _isTransitioning) return;
		_background.SetMode(SetupBackgroundMode.Crossroads);
	}

	private async void OnArchitectCardPointerEntered(object? sender, PointerEventArgs e)
	{
		if (_currentState != ViewState.Ready || _currentContext != ViewContext.Crossroads) return;
		_background.SetMode(SetupBackgroundMode.ArchitectHover);
		await _background.PrepareSelectionBurstAsync(SetupBackgroundMode.ArchitectHover);
	}

	private void OnArchitectCardPointerExited(object? sender, PointerEventArgs e)
	{
		if (_currentContext != ViewContext.Crossroads || _isTransitioning) return;
		_background.SetMode(SetupBackgroundMode.Crossroads);
	}

	private void OnBackButtonHovered(object? sender, PointerEventArgs e)
	{
		if (_currentState != ViewState.Ready) return;
		VisualStateManager.GoToState(BackButton, "BackPointerOver");
	}

	private void OnBackButtonUnhovered(object? sender, PointerEventArgs e)
		=> VisualStateManager.GoToState(BackButton, "BackNormal");

	private void OnPrimaryButtonHovered(object? sender, PointerEventArgs e)
	{
		if (_currentState != ViewState.Ready) return;
		VisualStateManager.GoToState(PrimaryActionButton, "PrimaryPointerOver");
	}

	private void OnPrimaryButtonUnhovered(object? sender, PointerEventArgs e)
		=> VisualStateManager.GoToState(PrimaryActionButton, "PrimaryNormal");

	private void OnConsoleBootHovered(object? sender, PointerEventArgs e)
	{
		if (_consoleBootActionState != ConsoleBootActionState.Standby) return;

		ConsoleBootButton.Stroke = NexusColors.White;
	}

	private void OnConsoleBootUnhovered(object? sender, PointerEventArgs e)
	{
		if (_consoleBootActionState != ConsoleBootActionState.Standby) return;

		ConsoleBootButton.Stroke = ConsoleAccentColor;
	}

	private void OnConsoleRetryHovered(object? sender, PointerEventArgs e)
	{
		if (_consoleBootActionState != ConsoleBootActionState.Failed) return;

		ConsoleRetryButton.BackgroundColor = ConsoleRetryHoverColor;
	}

	private void OnConsoleRetryUnhovered(object? sender, PointerEventArgs e)
	{
		if (_consoleBootActionState != ConsoleBootActionState.Failed) return;

		ConsoleRetryButton.BackgroundColor = ConsoleRetryNormalColor;
	}

	// ------------------------------------------------------------
	// SMART SCALING
	// ------------------------------------------------------------
	private void OnViewSizeChanged(object? sender, EventArgs e)
	{
		ApplySetupScale();
	}

	private void ApplySetupScale()
	{
		if (Width <= 0 || Height <= 0) return;

		const double baseWidth = 1024;
		const double baseHeight = 1000;
		// Relative Safe Area: Always maintain 5% padding at top and bottom (total 10% vertical buffer)
		double verticalBuffer = Height * 0.1;
		double horizontalBuffer = Width * 0.08; // Roughly 4% on each side for balance

		double availableWidth = Width - horizontalBuffer;
		double availableHeight = Height - verticalBuffer;

		double scaleX = availableWidth / baseWidth;
		double scaleY = availableHeight / baseHeight;

		// Choose the smaller scale to fit the safe area
		double scale = Math.Min(scaleX, scaleY);

		// Clamp the scale: Allow upscaling to 1.25 for a "Full" cinematic look on large screens
		if (scale > 1.25) scale = 1.25;
		if (scale < 0.4) scale = 0.4;

		ScaleContainer.Scale = scale;
		GlobalGlowLayer.Scale = scale;

		// Dynamic Anchoring Logic (Full Balanced Mode)
		if (Height < 750)
		{
			MainUIPrefab.TranslationY = -15;
			GlowSyncGroup.TranslationY = -15;
		}
		else
		{
			MainUIPrefab.TranslationY = 0;
			GlowSyncGroup.TranslationY = 0;
		}

	}

	private void OnBlockerTapped(object? sender, EventArgs e)
	{
		// Silently consume input while transitioning
	}

	private bool IsMouseOverElement(VisualElement element, Point mousePos)
	{
		if (element == null) return false;

		// In MAUI, we calculate absolute screen-space bounds manually by traversing the visual tree
		double x = 0;
		double y = 0;
		Element? current = element;

		while (current is VisualElement visual)
		{
			x += visual.X;
			y += visual.Y;

			// Account for Translation
			x += visual.TranslationX;
			y += visual.TranslationY;

			current = visual.Parent;
		}

		// Check if mouse point is within the calculated rectangle
		return x <= mousePos.X && mousePos.X <= x + element.Width &&
			   y <= mousePos.Y && mousePos.Y <= y + element.Height;
	}
}
