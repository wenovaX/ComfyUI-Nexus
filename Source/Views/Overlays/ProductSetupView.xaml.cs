using System.Collections.ObjectModel;
using System.Linq;
using ComfyUI_Nexus.Configuration;
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
	private readonly NexusAppManager _appManager;
	private SetupSettingsService SettingsService => _appManager.Settings;
	private readonly SetupSequenceOrchestrator _setupSequence;
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
	private readonly NexusOperationController _latestOperations;
	private CancellationTokenSource? _repairCts;
	private CancellationTokenSource? _diagnosticActionCts;
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
		_appManager = NexusAppManager.Instance;
		_latestOperations = new NexusOperationController("product-setup", _appManager.BackgroundWorkers);
		_setupSequence = new SetupSequenceOrchestrator(
			_appManager.Tooling,
			_appManager.ComfyInstall,
			_appManager.ServerProcesses);
		InitializeComponent();
		_motion = new NexusMotionController("product-setup", "SETUP:UI", Dispatcher);
		_crossroadsAmbientClip = new NexusAnimatedWebpClip(_motion, _appManager.AnimatedWebpFrames, CrossroadsAmbientLoop, CrossroadsAmbientWebpAnimationName, NexusAnimatedWebpCacheCatalog.SetupCrossroadsAmbient);
		_welcomeTitleClip = new NexusAnimatedWebpClip(_motion, _appManager.AnimatedWebpFrames, WelcomeTitleAnimation, WelcomeTitleWebpAnimationName, NexusAnimatedWebpCacheCatalog.SetupWelcomeTitle);
		_vanguardIconClip = new NexusAnimatedWebpClip(_motion, _appManager.AnimatedWebpFrames, VanguardModeIcon, VanguardIconWebpAnimationName, NexusAnimatedWebpCacheCatalog.SetupVanguardIcon);
		_architectIconClip = new NexusAnimatedWebpClip(_motion, _appManager.AnimatedWebpFrames, ArchitectModeIcon, ArchitectIconWebpAnimationName, NexusAnimatedWebpCacheCatalog.SetupArchitectIcon);
		_consoleReadyPulseClip = new NexusAnimatedWebpClip(_motion, _appManager.AnimatedWebpFrames, ConsoleBootPulseSurface, ConsoleReadyPulseWebpAnimationName, NexusAnimatedWebpCacheCatalog.SetupConsoleReadyPulse);
		_consoleBootingPulseClip = new NexusAnimatedWebpClip(_motion, _appManager.AnimatedWebpFrames, ConsoleBootPulseSurface, ConsoleBootingPulseWebpAnimationName, NexusAnimatedWebpCacheCatalog.SetupConsoleBootingPulse);
		_consoleStatusBootingPulseClip = new NexusAnimatedWebpClip(_motion, _appManager.AnimatedWebpFrames, ConsoleStatusPulseSurface, ConsoleStatusBootingPulseWebpAnimationName, NexusAnimatedWebpCacheCatalog.SetupConsoleStatusBootingPulse);
		_primaryActionReadyPulseClip = new NexusAnimatedWebpClip(_motion, _appManager.AnimatedWebpFrames, PrimaryActionPulseSurface, PrimaryActionReadyPulseWebpAnimationName, NexusAnimatedWebpCacheCatalog.SetupPrimaryActionReadyPulse);
		_background = new SetupBackgroundController(
			_motion,
			_appManager.AnimatedWebpFrames,
			BackgroundLayer,
			CrossroadsAmbientLoop,
			VanguardGlow,
			ArchitectGlow,
			VanguardSelectionBurst,
			ArchitectSelectionBurst,
			_vanguardIconClip,
			_architectIconClip);
		_initiationSequence = new InitiationSequenceRunner(
			_appManager.Tooling,
			_appManager.ComfyInstall,
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
		_diagnosticActionCts?.Cancel();
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
			_ = _appManager.ComfyInstall;
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
		SettingsService.UseLocalRuntime();

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

		var settings = SettingsService.Settings;
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
		var vm = new DiagnosticNodeViewModel(node, _appManager.Paths);
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
			Type type when type == typeof(GitDiagnosticNode) => new GitDiagnosticNode(_appManager.ComfyInstall),
			Type type when type == typeof(PythonDiagnosticNode) => new PythonDiagnosticNode(_appManager.ComfyInstall),
			Type type when type == typeof(ComfyCoreDiagnosticNode) => new ComfyCoreDiagnosticNode(_appManager.ComfyInstall),
			Type type when type == typeof(BaseResourceDiagnosticNode) => new BaseResourceDiagnosticNode(_appManager.ComfyInstall),
			Type type when type == typeof(ManagerExtensionDiagnosticNode) => new ManagerExtensionDiagnosticNode(_appManager.ComfyInstall),
			Type type when type == typeof(PythonEnvironmentDiagnosticNode) => new PythonEnvironmentDiagnosticNode(_appManager.ComfyInstall),
			Type type when type == typeof(PipCacheDiagnosticNode) => new PipCacheDiagnosticNode(_appManager.Settings),
			Type type when type == typeof(ModelLibraryDiagnosticNode) => new ModelLibraryDiagnosticNode(true, _appManager.Settings),
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
			step.ViewModel.EnvironmentPath = _appManager.Paths.ActiveVenvPath;
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
				vm.SecondaryEnvironmentPath = configurableNode.SecondaryEnvironmentPath;
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
		var settings = SettingsService.Settings;
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
		if (ServerConsolePanel.IsVisible && _consoleBootActionState == ConsoleBootActionState.Online) return;
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
			SettingsService.UseExistingComfyPath(path);
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
				SettingsService.UseExistingComfyPath(_architectCandidateComfyPath);
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

		_animationCacheAcquireTask ??= _appManager.AnimatedWebpFrames.AcquireAsync(NexusAnimatedWebpCacheGroup.Setup);
		_animationCacheLease = await _animationCacheAcquireTask.ConfigureAwait(false);
	}

	private void ReleaseAnimationCache()
	{
		_animationCacheLease?.Dispose();
		_animationCacheLease = null;
		_animationCacheAcquireTask = null;
	}

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
