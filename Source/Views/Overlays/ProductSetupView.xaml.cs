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
	private const int ConsoleLogScrollDelayMs = 50;
	private const int MaxConsoleLogItems = 100;
	private const string ConsoleLogStyleKey = "ConsoleLogStyle";
	private const string DiagnosticActionCustom = "custom";
	private const string DiagnosticActionSystem = "system";
	private const string DiagnosticActionKeep = "keep";
	private const string DiagnosticNodeGitCore = "git-core";
	private const string DiagnosticNodePythonEngine = "python-engine";
	private const string DiagnosticNodeComfyCore = "comfy-core";
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
	private static readonly Color ConsoleLogTimeColor = NexusColors.TextDim;
	private static readonly Color ConsoleLogBodyColor = Color.FromArgb("#a3c4db");
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
	private readonly RuntimeDiagnosticCatalog _diagnosticCatalog;
	private readonly ObservableCollection<DiagnosticNodeViewModel> _diagnosticViewModels = new();
	private readonly List<GpuDeviceInfo> _gpuDevices = new();
	private readonly List<Border> _gpuOptionCards = new();
	private CancellationTokenSource? _repairCts;
	private CancellationTokenSource? _pulseCts;
	private CancellationTokenSource? _gpuDiscoveryCts;
	private readonly SemaphoreSlim _architectOptionalRefreshGate = new(1, 1);
	private bool _isDisposed;
	private bool _isGpuSelectorExpanded;
	private bool _isUpdatingServerPythonMode;
	private bool _isArchitectInitiationRunning;
	private bool _pendingArchitectOptionalRefresh;
	private bool _isDiagnosticScrollAdjusting;
	private bool _isDiagnosticActionRunning;
#if WINDOWS
	private bool _nativeScrollDragAttached;
	private bool _isNativeInitiationScrollDragging;
	private double _nativeInitiationDragStartY;
	private double _nativeInitiationDragStartOffsetY;
	private Microsoft.UI.Xaml.Controls.ScrollViewer? _nativeDraggedInitiationScrollViewer;
	private View? _nativeDraggedInitiationScrollContent;
#endif

	private enum ViewState { StartAction, Ready, EndAction }
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

	// Animation Timers
	private IDispatcherTimer? _driftTimer;
	private double _driftTime = 0;

	public ProductSetupView()
	{
		InitializeComponent();
		_initiationSequence = new InitiationSequenceRunner(
			PopulateInlineActions,
			WaitForNodeReadyAsync,
			RequestDiagnosticScroll,
			EvaluateCurrentInitiationReadiness,
			UpdateDiagnosticProgress);

		_diagnosticCatalog = new RuntimeDiagnosticCatalog(new IRuntimeDiagnosticNode[]
		{
			new GitDiagnosticNode(),
			new PythonDiagnosticNode(),
			new ComfyCoreDiagnosticNode(),
			new BaseResourceDiagnosticNode(),
			new ManagerExtensionDiagnosticNode(),
			new ModelLibraryDiagnosticNode(allowMultipleRoots: false)
		});

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
		MainThread.BeginInvokeOnMainThread(InitializeLifecycle);
	}

	private void OnProductSetupUnloaded(object? sender, EventArgs e)
	{
		_isDisposed = true;
		DetachNativeInitiationScrollDragHandlers();
		CancelTransientAnimationLoops();
		StopDriftEngine();
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
			await content.TranslateToAsync(0, 0, InitiationOverscrollReturnLength, Easing.SpringOut);
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
		_pulseCts?.Cancel();
		_pulseCts = null;
		_gpuDiscoveryCts?.Cancel();
		_consolePulseCts?.Cancel();
		_consoleBootingCts?.Cancel();
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
				GlassPanel.ScaleToAsync(1.0, GlassRevealLength, Easing.SpringOut),
				HeaderBar.FadeToAsync(1, HeaderRevealLength, Easing.CubicOut),
				HeaderBar.TranslateToAsync(0, 0, HeaderRevealLength, Easing.CubicOut),
				WelcomeContainer.FadeToAsync(1, WelcomeRevealLength, Easing.CubicOut),
				WelcomeContainer.TranslateToAsync(0, 0, WelcomeRevealLength, Easing.CubicOut)
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
		if (_driftTimer != null)
		{
			_driftTimer.Stop();
			_driftTimer = null;
		}
	}

	private void StartDriftEngine()
	{
		StopDriftEngine(); // Ensure previous timer is stopped
		InitParticles();

		_driftTimer = Dispatcher.CreateTimer();
		_driftTimer.Interval = TimeSpan.FromMilliseconds(DriftFrameIntervalMs);
		_driftTimer.Tick += (s, e) =>
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
		};
		_driftTimer.Start();
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
			_isArchitectInitiationRunning = false;
			_pendingArchitectOptionalRefresh = false;
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

		_ = RunRepairSequenceAsync();
	}

	private async void OnArchitectSelected(object? sender, TappedEventArgs e)
	{
		if (_currentState != ViewState.Ready || _currentContext != ViewContext.Crossroads) return;

		_currentState = ViewState.StartAction;
		_selectedInstallMode = SetupInstallModes.ExistingComfyPath;
		ArchitectNodes.Clear();

		var settings = SetupSettingsService.Instance.Settings;
		ArchitectFolderBrowser.InitializePath(settings.ComfyPath ?? "C:\\");

		_ = BackgroundLayer.FadeToAsync(BackgroundDimmedOpacity, BackgroundDimLength, Easing.CubicOut);
		await Task.WhenAll(
			TriggerInitiationImpact(ArchitectContainer, ArchitectGlow, VanguardGlow),
			TransitionToPanel(ArchitectWorkspacePanel, LocalizationManager.Text("common.next"))
		);

		UpdateArchitectWorkspaceReadiness();
		_isTransitioning = false;
		_currentState = ViewState.Ready;
	}

	private DiagnosticNodeViewModel CreateArchitectVM(IRuntimeDiagnosticNode node)
	{
		var vm = new DiagnosticNodeViewModel(node);
		vm.UpdateState(HealthState.Pending);
		return vm;
	}

	private void PrepareArchitectChecklist()
	{
		ArchitectNodes.Clear();
		ArchitectOptionalNodes.Clear();
		BindableLayout.SetItemsSource(ArchitectNodesList, ArchitectNodes);
		BindableLayout.SetItemsSource(ArchitectOptionalNodesList, ArchitectOptionalNodes);

		// 1. Git Core Node
		var gitNode = _diagnosticCatalog.Nodes.OfType<GitDiagnosticNode>().FirstOrDefault() ?? new GitDiagnosticNode();
		var gitVm = CreateArchitectVM(gitNode);
		gitVm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(DiagnosticNodeViewModel.CurrentHealth)) CheckArchitectInitiationStatus(); };
		ArchitectNodes.Add(gitVm);

		// 2. Python Runtime Node
		var pythonNode = _diagnosticCatalog.Nodes.OfType<PythonDiagnosticNode>().FirstOrDefault() ?? new PythonDiagnosticNode();
		var pythonVm = CreateArchitectVM(pythonNode);
		pythonVm.PropertyChanged += (s, e) =>
		{
			if (e.PropertyName != nameof(DiagnosticNodeViewModel.CurrentHealth)) return;

			CheckArchitectInitiationStatus();
			if (_isArchitectInitiationRunning)
			{
				_pendingArchitectOptionalRefresh = true;
				return;
			}

			_ = RefreshArchitectOptionalNodesSafeAsync();
		};
		ArchitectNodes.Add(pythonVm);

		// Optional ComfyUI .venv Node
		var pythonEnvironmentVm = CreateArchitectVM(new PythonEnvironmentDiagnosticNode());
		pythonEnvironmentVm.EnvironmentDetails = LocalizationManager.Text("setup.venv.requires_python");
		pythonEnvironmentVm.EnvironmentPath = ComfyInstallService.ComfyVenvPath;
		ArchitectOptionalNodes.Add(pythonEnvironmentVm);

		var pipCacheVm = CreateArchitectVM(new PipCacheDiagnosticNode());
		ArchitectOptionalNodes.Add(pipCacheVm);

		var modelLibraryVm = CreateArchitectVM(new ModelLibraryDiagnosticNode(allowMultipleRoots: true));
		ArchitectOptionalNodes.Add(modelLibraryVm);
		UpdateArchitectOptionalSectionVisibility();

		// 3. ComfyUI Extension Node
		var extensionNode = _diagnosticCatalog.Nodes.OfType<ManagerExtensionDiagnosticNode>().FirstOrDefault() ?? new ManagerExtensionDiagnosticNode();
		var extensionVm = CreateArchitectVM(extensionNode);
		extensionVm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(DiagnosticNodeViewModel.CurrentHealth)) CheckArchitectInitiationStatus(); };
		ArchitectNodes.Add(extensionVm);

		UpdateArchitectRequiredStatus();
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

			bool pythonReady = ArchitectNodes.Any(vm => vm.Node is PythonDiagnosticNode && vm.CurrentHealth == HealthState.Healthy);

			foreach (var vm in ArchitectOptionalNodes)
			{
				if (vm.Node is not IConfigurableDiagnosticNode configurableNode) continue;

				await InvokeOnMainThreadSafeAsync(() =>
				{
					vm.Actions.Clear();
					vm.NotifyActionsChanged();
					return Task.CompletedTask;
				});

				if (!pythonReady && vm.Node is PythonEnvironmentDiagnosticNode)
				{
					await InvokeOnMainThreadSafeAsync(() =>
					{
						vm.UpdateState(HealthState.Pending);
						vm.EnvironmentDetails = LocalizationManager.Text("setup.venv.requires_python");
						vm.EnvironmentPath = ComfyInstallService.ComfyVenvPath;
						return Task.CompletedTask;
					});
					continue;
				}

				await configurableNode.ProbeEnvironmentAsync(CancellationToken.None);
				var health = await vm.Node.CheckHealthAsync(CancellationToken.None);
				await InvokeOnMainThreadSafeAsync(() =>
				{
					vm.UpdateState(health);
					vm.EnvironmentDetails = configurableNode.EnvironmentDetails;
					vm.EnvironmentPath = configurableNode.EnvironmentPath;
					return Task.CompletedTask;
				});

				if (health == HealthState.OptionalMissing)
				{
					await InvokeOnMainThreadSafeAsync(() =>
					{
						PopulateInlineActions(vm, configurableNode);
						SetDiagnosticNodeInteraction(vm, true);
						return Task.CompletedTask;
					});
				}
			}
		}
		finally
		{
			_architectOptionalRefreshGate.Release();
		}
	}

	private async Task RunArchitectInitiationSequenceAsync()
	{
		_currentContext = ViewContext.Architect;
		_isArchitectInitiationRunning = true;
		_pendingArchitectOptionalRefresh = false;

		try
		{
			await _initiationSequence.RunArchitectAsync(ArchitectNodes, CancellationToken.None);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETUP:UI] Architect initiation sequence failed");
			AddConsoleLog($"[SETUP:UI] Architect initiation sequence failed: {ex.Message}");
		}
		finally
		{
			_isArchitectInitiationRunning = false;
		}

		if (_pendingArchitectOptionalRefresh || ArchitectOptionalNodes.Count > 0)
		{
			_pendingArchitectOptionalRefresh = false;
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
			&& HasConfiguredExecutable(settings.GitPath)
			&& HasConfiguredExecutable(settings.PythonPath)
			&& ArchitectNodes.All(IsDiagnosticNodeReadyForNext);
	}

	private static bool IsDiagnosticNodeReadyForNext(DiagnosticNodeViewModel vm)
	{
		if (vm.CurrentHealth is HealthState.Healthy or HealthState.OptionalMissing) return true;

		if (vm.Node is not IConfigurableDiagnosticNode configurableNode) return false;

		return configurableNode.NodeId switch
		{
			DiagnosticNodeGitCore => HasConfiguredExecutable(SetupSettingsService.Instance.Settings.GitPath),
			DiagnosticNodePythonEngine => HasConfiguredExecutable(SetupSettingsService.Instance.Settings.PythonPath),
			_ => false
		};
	}

	private static bool HasConfiguredExecutable(string? path)
	{
		if (string.IsNullOrWhiteSpace(path)) return false;

		bool hasDirectory = path.Contains(System.IO.Path.DirectorySeparatorChar)
			|| path.Contains(System.IO.Path.AltDirectorySeparatorChar);
		return !hasDirectory || File.Exists(path);
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
			ArchitectInitiationPanel.TranslateToAsync(0, 0, ArchitectInitiationShowLength, Easing.CubicOut)
		);

		_ = RunArchitectInitiationSequenceAsync();
	}

	private async Task TransitionToConsoleAsync()
	{
		VisualElement? currentPanel = null;
		if (VanguardPanel.IsVisible) currentPanel = VanguardPanel;
		else if (ArchitectWorkspacePanel.IsVisible) currentPanel = ArchitectWorkspacePanel;
		else if (ArchitectInitiationPanel.IsVisible) currentPanel = ArchitectInitiationPanel;

		if (currentPanel != null)
		{
			await FadeOutAndHideAsync(currentPanel, PanelQuickAnimationLength, Easing.CubicIn);
		}

		ConsoleLogList.Children.Clear(); // Clear existing logs

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
			ServerConsolePanel.TranslateToAsync(0, 0, ConsoleShowLength, Easing.CubicOut),
			ActionBottomBar.FadeToAsync(1, ConsoleShowLength, Easing.CubicOut),
			ActionBottomBar.TranslateToAsync(0, 0, ConsoleShowLength, Easing.CubicOut)
		);

		UpdateBootChannelInfo();
		AutoSelectServerPythonModeFromComfyVenv();
		UpdateServerPythonModeVisual();
		await RefreshGpuSelectorAsync();

		_ = RunSystemBootCheckAsync();
	}

	private async Task RunSystemBootCheckAsync()
	{
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

	private async Task RefreshGpuSelectorAsync()
	{
		_gpuDiscoveryCts?.Cancel();
		_gpuDiscoveryCts?.Dispose();
		_gpuDiscoveryCts = new CancellationTokenSource();
		CancellationToken cancellationToken = _gpuDiscoveryCts.Token;

		GpuSelectorStack.Children.Clear();
		GpuSelectorDropdownPanel.IsVisible = false;
		_gpuOptionCards.Clear();
		_gpuDevices.Clear();
		SetGpuSelectorExpanded(false);

		string selectedGpuId = SetupSettingsService.Instance.Settings.GpuId;
		SelectedGpuLabel.Text = FormatGpuLabel(selectedGpuId);
		SelectedGpuNameLabel.Text = FormatGpuLabel(selectedGpuId);
		SelectedGpuDetailLabel.Text = LocalizationManager.Text("setup.gpu.scanning");
		AddConsoleLog("[GPU] Scanning available CUDA devices...");

		IReadOnlyList<GpuDeviceInfo> devices;
		try
		{
			devices = await GpuDiscoveryService.DiscoverAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		if (_isDisposed || cancellationToken.IsCancellationRequested) return;

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

	private CancellationTokenSource? _consolePulseCts;
	private CancellationTokenSource? _consoleBootingCts;

	private static Task InvokeOnMainThreadSafeAsync(Func<Task> action)
	{
		return UiThread.InvokeAsync(action, "PRODUCT_SETUP:UI");
	}

	private void StartConsoleBootPulse()
	{
		if (_isDisposed) return;

		_consolePulseCts?.Cancel();
		_consolePulseCts = new CancellationTokenSource();
		var token = _consolePulseCts.Token;

		Task.Run(async () =>
		{
			while (!token.IsCancellationRequested)
			{
				try
				{
					await InvokeOnMainThreadSafeAsync(async () =>
					{
						if (_isDisposed || token.IsCancellationRequested) return;

						await ConsoleBootPulseGlow.FadeToAsync(ConsolePulseHighOpacity, PulseAnimationLength, Easing.CubicInOut);
						if (_isDisposed || token.IsCancellationRequested) return;

						await ConsoleBootPulseGlow.FadeToAsync(0, PulseAnimationLength, Easing.CubicInOut);
					});
				}
				catch (ObjectDisposedException)
				{
					break;
				}
				catch (InvalidOperationException)
				{
					break;
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception ex)
				{
					NexusLog.Exception(ex, "[SETUP:UI] Console boot pulse failed");
					break;
				}
			}
		}, token);
	}

	private void StopConsoleBootPulse()
	{
		_consolePulseCts?.Cancel();
		_consolePulseCts = null;
		if (_isDisposed) return;

		ConsoleBootPulseGlow.CancelAnimations();
		ConsoleBootPulseGlow.Opacity = 0;
	}

	private void StartConsoleBootingAnimation()
	{
		if (_isDisposed) return;

		_consoleBootingCts?.Cancel();
		_consoleBootingCts = new CancellationTokenSource();
		var token = _consoleBootingCts.Token;

		Task.Run(async () =>
		{
			while (!token.IsCancellationRequested)
			{
				try
				{
					await InvokeOnMainThreadSafeAsync(async () =>
					{
						if (_isDisposed || token.IsCancellationRequested) return;

						ConsoleBootPulseGlow.BackgroundColor = ConsoleWarningColor;
						await Task.WhenAll(
							ConsoleBootPulseGlow.FadeToAsync(ConsoleBootingGlowHighOpacity, ConsoleBootingPulseLength, Easing.CubicInOut),
							ConsoleBootButton.ScaleToAsync(ConsoleBootingButtonHighScale, ConsoleBootingPulseLength, Easing.CubicInOut),
							ConsoleStatusBorder.FadeToAsync(0.55, ConsoleBootingPulseLength, Easing.CubicInOut)
						);

						if (_isDisposed || token.IsCancellationRequested) return;

						await Task.WhenAll(
							ConsoleBootPulseGlow.FadeToAsync(ConsoleBootingGlowLowOpacity, ConsoleBootingPulseLength, Easing.CubicInOut),
							ConsoleBootButton.ScaleToAsync(1.0, ConsoleBootingPulseLength, Easing.CubicInOut),
							ConsoleStatusBorder.FadeToAsync(1.0, ConsoleBootingPulseLength, Easing.CubicInOut)
						);
					});
				}
				catch (ObjectDisposedException)
				{
					break;
				}
				catch (InvalidOperationException)
				{
					break;
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception ex)
				{
					NexusLog.Exception(ex, "[SETUP:UI] Console booting animation failed");
					break;
				}
			}
		}, token);
	}

	private void StopConsoleBootingAnimation()
	{
		_consoleBootingCts?.Cancel();
		_consoleBootingCts = null;
		if (_isDisposed) return;

		ConsoleBootButton.CancelAnimations();
		ConsoleStatusBorder.CancelAnimations();
		ConsoleBootPulseGlow.CancelAnimations();
		ConsoleBootButton.Scale = 1.0;
		ConsoleStatusBorder.Opacity = 1.0;
		ConsoleBootPulseGlow.Opacity = 0;
		ConsoleBootPulseGlow.BackgroundColor = ConsoleAccentColor;
	}

	private async void OnConsoleRetryClicked(object? sender, EventArgs e)
	{
		if (_consoleBootActionState != ConsoleBootActionState.Failed) return;

		await RunSystemBootCheckAsync();
		if (_consoleBootActionState == ConsoleBootActionState.Standby)
		{
			await RunServerBootSequenceAsync();
		}
	}

	private async void OnConsoleRecoverBootClicked(object? sender, EventArgs e)
	{
		if (_consoleBootActionState is not (ConsoleBootActionState.Standby or ConsoleBootActionState.Failed)) return;

		var page = GetPromptPage();
		if (page != null)
		{
			string repairTarget = RuntimeRepairTarget.GetDisplay();
			bool confirmed = await page.DisplayAlertAsync(
				LocalizationManager.Text("setup.console.recover_boot_title"),
				LocalizationManager.Format("setup.console.recover_boot_message", repairTarget),
				LocalizationManager.Text("setup.console.recover_boot"),
				LocalizationManager.Text("common.cancel"));
			if (!confirmed) return;
		}

		if (_consoleBootActionState == ConsoleBootActionState.Failed)
		{
			await RunSystemBootCheckAsync();
			if (_consoleBootActionState != ConsoleBootActionState.Standby) return;
		}

		await RunServerBootSequenceAsync(repairRuntimeBeforeBoot: true);
	}

	private void ApplyConsoleBootActionState(ConsoleBootActionState state, string? detail = null)
	{
		_consoleBootActionState = state;
		if (_isDisposed) return;

		StopConsoleBootPulse();
		StopConsoleBootingAnimation();
		StopPrimaryButtonPulse();

		ConsoleBootButton.CancelAnimations();
		ConsoleRecoverBootButton.CancelAnimations();
		ConsoleRetryButton.CancelAnimations();
		ConsoleBootPulseGlow.CancelAnimations();
		ConsoleBootButton.Scale = 1.0;
		ConsoleRecoverBootButton.Scale = 1.0;
		ConsoleRetryButton.Scale = 1.0;
		ConsoleBootPulseGlow.Opacity = 0;
		ConsoleBootPulseGlow.BackgroundColor = ConsoleAccentColor;

		bool showRecoverBoot = ShouldShowConsoleRecoverBoot(state);
		ConsoleBootActionsGrid.Spacing = showRecoverBoot ? ConsoleBootActionsExpandedRowSpacing : 0;
		SetConsoleButtonAvailability(
			ConsoleBootButton,
			isVisible: state is ConsoleBootActionState.Preparing or ConsoleBootActionState.Standby or ConsoleBootActionState.Booting,
			isEnabled: state == ConsoleBootActionState.Standby);
		SetConsoleButtonAvailability(
			ConsoleRecoverBootButton,
			isVisible: showRecoverBoot,
			isEnabled: showRecoverBoot);
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
		ConsoleRecoverBootButton.Opacity = showRecoverBoot ? 1.0 : 0.0;
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
		UiThread.TryBeginInvoke(async () =>
		{
			try
			{
				if (_isDisposed) return;

				var logLabel = new Label
				{
					FormattedText = CreateConsoleLogText(message)
				};
				if (Resources.TryGetValue(ConsoleLogStyleKey, out object styleResource)
					&& styleResource is Style consoleLogStyle)
				{
					logLabel.Style = consoleLogStyle;
				}
				else
				{
					logLabel.FontSize = 11;
					logLabel.LineBreakMode = LineBreakMode.WordWrap;
					logLabel.InputTransparent = true;
				}

				if (_isDisposed) return;

				TrimConsoleLog();

				ConsoleLogList.Children.Add(logLabel);

				await Task.Delay(ConsoleLogScrollDelayMs);
				if (_isDisposed || LogScrollView == null) return;

				await LogScrollView.ScrollToAsync(ConsoleLogList, ScrollToPosition.End, true);
			}
			catch (ObjectDisposedException)
			{
			}
			catch (InvalidOperationException)
			{
			}
			catch (Exception ex)
			{
				NexusLog.Exception(ex, "[SETUP:UI] Console log append failed");
			}
		}, "PRODUCT_SETUP:CONSOLE_LOG");
	}

	private static FormattedString CreateConsoleLogText(string message)
	{
		var formatted = new FormattedString();
		formatted.Spans.Add(CreateConsoleLogSpan($"[{DateTime.Now:HH:mm:ss}] ", ConsoleLogTimeColor));

		string body = message;
		if (TrySplitConsoleLogTag(message, out string tag, out body))
		{
			formatted.Spans.Add(CreateConsoleLogSpan(tag + " ", ConsoleAccentColor, FontAttributes.Bold));
		}

		formatted.Spans.Add(CreateConsoleLogSpan(body, ConsoleLogBodyColor));
		return formatted;
	}

	private static Span CreateConsoleLogSpan(string text, Color color, FontAttributes fontAttributes = FontAttributes.None)
	{
		return new Span
		{
			Text = text,
			TextColor = color,
			FontAttributes = fontAttributes
		};
	}

	private static bool TrySplitConsoleLogTag(string message, out string tag, out string body)
	{
		tag = string.Empty;
		body = message;
		if (!message.StartsWith("[", StringComparison.Ordinal) || !message.Contains(']'))
		{
			return false;
		}

		int endIdx = message.IndexOf(']');
		tag = message[..(endIdx + 1)];
		body = message[(endIdx + 1)..];
		return true;
	}

	private void TrimConsoleLog()
	{
		if (ConsoleLogList.Children.Count > MaxConsoleLogItems)
		{
			ConsoleLogList.Children.RemoveAt(0);
		}
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
			panel.TranslateToAsync(0, 0, PanelShowLength, Easing.CubicOut),
			ActionBottomBar.FadeToAsync(1, PanelShowLength, Easing.CubicOut),
			ActionBottomBar.TranslateToAsync(0, 0, PanelShowLength, Easing.CubicOut)
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
			panel.TranslateToAsync(0, PanelRevealOffsetY, PanelQuickAnimationLength, Easing.CubicIn),
			ActionBottomBar.FadeToAsync(0, PanelQuickAnimationLength, Easing.CubicIn),
			ActionBottomBar.TranslateToAsync(0, ActionBarHideOffsetY, PanelQuickAnimationLength, Easing.CubicIn)
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
		var settings = SetupSettingsService.Instance.Settings;
		bool isValid = IsValidComfyPath(settings.ComfyPath);

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

		if (VanguardPanel.IsVisible)
		{
			bool allReady = _diagnosticViewModels.All(vm => vm.CurrentHealth == HealthState.Healthy || vm.CurrentHealth == HealthState.OptionalMissing);
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
			var settings = SetupSettingsService.Instance.Settings;
			bool isValid = IsValidComfyPath(settings.ComfyPath);
			if (isValid)
			{
				await TransitionToArchitectInitiationAsync();
			}
		}
		else if (ServerConsolePanel.IsVisible)
		{
			if (_consoleBootActionState == ConsoleBootActionState.Standby)
			{
				await RunServerBootSequenceAsync();
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
			ActionBottomBar.TranslateToAsync(0, ActionBarHideOffsetY, PanelQuickAnimationLength, Easing.CubicIn)
		);

		this.IsVisible = false;
		SetupFinalized?.Invoke(this, EventArgs.Empty);
	}

	private async Task RunRepairSequenceAsync()
	{
		_repairCts?.Cancel();
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
		}

		_currentState = ViewState.Ready;

		try
		{
			await _initiationSequence.RunVanguardAsync(_diagnosticViewModels, token);
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

		// All done
		BackButton.IsEnabled = true;
		BackButton.InputTransparent = false;
		_currentState = ViewState.Ready;
	}

	private async Task WaitForNodeReadyAsync(DiagnosticNodeViewModel vm, CancellationToken token)
	{
		if (vm.CurrentHealth == HealthState.Healthy || vm.CurrentHealth == HealthState.OptionalMissing)
		{
			return;
		}

		var tcs = new TaskCompletionSource();
		using var reg = token.Register(() => tcs.TrySetCanceled());

		void handler(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(DiagnosticNodeViewModel.CurrentHealth)
				&& (vm.CurrentHealth == HealthState.Healthy || vm.CurrentHealth == HealthState.OptionalMissing))
			{
				tcs.TrySetResult();
			}
		}

		vm.PropertyChanged += handler;
		try
		{
			await tcs.Task;
		}
		finally
		{
			vm.PropertyChanged -= handler;
		}
	}

	private void RequestDiagnosticScroll(int index)
	{
		UiThread.TryBeginInvoke(async () =>
		{
			try
			{
				if (_isDisposed || index < 0) return;

				await Task.Delay(250);
				if (_isDisposed) return;

				ScrollActiveDiagnosticListTo(index);
				await Task.Delay(350);
				if (_isDisposed) return;

				ScrollActiveDiagnosticListTo(index);
			}
			catch (ObjectDisposedException)
			{
			}
			catch (InvalidOperationException)
			{
			}
			catch (Exception ex)
			{
				NexusLog.Exception(ex, "[INIT:UI] Diagnostic scroll failed");
			}
		}, "PRODUCT_SETUP:DIAGNOSTIC_SCROLL");
	}

	private void ScrollActiveDiagnosticListTo(int index)
	{
		try
		{
			ScrollView scrollView = _currentContext == ViewContext.Architect
				? ArchitectInitiationScrollView
				: VanguardInitiationScrollView;

			ScrollInitiationSurfaceTo(scrollView, index);
		}
		catch
		{
		}
	}

	private static void ScrollInitiationSurfaceTo(ScrollView scrollView, int index)
	{
		if (index <= 0)
		{
			_ = scrollView.ScrollToAsync(0, 0, true);
			return;
		}

		double targetY = Math.Max(0, index * 128 - 12);
		_ = scrollView.ScrollToAsync(0, targetY, true);
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
			vm.ProgressValue = progress;
			vm.EnvironmentDetails = message;
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
		int totalCount = ArchitectNodes.Count;
		int readyCount = ArchitectNodes.Count(vm => vm.CurrentHealth == HealthState.Healthy);
		ArchitectRequiredStatusLabel.Text = $"{readyCount}/{totalCount} READY";
		ArchitectRequiredStatusLabel.TextColor = readyCount == totalCount && totalCount > 0
			? DiagnosticRequiredReadyColor
			: DiagnosticRequiredPendingColor;
	}

	private void PrepareVanguardChecklist()
	{
		_diagnosticViewModels.Clear();
		VanguardNodes.Clear();
		VanguardOptionalNodes.Clear();
		foreach (var node in _diagnosticCatalog.Nodes)
		{
			var vm = new DiagnosticNodeViewModel(node);
			vm.UpdateState(HealthState.Pending);
			_diagnosticViewModels.Add(vm);
			if (node is IOptionalConfigurableDiagnosticNode)
			{
				VanguardOptionalNodes.Add(vm);
			}
			else
			{
				VanguardNodes.Add(vm);
			}
		}

		BindableLayout.SetItemsSource(DiagnosticNodesList, VanguardNodes);
		BindableLayout.SetItemsSource(VanguardOptionalNodesList, VanguardOptionalNodes);
		UpdateVanguardOptionalSectionVisibility();
		EvaluateOverallReadiness();
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

		ring.CancelAnimations();
		ring.Opacity = 0;
		ring.Rotation = 0;
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

	private static void UpdateLoadingRingInList(Layout list, DiagnosticNodeViewModel vm)
	{
		foreach (var ring in FindVisualChildren<Border>(list))
		{
			if (ring.BindingContext == vm && ring.StyleId == "DiagnosticLoadingRing")
			{
				UpdateDiagnosticLoadingRing(ring, vm.IsLoading);
			}
		}
	}

	private static void UpdateDiagnosticLoadingRing(Border ring, bool isLoading)
	{
		if (isLoading)
		{
			ring.Opacity = 0.95;
			StartDiagnosticLoadingRingRotation(ring);
			return;
		}

		ring.CancelAnimations();
		ring.Opacity = 0;
		ring.Rotation = 0;
	}

	private static void StartDiagnosticLoadingRingRotation(Border ring)
	{
		ring.CancelAnimations();
		ring.RotateToAsync(360, DiagnosticLoadingRingRotateLength, Easing.Linear).ContinueWith(_ =>
		{
			UiThread.TryBeginInvoke(() =>
			{
				if (ring.Opacity <= 0) return;

				ring.Rotation = 0;
				StartDiagnosticLoadingRingRotation(ring);
			}, "PRODUCT_SETUP:DIAGNOSTIC_RING_ANIMATION");
		});
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
		if (_isDiagnosticScrollAdjusting) return;
		if (sender is not View { BindingContext: DiagnosticNodeViewModel nodeVm } editTrigger) return;
		if (ShouldKeepInlineActionsVisible(nodeVm)) return;
		if (!CanReconfigureReadyNode(nodeVm)) return;
		if (nodeVm.Node is not IConfigurableDiagnosticNode configurableNode) return;

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
		await ScrollDiagnosticNodeIntoViewAsync(editTrigger);
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
		if (_isDiagnosticScrollAdjusting) return;
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

		if (sender is View retryTrigger)
		{
			await ScrollDiagnosticNodeIntoViewAsync(retryTrigger);
		}
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

	private async Task ScrollDiagnosticNodeIntoViewAsync(View nodeContainer)
	{
		const int ScrollLayoutSettleDelayMs = 60;
		const int ScrollInputReleaseDelayMs = 120;
		const int ScrollAnimationTimeoutMs = 700;

		ScrollView? scrollView = _currentContext switch
		{
			ViewContext.Vanguard or ViewContext.Repairing => VanguardInitiationScrollView,
			ViewContext.Architect => ArchitectInitiationScrollView,
			_ => null
		};

		if (scrollView == null) return;

		_isDiagnosticScrollAdjusting = true;
		try
		{
			await Task.Delay(ScrollLayoutSettleDelayMs);
			Task scrollTask = scrollView.ScrollToAsync(nodeContainer, ScrollToPosition.MakeVisible, true);
			Task timeoutTask = Task.Delay(ScrollAnimationTimeoutMs);
			await Task.WhenAny(scrollTask, timeoutTask);
			await Task.Delay(ScrollInputReleaseDelayMs);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETUP:UI] Diagnostic card scroll adjustment failed");
		}
		finally
		{
			_isDiagnosticScrollAdjusting = false;
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

			try
			{
				await Task.Delay(180);
				await ScrollViewToBottomAsync(ArchitectInitiationScrollView);
			}
			catch (Exception ex)
			{
				NexusLog.Exception(ex, "[SETUP:UI] Optional section scroll adjustment failed");
			}
		});
	}

	private static async Task ScrollViewToBottomAsync(ScrollView scrollView)
	{
		double maxScrollY = Math.Max(0, scrollView.ContentSize.Height - scrollView.Height);
		await scrollView.ScrollToAsync(0, maxScrollY, true);
	}

	private static bool CanReconfigureReadyNode(DiagnosticNodeViewModel nodeVm)
		=> nodeVm.CurrentHealth is HealthState.Healthy or HealthState.OptionalMissing
			&& nodeVm.Node is IConfigurableDiagnosticNode;

	private static bool ShouldKeepInlineActionsVisible(DiagnosticNodeViewModel nodeVm)
		=> nodeVm.Node.NodeId == "model-library"
			&& nodeVm.CurrentHealth is (HealthState.Healthy or HealthState.OptionalMissing);

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
		nodeVm.HighlightBackground = "Transparent";
		nodeVm.InteractionOverlayOpacity = isActive ? 1.0 : 0.0;
	}

	private void SetDiagnosticActionNavigationBlocked(bool isBlocked)
	{
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

		_isDiagnosticActionRunning = true;
		SetDiagnosticActionNavigationBlocked(true);
		try
		{
			vm.IsLoading = true;
		SetDiagnosticNodeInteraction(vm, false);
		vm.Actions.Clear();
		vm.NotifyActionsChanged();

		if (confNode is IFolderSelectionDiagnosticNode folderNode
			&& folderNode.RequiresFolderSelection(actionId))
		{
			var folderResult = await PlatformManager.Current.FilePicker.PickFolderAsync(
				GetDiagnosticFolderPickerTitle(confNode));
			if (!folderResult.IsSupported || !folderResult.IsSuccess || string.IsNullOrWhiteSpace(folderResult.Value))
			{
				vm.IsLoading = false;
				PopulateInlineActions(vm, confNode);
				return;
			}

			RecoveryResult selectionResult = folderNode.ApplySelectedFolder(actionId, folderResult.Value);
			if (!selectionResult.IsSuccess)
			{
				vm.UpdateState(HealthState.OptionalMissing);
				vm.EnvironmentDetails = selectionResult.Message;
				PopulateInlineActions(vm, confNode);
				return;
			}
		}
		else
		{
			confNode.SelectOption(actionId);
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

				if (confNode.NodeId == DiagnosticNodeComfyCore)
				{
					var folderPicker = new Windows.Storage.Pickers.FolderPicker();
					folderPicker.FileTypeFilter.Add("*");
					WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
					var folder = await folderPicker.PickSingleFolderAsync();
					if (folder != null)
					{
						SetupSettingsService.Instance.Settings.ComfyPath = folder.Path;
					}
					else { vm.IsLoading = false; PopulateInlineActions(vm, confNode); return; }
				}
				else
				{
					var file = await picker.PickSingleFileAsync();
					if (file != null)
					{
						if (confNode.NodeId == DiagnosticNodeGitCore) SetupSettingsService.Instance.Settings.GitPath = file.Path;
						else if (confNode.NodeId == DiagnosticNodePythonEngine) SetupSettingsService.Instance.Settings.PythonPath = file.Path;
					}
					else { vm.IsLoading = false; PopulateInlineActions(vm, confNode); return; }
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
					vm.ProgressValue = p;
					vm.EnvironmentDetails = msg;
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
					ComfyInstallService.Instance.OnProgress = originalOnProgress;
					EvaluateCurrentInitiationReadiness();
					return;
				}
			}
			catch (OperationCanceledException) { }

			ComfyInstallService.Instance.OnProgress = originalOnProgress;
			vm.ShowProgress = false;
		}

		await CheckSingleNodeHealthAsync(vm);

		// Final sync after recovery
		vm.EnvironmentDetails = confNode.EnvironmentDetails;
		vm.EnvironmentPath = confNode.EnvironmentPath;

		DiagnosticNodesList.InputTransparent = false;
		VanguardOptionalNodesList.InputTransparent = false;
		EvaluateCurrentInitiationReadiness();
		}
		finally
		{
			_isDiagnosticActionRunning = false;
			SetDiagnosticActionNavigationBlocked(false);
		}
	}

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
		if (_diagnosticViewModels.Count == 0) return;

		bool allReady = _diagnosticViewModels.All(vm => vm.CurrentHealth == HealthState.Healthy || vm.CurrentHealth == HealthState.OptionalMissing);

		if (_currentContext == ViewContext.Vanguard || _currentContext == ViewContext.Repairing)
		{
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
		ActionBottomBar.Opacity = 0;
		ActionBottomBar.IsVisible = true;
		ActionBottomBar.InputTransparent = false;
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

	private void StartPrimaryButtonPulse()
	{
		if (_isDisposed) return;
		if (_pulseCts is { IsCancellationRequested: false }) return;

		_pulseCts = new CancellationTokenSource();
		var token = _pulseCts.Token;

		Task.Run(async () =>
		{
			while (!token.IsCancellationRequested)
			{
				try
				{
					await InvokeOnMainThreadSafeAsync(async () =>
					{
						if (_isDisposed || token.IsCancellationRequested) return;

						await PrimaryActionPulseGlow.FadeToAsync(PrimaryPulseHighOpacity, PulseAnimationLength, Easing.CubicInOut);
						if (_isDisposed || token.IsCancellationRequested) return;

						await PrimaryActionPulseGlow.FadeToAsync(PrimaryPulseLowOpacity, PulseAnimationLength, Easing.CubicInOut);
					});
				}
				catch (ObjectDisposedException)
				{
					break;
				}
				catch (InvalidOperationException)
				{
					break;
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception ex)
				{
					NexusLog.Exception(ex, "[SETUP:UI] Primary action pulse failed");
					break;
				}
			}
		}, token);
	}

	private void StopPrimaryButtonPulse()
	{
		_pulseCts?.Cancel();
		_pulseCts = null;
		if (_isDisposed) return;

		PrimaryActionPulseGlow.CancelAnimations();
		PrimaryActionPulseGlow.Opacity = 0;
	}

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

	private void OnConsoleRecoverBootHovered(object? sender, PointerEventArgs e)
	{
		if (_consoleBootActionState is not (ConsoleBootActionState.Standby or ConsoleBootActionState.Failed)) return;

		ConsoleRecoverBootButton.BackgroundColor = ConsoleRecoverHoverColor;
	}

	private void OnConsoleRecoverBootUnhovered(object? sender, PointerEventArgs e)
	{
		if (_consoleBootActionState is not (ConsoleBootActionState.Standby or ConsoleBootActionState.Failed)) return;

		ConsoleRecoverBootButton.BackgroundColor = ConsoleRecoverNormalColor;
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
		await target.ScaleToAsync(ImpactCardScale, ImpactCardScaleOutLength, Easing.CubicOut);
		_ = target.ScaleToAsync(1.0, ImpactCardScaleInLength, Easing.CubicIn);
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

	private async Task FloatingAnimationLoop(View target, double amplitude, double duration)
	{
		while (true)
		{
			if (target == null) break;
			await target.TranslateToAsync(0, -amplitude, (uint)(duration / 2), Easing.SinInOut);
			await target.TranslateToAsync(0, amplitude, (uint)(duration / 2), Easing.SinInOut);
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
