using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Services;
using Microsoft.Maui.Controls.Shapes;
using System.Runtime.InteropServices;

namespace ComfyUI_Nexus.Views.Overlays;

public partial class LoadingOverlayView : ContentView
{
	private const int MaxServerBootLogItems = 120;
	private const int ServerBootLogScrollDelayMs = 80;
	private const int ServerBootAnimationRate = 16;
	private const double LoadingProgressWidth = 360;
	private const double DisabledConfigEditOpacity = 0.55;
	private const double ServerBootStatusRowOpenSetupSpacing = 10;
	private const double ServerBootLogFontSize = 11;
	private const float ServerBootOuterRingGlowRadius = 18;
	private const float ServerBootMarkerGlowRadius = 12;
	private const float ServerBootLogoGlowRadius = 32;
	private const double ServerBootRotationStart = -12;
	private const double ServerBootRotationEnd = 348;
	private const double ServerBootPulseMidpoint = 0.5;
	private const double ServerBootFailedGlowOpacity = 0.8;
	private const double ServerBootOuterRingShadowMaxOpacity = 0.85;
	private const double ServerBootMarkerShadowMaxOpacity = 0.9;
	private const int ServerBootCopyFeedbackDelayMs = 900;
	private const uint ServerBootPanelFadeLength = 360;
	private const string ServerBootOuterRingRotateAnimationName = "ServerBootOuterRingRotate";
	private const string ServerBootOrbitMorphAnimationName = "ServerBootOrbitMorph";
	private const string ServerBootGlowPulseAnimationName = "ServerBootGlowPulse";
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
	private static readonly Color ServerBootLogNormalColor = Color.FromArgb("#9fb8c8");
	private static readonly Color ServerBootLogErrorColor = NexusColors.DangerText;
	private static readonly Color ServerBootCopyLogNormalColor = NexusColors.SurfaceDarkTranslucent;
	private static readonly Color ServerBootCopyLogHoverColor = NexusColors.SurfaceRaised;
	private static readonly Color ServerBootCopyLogTextColor = NexusColors.Accent;
	private static readonly Color ServerBootCopyLogHoverTextColor = NexusColors.TextPrimary;

	private enum ServerBootVisualState
	{
		Idle,
		Booting,
		WaitingForPort,
		Failed,
		Online
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
	private bool _isServerBootRunning;
	private bool _isOverlayUnloading;
	private bool _isMaintenanceRecoveryMode;
	private bool _disableDynamicShadows;
	private ServerBootVisualState _serverBootVisualState = ServerBootVisualState.Idle;
	private int _serverBootCopyFeedbackId;
	private int _serverBootLogScrollRequestId;
	private VisualElement? _serverBootLogScrollTarget;
	private readonly SetupSequenceOrchestrator _serverBootSequence = new();

	internal event EventHandler? SelectCoreRequested;
	internal event EventHandler<TappedEventArgs>? GetComfyRequested;
	internal event EventHandler<TappedEventArgs>? GetGitHubRequested;

	private static string Text(string key)
		=> LocalizationManager.Text(TextKeyPrefix + key);

	private static string CommonText(string key)
		=> LocalizationManager.Text("common." + key);

	public LoadingOverlayView()
	{
		InitializeComponent();
		ProductSetupControl.SetupFinalized += OnProductSetupFinalized;
		_serverBootSequence.OnMessage += AddServerBootLog;
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
			LoadingLogoImage.Opacity = 0;
		}
		else if (display.State is LoadingOverlayState.Show or LoadingOverlayState.Hold or LoadingOverlayState.Message)
		{
			SuccessIconLabel.Opacity = 0;
			SuccessIconLabel.Scale = 0;
			SuccessIconHost.Opacity = 0;
			SuccessIconHost.Scale = 0;
			LoadingLogoImage.Opacity = 1;
		}

		SetProgress(display.Progress, display.AccentColor);
		SetLoadingSetupActionVisible(display.State == LoadingOverlayState.Error);
	}

	internal void ClearDisplayDetails()
	{
		LoadingTitleLabel.IsVisible = false;
		LoadingDescriptionLabel.IsVisible = false;
		LoadingProgressHost.IsVisible = false;
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
		LoadingStackLayout.IsVisible = ReferenceEquals(activeSurface, LoadingStackLayout);
		ConfigStackLayout.IsVisible = ReferenceEquals(activeSurface, ConfigStackLayout);
		ProductSetupControl.IsVisible = ReferenceEquals(activeSurface, ProductSetupControl);
		ServerBootLayout.IsVisible = ReferenceEquals(activeSurface, ServerBootLayout);
	}

	internal void ResetVisualState(string successText, Color successColor, Color ringColor)
	{
		LoadingLogoImage.Opacity = 1;
		LoadingLogoImage.Scale = 1;
		LoadingLogoImage.Rotation = 0;
		ApplyDynamicShadow(LoadingLogoImage, CreateGlowShadow(ringColor, 32, 0.75));
		LoadingOrbitHost.Rotation = -12;
		LoadingOrbitHost.Scale = 1;

		SuccessIconLabel.Text = successText;
		SuccessIconLabel.TextColor = successColor;
		SuccessIconLabel.Opacity = 0;
		SuccessIconLabel.Scale = 0;
		SuccessIconLabel.Rotation = -10;
		SuccessIconHost.Opacity = 0;
		SuccessIconHost.Scale = 0;
		SuccessIconHost.Rotation = -10;

		LoadingRingEllipse.Fill = new SolidColorBrush(ringColor);
		LoadingRingEllipse.Opacity = 0.055;
		LoadingRingEllipse.Scale = 1;
		ApplyDynamicShadow(LoadingRingEllipse, CreateGlowShadow(ringColor, 18, 0.7));
		LoadingOrbitMarker.Fill = new SolidColorBrush(ringColor);
		ApplyDynamicShadow(LoadingOrbitMarker, CreateGlowShadow(ringColor, 14, 0.85));

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

	internal void SetLoadingRingStyle(Color stroke, double thickness)
	{
		LoadingRingEllipse.Fill = new SolidColorBrush(stroke);
		LoadingOrbitMarker.Fill = new SolidColorBrush(stroke);
	}

	internal void SetMode(bool showConfig)
	{
		ShowOnlySurface(showConfig ? ConfigStackLayout : LoadingStackLayout);
	}

	internal void ShowProductSetup()
	{
		NexusLog.Info("[LOADING] Product setup handoff starting.");
		_isMaintenanceRecoveryMode = false;
		ShowOverlayRoot();
		ShowOnlySurface(ProductSetupControl);
		_isProductSetupFinalizing = false;
		NexusLog.Info("[LOADING] Product setup reset requested.");
		ProductSetupControl.ResetFlow();
		ProductSetupControl.ActivateLifecycle();
		NexusLog.Info("[LOADING] Product setup handoff completed.");
	}

	internal void ShowServerLaunchOnly()
		=> _ = ShowServerLaunchPanelSafeAsync(resumePendingProcess: false);

	internal void ShowServerStartupPending()
		=> _ = ShowServerLaunchPanelSafeAsync(resumePendingProcess: true);

	internal void RestartServerLaunch(bool repairRuntimeBeforeBoot = false)
		=> _ = ShowServerLaunchPanelSafeAsync(
			resumePendingProcess: false,
			autoStart: true,
			repairRuntimeBeforeBoot: repairRuntimeBeforeBoot);

	internal void ShowMaintenanceRecovery()
		=> _ = ShowMaintenanceRecoverySafeAsync();

	private async Task ShowMaintenanceRecoverySafeAsync()
	{
		try
		{
			await ShowMaintenanceRecoveryAsync();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Exception(ex, "[LOADING] Maintenance recovery panel failed");
		}
	}

	private async Task ShowMaintenanceRecoveryAsync()
	{
		ShowOverlayRoot();
		ShowOnlySurface(null);
		_isProductSetupFinalizing = false;
		_isMaintenanceRecoveryMode = true;

		if (!CanUseOverlay()) return;
		ShowMaintenanceRecoveryPanel();
		await StartMaintenanceRecoveryAsync();
	}

	private async Task ShowServerLaunchPanelSafeAsync(
		bool resumePendingProcess,
		bool autoStart = false,
		bool repairRuntimeBeforeBoot = false)
	{
		try
		{
			await ShowServerLaunchPanelAsync(resumePendingProcess, autoStart, repairRuntimeBeforeBoot);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Exception(ex, "[LOADING] Server launch panel failed");
			if (CanUseOverlay())
			{
				ShowServerBootPanel(resumePendingProcess);
				AddServerBootLog($"[ERROR] Server boot view recovered after UI error: {ex.Message}");
			}
		}
	}

	private async Task ShowServerLaunchPanelAsync(
		bool resumePendingProcess,
		bool autoStart = false,
		bool repairRuntimeBeforeBoot = false)
	{
		NexusLog.Info($"[LOADING] Server launch panel requested. resume={resumePendingProcess}, autoStart={autoStart}, repair={repairRuntimeBeforeBoot}");
		_isMaintenanceRecoveryMode = false;
		ShowOverlayRoot();
		ShowOnlySurface(null);
		_isProductSetupFinalizing = false;

		if (!CanUseOverlay()) return;
		ShowServerBootPanel(resumePendingProcess);
		if (autoStart)
		{
			AddServerBootLog(repairRuntimeBeforeBoot
				? "[RESTART] Relaunching with runtime repair before ComfyUI server boot."
				: "[RESTART] Relaunching ComfyUI server with current Nexus settings.");
			await StartServerBootFromLoadingAsync(
				resumePendingProcess: false,
				repairRuntimeBeforeBoot: repairRuntimeBeforeBoot);
		}
	}

	internal void SetNexusAppEntry(INexusAppEntry appEntry)
	{
		_nexusAppEntry = appEntry;
	}

	private void ShowServerBootPanel(bool resumePendingProcess = false)
	{
		ResetServerBootPanelAnimations();
		ShowOnlySurface(ServerBootLayout);
		ServerBootLayout.Opacity = 1;
		ServerBootOuterRingHost.Rotation = 0;
		ServerBootTitleLabel.Text = Text("nexus_server_boot");

		ServerBootLogList.Children.Clear();
		UpdateServerBootEndpoint();
		if (resumePendingProcess)
		{
			ApplyServerBootVisualState(ServerBootVisualState.WaitingForPort, Text("backend_process_already_starting"));
			AddServerBootLog("[ROUTE] Existing setup detected. Backend service is still starting.");
			AddServerBootLog("[SYSTEM] Reattaching to the active ComfyUI server launch.");
			_ = StartServerBootFromLoadingAsync(resumePendingProcess: true);
		}
		else
		{
			ApplyServerBootVisualState(ServerBootVisualState.Idle, Text("backend_offline_engage"));
			AddServerBootLog("[ROUTE] Existing setup detected. Backend service is offline.");
			AddServerBootLog("[SYSTEM] Ready to engage ComfyUI server process.");
		}

	}

	private void ShowMaintenanceRecoveryPanel()
	{
		ResetServerBootPanelAnimations();
		ShowOnlySurface(ServerBootLayout);
		ServerBootLayout.Opacity = 1;
		ServerBootOuterRingHost.Rotation = 0;
		ServerBootTitleLabel.Text = Text("nexus_maintenance");

		ServerBootLogList.Children.Clear();
		UpdateServerBootEndpoint();
		ApplyServerBootVisualState(ServerBootVisualState.Booting, Text("preparing_runtime_cleanup_recovery"));
		AddServerBootLog("[MAINTENANCE] Runtime purge was requested or interrupted.");
		AddServerBootLog("[MAINTENANCE] Preparing cleanup before setup resumes.");

	}

	private void ResetServerBootPanelAnimations()
	{
		ServerBootLayout.AbortAnimation("FadeTo");
		this.AbortAnimation(ServerBootOuterRingRotateAnimationName);
		this.AbortAnimation(ServerBootOrbitMorphAnimationName);
		this.AbortAnimation(ServerBootGlowPulseAnimationName);
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

	private async void OnServerBootCopyLogTapped(object? sender, TappedEventArgs e)
	{
		string logText = CreateServerBootLogText();
		if (string.IsNullOrWhiteSpace(logText))
		{
			return;
		}

		try
		{
			await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(logText);
			ShowServerBootCopyFeedback();
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[BOOT] Failed to copy server boot log");
		}
	}

	private void ShowServerBootCopyFeedback()
	{
		int feedbackId = ++_serverBootCopyFeedbackId;
		ServerBootCopyLogLabel.Text = "COPIED";
		_ = ResetServerBootCopyFeedbackAsync(feedbackId);
	}

	private async Task ResetServerBootCopyFeedbackAsync(int feedbackId)
	{
		await Task.Delay(ServerBootCopyFeedbackDelayMs);
		if (!CanUseOverlay() || feedbackId != _serverBootCopyFeedbackId)
		{
			return;
		}

		ServerBootCopyLogLabel.Text = CommonText("copy");
	}

	private void OnServerBootCopyLogHovered(object? sender, PointerEventArgs e)
	{
		ServerBootCopyLogButton.BackgroundColor = ServerBootCopyLogHoverColor;
		ServerBootCopyLogLabel.TextColor = ServerBootCopyLogHoverTextColor;
	}

	private void OnServerBootCopyLogUnhovered(object? sender, PointerEventArgs e)
	{
		ServerBootCopyLogButton.BackgroundColor = ServerBootCopyLogNormalColor;
		ServerBootCopyLogLabel.TextColor = ServerBootCopyLogTextColor;
	}

	private void OnServerBootSetupClicked(object? sender, TappedEventArgs e)
	{
		if (_isMaintenanceRecoveryMode)
		{
			return;
		}

		if (_serverBootVisualState != ServerBootVisualState.Idle)
		{
			return;
		}

		ShowProductSetup();
	}

	private void OnLoadingSetupActionClicked(object? sender, TappedEventArgs e)
	{
		if (_isMaintenanceRecoveryMode)
		{
			return;
		}

		NexusLog.Info("[LOADING] Setup handoff requested from loading error.");
		ShowProductSetup();
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

	private async Task StartMaintenanceRecoveryAsync()
	{
		if (_isServerBootRunning) return;

		_isServerBootRunning = true;
		ApplyServerBootVisualState(ServerBootVisualState.Booting, Text("stopping_runtime_processes"));
		AddServerBootLog("[MAINTENANCE] Cleanup started.");

		SetupStepResult result;
		try
		{
			await GpuDiscoveryService.ShutdownAsync();
			var processInfo = await Task.Run(ComfyServerProcessRegistry.FindServerProcess);
			if (processInfo != null)
			{
				AddServerBootLog($"[MAINTENANCE] Terminating server process: {processInfo.ProcessName} ({processInfo.ProcessId}).");
				await ComfyServerProcessRegistry.ShutdownAsync(processInfo, TimeSpan.FromSeconds(5));
			}

			result = await _serverBootSequence.RunServerBootAsync(false, AddServerBootLog, CancellationToken.None);
		}
		catch (Exception ex)
		{
			result = new SetupStepResult(false, $"Runtime cleanup failed: {ex.Message}", 0);
		}
		finally
		{
			_isServerBootRunning = false;
		}

		if (!result.IsSuccess)
		{
			AddServerBootLog($"[ERROR] {result.Message}");
			ApplyServerBootVisualState(ServerBootVisualState.Failed, result.Message);
			ServerBootPrimaryActionLabel.Text = "RETRY CLEANUP";
			ServerBootRecoverActionButton.IsVisible = false;
			ServerBootRecoverActionButton.InputTransparent = true;
			ServerBootSetupButton.IsVisible = false;
			ServerBootSetupButton.InputTransparent = true;
			return;
		}

		AddServerBootLog("[MAINTENANCE] Runtime cleanup completed.");
		ApplyServerBootVisualState(ServerBootVisualState.Online, Text("runtime_cleanup_complete_setup"));
		await Task.Delay(500);
		_isMaintenanceRecoveryMode = false;
		ShowProductSetup();
	}

	private async Task StartServerBootFromLoadingAsync(bool resumePendingProcess, bool repairRuntimeBeforeBoot = false)
	{
		if (_isServerBootRunning) return;

		_isServerBootRunning = true;
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

		SetupStepResult result;
		try
		{
			result = await _serverBootSequence.RunServerBootAsync(repairRuntimeBeforeBoot, AddServerBootLog, CancellationToken.None);
			UpdateServerBootEndpoint();
		}
		catch (Exception ex)
		{
			result = new SetupStepResult(false, $"Server boot failed: {ex.Message}", 0);
		}
		finally
		{
			_isServerBootRunning = false;
		}

		if (!result.IsSuccess)
		{
			AddServerBootLog($"[ERROR] {result.Message}");
			ApplyServerBootVisualState(ServerBootVisualState.Failed, result.Message);
			return;
		}

		if (result.RequiresSetupHandoff)
		{
			AddServerBootLog($"[SYSTEM] {result.Message}");
			ApplyServerBootVisualState(ServerBootVisualState.Online, Text("maintenance_completed_setup"));
			await Task.Delay(500);
			ShowProductSetup();
			return;
		}

		AddServerBootLog("[SYSTEM] Backend server is online.");
		AddServerBootLog("[SYSTEM] Backend HTTP readiness confirmed. Loading Nexus bridge...");
		ApplyServerBootVisualState(ServerBootVisualState.Online, Text("backend_ready_handoff"));
		await Task.Delay(500);
		await LaunchFromServerBootAsync();
	}

	private async Task LaunchFromServerBootAsync()
	{
		AddServerBootLog("[SYSTEM] Switching from server boot monitor to Nexus WebView hand-off.");
		ShowOnlySurface(LoadingStackLayout);

		ApplyDisplay(new LoadingOverlayDisplay(
			LoadingOverlayState.Hold,
			Text("backend_online_title"),
			Text("comfyui_ready_loading_nexus_interface"),
			Text("finalizing_nexus_link"),
			NexusAccentColor,
			Progress: 0.72));

		if (_nexusAppEntry == null)
		{
			ApplyServerBootVisualState(ServerBootVisualState.Failed, Text("nexus_app_entry_not_connected"));
			ShowOnlySurface(ServerBootLayout);
			return;
		}

		try
		{
			await _nexusAppEntry.LaunchAsync(CancellationToken.None);
		}
		catch (Exception ex)
		{
			AddServerBootLog($"[ERROR] Launch hand-off failed: {ex.Message}");
			ApplyServerBootVisualState(ServerBootVisualState.Failed, ex.Message);
			ShowOnlySurface(ServerBootLayout);
		}
	}

	private void ApplyServerBootVisualState(ServerBootVisualState state, string description)
	{
		_serverBootVisualState = state;

		ServerBootVisualSpec spec = GetServerBootVisualSpec(state);

		ServerBootStateLabel.Text = spec.StateText;
		ServerBootStateLabel.TextColor = spec.Color;
		ServerBootDescriptionLabel.Text = description;
		ServerBootOuterRingEllipse.Fill = new SolidColorBrush(spec.Color);
		ApplyDynamicShadow(ServerBootOuterRingEllipse, CreateGlowShadow(spec.Color, ServerBootOuterRingGlowRadius, spec.GlowOpacity));
		ServerBootOuterRingMarker.Fill = new SolidColorBrush(spec.Color);
		ApplyDynamicShadow(ServerBootOuterRingMarker, CreateGlowShadow(spec.Color, ServerBootMarkerGlowRadius, spec.GlowOpacity));
		ApplyDynamicShadow(ServerBootLogoImage, CreateGlowShadow(spec.Color, ServerBootLogoGlowRadius, spec.GlowOpacity));
		ServerBootPrimaryActionLabel.Text = spec.PrimaryText;

		bool showRecoverAction = ShouldShowServerBootRecoverAction(state);
		ApplyBootActionVisibility(ServerBootPrimaryActionButton, spec.PrimaryVisible);
		ApplyBootActionVisibility(ServerBootRecoverActionButton, showRecoverAction);
		ApplyBootActionVisibility(ServerBootSetupButton, spec.SetupVisible);
		ServerBootStatusRowGrid.ColumnSpacing = spec.SetupVisible ? ServerBootStatusRowOpenSetupSpacing : 0;
		SetServerBootConfigEditState(!_isMaintenanceRecoveryMode && state == ServerBootVisualState.Idle);

		ApplyServerBootButtonBackgrounds();
		StartServerBootLogoAnimation(state);
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

	private void StartServerBootLogoAnimation(ServerBootVisualState state)
	{
		StopServerBootAnimations();

		ServerBootLogoImage.Scale = 1;
		ServerBootOuterRingHost.Rotation = ServerBootRotationStart;
		SetServerBootOrbitShape(138, 102);

		if (state == ServerBootVisualState.Booting)
		{
			ServerBootOuterRingEllipse.Opacity = 0.1;
			StartServerBootRotation(2200, CanRepeatServerBootAnimation);
			StartServerBootOrbitMorph(116, 116, 158, 96, 1500, CanRepeatServerBootAnimation);
			StartServerBootGlowPulse(0.56, 0.98, 900, CanRepeatServerBootAnimation);
			return;
		}

		if (state == ServerBootVisualState.WaitingForPort)
		{
			ServerBootOuterRingEllipse.Opacity = 0.075;
			StartServerBootRotation(5200, CanRepeatServerBootAnimation);
			StartServerBootOrbitMorph(118, 112, 156, 98, 2600, CanRepeatServerBootAnimation);
			StartServerBootGlowPulse(0.42, 0.78, 1600, CanRepeatServerBootAnimation);
			return;
		}

		if (state == ServerBootVisualState.Online)
		{
			ServerBootOuterRingEllipse.Opacity = 0.07;
			StartServerBootRotation(12000, CanRepeatServerBootAnimation);
			StartServerBootOrbitMorph(122, 112, 152, 100, 5200, CanRepeatServerBootAnimation);
			StartServerBootGlowPulse(0.48, 0.86, 2200, CanRepeatServerBootAnimation);
			return;
		}

		if (state == ServerBootVisualState.Failed)
		{
			ServerBootOuterRingEllipse.Opacity = 0.08;
			SetServerBootGlowOpacity(ServerBootFailedGlowOpacity);
			return;
		}

		var idlePulse = new Animation();
		idlePulse.Add(0, ServerBootPulseMidpoint, new Animation(v => SetServerBootIdlePulse(v), 0, 1, Easing.CubicInOut));
		idlePulse.Add(ServerBootPulseMidpoint, 1, new Animation(v => SetServerBootIdlePulse(v), 1, 0, Easing.CubicInOut));
		idlePulse.Commit(this, ServerBootGlowPulseAnimationName, ServerBootAnimationRate, 4200, Easing.Linear, repeat: CanRepeatIdleServerBootAnimation);

		StartServerBootRotation(18000, CanRepeatIdleServerBootAnimation);
		StartServerBootOrbitMorph(118, 118, 158, 102, 8600, CanRepeatIdleServerBootAnimation);
	}

	private void StartServerBootRotation(uint length, Func<bool> repeat)
	{
		new Animation(v => ServerBootOuterRingHost.Rotation = v, ServerBootRotationStart, ServerBootRotationEnd)
			.Commit(this, ServerBootOuterRingRotateAnimationName, ServerBootAnimationRate, length, Easing.Linear, repeat: repeat);
	}

	private void StartServerBootGlowPulse(double lowOpacity, double highOpacity, uint length, Func<bool> repeat)
	{
		var glowPulse = new Animation();
		glowPulse.Add(0, ServerBootPulseMidpoint, new Animation(v => SetServerBootGlowOpacity(v), lowOpacity, highOpacity, Easing.CubicInOut));
		glowPulse.Add(ServerBootPulseMidpoint, 1, new Animation(v => SetServerBootGlowOpacity(v), highOpacity, lowOpacity, Easing.CubicInOut));
		glowPulse.Commit(this, ServerBootGlowPulseAnimationName, ServerBootAnimationRate, length, Easing.Linear, repeat: repeat);
	}

	private bool CanUseOverlay()
	{
		return !_isOverlayUnloading && Handler != null;
	}

	private bool CanRepeatServerBootAnimation()
	{
		return CanUseOverlay() && ServerBootLayout.IsVisible;
	}

	private bool CanRepeatIdleServerBootAnimation()
	{
		return CanRepeatServerBootAnimation() && _serverBootVisualState == ServerBootVisualState.Idle;
	}

	private static Shadow CreateGlowShadow(Color color, float radius, double opacity)
	{
		return new Shadow
		{
			Brush = new SolidColorBrush(color),
			Offset = new Point(0, 0),
			Radius = radius,
			Opacity = (float)opacity
		};
	}

	private void ApplyDynamicShadow(VisualElement element, Shadow shadow)
	{
		if (_disableDynamicShadows)
		{
			return;
		}

		try
		{
			element.Shadow = shadow;
		}
		catch (COMException ex)
		{
			_disableDynamicShadows = true;
			NexusLog.Warning($"[LOADING] Dynamic shadows disabled after platform COM error: {ex.Message}");
		}
		catch (InvalidOperationException ex)
		{
			_disableDynamicShadows = true;
			NexusLog.Warning($"[LOADING] Dynamic shadows disabled because the handler is not ready: {ex.Message}");
		}
	}

	private void SetServerBootGlowOpacity(double opacity)
	{
		if (!CanUseOverlay()) return;
		if (_serverBootVisualState == ServerBootVisualState.Booting)
		{
			ServerBootOuterRingEllipse.Opacity = 0.04 + (0.06 * opacity);
		}
		else if (_serverBootVisualState == ServerBootVisualState.WaitingForPort)
		{
			ServerBootOuterRingEllipse.Opacity = 0.02 + (0.055 * opacity);
		}
		else if (_serverBootVisualState == ServerBootVisualState.Online)
		{
			ServerBootOuterRingEllipse.Opacity = 0.02 + (0.05 * opacity);
		}

		if (!_disableDynamicShadows && ServerBootLogoImage.Shadow != null)
		{
			ServerBootLogoImage.Shadow.Opacity = (float)opacity;
		}

		if (!_disableDynamicShadows && ServerBootOuterRingEllipse.Shadow != null)
		{
			ServerBootOuterRingEllipse.Shadow.Opacity = (float)Math.Min(ServerBootOuterRingShadowMaxOpacity, opacity);
		}

		if (!_disableDynamicShadows && ServerBootOuterRingMarker.Shadow != null)
		{
			ServerBootOuterRingMarker.Shadow.Opacity = (float)Math.Min(ServerBootMarkerShadowMaxOpacity, opacity);
		}
	}

	private void SetServerBootIdlePulse(double value)
	{
		if (!CanUseOverlay()) return;

		ServerBootOuterRingEllipse.Opacity = 0.055 * value;
		SetServerBootGlowOpacity(0.34 + (0.18 * value));
	}

	private void StartServerBootOrbitMorph(double circleWidth, double circleHeight, double ellipseWidth, double ellipseHeight, uint length, Func<bool> repeat)
	{
		var orbitMorph = new Animation();
		orbitMorph.Add(0, ServerBootPulseMidpoint, new Animation(v => ServerBootOuterRingHost.WidthRequest = v, circleWidth, ellipseWidth, Easing.CubicInOut));
		orbitMorph.Add(0, ServerBootPulseMidpoint, new Animation(v => ServerBootOuterRingHost.HeightRequest = v, circleHeight, ellipseHeight, Easing.CubicInOut));
		orbitMorph.Add(ServerBootPulseMidpoint, 1, new Animation(v => ServerBootOuterRingHost.WidthRequest = v, ellipseWidth, circleWidth, Easing.CubicInOut));
		orbitMorph.Add(ServerBootPulseMidpoint, 1, new Animation(v => ServerBootOuterRingHost.HeightRequest = v, ellipseHeight, circleHeight, Easing.CubicInOut));
		orbitMorph.Commit(this, ServerBootOrbitMorphAnimationName, ServerBootAnimationRate, length, Easing.Linear, repeat: repeat);
	}

	private void SetServerBootOrbitShape(double width, double height)
	{
		ServerBootOuterRingHost.WidthRequest = width;
		ServerBootOuterRingHost.HeightRequest = height;
	}

	private void StopServerBootAnimations()
	{
		this.AbortAnimation(ServerBootOuterRingRotateAnimationName);
		this.AbortAnimation(ServerBootOrbitMorphAnimationName);
		this.AbortAnimation(ServerBootGlowPulseAnimationName);
	}

	private void OnUnloaded(object? sender, EventArgs e)
	{
		_isOverlayUnloading = true;
		StopServerBootAnimations();
	}

	private void OnLoaded(object? sender, EventArgs e)
	{
		_isOverlayUnloading = false;
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
		UiThread.TryBeginInvoke(() =>
		{
			if (!CanUseOverlay()) return;

			if (_serverBootVisualState == ServerBootVisualState.Booting
				&& message.Contains("Waiting for server port", StringComparison.OrdinalIgnoreCase))
			{
				ApplyServerBootVisualState(ServerBootVisualState.WaitingForPort, Text("backend_alive_waiting_for_port"));
			}

			var label = CreateServerBootLogLabel(message);

			ServerBootLogList.Children.Add(label);
			TrimServerBootLog();
			RequestServerBootLogScroll(label);
		}, "LOADING:BOOT_LOG");
	}

	private static Label CreateServerBootLogLabel(string message)
	{
		return new Label
		{
			Text = message,
			TextColor = message.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase)
				? ServerBootLogErrorColor
				: ServerBootLogNormalColor,
			FontSize = ServerBootLogFontSize,
			LineBreakMode = LineBreakMode.WordWrap
		};
	}

	private string CreateServerBootLogText()
	{
		var lines = ServerBootLogList.Children
			.OfType<Label>()
			.Select(label => label.Text)
			.Where(line => !string.IsNullOrWhiteSpace(line));

		return string.Join(Environment.NewLine, lines);
	}

	private void TrimServerBootLog()
	{
		while (ServerBootLogList.Children.Count > MaxServerBootLogItems)
		{
			ServerBootLogList.Children.RemoveAt(0);
		}
	}

	private void RequestServerBootLogScroll(VisualElement target)
	{
		_serverBootLogScrollTarget = target;
		int requestId = ++_serverBootLogScrollRequestId;
		_ = ScrollServerBootLogToEndAsync(requestId);
	}

	private async Task ScrollServerBootLogToEndAsync(int requestId)
	{
		try
		{
			await Task.Delay(ServerBootLogScrollDelayMs);
			if (!CanUseOverlay() || requestId != _serverBootLogScrollRequestId || _serverBootLogScrollTarget == null) return;

			await ServerBootLogScrollView.ScrollToAsync(_serverBootLogScrollTarget, ScrollToPosition.End, false);
		}
		catch (ObjectDisposedException)
		{
		}
		catch (InvalidOperationException)
		{
		}
	}

	internal void SetLoadingRingRotation(double rotation) => LoadingOrbitHost.Rotation = rotation;
	internal void SetLoadingRingScale(double scale) => LoadingOrbitHost.Scale = scale;
	internal void SetLoadingOrbitWidth(double width) => LoadingOrbitHost.WidthRequest = width;
	internal void SetLoadingOrbitHeight(double height) => LoadingOrbitHost.HeightRequest = height;
	internal void SetLoadingOrbitOpacity(double opacity) => LoadingRingEllipse.Opacity = opacity;
	internal void SetScanLineTranslation(double translationY) => ScanLine.TranslationY = translationY;
	internal void SetLoadingLogoRotation(double rotation) => LoadingLogoImage.Rotation = rotation;
	internal void SetLoadingLogoScale(double scale) => LoadingLogoImage.Scale = scale;
	internal void SetStatusOpacity(double opacity) => LoadingStatusLabel.Opacity = opacity;

	internal Task ScaleStatusAsync(double scale, uint length, Easing easing) => LoadingStatusLabel.ScaleToAsync(scale, length, easing);
	internal Task TranslateStatusAsync(double x, double y, uint length, Easing easing) => LoadingStatusLabel.TranslateToAsync(x, y, length, easing);
	internal Task FadeLoadingLogoAsync(double opacity, uint length, Easing easing) => LoadingLogoImage.FadeToAsync(opacity, length, easing);
	internal Task ScaleLoadingLogoAsync(double scale, uint length, Easing easing) => LoadingLogoImage.ScaleToAsync(scale, length, easing);
	internal Task FadeSuccessGlyphAsync(double opacity, uint length, Easing easing)
		=> Task.WhenAll(SuccessIconLabel.FadeToAsync(opacity, length, easing), SuccessIconHost.FadeToAsync(opacity, length, easing));

	internal Task ScaleSuccessGlyphAsync(double scale, uint length, Easing easing)
		=> Task.WhenAll(SuccessIconLabel.ScaleToAsync(scale, length, easing), SuccessIconHost.ScaleToAsync(scale, length, easing));

	internal Task RotateSuccessGlyphAsync(double rotation, uint length, Easing easing)
		=> Task.WhenAll(SuccessIconLabel.RotateToAsync(rotation, length, easing), SuccessIconHost.RotateToAsync(rotation, length, easing));
	internal Task ScaleLoadingRingAsync(double scale, uint length, Easing easing) => LoadingRingEllipse.ScaleToAsync(scale, length, easing);

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
