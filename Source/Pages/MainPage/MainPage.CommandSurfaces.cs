using Microsoft.Maui.Controls;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Views;
using ComfyUI_Nexus.Views.Overlays;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private const int CommandInputRefocusDelayMs = 50;
	private const int ToastHoldDelayMs = 1500;
	private const string ShutdownBlockerLogoBounceAnimationName = "ShutdownBlockerLogoBounce";
	private const double ShutdownBlockerHiddenScale = 0.94;
	private const double ShutdownBlockerHiddenOffsetY = 12;
	private const double ShutdownBlockerLogoBounceHeight = 14;
	private const double ShutdownBlockerGroundGlowRestOpacity = 0.24;
	private const double ShutdownBlockerGroundGlowLiftOpacity = 0.1;
	private const double ShutdownBlockerGroundGlowLiftScale = 0.72;
	private const double ToastHiddenOffsetY = -8;
	private const uint ShutdownBlockerBackdropShowLength = 150;
	private const uint ShutdownBlockerPanelFadeLength = 170;
	private const uint ShutdownBlockerPanelTransformLength = 180;
	private const uint ShutdownBlockerHideLength = 90;
	private const uint ShutdownBlockerLogoBounceLength = 2240;
	private const uint ToastShowLength = 120;
	private const uint ToastHideLength = 160;

	private async Task SetCommandMenuVisible(bool isVisible)
	{
		if (isVisible)
		{
			await SetWorkflowActionsMenuVisible(false);
			await SetSettingsOverlayVisible(false);
			await SetHelpOverlayVisible(false);
			await SetAboutOverlayVisible(false);
		}

		if (CommandMenuControl.IsShown(isVisible))
		{
			return;
		}

		if (isVisible)
		{
			CommandMenuControl.PrepareToShow();
			CaptureAppKeyboardFocus();
			await CommandMenuControl.AnimateShowAsync();
			CaptureAppKeyboardFocus();
		}
		else
		{
			CommandMenuControl.PrepareToHide();
			await CommandMenuControl.AnimateHideAsync();
			CommandMenuControl.ResetHiddenState();
			RestoreWebViewKeyboardFocus();
		}
	}

	private async Task SetSettingsOverlayVisible(bool isVisible)
		=> await SetExclusiveOverlayVisibleAsync(
			isVisible,
			SettingsOverlayControl.IsShown,
			SettingsOverlayControl.PrepareToShow,
			SettingsOverlayControl.AnimateShowAsync,
			SettingsOverlayControl.PrepareToHide,
			SettingsOverlayControl.AnimateHideAsync,
			SettingsOverlayControl.ResetHiddenState,
			closePeerOverlayAsync: CloseHelpAndAboutOverlaysAsync);

	private async Task SetHelpOverlayVisible(bool isVisible)
		=> await SetExclusiveOverlayVisibleAsync(
			isVisible,
			HelpOverlayControl.IsShown,
			HelpOverlayControl.PrepareToShow,
			HelpOverlayControl.AnimateShowAsync,
			HelpOverlayControl.PrepareToHide,
			HelpOverlayControl.AnimateHideAsync,
			HelpOverlayControl.ResetHiddenState,
			closePeerOverlayAsync: CloseSettingsAndAboutOverlaysAsync);

	private async Task SetAboutOverlayVisible(bool isVisible)
		=> await SetExclusiveOverlayVisibleAsync(
			isVisible,
			AboutOverlayControl.IsShown,
			AboutOverlayControl.PrepareToShow,
			AboutOverlayControl.AnimateShowAsync,
			AboutOverlayControl.PrepareToHide,
			AboutOverlayControl.AnimateHideAsync,
			AboutOverlayControl.ResetHiddenState,
			closePeerOverlayAsync: CloseSettingsAndHelpOverlaysAsync);

	private async Task CloseSettingsAndHelpOverlaysAsync()
	{
		await SetSettingsOverlayVisible(false);
		await SetHelpOverlayVisible(false);
	}

	private async Task CloseHelpAndAboutOverlaysAsync()
	{
		await SetHelpOverlayVisible(false);
		await SetAboutOverlayVisible(false);
	}

	private async Task CloseSettingsAndAboutOverlaysAsync()
	{
		await SetSettingsOverlayVisible(false);
		await SetAboutOverlayVisible(false);
	}

	private async Task SetExclusiveOverlayVisibleAsync(
		bool isVisible,
		Func<bool, bool> isShown,
		Action prepareToShow,
		Func<Task> animateShowAsync,
		Action prepareToHide,
		Func<Task> animateHideAsync,
		Action resetHiddenState,
		Func<Task> closePeerOverlayAsync)
	{
		if (isShown(isVisible))
		{
			return;
		}

		if (isVisible)
		{
			await SetCommandMenuVisible(false);
			await SetWorkflowActionsMenuVisible(false);
			await closePeerOverlayAsync();
			prepareToShow();
			CaptureAppKeyboardFocus();
			await animateShowAsync();
			CaptureAppKeyboardFocus();
		}
		else
		{
			prepareToHide();
			await animateHideAsync();
			resetHiddenState();
			RestoreWebViewKeyboardFocus();
		}
	}

	private async Task SetWorkflowActionsMenuVisible(bool isVisible)
	{
		if (WorkflowActionsMenuControl.IsShown(isVisible))
		{
			return;
		}

		if (isVisible)
		{
			await SetCommandMenuVisible(false);
			var actionState = _tabController.GetActiveWorkflowActionState();
			WorkflowActionsMenuControl.SetActions(WorkflowActionCatalog.BuildMenuItems(actionState));

			double topOffset = _lastMeasuredHeaderHeight > 0 ? _lastMeasuredHeaderHeight : HeaderControl.Height;
			WorkflowActionsMenuControl.PrepareToShow(topOffset);
			CaptureAppKeyboardFocus();
			await WorkflowActionsMenuControl.AnimateShowAsync();
			CaptureAppKeyboardFocus();
		}
		else
		{
			WorkflowActionsMenuControl.PrepareToHide();
			await WorkflowActionsMenuControl.AnimateHideAsync();
			WorkflowActionsMenuControl.ResetHiddenState();
			RestoreWebViewKeyboardFocus();
		}
	}

	private async Task SetCanvasModeMenuVisible(bool isVisible)
	{
		if (CanvasModeMenuControl.IsShown(isVisible))
		{
			return;
		}

		if (isVisible)
		{
			await SetWorkflowActionsMenuVisible(false);
			CanvasModeMenuControl.PrepareToShow();
			CaptureAppKeyboardFocus();
			await CanvasModeMenuControl.AnimateShowAsync();
			CaptureAppKeyboardFocus();
		}
		else
		{
			CanvasModeMenuControl.PrepareToHide();
			await CanvasModeMenuControl.AnimateHideAsync();
			CanvasModeMenuControl.ResetHiddenState();
			RestoreWebViewKeyboardFocus();
		}
	}

	private async Task SetCommandInputVisibleAsync(bool isVisible)
	{
		if (CommandInputControl.IsOverlayAtState(isVisible))
		{
			return;
		}

		if (isVisible)
		{
			CommandInputControl.PrepareToShow();
			CommandInputControl.FocusInput();
			await CommandInputControl.AnimateShowAsync();
			await Task.Delay(CommandInputRefocusDelayMs);
			CommandInputControl.FocusInput();
		}
		else
		{
			CommandInputControl.PrepareToHide();
			await CommandInputControl.AnimateHideAsync();
			CommandInputControl.ResetHiddenState();
			RestoreWebViewKeyboardFocus();
		}
	}

	private async Task OpenNexusCommandConsoleAsync()
	{
		await SetCommandMenuVisible(false);
		await SetCommandInputVisibleAsync(true);
	}

	internal async Task SetShutdownBlockerVisibleAsync(
		bool isVisible,
		string title = "PREPARING RESTART",
		string detail = "Stopping probes and server process...")
	{
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
				ShutdownBlockerBackdrop.FadeToAsync(1, ShutdownBlockerBackdropShowLength, Easing.CubicOut),
				ShutdownBlockerPanel.FadeToAsync(1, ShutdownBlockerPanelFadeLength, Easing.CubicOut),
				ShutdownBlockerPanel.ScaleToAsync(1, ShutdownBlockerPanelTransformLength, Easing.CubicOut),
				ShutdownBlockerPanel.TranslateToAsync(0, 0, ShutdownBlockerPanelTransformLength, Easing.CubicOut));
			return;
		}

		StopShutdownBlockerLogoBounce();
		if (!InputBlockerOverlay.IsVisible)
		{
			return;
		}

		await Task.WhenAll(
			ShutdownBlockerBackdrop.FadeToAsync(0, ShutdownBlockerHideLength, Easing.CubicIn),
			ShutdownBlockerPanel.FadeToAsync(0, ShutdownBlockerHideLength, Easing.CubicIn));
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
		this.AbortAnimation(ShutdownBlockerLogoBounceAnimationName);
		ShutdownBlockerLogo.TranslationY = 0;
		ShutdownBlockerLogoGroundGlow.Opacity = ShutdownBlockerGroundGlowRestOpacity;
		ShutdownBlockerLogoGroundGlow.ScaleX = 1;

		var bounce = new Animation();
		bounce.Add(0, 0.232, new Animation(
			value => ShutdownBlockerLogo.TranslationY = value,
			0,
			-ShutdownBlockerLogoBounceHeight,
			Easing.CubicOut));
		bounce.Add(0.232, 0.5, new Animation(
			value => ShutdownBlockerLogo.TranslationY = value,
			-ShutdownBlockerLogoBounceHeight,
			0,
			Easing.CubicIn));
		bounce.Add(0.5, 0.732, new Animation(
			value => ShutdownBlockerLogo.TranslationY = value,
			0,
			-ShutdownBlockerLogoBounceHeight,
			Easing.CubicOut));
		bounce.Add(0.732, 1, new Animation(
			value => ShutdownBlockerLogo.TranslationY = value,
			-ShutdownBlockerLogoBounceHeight,
			0,
			Easing.CubicIn));
		AddShutdownBlockerGroundGlowBounce(bounce, 0, 0.232);
		AddShutdownBlockerGroundGlowReturn(bounce, 0.232, 0.5);
		AddShutdownBlockerGroundGlowBounce(bounce, 0.5, 0.732);
		AddShutdownBlockerGroundGlowReturn(bounce, 0.732, 1);
		bounce.Commit(
			this,
			ShutdownBlockerLogoBounceAnimationName,
			16,
			ShutdownBlockerLogoBounceLength,
			Easing.Linear,
			repeat: () => InputBlockerOverlay.IsVisible);
	}

	private void AddShutdownBlockerGroundGlowBounce(Animation bounce, double start, double end)
	{
		bounce.Add(start, end, new Animation(
			value => ShutdownBlockerLogoGroundGlow.Opacity = value,
			ShutdownBlockerGroundGlowRestOpacity,
			ShutdownBlockerGroundGlowLiftOpacity,
			Easing.CubicOut));
		bounce.Add(start, end, new Animation(
			value => ShutdownBlockerLogoGroundGlow.ScaleX = value,
			1,
			ShutdownBlockerGroundGlowLiftScale,
			Easing.CubicOut));
	}

	private void AddShutdownBlockerGroundGlowReturn(Animation bounce, double start, double end)
	{
		bounce.Add(start, end, new Animation(
			value => ShutdownBlockerLogoGroundGlow.Opacity = value,
			ShutdownBlockerGroundGlowLiftOpacity,
			ShutdownBlockerGroundGlowRestOpacity,
			Easing.CubicIn));
		bounce.Add(start, end, new Animation(
			value => ShutdownBlockerLogoGroundGlow.ScaleX = value,
			ShutdownBlockerGroundGlowLiftScale,
			1,
			Easing.CubicIn));
	}

	private void StopShutdownBlockerLogoBounce()
	{
		this.AbortAnimation(ShutdownBlockerLogoBounceAnimationName);
		ShutdownBlockerLogo.TranslationY = 0;
		ShutdownBlockerLogoGroundGlow.Opacity = ShutdownBlockerGroundGlowRestOpacity;
		ShutdownBlockerLogoGroundGlow.ScaleX = 1;
	}

	private void ShowProductSetup()
	{
		_loadingOverlayController.ShowProductSetup();
	}

	private async void OnCommandMenuRestartServerRequested(object? sender, EventArgs e)
	{
		bool repairRuntimeBeforeBoot = e is SettingsRestartRequestedEventArgs settingsArgs
			&& settingsArgs.RepairRuntimeBeforeBoot;
		await RestartServerFromCommandMenuAsync(repairRuntimeBeforeBoot);
	}

	private async Task RestartServerFromCommandMenuAsync(bool repairRuntimeBeforeBoot = false)
	{
		if (_isRebooting)
		{
			return;
		}

		_isRebooting = true;
		bool handedOffToLoadingOverlay = false;
		await SetShutdownBlockerVisibleAsync(
			true,
			repairRuntimeBeforeBoot ? "REPAIR & RESTART" : "PREPARING RESTART",
			"Stopping GPU probes and ComfyUI server safely...");

		try
		{
			await SetCommandMenuVisible(false);
			await SetSettingsOverlayVisible(false);
			await Task.Yield();

			Log(repairRuntimeBeforeBoot
				? "[SYSTEM] Restart server requested with runtime repair."
				: "[SYSTEM] Restart server requested from Nexus menu.");

			await PrepareShellForServerInterruptionAsync("Restart hand-off requested.");

			var processInfo = await Task.Run(ComfyServerProcessRegistry.FindServerProcess);
			if (processInfo != null)
			{
				Log($"[SYSTEM] Terminating server process before restart: {processInfo.ProcessName} ({processInfo.ProcessId}).");
				await ComfyServerProcessRegistry.ShutdownAsync(processInfo, TimeSpan.FromSeconds(5));
				await WaitForServerPortClosedBeforeRestartAsync();
			}
			else
			{
				Log("[SYSTEM] No active server process detected before restart.");
			}

			_loadingOverlayController.RestartServerLaunch(repairRuntimeBeforeBoot);
			handedOffToLoadingOverlay = true;
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
			if (!handedOffToLoadingOverlay && InputBlockerOverlay.IsVisible)
			{
				await SetShutdownBlockerVisibleAsync(false);
			}

			_isRebooting = false;
		}
	}

	private async Task WaitForServerPortClosedBeforeRestartAsync()
	{
		var settings = SetupSettingsService.Instance.Settings;
		string host = Uri.TryCreate(GetBrowsableServerUrl(settings.ListenAddress, settings.ServerPort), UriKind.Absolute, out var uri)
			? uri.Host
			: "127.0.0.1";

		var pollingDelay = TimeSpan.FromMilliseconds(Math.Max(50, settings.PortProbeIntervalMilliseconds));
		bool closed = await PortProbeService.WaitUntilClosedAsync(
			host,
			settings.ServerPort,
			TimeSpan.FromSeconds(4),
			pollingDelay,
			CancellationToken.None);

		Log(closed
			? $"[SYSTEM] Server port {settings.ServerPort} closed. Restart hand-off is clear."
			: $"[SYSTEM] Server port {settings.ServerPort} still appears open after shutdown wait; continuing with readiness checks.");
	}

	private async void OnCommandMenuSettingsRequested(object? sender, EventArgs e)
	{
		await SetSettingsOverlayVisible(true);
	}

	private async void OnCommandMenuHelpRequested(object? sender, EventArgs e)
	{
		await SetHelpOverlayVisible(true);
	}

	private async void OnCommandMenuAboutRequested(object? sender, EventArgs e)
	{
		await SetCommandMenuVisible(false);
		var settings = SetupSettingsService.Instance.Settings;
		string serverUrl = GetBrowsableServerUrl(settings.ListenAddress, settings.ServerPort);
		AboutOverlayControl.SetDetails(
			AppInfo.Current.VersionString,
			ComfyInstallService.ComfyPath,
			serverUrl,
			settings.ServerPythonMode);
		await SetAboutOverlayVisible(true);
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
		await PrepareShellForServerInterruptionAsync("Runtime maintenance recovery requested.");
		_loadingOverlayController.ShowMaintenanceRecovery();
	}

	private async void OnSettingsOverlayRuntimeRestoreRequested(object? sender, RuntimeRestoreRequestedEventArgs e)
	{
		if (_isRebooting)
		{
			e.Completion.TrySetResult(new RuntimeRestoreResult(
				false,
				"Another server lifecycle operation is already running.",
				0,
				e.Request.Analysis.AddCount + e.Request.Analysis.ReplaceCount));
			return;
		}

		_isRebooting = true;
		bool handedOffToLoadingOverlay = false;
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
			await Task.Yield();

			var processInfo = await Task.Run(ComfyServerProcessRegistry.FindServerProcess);
			bool serverWasRunning = e.Request.ServerWasRunning || processInfo != null;
			if (serverWasRunning)
			{
				await PrepareShellForServerInterruptionAsync("Runtime restore requested.");
			}
			if (processInfo != null)
			{
				UpdateShutdownBlockerProgress(0, LocalizationManager.Text("settings.runtime_backup.blocker_stopping_server"));
				await ComfyServerProcessRegistry.ShutdownAsync(processInfo, TimeSpan.FromSeconds(5));
				await WaitForServerPortClosedBeforeRestartAsync();
				if (await Task.Run(ComfyServerProcessRegistry.FindServerProcess) != null)
				{
					throw new InvalidOperationException("The ComfyUI server did not stop. Runtime restore was not started.");
				}
			}

			service.OnMessage = message => Log(message);
			service.OnProgress = UpdateShutdownBlockerProgress;
			UpdateShutdownBlockerProgress(0, LocalizationManager.Text("settings.runtime_backup.blocker_merging"));
			RuntimeRestoreResult result = await service.RestoreRuntimeBackupAsync(
				e.Request.Analysis,
				CancellationToken.None);
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
			if (serverWasRunning)
			{
				_loadingOverlayController.RestartServerLaunch();
				handedOffToLoadingOverlay = true;
				await SetShutdownBlockerVisibleAsync(false);
			}
			else
			{
				await SetShutdownBlockerVisibleAsync(false);
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
			if (!handedOffToLoadingOverlay && InputBlockerOverlay.IsVisible)
			{
				await SetShutdownBlockerVisibleAsync(false);
			}
			_isRebooting = false;
		}
	}

	private async Task PrepareShellForServerInterruptionAsync(string reason)
	{
		Log($"[SYSTEM] Preparing shell for server interruption: {reason}");
		_loginSequence.Cancel();
		_isBooted = false;
		_bootReadyHandled = false;
		_stabilizedVisualStateApplied = false;
		HeaderControl.SetInstantQueueButtonStop(false);
		WorkspaceControl.HideBrowserSurface();
		await ResetWebViewForServerInterruptionAsync();
		await GpuDiscoveryService.ShutdownAsync();
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

	private void ShowToast(string message, string type = "info")
	{
		_toastCts?.Cancel();
		var cts = new CancellationTokenSource();
		_toastCts = cts;

		MainThread.BeginInvokeOnMainThread(async () =>
		{
			try
			{
				ToastMessageLabel.Text = message;
				ToastAccentDot.Fill = type == "warn" ? Color.FromArgb("#FFFFC857") : Color.FromArgb("#FF8DE7FF");
				ToastBorder.Stroke = type == "warn" ? Color.FromArgb("#88FFC857") : Color.FromArgb("#668DE7FF");
				ToastBorder.TranslationY = ToastHiddenOffsetY;
				ToastBorder.Opacity = 0;
				ToastBorder.IsVisible = true;

				await Task.WhenAll(
					ToastBorder.FadeToAsync(1, ToastShowLength, Easing.CubicOut),
					ToastBorder.TranslateToAsync(0, 0, ToastShowLength, Easing.CubicOut));

				await Task.Delay(ToastHoldDelayMs, cts.Token);

				await Task.WhenAll(
					ToastBorder.FadeToAsync(0, ToastHideLength, Easing.CubicIn),
					ToastBorder.TranslateToAsync(0, ToastHiddenOffsetY, ToastHideLength, Easing.CubicIn));

				if (!cts.IsCancellationRequested)
				{
					ToastBorder.IsVisible = false;
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				LogThrottled("toast-error", $"Toast failed: {ex.Message}", 1000);
			}
		});
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

	private async void OnCanvasModeMenuDismissRequested(object? sender, EventArgs e)
	{
		await SetCanvasModeMenuVisible(false);
	}
}
