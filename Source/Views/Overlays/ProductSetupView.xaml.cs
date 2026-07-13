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
	private const int InitialLayoutDelayMs = 160;
	private const int LayoutReadyPollDelayMs = 50;
	private const int LayoutReadyMaxWaitMs = 1000;
	private const int DashboardRevealDelayMs = 120;
	private const int StabilizationDelayMs = 150;
	private const double HeaderInitialOffsetY = 30;
	private const double WelcomeInitialOffsetY = 50;
	private const double PanelRevealOffsetY = 20;
	private const double ActionBarHideOffsetY = 50;
	private const double BackgroundRevealOpacity = 0.5;
	private const double BackgroundDimmedOpacity = 0.15;
	private const double GlassInitialScale = 0.95;
	private const int CrossroadsReturnDelayMs = 50;
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
	private const double AmbientCardGlowOpacity = 0.15;
	private const double HoveredCardGlowOpacity = 0.8;
	private const double PersistentCardGlowOpacity = 1.0;
	private const double ConsolePulseHighOpacity = 0.2;
	private const double ConsoleBootingGlowHighOpacity = 0.32;
	private const double ConsoleBootingGlowLowOpacity = 0.03;
	private const double ConsoleBootingButtonHighScale = 1.015;
	private const double ConsoleConfigDisabledOpacity = 0.45;
	private const double ConsoleBootActionsExpandedRowSpacing = 8;
	private const double ConsoleButtonPreparingOpacity = 0.4;
	private const double ConsoleButtonBootingOpacity = 0.5;
	private const double PrimaryPulseHighOpacity = 0.8;
	private const double PrimaryPulseLowOpacity = 0.1;
	private const double ImpactCardScale = 1.04;
	private const double GpuCardCornerRadius = 8;
	private const double GpuCardTitleFontSize = 10;
	private const double GpuCardNameFontSize = 9;
	private const double GpuCardMemoryFontSize = 8;
	private const double InitiationOverscrollResistance = 0.28;
	private const double InitiationOverscrollMaxOffset = 42;
	private const uint InitiationOverscrollReturnLength = 220;
	private const uint DiagnosticLoadingRingRotateLength = 950;
	private const int ImpactFirstFlashDelayMs = 70;
	private const int ImpactVoidDelayMs = 50;
	private const int ImpactSecondFlashDelayMs = 60;
	private const int DriftFrameIntervalMs = 16;
	private const int ParticleCount = 15;
	private const int ParticleMinSize = 2;
	private const int ParticleMaxSizeExclusive = 6;
	private const int ParticleAccentInterval = 3;
	private const uint PulseAnimationLength = 800;
	private const uint ConsoleBootingPulseLength = 520;
	private const string ConsoleBootPulseAnimationName = "ProductSetup.ConsoleBootPulse";
	private const string ConsoleBootingAnimationName = "ProductSetup.ConsoleBooting";
	private const string PrimaryActionPulseAnimationName = "ProductSetup.PrimaryActionPulse";
	private const string DiagnosticLoadingRingPulseAnimationName = "ProductSetup.DiagnosticLoadingRingPulse";
	private const string DriftMotionName = "ProductSetup.Drift";
	private const uint CardGlowAnimationLength = 400;
	private const uint PersistentCardGlowAnimationLength = 500;
	private const uint OtherCardGlowHideLength = 300;
	private const uint ImpactCardScaleOutLength = 100;
	private const uint ImpactCardScaleInLength = 300;
	private const double DriftTimeStep = 0.02;
	private const double CrossroadsCommonDriftAmplitude = 8;
	private const double CrossroadsBgDriftAmplitude = 5;
	private const double ParticleDriftAmplitude = 20;
	private const double ParticleOpacityBase = 0.1;
	private const double ParticleOpacityRange = 0.3;
	private const double ParticleOpacityPulseBase = 0.6;
	private const double ParticleOpacityPulseRange = 0.4;
	private const double ParticleMinSpeed = 0.01;
	private const double ParticleSpeedRange = 0.02;
	private const double ParticleMaxTimeOffset = 10;
	private const double ParticleSpreadWidth = 1920;
	private const double ParticleSpreadLeftOffset = 460;
	private const double ParticleSpreadHeight = 1080;
	private const double ParticleSpreadTopOffset = 150;
	private static readonly Color GpuCardTitleColor = Color.FromArgb("#e8fbff");
	private static readonly Color GpuCardNameColor = NexusColors.TextDim;
	private static readonly Color GpuCardMemoryColor = NexusColors.AccentGlow;
	private static readonly Color GpuCardSelectedBackgroundColor = NexusColors.AccentSoft;
	private static readonly Color GpuCardNormalBackgroundColor = Color.FromArgb("#08000000");
	private static readonly Color GpuCardSelectedStrokeColor = NexusColors.AccentStrokeStrong;
	private static readonly Color GpuCardNormalStrokeColor = NexusColors.AccentStroke;
	private static readonly Color ConsoleAccentColor = NexusColors.Accent;
	private static readonly Color ConsoleWarningColor = NexusColors.Warning;
	private static readonly Color ConsoleBootNormalColor = NexusColors.AccentSoft;
	private static readonly Color ConsoleBootHoverColor = NexusColors.AccentHoverSoft;
	private static readonly Color ConsoleBootingButtonColor = Color.FromArgb("#24ffaa00");
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
	private readonly NexusLatestOperationCoordinator _latestOperations = new("product-setup");
	private CancellationTokenSource? _repairCts;
	private readonly SemaphoreSlim _vanguardOptionalRefreshGate = new(1, 1);
	private readonly SemaphoreSlim _architectOptionalRefreshGate = new(1, 1);
	private bool _isDisposed;
	private bool _isGpuSelectorExpanded;
	private bool _isUpdatingServerPythonMode;
	private bool _isDiagnosticActionRunning;
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
	private enum CardState { Ambient, Hovered, Initiating, Persistent, Hidden }
	private enum ConsoleBootActionState { Preparing, Standby, Booting, Failed, Online }

	private ViewContext _currentContext = ViewContext.Intro;
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

	private double _driftTime = 0;

	public ProductSetupView()
	{
		InitializeComponent();
		_motion = new NexusMotionController("product-setup", "SETUP:UI", Dispatcher);
		_initiationSequence = new InitiationSequenceRunner(
			PopulateInlineActions,
			EnableDiagnosticNodeInteraction,
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

	internal void ActivateLifecycle()
	{
		_isDisposed = false;
		StartDriftEngine();
		AttachNativeInitiationScrollDragHandlers();
		if (MainThread.IsMainThread)
		{
			InitializeLifecycle();
		}
		else
		{
			MainThread.BeginInvokeOnMainThread(InitializeLifecycle);
		}
	}

	internal void PrepareForLifecycleHandoff()
	{
		if (_introPlayed) return;

		_currentContext = ViewContext.Intro;
		_currentState = ViewState.StartAction;
		ScaleContainer.InputTransparent = true;

		BackgroundLayer.Opacity = 0;
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
		DetachNativeInitiationScrollDragHandlers();
		CancelTransientAnimationLoops();
		StopDriftEngine();
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

	private void CancelTransientAnimationLoops()
	{
		_repairCts?.Cancel();
		_latestOperations.StopAll();
		StopConsoleBootPulse();
		StopConsoleBootingAnimation();
		StopPrimaryButtonPulse();
		StopDiagnosticLoadingRingAnimations();
	}

	private async void InitializeLifecycle()
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

			HeaderBar.IsVisible = true;
			HeaderBar.Opacity = 0;
			HeaderBar.TranslationY = HeaderInitialOffsetY;

			WelcomeContainer.IsVisible = true;
			WelcomeContainer.Opacity = 0;
			WelcomeContainer.TranslationY = WelcomeInitialOffsetY;

			GlassPanel.Opacity = 0;
			GlassPanel.Scale = GlassInitialScale;

			// Give MAUI a moment to prepare layout
			await Task.Delay(InitialLayoutDelayMs);

			// Reveal the setup dashboard after the shared startup splash hands off to this view.
			await Task.Delay(DashboardRevealDelayMs);

			await Task.WhenAll(
				BackgroundLayer.FadeToAsync(BackgroundRevealOpacity, BackgroundRevealLength, Easing.CubicOut),

				GlassPanel.FadeToAsync(1, GlassRevealLength, Easing.CubicOut),
				SafeAnimation.ScaleToAsync(GlassPanel, 1.0, GlassRevealLength, Easing.SpringOut, "Setup.Reveal"),
				HeaderBar.FadeToAsync(1, HeaderRevealLength, Easing.CubicOut),
				SafeAnimation.TranslateToAsync(HeaderBar, 0, 0, HeaderRevealLength, Easing.CubicOut, "Setup.Reveal"),
				WelcomeContainer.FadeToAsync(1, WelcomeRevealLength, Easing.CubicOut),
				SafeAnimation.TranslateToAsync(WelcomeContainer, 0, 0, WelcomeRevealLength, Easing.CubicOut, "Setup.Reveal")
			);

			// 4. Final Stabilization Buffer
			// Ensure everything is visually settled before unlocking
			await Task.Delay(StabilizationDelayMs);
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

				ApplyCardState(VanguardCard, VanguardGlow, CardState.Ambient);
				ApplyCardState(ArchitectCard, ArchitectGlow, CardState.Ambient);
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

	private void StopDriftEngine()
	{
		_motion.Stop(DriftMotionName);
	}

	private void StartDriftEngine()
	{
		StopDriftEngine(); // Ensure previous timer is stopped
		InitParticles();

		_motion.StartFrameLoop(
			DriftMotionName,
			TimeSpan.FromMilliseconds(DriftFrameIntervalMs),
			CanRunDriftEngine,
			() =>
			{
			_driftTime += DriftTimeStep;

			// 1. Prefab Layered Animation (Drift & Rotate)
			if (_currentContext == ViewContext.Crossroads)
			{
				double commonDrift = Math.Sin(_driftTime) * CrossroadsCommonDriftAmplitude;

				// Vanguard Prefab (Moving the Container moves Card + Proxy together for stability)
				VanguardContainer.TranslationY = commonDrift;
				VanguardGlow.TranslationY = commonDrift; // Sync Glow with Card
				VanguardBgImage.TranslationY = Math.Sin(_driftTime * 1.2) * CrossroadsBgDriftAmplitude;
				VanguardRing1.Rotation = _driftTime * 20;
				VanguardRing2.Rotation = -_driftTime * 15;

				// Architect Prefab (Moving the Container)
				double architectDrift = Math.Cos(_driftTime * 0.8) * CrossroadsCommonDriftAmplitude;
				ArchitectContainer.TranslationY = architectDrift;
				ArchitectGlow.TranslationY = architectDrift; // Sync Glow
				ArchitectBgImage.TranslationY = Math.Cos(_driftTime * 1.1) * CrossroadsBgDriftAmplitude;
				ArchitectRing1.Rotation = _driftTime * 12;
				ArchitectRing2.Rotation = -_driftTime * -25;
			}

			// 2. Neon Breathing (Glow) - Disabled to prevent conflict with hover logic
			// double breathe = (Math.Sin(_driftTime * 0.5) + 1) / 2; // 0 to 1
			// VanguardGlow.Opacity = 0.05 + (breathe * 0.15);
			// ArchitectGlow.Opacity = 0.05 + (breathe * 0.15);

			// 3. Particle Animation
			foreach (var p in _particles)
			{
				p.TimeOffset += p.Speed;
				p.Element.TranslationX = p.BaseX + Math.Sin(p.TimeOffset) * ParticleDriftAmplitude;
				p.Element.TranslationY = p.BaseY + Math.Cos(p.TimeOffset * 0.7) * ParticleDriftAmplitude;
				p.Element.Opacity = p.BaseOpacity * (ParticleOpacityPulseBase + Math.Sin(p.TimeOffset * 1.5) * ParticleOpacityPulseRange);
			}
			},
			ResetDriftVisuals);
	}

	private bool CanRunDriftEngine()
		=> !_isDisposed && IsVisible && Handler is not null;

	private void ResetDriftVisuals()
	{
		VanguardContainer.TranslationY = 0;
		VanguardGlow.TranslationY = 0;
		VanguardBgImage.TranslationY = 0;
		VanguardRing1.Rotation = 0;
		VanguardRing2.Rotation = 0;
		ArchitectContainer.TranslationY = 0;
		ArchitectGlow.TranslationY = 0;
		ArchitectBgImage.TranslationY = 0;
		ArchitectRing1.Rotation = 0;
		ArchitectRing2.Rotation = 0;

		foreach (Particle particle in _particles)
		{
			particle.Element.TranslationX = particle.BaseX;
			particle.Element.TranslationY = particle.BaseY;
			particle.Element.Opacity = particle.BaseOpacity;
		}
	}

	private sealed class Particle { public BoxView Element = null!; public double BaseX; public double BaseY; public double Speed; public double TimeOffset; public double BaseOpacity; }
	private readonly List<Particle> _particles = new();

	private void InitParticles()
	{
		var rand = new Random();
		ParticleContainer.Children.Clear();
		_particles.Clear();

		for (int i = 0; i < ParticleCount; i++)
		{
			var size = rand.Next(ParticleMinSize, ParticleMaxSizeExclusive);
			var pElement = new BoxView
			{
				WidthRequest = size,
				HeightRequest = size,
				CornerRadius = size / 2,
				Color = i % ParticleAccentInterval == 0 ? ConsoleAccentColor : DiagnosticActionDefaultTextColor,
				Opacity = 0,
				HorizontalOptions = LayoutOptions.Start,
				VerticalOptions = LayoutOptions.Start,
				InputTransparent = true
			};

			double baseX = rand.NextDouble() * ParticleSpreadWidth - ParticleSpreadLeftOffset;
			double baseY = rand.NextDouble() * ParticleSpreadHeight - ParticleSpreadTopOffset;

			ParticleContainer.Children.Add(pElement);
			_particles.Add(new Particle
			{
				Element = pElement,
				BaseX = baseX,
				BaseY = baseY,
				Speed = ParticleMinSpeed + rand.NextDouble() * ParticleSpeedRange,
				TimeOffset = rand.NextDouble() * ParticleMaxTimeOffset,
				BaseOpacity = ParticleOpacityBase + rand.NextDouble() * ParticleOpacityRange
			});
		}
	}

	internal void ResetFlow()
	{
		try
		{
			NexusLog.Info("[SETUP:UI] ResetFlow starting.");
			CancelTransientAnimationLoops();
			StopConsoleBootPulse();
			StopConsoleBootingAnimation();
			StopPrimaryButtonPulse();

			_currentContext = ViewContext.Crossroads;
			_currentState = ViewState.Ready;
			_isTransitioning = false;
			InputTransparent = false;
			ScaleContainer.InputTransparent = false;

			BackgroundLayer.Opacity = BackgroundRevealOpacity;
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
			VanguardGlow.CancelAnimations();
			ArchitectGlow.CancelAnimations();
			VanguardContainer.Scale = 1;
			ArchitectContainer.Scale = 1;
			VanguardContainer.TranslationY = 0;
			ArchitectContainer.TranslationY = 0;
			VanguardGlow.TranslationY = 0;
			ArchitectGlow.TranslationY = 0;
			VanguardGlow.Opacity = AmbientCardGlowOpacity;
			ArchitectGlow.Opacity = AmbientCardGlowOpacity;
			ApplyCardState(VanguardCard, VanguardGlow, CardState.Ambient);
			ApplyCardState(ArchitectCard, ArchitectGlow, CardState.Ambient);

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
		_selectedInstallMode = SetupInstallModes.LocalRuntime;
		SetupSettingsService.Instance.UseLocalRuntime();

		// Initiation Phase
		PrepareVanguardChecklist();
		_ = BackgroundLayer.FadeToAsync(BackgroundDimmedOpacity, BackgroundDimLength, Easing.CubicOut);
		await Task.WhenAll(
			TriggerInitiationImpact(VanguardContainer, VanguardGlow, ArchitectGlow),
			TransitionToPanel(VanguardPanel, LocalizationManager.Text("setup.status.pending"))
		);
		ResetInitiationScrollPosition(VanguardInitiationScrollView);
		_ = RefreshVanguardOptionalNodesSafeAsync();

		_ = RunRepairSequenceAsync();
	}

	private async void OnArchitectSelected(object? sender, TappedEventArgs e)
	{
		if (_currentState != ViewState.Ready || _currentContext != ViewContext.Crossroads) return;

		_currentState = ViewState.StartAction;
		_selectedInstallMode = SetupInstallModes.ExistingComfyPath;
		ArchitectNodes.Clear();

		var settings = SetupSettingsService.Instance.Settings;
		_architectCandidateComfyPath = settings.ComfyPath ?? string.Empty;
		ArchitectFolderBrowser.InitializePath(string.IsNullOrWhiteSpace(_architectCandidateComfyPath) ? "C:\\" : _architectCandidateComfyPath);

		_ = BackgroundLayer.FadeToAsync(BackgroundDimmedOpacity, BackgroundDimLength, Easing.CubicOut);
		await Task.WhenAll(
			TriggerInitiationImpact(ArchitectContainer, ArchitectGlow, VanguardGlow),
			TransitionToPanel(ArchitectWorkspacePanel, LocalizationManager.Text("common.next"))
		);

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
				PrimaryActionButton.IsEnabled = !_isDiagnosticActionRunning;
				PrimaryActionButton.InputTransparent = _isDiagnosticActionRunning;
				PrimaryActionButton.Opacity = _isDiagnosticActionRunning ? 0.35 : 1;
				if (_isDiagnosticActionRunning) StopPrimaryButtonPulse();
				else StartPrimaryButtonPulse();
			}
			else
			{
				PrimaryActionLabel.Text = LocalizationManager.Text("setup.status.pending");
				PrimaryActionButton.IsEnabled = false;
				PrimaryActionButton.InputTransparent = true;
				PrimaryActionButton.Opacity = 0.3;
				StopPrimaryButtonPulse();
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
		StopPrimaryButtonPulse();

		await Task.WhenAll(
			ArchitectInitiationPanel.FadeToAsync(1, ArchitectInitiationShowLength, Easing.CubicOut),
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
			ServerConsolePanel.FadeToAsync(1, ConsoleShowLength, Easing.CubicOut),
			SafeAnimation.TranslateToAsync(ServerConsolePanel, 0, 0, ConsoleShowLength, Easing.CubicOut, "Setup.Console"),
			ActionBottomBar.FadeToAsync(1, ConsoleShowLength, Easing.CubicOut),
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

		AddConsoleLog("[SYSTEM] Initiating Secure Boot Protocol...");
		await Task.Delay(SystemBootKernelDelayMs);
		AddConsoleLog("[SYSTEM] Loading Nexus Kernel Modules...");
		await Task.Delay(SystemBootModulesDelayMs);
		AddConsoleLog("[SYSTEM] Validating Environment Integrity...");
		await Task.Delay(SystemBootValidationDelayMs);
		AddConsoleLog("[SYSTEM] Establishing Neural Link with Workspace...");
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
		_ = border.FadeToAsync(1, 120, Easing.CubicOut);
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

	private void StartConsoleBootPulse()
	{
		if (_isDisposed) return;

		ConsoleBootPulseGlow.Opacity = 0;
		_motion.StartTimeline(
			ConsoleBootPulseAnimationName,
			this,
			16,
			PulseAnimationLength * 2,
			Easing.Linear,
			CanRepeatConsoleBootPulse,
			ResetConsoleBootPulse,
			new SafeAnimation.TimelineSegment(0, 0.5, value => ConsoleBootPulseGlow.Opacity = value, 0, ConsolePulseHighOpacity, Easing.CubicInOut),
			new SafeAnimation.TimelineSegment(0.5, 1, value => ConsoleBootPulseGlow.Opacity = value, ConsolePulseHighOpacity, 0, Easing.CubicInOut));
	}

	private void StopConsoleBootPulse()
	{
		_motion.Stop(ConsoleBootPulseAnimationName);
		ResetConsoleBootPulse();
	}

	private void StartConsoleBootingAnimation()
	{
		if (_isDisposed) return;

		ConsoleBootPulseGlow.BackgroundColor = ConsoleWarningColor;
		ConsoleBootPulseGlow.Opacity = ConsoleBootingGlowLowOpacity;
		ConsoleStatusBorder.Opacity = 1;
		ConsoleBootButton.Scale = 1;
		_motion.StartTimeline(
			ConsoleBootingAnimationName,
			this,
			16,
			ConsoleBootingPulseLength * 2,
			Easing.Linear,
			CanRepeatConsoleBootingAnimation,
			ResetConsoleBootingAnimation,
			new SafeAnimation.TimelineSegment(0, 0.5, value => ConsoleBootPulseGlow.Opacity = value, ConsoleBootingGlowLowOpacity, ConsoleBootingGlowHighOpacity, Easing.CubicInOut),
			new SafeAnimation.TimelineSegment(0.5, 1, value => ConsoleBootPulseGlow.Opacity = value, ConsoleBootingGlowHighOpacity, ConsoleBootingGlowLowOpacity, Easing.CubicInOut),
			new SafeAnimation.TimelineSegment(0, 0.5, value => ConsoleStatusBorder.Opacity = value, 1, 0.55, Easing.CubicInOut),
			new SafeAnimation.TimelineSegment(0.5, 1, value => ConsoleStatusBorder.Opacity = value, 0.55, 1, Easing.CubicInOut),
			new SafeAnimation.TimelineSegment(0, 0.5, value => ConsoleBootButton.Scale = value, 1, ConsoleBootingButtonHighScale, Easing.CubicInOut),
			new SafeAnimation.TimelineSegment(0.5, 1, value => ConsoleBootButton.Scale = value, ConsoleBootingButtonHighScale, 1, Easing.CubicInOut));
	}

	private void StopConsoleBootingAnimation()
	{
		_motion.Stop(ConsoleBootingAnimationName);
		ResetConsoleBootingAnimation();
	}

	private void ResetConsoleBootPulse()
	{
		ConsoleBootPulseGlow.Opacity = 0;
	}

	private void ResetConsoleBootingAnimation()
	{
		ConsoleBootButton.Scale = 1.0;
		ConsoleStatusBorder.Opacity = 1.0;
		ConsoleBootPulseGlow.Opacity = 0;
		ConsoleBootPulseGlow.BackgroundColor = ConsoleAccentColor;
	}

	private bool CanRepeatConsoleBootPulse()
		=> !_isDisposed
			&& IsVisible
			&& Handler is not null
			&& ServerConsolePanel.IsVisible
			&& _consoleBootActionState == ConsoleBootActionState.Standby;

	private bool CanRepeatConsoleBootingAnimation()
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

		StopConsoleBootPulse();
		StopConsoleBootingAnimation();
		StopPrimaryButtonPulse();

		ConsoleBootButton.CancelAnimations();
		ConsoleRepairBeforeBootToggle.CancelAnimations();
		ConsoleRetryButton.CancelAnimations();
		ConsoleBootPulseGlow.CancelAnimations();
		ConsoleBootButton.Scale = 1.0;
		ConsoleRetryButton.Scale = 1.0;
		ConsoleBootPulseGlow.Opacity = 0;
		ConsoleBootPulseGlow.BackgroundColor = ConsoleAccentColor;

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
		UpdateConsoleConfigAvailability();

		switch (state)
		{
			case ConsoleBootActionState.Preparing:
				SetServerState(LocalizationManager.Text("setup.console.state_standby"), LocalizationManager.Text("setup.console.preparing_detail"), ConsoleStateAccentHex, LocalizationManager.Text("setup.console.badge_ready"));
				SetConsoleStatus(LocalizationManager.Text("setup.console.state_initializing"), ConsoleStateInitializingHex);
				ConsoleBootButton.BackgroundColor = ConsoleBootNormalColor;
				break;
			case ConsoleBootActionState.Standby:
				SetServerState(LocalizationManager.Text("setup.console.state_standby"), LocalizationManager.Text("setup.console.standby_detail"), ConsoleStateAccentHex, LocalizationManager.Text("setup.console.badge_ready"));
				SetConsoleStatus(LocalizationManager.Text("setup.console.state_standby"), ConsoleStateAccentHex);
				ConsoleBootButton.BackgroundColor = ConsoleBootNormalColor;
				StartConsoleBootPulse();
				break;
			case ConsoleBootActionState.Booting:
				SetServerState(LocalizationManager.Text("setup.console.state_booting"), LocalizationManager.Text("setup.console.booting_detail"), ConsoleStateWarningHex, LocalizationManager.Text("setup.console.badge_boot"));
				SetConsoleStatus(LocalizationManager.Text("setup.console.state_booting"), ConsoleStateWarningHex);
				ConsoleBootButton.BackgroundColor = ConsoleBootingButtonColor;
				StartConsoleBootingAnimation();
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
				StartPrimaryButtonPulse();
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
		await WelcomeContainer.FadeToAsync(0, PanelQuickAnimationLength, Easing.CubicIn);
		WelcomeContainer.IsVisible = false;

		PreparePanelReveal(panel);

		ShowActionBottomBar(actionText, false);

		await Task.WhenAll(
			panel.FadeToAsync(1, PanelShowLength, Easing.CubicOut),
			SafeAnimation.TranslateToAsync(panel, 0, 0, PanelShowLength, Easing.CubicOut, "Setup.Panel"),
			ActionBottomBar.FadeToAsync(1, PanelShowLength, Easing.CubicOut),
			SafeAnimation.TranslateToAsync(ActionBottomBar, 0, 0, PanelShowLength, Easing.CubicOut, "Setup.Panel")
		);
	}

	private async void OnBackClicked(object? sender, EventArgs e)
	{
		if (_isDiagnosticActionRunning) return;
		if (_currentState != ViewState.Ready || _isTransitioning) return;

		if (_currentContext == ViewContext.Repairing)
		{
			_repairCts?.Cancel();
		}

		if (ServerConsolePanel.IsVisible)
		{
			// RETRY ACTION: Go back to diagnostics
			await FadeOutAndHideAsync(ServerConsolePanel, PanelQuickAnimationLength, Easing.CubicIn);

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

		// 1. Instantly Revert Card States (while container is hidden)
		ApplyCardState(VanguardCard, VanguardGlow, CardState.Ambient);
		ApplyCardState(ArchitectCard, ArchitectGlow, CardState.Ambient);

		var panel = _currentContext == ViewContext.Vanguard || _currentContext == ViewContext.Repairing ?
					VanguardPanel :
					(ArchitectInitiationPanel.IsVisible ? ArchitectInitiationPanel : ArchitectWorkspacePanel);

		await Task.WhenAll(
			panel.FadeToAsync(0, PanelQuickAnimationLength, Easing.CubicIn),
			SafeAnimation.TranslateToAsync(panel, 0, PanelRevealOffsetY, PanelQuickAnimationLength, Easing.CubicIn, "Setup.Panel"),
			ActionBottomBar.FadeToAsync(0, PanelQuickAnimationLength, Easing.CubicIn),
			SafeAnimation.TranslateToAsync(ActionBottomBar, 0, ActionBarHideOffsetY, PanelQuickAnimationLength, Easing.CubicIn, "Setup.Panel")
		);

		panel.IsVisible = false;
		HideArchitectPanelsAndActionBar();

		// 2. Start Main Action (Return to Crossroads)
		_currentState = ViewState.StartAction;

		// Reset states for clean fade-in
		WelcomeContainer.Opacity = 0;
		WelcomeContainer.IsVisible = true;
		BackgroundLayer.Opacity = BackgroundDimmedOpacity;

		// Brief delay to allow MAUI to register IsVisible change before animating
		await Task.Delay(CrossroadsReturnDelayMs);

		_ = BackgroundLayer.FadeToAsync(BackgroundRevealOpacity, CrossroadsBackgroundRevealLength, Easing.CubicOut);
		await WelcomeContainer.FadeToAsync(1, CrossroadsWelcomeRevealLength, Easing.CubicOut);

		// 3. Ready State
		_currentContext = ViewContext.Crossroads;
		_currentState = ViewState.Ready;
		_isTransitioning = false;
	}

	private static void PreparePanelReveal(VisualElement panel)
	{
		panel.Opacity = 0;
		panel.IsVisible = true;
		panel.TranslationY = PanelRevealOffsetY;
	}

	private static async Task FadeOutAndHideAsync(VisualElement panel, uint length, Easing? easing = null)
	{
		await panel.FadeToAsync(0, length, easing);
		panel.IsVisible = false;
	}

	private Task ShowPanelAndActionBarQuickAsync(VisualElement panel)
	{
		panel.IsVisible = true;
		EnsureActionBottomBarReady(restoreOpacity: false);

		return Task.WhenAll(
			panel.FadeToAsync(1, PanelQuickAnimationLength),
			ActionBottomBar.FadeToAsync(1, PanelQuickAnimationLength));
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

		ApplyCardState(VanguardCard, VanguardGlow, isOverVanguard ? CardState.Hovered : CardState.Ambient);
		ApplyCardState(ArchitectCard, ArchitectGlow, isOverArchitect ? CardState.Hovered : CardState.Ambient);
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

		if (isValid) StartPrimaryButtonPulse();
		else StopPrimaryButtonPulse();
	}

	private async void OnPrimaryActionClicked(object? sender, EventArgs e)
	{
		if (_isDiagnosticActionRunning) return;
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

		AddConsoleLog("[SYSTEM] Handshaking complete. Redirecting to Nexus Dashboard...");

		await Task.WhenAll(
			GlassPanel.FadeToAsync(0, FinalizeGlassFadeLength, Easing.CubicIn),
			BackgroundLayer.FadeToAsync(0, FinalizeBackgroundFadeLength, Easing.CubicIn),
			ActionBottomBar.FadeToAsync(0, PanelQuickAnimationLength, Easing.CubicIn),
			SafeAnimation.TranslateToAsync(ActionBottomBar, 0, ActionBarHideOffsetY, PanelQuickAnimationLength, Easing.CubicIn, "Setup.Panel")
		);

		CancelTransientAnimationLoops();
		this.IsVisible = false;
		SetupFinalized?.Invoke(this, EventArgs.Empty);
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
			await ArchitectWorkspacePanel.FadeToAsync(0, PanelQuickAnimationLength);
			ArchitectWorkspacePanel.IsVisible = false;
			PrepareVanguardChecklist();
			VanguardPanel.IsVisible = true;
			await VanguardPanel.FadeToAsync(1, PanelQuickAnimationLength);
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
			BackButton.IsEnabled = true;
			BackButton.InputTransparent = false;
		}

		if (VanguardOptionalNodes.Count > 0)
		{
			await RefreshVanguardOptionalNodesSafeAsync();
			await ScrollVanguardOptionalSectionIntoViewAsync();
		}

		// All done
		BackButton.IsEnabled = true;
		BackButton.InputTransparent = false;
		_currentState = ViewState.Ready;
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
		if (sender is not Border ring || ring.BindingContext is not DiagnosticNodeViewModel vm) return;

		vm.PropertyChanged -= OnDiagnosticLoadingRingViewModelChanged;
		vm.PropertyChanged += OnDiagnosticLoadingRingViewModelChanged;
		UpdateDiagnosticLoadingRing(ring, vm.IsLoading);
	}

	private void OnDiagnosticLoadingRingUnloaded(object? sender, EventArgs e)
	{
		if (sender is not Border ring) return;

		if (ring.BindingContext is DiagnosticNodeViewModel vm)
		{
			vm.PropertyChanged -= OnDiagnosticLoadingRingViewModelChanged;
		}

		StopDiagnosticLoadingRing(ring);
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
		foreach (var ring in FindVisualChildren<Border>(list))
		{
			if (ring.BindingContext == vm && ring.StyleId == "DiagnosticLoadingRing")
			{
				UpdateDiagnosticLoadingRing(ring, vm.IsLoading);
			}
		}
	}

	private void UpdateDiagnosticLoadingRing(Border ring, bool isLoading)
	{
		if (isLoading)
		{
			StartDiagnosticLoadingRingPulse(ring);
			return;
		}

		StopDiagnosticLoadingRing(ring);
	}

	private void StartDiagnosticLoadingRingPulse(Border ring)
	{
		if (_isDisposed || !IsVisible || !ring.IsVisible)
		{
			return;
		}

		ring.Rotation = 0;
		ring.Opacity = 0.35;
		_motion.StartTimeline(
			GetDiagnosticLoadingRingMotionName(ring),
			ring,
			16,
			DiagnosticLoadingRingRotateLength,
			Easing.Linear,
			() => !_isDisposed && IsVisible && ring.IsVisible && ring.Opacity > 0,
			() => ResetDiagnosticLoadingRing(ring),
			new SafeAnimation.TimelineSegment(0, 1, value => ring.Rotation = value, 0, 360, Easing.Linear),
			new SafeAnimation.TimelineSegment(0, 0.5, value => ring.Opacity = value, 0.35, 0.95, Easing.CubicInOut),
			new SafeAnimation.TimelineSegment(0.5, 1, value => ring.Opacity = value, 0.95, 0.35, Easing.CubicInOut));
	}

	private void StopDiagnosticLoadingRing(Border ring)
	{
		_motion.Stop(GetDiagnosticLoadingRingMotionName(ring));
		ResetDiagnosticLoadingRing(ring);
	}

	private static string GetDiagnosticLoadingRingMotionName(Border ring)
		=> $"{DiagnosticLoadingRingPulseAnimationName}.{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ring):X8}";

	private static void ResetDiagnosticLoadingRing(Border ring)
	{
		ring.Opacity = 0;
		ring.Rotation = 0;
	}

	private void StopDiagnosticLoadingRingAnimations()
	{
		foreach (Border ring in FindVisualChildren<Border>(this))
		{
			if (ring.StyleId == "DiagnosticLoadingRing")
			{
				StopDiagnosticLoadingRing(ring);
			}
		}
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
		SetInitiationUserScrollBlocked(isBlocked || IsAnyDiagnosticStepWorking());

		BackButton.IsEnabled = !isBlocked;
		BackButton.InputTransparent = isBlocked;
		BackButton.Opacity = isBlocked ? 0.35 : 1.0;

		if (isBlocked)
		{
			PrimaryActionButton.IsEnabled = false;
			PrimaryActionButton.InputTransparent = true;
			PrimaryActionButton.Opacity = 0.35;
			StopPrimaryButtonPulse();
			return;
		}

		EvaluateCurrentInitiationReadiness();
	}

	private void UpdateInitiationUserScrollBlock()
	{
		SetInitiationUserScrollBlocked(_isDiagnosticActionRunning || IsAnyDiagnosticStepWorking());
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
			bool primaryEnabled = allReady && !_isDiagnosticActionRunning;
			PrimaryActionButton.IsEnabled = primaryEnabled;
			PrimaryActionButton.InputTransparent = !primaryEnabled;
			PrimaryActionButton.Opacity = primaryEnabled ? 1.0 : 0.3;
			PrimaryActionLabel.Text = allReady
				? LocalizationManager.Text("common.next")
				: LocalizationManager.Text("setup.status.requirements_pending");

			if (primaryEnabled) StartPrimaryButtonPulse();
			else StopPrimaryButtonPulse();
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

		if (primaryEnabled) StartPrimaryButtonPulse();
		else StopPrimaryButtonPulse();

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
		BackButton.IsEnabled = !_isDiagnosticActionRunning;
		BackButton.InputTransparent = _isDiagnosticActionRunning;
		BackButton.Opacity = _isDiagnosticActionRunning ? 0.35 : 1.0;
	}

	private void StartPrimaryButtonPulse()
	{
		if (_isDisposed) return;

		PrimaryActionPulseGlow.Opacity = PrimaryPulseLowOpacity;
		_motion.StartTimeline(
			PrimaryActionPulseAnimationName,
			this,
			16,
			PulseAnimationLength * 2,
			Easing.Linear,
			CanRepeatPrimaryActionPulse,
			ResetPrimaryButtonPulse,
			new SafeAnimation.TimelineSegment(0, 0.5, value => PrimaryActionPulseGlow.Opacity = value, PrimaryPulseLowOpacity, PrimaryPulseHighOpacity, Easing.CubicInOut),
			new SafeAnimation.TimelineSegment(0.5, 1, value => PrimaryActionPulseGlow.Opacity = value, PrimaryPulseHighOpacity, PrimaryPulseLowOpacity, Easing.CubicInOut));
	}

	private void StopPrimaryButtonPulse()
	{
		_motion.Stop(PrimaryActionPulseAnimationName);
		ResetPrimaryButtonPulse();
	}

	private void ResetPrimaryButtonPulse()
	{
		PrimaryActionPulseGlow.Opacity = 0;
	}

	private bool CanRepeatPrimaryActionPulse()
		=> !_isDisposed
			&& IsVisible
			&& Handler is not null
			&& ActionBottomBar.IsVisible
			&& PrimaryActionButton.IsEnabled
			&& !_isDiagnosticActionRunning;

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

	private void OnVanguardCardPointerEntered(object? sender, PointerEventArgs e)
	{
		if (_currentState != ViewState.Ready || _currentContext != ViewContext.Crossroads) return;
		ApplyCardState(VanguardCard, VanguardGlow, CardState.Hovered);
	}

	private void OnVanguardCardPointerExited(object? sender, PointerEventArgs e)
	{
		if (_currentContext != ViewContext.Crossroads || _isTransitioning) return;
		ApplyCardState(VanguardCard, VanguardGlow, CardState.Ambient);
	}

	private void OnArchitectCardPointerEntered(object? sender, PointerEventArgs e)
	{
		if (_currentState != ViewState.Ready || _currentContext != ViewContext.Crossroads) return;
		ApplyCardState(ArchitectCard, ArchitectGlow, CardState.Hovered);
	}

	private void OnArchitectCardPointerExited(object? sender, PointerEventArgs e)
	{
		if (_currentContext != ViewContext.Crossroads || _isTransitioning) return;
		ApplyCardState(ArchitectCard, ArchitectGlow, CardState.Ambient);
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

		ConsoleBootButton.BackgroundColor = ConsoleBootHoverColor;
	}

	private void OnConsoleBootUnhovered(object? sender, PointerEventArgs e)
	{
		if (_consoleBootActionState != ConsoleBootActionState.Standby) return;

		ConsoleBootButton.BackgroundColor = ConsoleBootNormalColor;
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

	private void ApplyCardState(Border card, View glow, CardState state)
	{
		glow.CancelAnimations();

		switch (state)
		{
			case CardState.Ambient:
				glow.FadeToAsync(AmbientCardGlowOpacity, CardGlowAnimationLength, Easing.CubicOut);
				break;
			case CardState.Hovered:
				glow.FadeToAsync(HoveredCardGlowOpacity, CardGlowAnimationLength, Easing.CubicOut);
				break;
			case CardState.Persistent:
				glow.FadeToAsync(PersistentCardGlowOpacity, PersistentCardGlowAnimationLength, Easing.CubicOut);
				break;
			case CardState.Hidden:
				glow.FadeToAsync(0, CardGlowAnimationLength, Easing.CubicOut);
				break;
		}
	}

	private async Task TriggerInitiationImpact(VisualElement target, View selectedGlow, View otherGlow)
	{
		selectedGlow.CancelAnimations();
		_ = otherGlow.FadeToAsync(0, OtherCardGlowHideLength, Easing.CubicOut);

		// 1. Aura Pure Strobe (Opacity Flicker Only)
		// First Flash
		selectedGlow.Opacity = PersistentCardGlowOpacity;
		await Task.Delay(ImpactFirstFlashDelayMs);

		// Void
		selectedGlow.Opacity = 0.0;
		await Task.Delay(ImpactVoidDelayMs);

		// Second Flash
		selectedGlow.Opacity = PersistentCardGlowOpacity;
		await Task.Delay(ImpactSecondFlashDelayMs);

		// Final Lock at max brightness with a smooth transition
		await selectedGlow.FadeToAsync(PersistentCardGlowOpacity, CardGlowAnimationLength, Easing.CubicOut);

		// Ensure final state machine integrity
		ApplyCardState(selectedGlow == VanguardGlow ? VanguardCard : ArchitectCard, selectedGlow, CardState.Persistent);
		ApplyCardState(otherGlow == VanguardGlow ? VanguardCard : ArchitectCard, otherGlow, CardState.Hidden);

		// 2. Impact Scale for the Card (Physical Pulse)
		await SafeAnimation.ScaleToAsync(target, ImpactCardScale, ImpactCardScaleOutLength, Easing.CubicOut, "Setup.Impact");
		_ = SafeAnimation.ScaleToAsync(target, 1.0, ImpactCardScaleInLength, Easing.CubicIn, "Setup.Impact");
	}

	private void UpdateCardHoverState(Border card, View glow, bool isHovered, string accentColor)
	{
		// Only react to hover in Crossroads state when READY
		if (_currentState != ViewState.Ready || _currentContext != ViewContext.Crossroads) return;

		// Restore smooth cinematic aura animation (Fade between Ambient 0.15 and Active 0.8)
		glow.CancelAnimations();
		double targetGlow = isHovered ? HoveredCardGlowOpacity : AmbientCardGlowOpacity;
		glow.FadeToAsync(targetGlow, CardGlowAnimationLength, Easing.CubicInOut);
	}

	// ------------------------------------------------------------
	// SMART SCALING
	// ------------------------------------------------------------
	private void OnViewSizeChanged(object? sender, EventArgs e)
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
