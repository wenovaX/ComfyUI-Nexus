using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Services;
#if WINDOWS
#endif

namespace ComfyUI_Nexus.Views.Overlays;

public partial class LoadingOverlayView : ContentView
{
	private const double LoadingProgressWidth = 360;
	private const double DisabledConfigEditOpacity = 0.55;
	private const double ServerBootStatusRowOpenSetupSpacing = 10;
	private const uint ServerBootPanelFadeLength = 360;
	private const string TextKeyPrefix = "views.overlays.loading_overlay_view.";
	private static readonly Color NexusAccentColor = NexusColors.Accent;
	private static readonly Color ServerBootWarningColor = NexusColors.Warning;
	private static readonly Color ServerBootFailedColor = NexusColors.Danger;
	private static readonly Color ServerBootOnlineColor = NexusColors.Success;
	private static readonly Color SetupActionNormalColor = NexusColors.SurfaceSubtle;
	private static readonly Color SetupActionHoverColor = Color.FromArgb("#1831d8ff");
	private static readonly Color ServerBootSetupHoverColor = Color.FromArgb("#18ffffff");
	private static readonly Color ServerBootRecoverNormalColor = NexusColors.WarningSoft;
	private static readonly Color ServerBootRecoverHoverColor = NexusColors.WarningHover;
	private static readonly Color ServerBootPrimaryNormalColor = NexusColors.AccentSoft;
	private static readonly Color ServerBootPrimaryHoverColor = NexusColors.AccentHoverSoft;
	private static readonly Color ServerBootPrimaryFailedNormalColor = NexusColors.DangerSoft;
	private static readonly Color ServerBootPrimaryFailedHoverColor = NexusColors.DangerHover;
	private static readonly Color ServerBootConfigNormalColor = NexusColors.AccentWash;
	private static readonly Color ServerBootOpenLogNormalColor = NexusColors.SurfaceDarkTranslucent;
	private static readonly Color ServerBootOpenLogHoverColor = NexusColors.SurfaceRaised;
	private static readonly Color ServerBootOpenLogTextColor = NexusColors.Accent;
	private static readonly Color ServerBootOpenLogHoverTextColor = NexusColors.TextPrimary;

	private enum ServerBootVisualState
	{
		Idle,
		Booting,
		WaitingForPort,
		Failed,
		Online
	}

	private enum ServerBootAnimationState
	{
		Idle,
		Booting,
		Success,
		Failed,
	}

	private readonly record struct ServerBootVisualSpec(
		string StateText,
		Color Color,
		double GlowOpacity,
		bool PrimaryVisible,
		bool SetupVisible,
		string PrimaryText);

	private INexusAppEntry? _nexusAppEntry;
	private bool _isProductSetupFinalizing;
	private bool _isOverlayUnloading;
	private bool _isMaintenanceRecoveryMode;
	private ServerBootVisualState _serverBootVisualState = ServerBootVisualState.Idle;
	private Task? _productSetupRevealPreparationTask;
	private readonly NexusMotionController _serverBootMotion;
	private readonly NexusVisualStateAnimator<ServerBootAnimationState> _serverBootAnimator;
	private Func<ServerLifecycleRequest, Task<ServerLifecycleResult>>? _runServerLifecycleAsync;

	internal event EventHandler? SelectCoreRequested;
	internal event EventHandler? RetryRequested;
	internal event EventHandler? ServerBootSetupRequested;
	internal event EventHandler<TappedEventArgs>? GetComfyRequested;
	internal event EventHandler<TappedEventArgs>? GetGitHubRequested;

	private static string Text(string key)
		=> LocalizationManager.Text(TextKeyPrefix + key);

	private static string CommonText(string key)
		=> LocalizationManager.Text("common." + key);

	public LoadingOverlayView()
	{
		InitializeComponent();
		_serverBootMotion = new NexusMotionController("loading-server-boot", "Loading.ServerBoot", Dispatcher);
		_serverBootAnimator = new NexusVisualStateAnimator<ServerBootAnimationState>(
			"loading-server-boot",
			_serverBootMotion,
			CanRepeatServerBootAnimation,
			[
				new NexusVisualStateAnimation("idle", ServerBootAnimationSurface, NexusAnimatedWebpCacheCatalog.ServerBootIdle, NexusVisualPlaybackKind.Loop, Preload: true),
				new NexusVisualStateAnimation("booting", ServerBootAnimationSurface, NexusAnimatedWebpCacheCatalog.ServerBooting, NexusVisualPlaybackKind.Loop, Preload: true),
				new NexusVisualStateAnimation("success", ServerBootAnimationSurface, NexusAnimatedWebpCacheCatalog.ServerBootSuccess, NexusVisualPlaybackKind.OneShot, FinalFrameBehavior: NexusAnimatedWebpFinalFrameBehavior.HoldFinalFrame, Preload: true),
				new NexusVisualStateAnimation("failed", ServerBootAnimationSurface, NexusAnimatedWebpCacheCatalog.ServerBootFailed, NexusVisualPlaybackKind.OneShot, FinalFrameBehavior: NexusAnimatedWebpFinalFrameBehavior.HoldFinalFrame, Preload: true),
			],
			new Dictionary<ServerBootAnimationState, IReadOnlyCollection<string>>
			{
				[ServerBootAnimationState.Idle] = ["idle"],
				[ServerBootAnimationState.Booting] = ["booting"],
				[ServerBootAnimationState.Success] = ["success"],
				[ServerBootAnimationState.Failed] = ["failed"],
			});
		ProductSetupControl.SetupFinalized += OnProductSetupFinalized;
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
	}

	internal void SetStatus(string text, Color textColor)
	{
		LoadingStatusLabel.Text = text;
		LoadingStatusLabel.TextColor = textColor;
	}

	internal void ApplyDisplay(LoadingOverlayDisplay display)
	{
		if (display.State != LoadingOverlayState.Hide)
		{
			ShowOnlySurface(LoadingStackLayout);
		}

		LoadingTitleLabel.Text = display.Title;
		LoadingTitleLabel.TextColor = display.AccentColor;
		LoadingTitleLabel.IsVisible = !string.IsNullOrWhiteSpace(display.Title);

		LoadingDescriptionLabel.Text = display.Description;
		LoadingDescriptionLabel.IsVisible = !string.IsNullOrWhiteSpace(display.Description);

		SetStatus(display.Status, display.AccentColor);

		if (!string.IsNullOrWhiteSpace(display.CenterGlyph))
		{
			SetSuccessGlyph(display.CenterGlyph, display.AccentColor);
			SetSuccessGlyphVisualState(1, 1, 0);
		}
		else if (display.State is LoadingOverlayState.Show or LoadingOverlayState.Hold or LoadingOverlayState.Message)
		{
			SuccessIconLabel.Opacity = 0;
			SuccessIconLabel.Scale = 0;
			SuccessIconHost.Opacity = 0;
			SuccessIconHost.Scale = 0;
		}

		SetProgress(display.Progress, display.AccentColor);
		bool isError = display.State == LoadingOverlayState.Error;
		SetLoadingRetryActionVisible(isError);
		SetLoadingSetupActionVisible(isError);
	}

	internal void ClearDisplayDetails()
	{
		LoadingTitleLabel.IsVisible = false;
		LoadingDescriptionLabel.IsVisible = false;
		LoadingProgressHost.IsVisible = false;
		SetLoadingRetryActionVisible(false);
		SetLoadingSetupActionVisible(false);
	}

	internal void SetProgress(double? progress, Color accentColor)
	{
		if (progress is null || progress.Value < 0 || progress.Value > 1)
		{
			LoadingProgressHost.IsVisible = false;
			LoadingProgressFill.WidthRequest = 0;
			return;
		}

		LoadingProgressHost.IsVisible = true;
		LoadingProgressFill.Color = accentColor;
		LoadingProgressFill.WidthRequest = progress.Value * LoadingProgressWidth;
	}

	internal void SetConfigSurfaceState(bool isVisible, double opacity, bool inputTransparent)
	{
		LoadingOverlayGrid.IsVisible = isVisible;
		LoadingOverlayGrid.Opacity = opacity;
		LoadingOverlayGrid.InputTransparent = inputTransparent;
	}

	private void ShowOverlayRoot()
	{
		LoadingOverlayGrid.IsVisible = true;
		LoadingOverlayGrid.Opacity = 1;
		LoadingOverlayGrid.InputTransparent = false;
	}

	private void ShowOnlySurface(View? activeSurface)
	{
		if (ServerBootLayout.IsVisible && !ReferenceEquals(activeSurface, ServerBootLayout))
		{
			StopServerBootAnimations();
		}

		LoadingStackLayout.IsVisible = ReferenceEquals(activeSurface, LoadingStackLayout);
		ConfigStackLayout.IsVisible = ReferenceEquals(activeSurface, ConfigStackLayout);
		ProductSetupControl.IsVisible = ReferenceEquals(activeSurface, ProductSetupControl);
		ServerBootLayout.IsVisible = ReferenceEquals(activeSurface, ServerBootLayout);
		if (activeSurface is null)
		{
			XamlLifetimeDiagnostics.RemoveSurface("loading");
		}
		else
		{
			XamlLifetimeDiagnostics.RecordSurface("loading", activeSurface.GetType().Name);
		}

	}

	internal void ResetVisualState(string successText, Color successColor)
	{
		LoadingProcessImage.Opacity = 0;

		SuccessIconLabel.Text = successText;
		SuccessIconLabel.TextColor = successColor;
		SuccessIconLabel.Opacity = 0;
		SuccessIconLabel.Scale = 0;
		SuccessIconLabel.Rotation = -10;
		SuccessIconHost.Opacity = 0;
		SuccessIconHost.Scale = 0;
		SuccessIconHost.Rotation = -10;
		SuccessAnimationImage.Opacity = 0;

		LoadingStatusLabel.Scale = 1;
		LoadingStatusLabel.Opacity = 0.95;
		LoadingStatusLabel.TranslationY = 0;
		ClearDisplayDetails();
	}

	internal void SetSuccessGlyph(string text, Color color)
	{
		bool useReadyImage = string.Equals(text, "OK", StringComparison.OrdinalIgnoreCase);
		SuccessIconHost.IsVisible = useReadyImage;
		SuccessIconLabel.IsVisible = !useReadyImage;

		SuccessIconLabel.Text = text;
		SuccessIconLabel.TextColor = color;
	}

	internal void SetSuccessGlyphVisualState(double opacity, double scale, double rotation)
	{
		SuccessIconLabel.Opacity = opacity;
		SuccessIconLabel.Scale = scale;
		SuccessIconLabel.Rotation = rotation;
		SuccessIconHost.Opacity = opacity;
		SuccessIconHost.Scale = scale;
		SuccessIconHost.Rotation = rotation;
	}

	internal void SetMode(bool showConfig)
	{
		ShowOnlySurface(showConfig ? ConfigStackLayout : LoadingStackLayout);
	}

	internal Task PrepareProductSetupForRevealAsync()
	{
		if (_productSetupRevealPreparationTask is { IsCompleted: false } preparation)
		{
			return preparation;
		}

		_productSetupRevealPreparationTask = PrepareProductSetupForRevealCoreAsync();
		return _productSetupRevealPreparationTask;
	}

	internal Task PrewarmProductSetupAnimationsAsync()
		=> ProductSetupControl.PrepareAnimationCacheAsync();

	internal async Task RevealPreparedProductSetupAsync()
	{
		NexusLog.Trace("[LOADING] Product setup reveal preparation starting.");
		_isMaintenanceRecoveryMode = false;
		ShowOverlayRoot();
		_isProductSetupFinalizing = false;
		NexusLog.Trace("[LOADING] Product setup reset requested.");
		ProductSetupControl.ResetFlow();
		ProductSetupControl.PrepareForLifecycleHandoff();
		ShowOnlySurface(ProductSetupControl);
		await ProductSetupControl.ActivateLifecycleAsync();
		NexusLog.Trace("[LOADING] Product setup reveal preparation completed.");
	}

	private async Task PrepareProductSetupForRevealCoreAsync()
	{
		try
		{
			await PrewarmProductSetupAnimationsAsync();
			await RevealPreparedProductSetupAsync();
		}
		finally
		{
			_productSetupRevealPreparationTask = null;
		}
	}

	internal void EnterServerBoot(ServerBootEntryRequest request)
	{
		NexusLog.Info($"[LOADING] Server boot entry requested. kind={request.Kind}");
		ShowOverlayRoot();
		ShowOnlySurface(null);
		_isProductSetupFinalizing = false;

		if (!CanUseOverlay())
		{
			return;
		}

		switch (request.Kind)
		{
			case ServerBootEntryKind.Idle:
			case ServerBootEntryKind.Restart:
				_isMaintenanceRecoveryMode = false;
				ShowServerBootPanel(request.Kind);
				break;
			case ServerBootEntryKind.ResumePending:
				_isMaintenanceRecoveryMode = false;
				ShowServerBootPanel(request.Kind);
				BeginPendingServerBoot();
				break;
			case ServerBootEntryKind.MaintenanceRecovery:
				_isMaintenanceRecoveryMode = true;
				ShowMaintenanceRecoveryPanel();
				BeginMaintenanceRecovery();
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "The server boot entry kind is not supported.");
		}
	}

	private void BeginPendingServerBoot()
		=> _ = BeginPendingServerBootAsync();

	private async Task BeginPendingServerBootAsync()
	{
		try
		{
			await StartServerBootFromLoadingAsync(resumePendingProcess: true);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Exception(ex, "[LOADING] Pending server boot failed");
			if (CanUseOverlay() && ServerBootLayout.IsVisible)
			{
				ApplyServerBootVisualState(ServerBootVisualState.Failed, ex.Message);
			}
		}
	}

	private void BeginMaintenanceRecovery()
		=> _ = BeginMaintenanceRecoveryAsync();

	private async Task BeginMaintenanceRecoveryAsync()
	{
		try
		{
			await StartMaintenanceRecoveryAsync();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Exception(ex, "[LOADING] Maintenance recovery failed");
			if (CanUseOverlay() && ServerBootLayout.IsVisible)
			{
				ApplyServerBootVisualState(ServerBootVisualState.Failed, ex.Message);
			}
		}
	}

	internal void SetNexusAppEntry(INexusAppEntry appEntry)
	{
		_nexusAppEntry = appEntry;
	}

	internal void SetServerLifecycleRunner(Func<ServerLifecycleRequest, Task<ServerLifecycleResult>> runner)
	{
		_runServerLifecycleAsync = runner;
	}

	internal void AppendServerLifecycleLog(string message)
		=> AddServerBootLog(message);

	internal void ApplyServerLifecycleSnapshot(ServerLifecycleSnapshot snapshot)
	{
		if (!CanUseOverlay()) return;
		if (!ServerBootLayout.IsVisible)
		{
			return;
		}

		switch (snapshot.State)
		{
			case ServerLifecycleState.QuiescingServices:
			case ServerLifecycleState.ServicesEnded:
			case ServerLifecycleState.StoppingServer:
				ApplyServerBootVisualState(ServerBootVisualState.Booting, Text("stopping_runtime_processes"));
				break;
			case ServerLifecycleState.VerifyingServerStopped:
				ApplyServerBootVisualState(ServerBootVisualState.WaitingForPort, Text("backend_alive_waiting_for_port"));
				break;
			case ServerLifecycleState.RunningMaintenance:
				ApplyServerBootVisualState(ServerBootVisualState.Booting, Text("preparing_runtime_cleanup_recovery"));
				break;
			case ServerLifecycleState.BootingServer:
				ApplyServerBootVisualState(ServerBootVisualState.Booting, Text("starting_backend_process"));
				break;
			case ServerLifecycleState.ServerReady:
				ApplyServerBootVisualState(ServerBootVisualState.Online, Text("backend_ready_handoff"));
				break;
			case ServerLifecycleState.Failed:
				AddServerBootLog($"[ERROR] {snapshot.Detail}");
				ApplyServerBootVisualState(ServerBootVisualState.Failed, snapshot.Detail);
				break;
		}
	}

	private void ShowServerBootPanel(ServerBootEntryKind entryKind)
	{
		bool resumePendingProcess = entryKind == ServerBootEntryKind.ResumePending;
		ResetServerBootPanelAnimations();
		ShowOnlySurface(ServerBootLayout);
		ServerBootLayout.Opacity = 1;
		ServerBootTitleLabel.Text = Text("nexus_server_boot");

		ServerBootLogTail.Clear();
		UpdateServerBootEndpoint();
		if (resumePendingProcess)
		{
			ApplyServerBootVisualState(ServerBootVisualState.WaitingForPort, Text("backend_process_already_starting"));
			AddServerBootLog("[ROUTE] Existing setup detected. Backend service is still starting.");
			AddServerBootLog("[SYSTEM] Reattaching to the active ComfyUI server launch.");
		}
		else
		{
			ApplyServerBootVisualState(ServerBootVisualState.Idle, Text("backend_offline_engage"));
			AddServerBootLog(entryKind == ServerBootEntryKind.Restart
				? "[RESTART] Server shutdown is verified. Ready to launch a fresh ComfyUI process."
				: "[ROUTE] Existing setup detected. Backend service is offline.");
			AddServerBootLog("[SYSTEM] Ready to engage ComfyUI server process.");
		}

	}

	private void ShowMaintenanceRecoveryPanel()
	{
		ResetServerBootPanelAnimations();
		ShowOnlySurface(ServerBootLayout);
		ServerBootLayout.Opacity = 1;
		ServerBootTitleLabel.Text = Text("nexus_maintenance");

		ServerBootLogTail.Clear();
		UpdateServerBootEndpoint();
		ApplyServerBootVisualState(ServerBootVisualState.Booting, Text("preparing_runtime_cleanup_recovery"));
		AddServerBootLog("[MAINTENANCE] Runtime purge was requested or interrupted.");
		AddServerBootLog("[MAINTENANCE] Preparing cleanup before setup resumes.");

	}

	private void ResetServerBootPanelAnimations()
	{
		SafeAnimation.AbortAnimation(ServerBootLayout, "FadeTo", "Loading.ServerBoot");
		_serverBootAnimator.Reset();
		ServerBootAnimationSurface.Source = "server_boot_idle.webp";
		ServerBootAnimationSurface.Opacity = 1;
	}

	private async void OnServerBootPrimaryActionClicked(object? sender, TappedEventArgs e)
	{
		if (_serverBootVisualState is not (ServerBootVisualState.Idle or ServerBootVisualState.Failed))
		{
			return;
		}

		if (_isMaintenanceRecoveryMode)
		{
			await StartMaintenanceRecoveryAsync();
			return;
		}

		await StartServerBootFromLoadingAsync(resumePendingProcess: false);
	}

	private async void OnServerBootRecoverActionClicked(object? sender, TappedEventArgs e)
	{
		if (_serverBootVisualState is not (ServerBootVisualState.Idle or ServerBootVisualState.Failed))
		{
			return;
		}

		var page = GetPromptPage();
		if (page != null)
		{
			string repairTarget = RuntimeRepairTarget.GetDisplay();
			bool confirmed = await page.DisplayAlertAsync(
				Text("recover_runtime_and_boot_title"),
				string.Format(Text("recover_runtime_and_boot_message"), repairTarget),
				CommonText("recover_boot"),
				CommonText("cancel"));
			if (!confirmed) return;
		}

		await StartServerBootFromLoadingAsync(resumePendingProcess: false, repairRuntimeBeforeBoot: true);
	}

	private async void OnServerBootOpenLogTapped(object? sender, TappedEventArgs e)
	{
		string logsPath = ComfyInstallService.GetLocalRuntimePath("Logs");
		string logPath = System.IO.Path.Combine(logsPath, SessionLogPaths.NexusLatestFileName);
		string targetPath = File.Exists(logPath) ? logPath : logsPath;
		var result = await PlatformManager.Current.Shell.OpenPathAsync(targetPath);
		if (!result.IsSuccess)
		{
			NexusLog.Warning($"[BOOT] Failed to open Nexus boot log path: {result.Message}");
		}
	}

	private void OnServerBootOpenLogHovered(object? sender, PointerEventArgs e)
	{
		ServerBootOpenLogButton.BackgroundColor = ServerBootOpenLogHoverColor;
		ServerBootOpenLogLabel.TextColor = ServerBootOpenLogHoverTextColor;
	}

	private void OnServerBootOpenLogUnhovered(object? sender, PointerEventArgs e)
	{
		ServerBootOpenLogButton.BackgroundColor = ServerBootOpenLogNormalColor;
		ServerBootOpenLogLabel.TextColor = ServerBootOpenLogTextColor;
	}

	private async void OnServerBootSetupClicked(object? sender, TappedEventArgs e)
	{
		if (_isMaintenanceRecoveryMode)
		{
			return;
		}

		if (_serverBootVisualState != ServerBootVisualState.Idle)
		{
			return;
		}

		if (ServerBootSetupRequested != null)
		{
			NexusLog.Info("[SETUP_ROUTE] Server boot setup click accepted.");
			ServerBootSetupRequested.Invoke(this, EventArgs.Empty);
			return;
		}

		await PrepareProductSetupForRevealAsync();
	}

	private async void OnLoadingSetupActionClicked(object? sender, TappedEventArgs e)
	{
		if (_isMaintenanceRecoveryMode)
		{
			return;
		}

		NexusLog.Info("[LOADING] Setup handoff requested from loading error.");
		await PrepareProductSetupForRevealAsync();
	}

	private void OnLoadingRetryActionClicked(object? sender, TappedEventArgs e)
	{
		if (LoadingRetryActionButton.InputTransparent)
		{
			return;
		}

		NexusLog.Info("[LOADING] Retry requested from loading error.");
		RetryRequested?.Invoke(this, EventArgs.Empty);
	}

	private void OnLoadingSetupActionHovered(object? sender, PointerEventArgs e)
	{
		if (sender is not Border border || border.InputTransparent) return;

		border.BackgroundColor = SetupActionHoverColor;
	}

	private void OnLoadingSetupActionUnhovered(object? sender, PointerEventArgs e)
	{
		if (sender is not Border border) return;

		border.BackgroundColor = SetupActionNormalColor;
	}

	private void SetLoadingSetupActionVisible(bool isVisible)
	{
		if (_isMaintenanceRecoveryMode)
		{
			isVisible = false;
		}

		LoadingSetupActionButton.IsVisible = isVisible;
		LoadingSetupActionButton.InputTransparent = !isVisible;
		LoadingSetupActionButton.Opacity = isVisible ? 1 : 0;
	}

	private void SetLoadingRetryActionVisible(bool isVisible)
	{
		LoadingRetryActionButton.IsVisible = isVisible;
		LoadingRetryActionButton.InputTransparent = !isVisible;
		LoadingRetryActionButton.Opacity = isVisible ? 1 : 0;
	}

	private async Task StartMaintenanceRecoveryAsync()
	{
		if (_runServerLifecycleAsync == null)
		{
			ApplyServerBootVisualState(ServerBootVisualState.Failed, "Server lifecycle coordinator is unavailable.");
			return;
		}

		AddServerBootLog("[MAINTENANCE] Cleanup started.");
		ServerLifecycleResult result = await _runServerLifecycleAsync(new ServerLifecycleRequest(ServerLifecycleMode.MaintenanceRecovery));
		if (!result.IsSuccess)
		{
			return;
		}

		AddServerBootLog("[MAINTENANCE] Runtime cleanup completed.");
		_isMaintenanceRecoveryMode = false;
		await PrepareProductSetupForRevealAsync();
	}

	private async Task StartServerBootFromLoadingAsync(bool resumePendingProcess, bool repairRuntimeBeforeBoot = false)
	{
		if (_runServerLifecycleAsync == null)
		{
			ApplyServerBootVisualState(ServerBootVisualState.Failed, "Server lifecycle coordinator is unavailable.");
			return;
		}

		if (resumePendingProcess)
		{
			ApplyServerBootVisualState(ServerBootVisualState.WaitingForPort, Text("backend_alive_waiting_for_port"));
			AddServerBootLog("[BOOT] Resume pending server launch.");
		}
		else
		{
			ApplyServerBootVisualState(
				ServerBootVisualState.Booting,
				repairRuntimeBeforeBoot
					? Text("repairing_runtime_before_backend_launch")
					: Text("starting_backend_process"));
		}

		ServerLifecycleResult result = await _runServerLifecycleAsync(new ServerLifecycleRequest(
			ServerLifecycleMode.Startup,
			RepairRuntimeBeforeBoot: repairRuntimeBeforeBoot,
			ResumePendingServerProcess: resumePendingProcess));
		await CompleteServerLifecycleAsync(result);
	}

	internal async Task CompleteServerLifecycleAsync(ServerLifecycleResult result)
	{
		UpdateServerBootEndpoint();
		if (!result.IsSuccess)
		{
			if (!ServerBootLayout.IsVisible)
			{
				ShowOnlySurface(ServerBootLayout);
			}

			ApplyServerBootVisualState(ServerBootVisualState.Failed, result.Message);
			return;
		}

		if (result.RequiresSetupHandoff)
		{
			AddServerBootLog($"[SYSTEM] {result.Message}");
			ApplyServerBootVisualState(ServerBootVisualState.Booting, Text("maintenance_completed_setup"));
			await PrepareProductSetupForRevealAsync();
			return;
		}

		AddServerBootLog("[SYSTEM] Backend server is online.");
		AddServerBootLog("[SYSTEM] Backend HTTP readiness confirmed. Loading Nexus bridge...");
		NexusVisualStateTransition<ServerBootAnimationState> successTransition = ApplyServerBootVisualState(
			ServerBootVisualState.Online,
			Text("backend_ready_handoff"));
		NexusVisualTransitionResult visualResult = await successTransition.Completion;
		if (_serverBootVisualState != ServerBootVisualState.Online || !ServerBootLayout.IsVisible)
		{
			return;
		}

		if (visualResult == NexusVisualTransitionResult.Superseded)
		{
			return;
		}

		if (visualResult == NexusVisualTransitionResult.Unavailable)
		{
			NexusLog.Warning("[LOADING] Server boot success visual was unavailable; continuing with the WebView hand-off.");
		}

		await LaunchFromServerBootAsync();
	}

	private async Task LaunchFromServerBootAsync()
	{
		AddServerBootLog("[SYSTEM] Switching from server boot monitor to Nexus WebView hand-off.");
		if (_nexusAppEntry == null)
		{
			ShowOnlySurface(ServerBootLayout);
			ApplyServerBootVisualState(ServerBootVisualState.Failed, Text("nexus_app_entry_not_connected"));
			return;
		}

		ShowOnlySurface(LoadingStackLayout);
		ApplyDisplay(new LoadingOverlayDisplay(
			LoadingOverlayState.Hold,
			Text("backend_online_title"),
			Text("comfyui_ready_loading_nexus_interface"),
			Text("finalizing_nexus_link"),
			NexusAccentColor,
			Progress: 0.72));

		try
		{
			await _nexusAppEntry.LaunchAsync(CancellationToken.None);
		}
		catch (Exception ex)
		{
			AddServerBootLog($"[ERROR] Launch hand-off failed: {ex.Message}");
			ShowOnlySurface(ServerBootLayout);
			ApplyServerBootVisualState(ServerBootVisualState.Failed, ex.Message);
		}
	}

	private NexusVisualStateTransition<ServerBootAnimationState> ApplyServerBootVisualState(ServerBootVisualState state, string description)
	{
		XamlLifetimeDiagnostics.RecordSurface("server-boot", state.ToString());
		_serverBootVisualState = state;

		ServerBootVisualSpec spec = GetServerBootVisualSpec(state);

		ServerBootStateLabel.Text = spec.StateText;
		ServerBootStateLabel.TextColor = spec.Color;
		ServerBootDescriptionLabel.Text = description;
		ServerBootPrimaryActionLabel.Text = spec.PrimaryText;

		bool showRecoverAction = ShouldShowServerBootRecoverAction(state);
		ApplyBootActionVisibility(ServerBootPrimaryActionButton, spec.PrimaryVisible);
		ApplyBootActionVisibility(ServerBootRecoverActionButton, showRecoverAction);
		ApplyBootActionVisibility(ServerBootSetupButton, spec.SetupVisible);
		ServerBootStatusRowGrid.ColumnSpacing = spec.SetupVisible ? ServerBootStatusRowOpenSetupSpacing : 0;
		SetServerBootConfigEditState(!_isMaintenanceRecoveryMode && state == ServerBootVisualState.Idle);

		ApplyServerBootButtonBackgrounds();
		ServerBootAnimationSurface.Opacity = 1;
		NexusVisualStateTransition<ServerBootAnimationState> transition = _serverBootAnimator.TransitionTo(MapServerBootAnimationState(state));
		XamlLifetimeDiagnostics.WriteSnapshot($"server-boot:{state}");
		return transition;
	}

	private ServerBootVisualSpec GetServerBootVisualSpec(ServerBootVisualState state)
	{
		if (_isMaintenanceRecoveryMode)
		{
			return state switch
			{
				ServerBootVisualState.Booting => new(Text("cleaning"), ServerBootWarningColor, 0.9, false, false, Text("cleaning")),
				ServerBootVisualState.Failed => new(Text("recovery_failed"), ServerBootFailedColor, 0.8, true, false, Text("retry_cleanup")),
				ServerBootVisualState.Online => new(Text("clean"), ServerBootOnlineColor, 0.95, false, false, Text("clean")),
				_ => new(CommonText("ready"), NexusAccentColor, 0.48, true, false, Text("continue_cleanup"))
			};
		}

		return state switch
		{
			ServerBootVisualState.Booting => new(Text("booting"), ServerBootWarningColor, 0.9, false, false, Text("engaging")),
			ServerBootVisualState.WaitingForPort => new(Text("awaiting_port"), NexusAccentColor, 0.72, false, false, Text("waiting")),
			ServerBootVisualState.Failed => new(Text("failed"), ServerBootFailedColor, 0.8, true, false, CommonText("retry_boot")),
			ServerBootVisualState.Online => new(CommonText("online"), ServerBootOnlineColor, 0.95, false, false, CommonText("online")),
			_ => new(CommonText("standby"), NexusAccentColor, 0.48, true, true, Text("engage_server"))
		};
	}

	private static void ApplyBootActionVisibility(VisualElement button, bool isVisible)
	{
		button.IsVisible = isVisible;
		button.InputTransparent = !isVisible;
	}

	private bool ShouldShowServerBootRecoverAction(ServerBootVisualState state)
	{
		if (_isMaintenanceRecoveryMode)
		{
			return false;
		}

		if (state == ServerBootVisualState.Failed)
		{
			return true;
		}

		return state == ServerBootVisualState.Idle
			&& SetupSettingsService.Instance.Settings.LastLaunchSuccessful;
	}

	private void OnServerBootButtonHovered(object? sender, PointerEventArgs e)
	{
		if (sender is not Border border || border.InputTransparent) return;

		border.BackgroundColor = ReferenceEquals(border, ServerBootSetupButton)
			? ServerBootSetupHoverColor
			: ReferenceEquals(border, ServerBootRecoverActionButton)
				? ServerBootRecoverHoverColor
			: GetServerBootPrimaryHoverColor();
	}

	private void OnServerBootButtonUnhovered(object? sender, PointerEventArgs e)
	{
		if (sender is not Border) return;

		ApplyServerBootButtonBackgrounds();
	}

	private async void OnServerBootHostEditClicked(object? sender, TappedEventArgs e)
	{
		if (_serverBootVisualState != ServerBootVisualState.Idle) return;

		var page = GetPromptPage();
		if (page == null) return;

		var settings = SetupSettingsService.Instance.Settings;
		string? result = await page.DisplayPromptAsync(
			Text("comfyui_host"),
			Text("set_host_used_when_nexus_starts_comfyui"),
			CommonText("save"),
			CommonText("cancel"),
			"127.0.0.1",
			64,
			Keyboard.Text,
			settings.ListenAddress);

		if (result == null) return;

		string host = result.Trim();
		if (!IsValidHostValue(host))
		{
			await page.DisplayAlertAsync(Text("invalid_host"), Text("use_compact_host_without_spaces"), CommonText("ok"));
			return;
		}

		settings.ListenAddress = host;
		settings.LastLaunchSuccessful = false;
		settings.LastActivePort = null;
		SetupSettingsService.Instance.Save();
		UpdateServerBootEndpoint();
		AddServerBootLog($"[CONFIG] ComfyUI host set to {host}.");
	}

	private async void OnServerBootPortEditClicked(object? sender, TappedEventArgs e)
	{
		if (_serverBootVisualState != ServerBootVisualState.Idle) return;

		var page = GetPromptPage();
		if (page == null) return;

		var settings = SetupSettingsService.Instance.Settings;
		string? result = await page.DisplayPromptAsync(
			Text("comfyui_port"),
			Text("set_port_used_when_nexus_starts_comfyui"),
			CommonText("save"),
			CommonText("cancel"),
			"8188",
			5,
			Keyboard.Numeric,
			settings.ServerPort.ToString());

		if (result == null) return;

		if (!int.TryParse(result.Trim(), out int port) || port is < 1 or > 65535)
		{
			await page.DisplayAlertAsync(Text("invalid_port"), Text("use_port_between_1_and_65535"), CommonText("ok"));
			return;
		}

		settings.ServerPort = port;
		settings.LastLaunchSuccessful = false;
		settings.LastActivePort = null;
		SetupSettingsService.Instance.Save();
		UpdateServerBootEndpoint();
		AddServerBootLog($"[CONFIG] ComfyUI port set to {port}.");
	}

	private void OnServerBootConfigHovered(object? sender, PointerEventArgs e)
	{
		if (sender is not Border border || border.InputTransparent) return;

		border.BackgroundColor = SetupActionHoverColor;
	}

	private void OnServerBootConfigUnhovered(object? sender, PointerEventArgs e)
	{
		if (sender is not Border) return;

		ApplyServerBootButtonBackgrounds();
	}

	private void ApplyServerBootButtonBackgrounds()
	{
		ServerBootSetupButton.BackgroundColor = SetupActionNormalColor;
		ServerBootPrimaryActionButton.BackgroundColor = _serverBootVisualState == ServerBootVisualState.Failed
			? ServerBootPrimaryFailedNormalColor
			: ServerBootPrimaryNormalColor;
		ServerBootRecoverActionButton.BackgroundColor = ServerBootRecoverNormalColor;
		ServerBootHostButton.BackgroundColor = ServerBootConfigNormalColor;
		ServerBootPortButton.BackgroundColor = ServerBootConfigNormalColor;
	}

	private Color GetServerBootPrimaryHoverColor()
	{
		return _serverBootVisualState == ServerBootVisualState.Failed
			? ServerBootPrimaryFailedHoverColor
			: ServerBootPrimaryHoverColor;
	}

	private static ServerBootAnimationState MapServerBootAnimationState(ServerBootVisualState state)
	{
		return state switch
		{
			ServerBootVisualState.Idle => ServerBootAnimationState.Idle,
			ServerBootVisualState.Failed => ServerBootAnimationState.Failed,
			ServerBootVisualState.Online => ServerBootAnimationState.Success,
			_ => ServerBootAnimationState.Booting,
		};
	}

	private bool CanUseOverlay()
	{
		return !_isOverlayUnloading && Handler != null;
	}

	private bool CanRepeatServerBootAnimation()
	{
		return CanUseOverlay() && ServerBootLayout.IsVisible;
	}

	private void StopServerBootAnimations()
	{
		_serverBootAnimator.StopAll();
		ServerBootAnimationSurface.Opacity = 1;
	}

	private void OnUnloaded(object? sender, EventArgs e)
	{
		_isOverlayUnloading = true;
		StopServerBootAnimations();
	}

	private void OnLoaded(object? sender, EventArgs e)
	{
		_isOverlayUnloading = false;
		if (ServerBootLayout.IsVisible)
		{
			ApplyServerBootVisualState(_serverBootVisualState, ServerBootDescriptionLabel.Text ?? string.Empty);
		}
	}

	private void UpdateServerBootEndpoint()
	{
		var settings = SetupSettingsService.Instance.Settings;
		ServerBootHostLabel.Text = settings.ListenAddress;
		ServerBootPortLabel.Text = settings.ServerPort.ToString();
		UpdateServerBootPythonMode();
	}

	private void UpdateServerBootPythonMode()
	{
		bool useVenv = RuntimePythonModePresenter.ShouldDisplayVenvMode(SetupSettingsService.Instance.Settings);
		ServerBootPythonModeLabel.Text = useVenv ? "VENV" : "DIRECT";
		ServerBootPythonModeLabel.TextColor = useVenv ? NexusAccentColor : ServerBootWarningColor;
		ServerBootPythonModePill.BackgroundColor = useVenv ? NexusColors.AccentWash : NexusColors.WarningSoft;
	}

	private void SetServerBootConfigEditState(bool canEdit)
	{
		ServerBootHostButton.InputTransparent = !canEdit;
		ServerBootPortButton.InputTransparent = !canEdit;
		ServerBootPythonModePill.Opacity = canEdit ? 1 : DisabledConfigEditOpacity;
		ServerBootHostButton.Opacity = canEdit ? 1 : DisabledConfigEditOpacity;
		ServerBootPortButton.Opacity = canEdit ? 1 : DisabledConfigEditOpacity;
	}

	private Page? GetPromptPage()
		=> Window?.Page ?? Application.Current?.Windows.FirstOrDefault()?.Page;

	private static bool IsValidHostValue(string host)
		=> !string.IsNullOrWhiteSpace(host)
			&& host.Length <= 64
			&& !host.Any(char.IsWhiteSpace);

	private void AddServerBootLog(string message)
	{
		NexusLog.Info(message);
		UiThread.TryBeginInvoke(() =>
		{
			if (!CanUseOverlay()) return;

			if (_serverBootVisualState == ServerBootVisualState.Booting
				&& message.Contains("Waiting for server port", StringComparison.OrdinalIgnoreCase))
			{
				ApplyServerBootVisualState(ServerBootVisualState.WaitingForPort, Text("backend_alive_waiting_for_port"));
			}

			ServerBootLogTail.AppendLine(message);
		}, "LOADING:BOOT_LOG");
	}

	internal void SetStatusVisualState(double opacity, double scale, double translationY)
	{
		LoadingStatusLabel.Opacity = opacity;
		LoadingStatusLabel.Scale = scale;
		LoadingStatusLabel.TranslationY = translationY;
	}

	internal Image SuccessAnimationSurface => SuccessAnimationImage;
	internal void SetSuccessAnimationVisible(bool isVisible) => SuccessAnimationImage.Opacity = isVisible ? 1 : 0;
	internal Image LoadingProcessAnimationSurface => LoadingProcessImage;
	internal void SetLoadingProcessVisible(bool isVisible) => LoadingProcessImage.Opacity = isVisible ? 1 : 0;

	private void OnSelectCoreClicked(object? sender, EventArgs e) => SelectCoreRequested?.Invoke(sender, e);

	private void OnGetComfyTapped(object? sender, TappedEventArgs e) => GetComfyRequested?.Invoke(sender, e);

	private void OnGetGitHubTapped(object? sender, TappedEventArgs e) => GetGitHubRequested?.Invoke(sender, e);

	private async void OnProductSetupFinalized(object? sender, EventArgs e)
	{
		if (_isProductSetupFinalizing)
		{
			return;
		}

		_isProductSetupFinalizing = true;
		ShowOnlySurface(LoadingStackLayout);

		ApplyDisplay(new LoadingOverlayDisplay(
			LoadingOverlayState.Hold,
			Text("launching_nexus"),
			Text("setup_complete_initializing_nexus_shell"),
			Text("system_booting"),
			NexusAccentColor,
			Progress: 0.12));

		if (_nexusAppEntry == null)
		{
			ApplyDisplay(new LoadingOverlayDisplay(
				LoadingOverlayState.Error,
				Text("launch_link_missing"),
				Text("nexus_app_entry_not_connected"),
				Text("launch_failed"),
				ServerBootFailedColor,
				CenterGlyph: "X"));
			_isProductSetupFinalizing = false;
			return;
		}

		try
		{
			await _nexusAppEntry.LaunchAsync(CancellationToken.None);
		}
		catch (Exception ex)
		{
			ApplyDisplay(new LoadingOverlayDisplay(
				LoadingOverlayState.Error,
				Text("launch_failed_title"),
				ex.Message,
				Text("launch_failed"),
				ServerBootFailedColor,
				CenterGlyph: "X"));
			_isProductSetupFinalizing = false;
		}
	}
}
