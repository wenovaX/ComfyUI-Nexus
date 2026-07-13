using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Ui.Popups;
using ComfyUI_Nexus.Views.Overlays;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private const int ToastHoldDelayMs = 1500;
	private const string ShutdownBlockerLogoBounceAnimationName = "ShutdownBlockerLogoBounce";
	private const double ShutdownBlockerHiddenScale = 0.94;
	private const double ShutdownBlockerHiddenOffsetY = 12;
	private const double ShutdownBlockerLogoBounceHeight = 14;
	private const double ShutdownBlockerGroundGlowRestOpacity = 0.24;
	private const double ShutdownBlockerGroundGlowLiftOpacity = 0.1;
	private const double ShutdownBlockerGroundGlowLiftScale = 0.72;
	private const double ToastHiddenOffsetY = -8;
	private const double ToastDefaultTopMargin = 54;
	private const double ToastRaisedTopMargin = 16;
	private const uint ShutdownBlockerBackdropShowLength = 150;
	private const uint ShutdownBlockerPanelFadeLength = 170;
	private const uint ShutdownBlockerPanelTransformLength = 180;
	private const uint ShutdownBlockerHideLength = 90;
	private const uint ShutdownBlockerLogoBounceLength = 2240;
	private const uint ToastShowLength = 120;
	private const uint ToastHideLength = 160;
	private const string PopupGroupSmallMenu = "SmallMenu";
	private const string PopupGroupOverlay = "Overlay";
	private const string PopupCommandMenu = "CommandMenu";
	private const string PopupSettings = "Settings";
	private const string PopupHelp = "Help";
	private const string PopupAbout = "About";
	private const string PopupWorkflowActions = "WorkflowActions";
	private const string PopupCanvasMode = "CanvasMode";
	private const string PopupCommandInput = "CommandInput";

	private void InitializePopupManager()
	{
		_popupManager = new NexusPopupManager(CaptureAppKeyboardFocus, RestoreWebViewKeyboardFocus);
		_popupManager.Register(CommandMenuControl);
		_popupManager.Register(SettingsOverlayControl);
		_popupManager.Register(HelpOverlayControl);
		_popupManager.Register(AboutOverlayControl);
		_popupManager.Register(WorkflowActionsMenuControl);
		_popupManager.Register(CanvasModeMenuControl);
		_popupManager.Register(CommandInputControl);
	}

	private async Task SetCommandMenuVisible(bool isVisible)
		=> await _popupManager.SetVisibleAsync(
			PopupCommandMenu,
			isVisible,
			closeGroupsBeforeShow: [PopupGroupSmallMenu, PopupGroupOverlay]);

	private async Task SetSettingsOverlayVisible(bool isVisible)
		=> await _popupManager.SetVisibleAsync(
			PopupSettings,
			isVisible,
			closeGroupsBeforeShow: [PopupGroupSmallMenu, PopupGroupOverlay]);

	private async Task SetHelpOverlayVisible(bool isVisible)
		=> await _popupManager.SetVisibleAsync(
			PopupHelp,
			isVisible,
			closeGroupsBeforeShow: [PopupGroupSmallMenu, PopupGroupOverlay]);

	private async Task SetAboutOverlayVisible(bool isVisible)
		=> await _popupManager.SetVisibleAsync(
			PopupAbout,
			isVisible,
			closeGroupsBeforeShow: [PopupGroupSmallMenu, PopupGroupOverlay]);

	private async Task SetWorkflowActionsMenuVisible(bool isVisible)
	{
		if (isVisible)
		{
			var actionState = _tabController.GetActiveWorkflowActionState();
			WorkflowActionsMenuControl.SetActions(WorkflowActionCatalog.BuildMenuItems(actionState));
		}

		await _popupManager.SetVisibleAsync(
			PopupWorkflowActions,
			isVisible,
			new NexusPopupOpenContext(TopOffset: GetHeaderTopOffset()),
			PopupGroupSmallMenu);
	}

	private async Task SetCanvasModeMenuVisible(bool isVisible)
		=> await _popupManager.SetVisibleAsync(
			PopupCanvasMode,
			isVisible,
			closeGroupsBeforeShow: [PopupGroupSmallMenu]);

	private async Task SetCommandInputVisibleAsync(bool isVisible)
		=> await _popupManager.SetVisibleAsync(
			PopupCommandInput,
			isVisible,
			new NexusPopupOpenContext(CaptureFocus: false),
			PopupGroupSmallMenu);

	private async Task OpenNexusCommandConsoleAsync()
	{
		await SetCommandInputVisibleAsync(true);
	}

	internal async Task CloseAllPopupSurfacesAsync(bool restoreFocusOnClose = false)
	{
		NexusUiActionTrace.Record("MainPage", "Popup.CloseAll");
		await HideWorkflowDropdownAsync();
		await _popupManager.CloseAllAsync(restoreFocusOnClose);
	}

	private double GetHeaderTopOffset()
		=> _lastMeasuredHeaderHeight > 0 ? _lastMeasuredHeaderHeight : HeaderControl.Height;

	internal async Task SetShutdownBlockerVisibleAsync(
		bool isVisible,
		string title = "PREPARING RESTART",
		string detail = "Stopping probes and server process...")
	{
		NexusUiActionTrace.Record("MainPage", "ShutdownBlocker", isVisible ? title : "hidden");
		if (isVisible)
		{
			ShutdownBlockerTitleLabel.Text = title;
			ShutdownBlockerDetailLabel.Text = detail;
			ShutdownBlockerProgressBar.Progress = 0;
			InputBlockerOverlay.BackgroundColor = Colors.Transparent;
			InputBlockerOverlay.IsVisible = true;
			ShutdownBlockerBackdrop.Opacity = 0;
			ShutdownBlockerPanel.Opacity = 0;
			ShutdownBlockerPanel.Scale = ShutdownBlockerHiddenScale;
			ShutdownBlockerPanel.TranslationY = ShutdownBlockerHiddenOffsetY;
			StartShutdownBlockerLogoBounce();
			await Task.WhenAll(
				SafeAnimation.FadeToAsync(ShutdownBlockerBackdrop, 1, ShutdownBlockerBackdropShowLength, Easing.CubicOut, "ShutdownBlocker.Show"),
				SafeAnimation.FadeToAsync(ShutdownBlockerPanel, 1, ShutdownBlockerPanelFadeLength, Easing.CubicOut, "ShutdownBlocker.Show"),
				SafeAnimation.ScaleToAsync(ShutdownBlockerPanel, 1, ShutdownBlockerPanelTransformLength, Easing.CubicOut, "ShutdownBlocker.Show"),
				SafeAnimation.TranslateToAsync(ShutdownBlockerPanel, 0, 0, ShutdownBlockerPanelTransformLength, Easing.CubicOut, "ShutdownBlocker.Show"));
			return;
		}

		StopShutdownBlockerLogoBounce();
		if (!InputBlockerOverlay.IsVisible)
		{
			return;
		}

		await Task.WhenAll(
			SafeAnimation.FadeToAsync(ShutdownBlockerBackdrop, 0, ShutdownBlockerHideLength, Easing.CubicIn, "ShutdownBlocker.Hide"),
			SafeAnimation.FadeToAsync(ShutdownBlockerPanel, 0, ShutdownBlockerHideLength, Easing.CubicIn, "ShutdownBlocker.Hide"));
		InputBlockerOverlay.BackgroundColor = Colors.Transparent;
		InputBlockerOverlay.IsVisible = false;
	}

	private void UpdateShutdownBlockerProgress(double progress, string detail)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			ShutdownBlockerProgressBar.Progress = Math.Clamp(progress, 0, 1);
			ShutdownBlockerDetailLabel.Text = string.IsNullOrWhiteSpace(detail)
				? $"{Math.Clamp(progress, 0, 1):P0}"
				: $"{detail}{Environment.NewLine}{Math.Clamp(progress, 0, 1):P0}";
		});
	}

	private void StartShutdownBlockerLogoBounce()
	{
		SafeAnimation.AbortAnimation(this, ShutdownBlockerLogoBounceAnimationName, "ShutdownBlocker");
		ShutdownBlockerLogo.TranslationY = 0;
		ShutdownBlockerLogoGroundGlow.Opacity = ShutdownBlockerGroundGlowRestOpacity;
		ShutdownBlockerLogoGroundGlow.ScaleX = 1;

		SafeAnimation.Timeline(
			this,
			ShutdownBlockerLogoBounceAnimationName,
			16,
			ShutdownBlockerLogoBounceLength,
			Easing.Linear,
			() => InputBlockerOverlay.IsVisible,
			"ShutdownBlocker",
			new SafeAnimation.TimelineSegment(0, 0.232, value => ShutdownBlockerLogo.TranslationY = value, 0, -ShutdownBlockerLogoBounceHeight, Easing.CubicOut),
			new SafeAnimation.TimelineSegment(0.232, 0.5, value => ShutdownBlockerLogo.TranslationY = value, -ShutdownBlockerLogoBounceHeight, 0, Easing.CubicIn),
			new SafeAnimation.TimelineSegment(0.5, 0.732, value => ShutdownBlockerLogo.TranslationY = value, 0, -ShutdownBlockerLogoBounceHeight, Easing.CubicOut),
			new SafeAnimation.TimelineSegment(0.732, 1, value => ShutdownBlockerLogo.TranslationY = value, -ShutdownBlockerLogoBounceHeight, 0, Easing.CubicIn),
			new SafeAnimation.TimelineSegment(0, 0.232, value => ShutdownBlockerLogoGroundGlow.Opacity = value, ShutdownBlockerGroundGlowRestOpacity, ShutdownBlockerGroundGlowLiftOpacity, Easing.CubicOut),
			new SafeAnimation.TimelineSegment(0, 0.232, value => ShutdownBlockerLogoGroundGlow.ScaleX = value, 1, ShutdownBlockerGroundGlowLiftScale, Easing.CubicOut),
			new SafeAnimation.TimelineSegment(0.232, 0.5, value => ShutdownBlockerLogoGroundGlow.Opacity = value, ShutdownBlockerGroundGlowLiftOpacity, ShutdownBlockerGroundGlowRestOpacity, Easing.CubicIn),
			new SafeAnimation.TimelineSegment(0.232, 0.5, value => ShutdownBlockerLogoGroundGlow.ScaleX = value, ShutdownBlockerGroundGlowLiftScale, 1, Easing.CubicIn),
			new SafeAnimation.TimelineSegment(0.5, 0.732, value => ShutdownBlockerLogoGroundGlow.Opacity = value, ShutdownBlockerGroundGlowRestOpacity, ShutdownBlockerGroundGlowLiftOpacity, Easing.CubicOut),
			new SafeAnimation.TimelineSegment(0.5, 0.732, value => ShutdownBlockerLogoGroundGlow.ScaleX = value, 1, ShutdownBlockerGroundGlowLiftScale, Easing.CubicOut),
			new SafeAnimation.TimelineSegment(0.732, 1, value => ShutdownBlockerLogoGroundGlow.Opacity = value, ShutdownBlockerGroundGlowLiftOpacity, ShutdownBlockerGroundGlowRestOpacity, Easing.CubicIn),
			new SafeAnimation.TimelineSegment(0.732, 1, value => ShutdownBlockerLogoGroundGlow.ScaleX = value, ShutdownBlockerGroundGlowLiftScale, 1, Easing.CubicIn));
	}

	private void StopShutdownBlockerLogoBounce()
	{
		SafeAnimation.AbortAnimation(this, ShutdownBlockerLogoBounceAnimationName, "ShutdownBlocker");
		ShutdownBlockerLogo.TranslationY = 0;
		ShutdownBlockerLogoGroundGlow.Opacity = ShutdownBlockerGroundGlowRestOpacity;
		ShutdownBlockerLogoGroundGlow.ScaleX = 1;
	}

	private void ShowProductSetup()
	{
		_loadingOverlayController.ShowProductSetup();
	}

	private async void OnSettingsOverlayRestartServerRequested(object? sender, EventArgs e)
	{
		bool repairRuntimeBeforeBoot = e is SettingsRestartRequestedEventArgs settingsArgs
			&& settingsArgs.RepairRuntimeBeforeBoot;
		await RestartServerFromCommandMenuAsync(repairRuntimeBeforeBoot);
	}

	private async Task RestartServerFromCommandMenuAsync(bool repairRuntimeBeforeBoot = false)
	{
		await CloseAllPopupSurfacesAsync();
		await SetShutdownBlockerVisibleAsync(
			true,
			repairRuntimeBeforeBoot ? "REPAIR & RESTART" : "PREPARING RESTART",
			"Stopping GPU probes and ComfyUI server safely...");

		try
		{
			Log(repairRuntimeBeforeBoot
				? "[SYSTEM] Restart server requested with runtime repair."
				: "[SYSTEM] Restart server requested from Nexus menu.");

			UpdateShutdownBlockerProgress(0.2, "Preparing server lifecycle...");
			ServerLifecycleResult result = await _serverLifecycle.RunAsync(new ServerLifecycleRequest(
				ServerLifecycleMode.Restart,
				RepairRuntimeBeforeBoot: repairRuntimeBeforeBoot,
				OnServerStoppedAsync: TransitionFromShutdownToServerBootAsync));
			if (!result.IsSuccess)
			{
				throw new InvalidOperationException(result.Message);
			}

			await LoadingOverlayControl.CompleteServerLifecycleAsync(result);
			await SetShutdownBlockerVisibleAsync(false);
		}
		catch (Exception ex)
		{
			Log($"[SYSTEM] Restart server failed: {ex.GetType().Name} - {ex.Message}");
			await SetShutdownBlockerVisibleAsync(false);
			await DisplayAlertAsync(
				LocalizationManager.Text("server_config.restart_failed_title"),
				ex.Message,
				LocalizationManager.Text("common.ok"));
		}
		finally
		{
			if (InputBlockerOverlay.IsVisible)
			{
				await SetShutdownBlockerVisibleAsync(false);
			}
		}
	}

	private async Task ExecuteCommandMenuSettingsAsync()
	{
		await SetSettingsOverlayVisible(true);
	}

	private async Task ExecuteCommandMenuHelpAsync()
	{
		await SetHelpOverlayVisible(true);
	}

	private async Task ExecuteCommandMenuAboutAsync()
	{
		await SetCommandMenuVisible(false);
		var settings = SetupSettingsService.Instance.Settings;
		string serverUrl = GetBrowsableServerUrl(settings.ListenAddress, settings.ServerPort);
			AboutOverlayControl.SetDetails(
			SafeAppInfo.VersionString,
			ComfyPathResolver.ResolveConfiguredComfyPath(),
			serverUrl,
			settings.ServerPythonMode);
		await SetAboutOverlayVisible(true);
	}

	private async Task ExecuteCommandMenuExitAsync()
	{
		await CloseAllPopupSurfacesAsync();

		if (Application.Current is App nexusApp)
		{
			await nexusApp.RequestExitWithConfirmationAsync(Window);
			return;
		}

		var app = Application.Current;
		var window = Window ?? app?.Windows.FirstOrDefault();
		if (app != null && window != null)
		{
			app.CloseWindow(window);
		}
	}

	private static string GetBrowsableServerUrl(string listenAddress, int port)
	{
		string host = string.IsNullOrWhiteSpace(listenAddress) ? "127.0.0.1" : listenAddress.Trim();
		if (host is "0.0.0.0" or "::" or "*")
		{
			host = "127.0.0.1";
		}

		return $"http://{host}:{port}";
	}

	private async void OnSettingsOverlayCloseRequested(object? sender, EventArgs e)
	{
		await SetSettingsOverlayVisible(false);
	}

	private async void OnHelpOverlayCloseRequested(object? sender, EventArgs e)
	{
		await SetHelpOverlayVisible(false);
	}

	private async void OnAboutOverlayCloseRequested(object? sender, EventArgs e)
	{
		await SetAboutOverlayVisible(false);
	}

	private async void OnSettingsOverlayRuntimePurgeRequested(object? sender, EventArgs e)
	{
		await SetCommandMenuVisible(false);
		await SetSettingsOverlayVisible(false);
		_loadingOverlayController.ShowMaintenanceRecovery();
	}

	private async void OnSettingsOverlayRuntimeRestoreRequested(object? sender, RuntimeRestoreRequestedEventArgs e)
	{
		var service = ComfyInstallService.Instance;
		Action<string>? previousLogHandler = service.OnMessage;
		Action<double, string>? previousProgressHandler = service.OnProgress;
		try
		{
			await SetShutdownBlockerVisibleAsync(
				true,
				LocalizationManager.Text("settings.runtime_backup.blocker_title"),
				LocalizationManager.Text("settings.runtime_backup.blocker_preparing"));
			await SetCommandMenuVisible(false);
			await SetSettingsOverlayVisible(false);

			ComfyServerProcessInfo? processInfo = ComfyServerProcessRegistry.FindServerProcess();
			bool serverWasRunning = e.Request.ServerWasRunning || processInfo != null;

			service.OnMessage = message => Log(message);
			service.OnProgress = UpdateShutdownBlockerProgress;
			RuntimeRestoreResult? restoreResult = null;
			async Task<SetupStepResult> RestoreAsync(CancellationToken cancellationToken)
			{
				UpdateShutdownBlockerProgress(0, LocalizationManager.Text("settings.runtime_backup.blocker_merging"));
				restoreResult = await service.RestoreRuntimeBackupAsync(e.Request.Analysis, cancellationToken);
				return new SetupStepResult(
					restoreResult.IsSuccess,
					restoreResult.Message,
					0);
			}

			if (serverWasRunning)
			{
				ServerLifecycleResult lifecycleResult = await _serverLifecycle.RunAsync(new ServerLifecycleRequest(
					ServerLifecycleMode.Restart,
					MaintenanceAsync: RestoreAsync,
					OnServerStoppedAsync: TransitionFromShutdownToServerBootAsync));
				if (!lifecycleResult.IsSuccess)
				{
					throw new InvalidOperationException(lifecycleResult.Message);
				}

				await LoadingOverlayControl.CompleteServerLifecycleAsync(lifecycleResult);
			}
			else
			{
				await RestoreAsync(CancellationToken.None);
			}

			RuntimeRestoreResult result = restoreResult ?? new RuntimeRestoreResult(
				false,
				"Runtime restore did not produce a result.",
				0,
				e.Request.Analysis.AddCount + e.Request.Analysis.ReplaceCount);
			if (!result.IsSuccess)
			{
				Log($"[SYSTEM] Runtime restore failed: {result.Message}");
				await SetShutdownBlockerVisibleAsync(false);
				await SetSettingsOverlayVisible(true);
				e.Completion.TrySetResult(result);
				return;
			}

			UpdateShutdownBlockerProgress(1, LocalizationManager.Text("settings.runtime_backup.blocker_complete"));
			e.Completion.TrySetResult(result);
			await SetShutdownBlockerVisibleAsync(false);
			if (!serverWasRunning)
			{
				await SetSettingsOverlayVisible(true);
			}
		}
		catch (Exception ex)
		{
			Log($"[SYSTEM] Runtime restore hand-off failed: {ex.GetType().Name} - {ex.Message}");
			await SetShutdownBlockerVisibleAsync(false);
			await SetSettingsOverlayVisible(true);
			e.Completion.TrySetResult(new RuntimeRestoreResult(
				false,
				ex.Message,
				0,
				e.Request.Analysis.AddCount + e.Request.Analysis.ReplaceCount));
		}
		finally
		{
			service.OnMessage = previousLogHandler;
			service.OnProgress = previousProgressHandler;
			if (InputBlockerOverlay.IsVisible)
			{
				await SetShutdownBlockerVisibleAsync(false);
			}
		}
	}

	private async Task TransitionFromShutdownToServerBootAsync()
	{
		UpdateShutdownBlockerProgress(0.85, "Shutdown checklist complete. Preparing server boot...");
		await SetShutdownBlockerVisibleAsync(false);
		await _loadingOverlayController.PrepareServerRestartSurfaceAsync();
	}

	private async Task PrepareShellForServerInterruptionAsync(string reason)
	{
		Log($"[SYSTEM] Preparing shell for server interruption: {reason}");
		_loginSequence.Cancel();
		_latestOperations.Stop("media-asset-snapshot-burst");
		_isBooted = false;
		_bootReadyHandled = false;
		_stabilizedVisualStateApplied = false;
		HeaderControl.SetInstantQueueButtonStop(false);
		WorkspaceControl.HideBrowserSurface();
		await ResetWebViewForServerInterruptionAsync();
		await QuiesceShellRuntimeServicesAsync(reason);
	}

	private async Task ResetWebViewForServerInterruptionAsync()
	{
		try
		{
			await MainThread.InvokeOnMainThreadAsync(() =>
			{
				WorkspaceControl.BrowserView.Source = "about:blank";
			});
		}
		catch (Exception ex)
		{
			Log($"[SYSTEM] WebView detach skipped: {ex.GetType().Name} - {ex.Message}");
		}
	}

	private async Task ExecuteCommandInputAsync()
	{
		string command = CommandInputControl.GetCommandText().Trim().ToLowerInvariant();
		if (string.IsNullOrWhiteSpace(command))
		{
			await SetCommandInputVisibleAsync(false);
			return;
		}

		switch (command)
		{
			case "console":
			case "deck":
			case "log":
				await SetControlDeckVisibleAsync(true);
				break;

			case "hide console":
			case "hide deck":
			case "close console":
			case "console end":
				await SetControlDeckVisibleAsync(false);
				break;

			case "trace on":
				SetBridgeDiagnosticsEnabled(true);
				break;

			case "trace off":
				SetBridgeDiagnosticsEnabled(false);
				break;

			case "nexus login":
			case "login":
			case "setup":
				ShowProductSetup();
				break;
		}

		await SetCommandInputVisibleAsync(false);
	}

	private async void OnLogoClicked(object? sender, EventArgs e)
	{
		if (!_isBooted)
		{
			return;
		}

		await SetCommandMenuVisible(!CommandMenuControl.IsMenuVisible);
	}

	private async void OnLogoFiveClicked(object? sender, EventArgs e)
	{
		if (!_isBooted)
		{
			return;
		}

		await OpenNexusCommandConsoleAsync();
	}

	private void CaptureAppKeyboardFocus()
	{
		try
		{
			AppKeyboardFocusSink.Focus();
		}
		catch
		{
		}
	}

	private void RestoreWebViewKeyboardFocus()
	{
		try
		{
			if (_isBooted && WorkspaceControl?.BrowserView?.IsVisible == true)
			{
				PlatformManager.Current.WebView.Focus(WorkspaceControl.BrowserView);
			}
		}
		catch
		{
		}
	}

	private void OnNexusDialogClosed(NexusDialogReturnFocusTarget target)
	{
		switch (target)
		{
			case NexusDialogReturnFocusTarget.App:
				CaptureAppKeyboardFocus();
				break;
			case NexusDialogReturnFocusTarget.WebView:
				RestoreWebViewKeyboardFocus();
				break;
		}
	}

	private void ShowToast(string message, string type = "info", double topMargin = ToastDefaultTopMargin)
	{
		int version = ++_toastVersion;
		StopToastHoldTimer();

		MainThread.BeginInvokeOnMainThread(async () =>
		{
			try
			{
				ToastMessageLabel.Text = message;
				ToastAccentDot.Fill = type == "warn" ? Color.FromArgb("#FFFFC857") : Color.FromArgb("#FF8DE7FF");
				ToastBorder.Stroke = type == "warn" ? Color.FromArgb("#88FFC857") : Color.FromArgb("#668DE7FF");
				ToastBorder.Margin = new Thickness(0, topMargin, 0, 0);
				ToastBorder.TranslationY = ToastHiddenOffsetY;
				ToastBorder.Opacity = 0;
				ToastBorder.IsVisible = true;

				await Task.WhenAll(
					SafeAnimation.FadeToAsync(ToastBorder, 1, ToastShowLength, Easing.CubicOut, "Toast.Show"),
					SafeAnimation.TranslateToAsync(ToastBorder, 0, 0, ToastShowLength, Easing.CubicOut, "Toast.Show"));

				if (version == _toastVersion)
				{
					StartToastHoldTimer(version);
				}
			}
			catch (Exception ex)
			{
				LogThrottled("toast-error", $"Toast failed: {ex.Message}", 1000);
			}
		});
	}

	private void StartToastHoldTimer(int version)
	{
		StopToastHoldTimer();
		_toastHoldTimer = Dispatcher.CreateTimer();
		_toastHoldTimer.Interval = TimeSpan.FromMilliseconds(ToastHoldDelayMs);
		_toastHoldTimerTick = async (_, _) =>
		{
			StopToastHoldTimer();
			if (version != _toastVersion)
			{
				return;
			}

			try
			{
				await Task.WhenAll(
					SafeAnimation.FadeToAsync(ToastBorder, 0, ToastHideLength, Easing.CubicIn, "Toast.Hide"),
					SafeAnimation.TranslateToAsync(ToastBorder, 0, ToastHiddenOffsetY, ToastHideLength, Easing.CubicIn, "Toast.Hide"));
				if (version == _toastVersion)
				{
					ToastBorder.IsVisible = false;
				}
			}
			catch (Exception ex)
			{
				LogThrottled("toast-error", $"Toast hide failed: {ex.Message}", 1000);
			}
		};
		_toastHoldTimer.Tick += _toastHoldTimerTick;
		_toastHoldTimer.Start();
	}

	private void StopToastHoldTimer()
	{
		if (_toastHoldTimer is not null)
		{
			_toastHoldTimer.Stop();
			if (_toastHoldTimerTick is not null)
			{
				_toastHoldTimer.Tick -= _toastHoldTimerTick;
			}
		}

		_toastHoldTimer = null;
		_toastHoldTimerTick = null;
	}

	private async void ShowDropdown(bool isRow2)
	{
		WorkflowDropdownControl.PrepareToShow(isRow2);
		await WorkflowDropdownControl.AnimateShowAsync(isRow2);
	}

	private async void HideDropdown()
	{
		await HideWorkflowDropdownAsync();
	}

	private async Task HideWorkflowDropdownAsync()
	{
		if (!WorkflowDropdownControl.IsOpen)
		{
			return;
		}

		await WorkflowDropdownControl.AnimateHideAsync();
		WorkflowDropdownControl.CompleteHide();
	}

	private async void OnWorkflowActionsMenuDismissRequested(object? sender, EventArgs e)
	{
		await SetWorkflowActionsMenuVisible(false);
	}

}
