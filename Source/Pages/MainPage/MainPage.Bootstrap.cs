using Microsoft.Maui.Controls;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private static readonly Color LoadingInfoColor = Color.FromArgb("#00e5ff");
	private static readonly Color LoadingSuccessColor = Color.FromArgb("#00ff88");
	private static readonly Color LoadingWarningColor = Color.FromArgb("#ff5f7a");
	private const int StartupSplashBounceCount = 3;
	private const double StartupSplashBounceHeight = 30;
	private const double StartupSplashExitOffsetY = -190;
	private const uint StartupSplashBounceUpLength = 520;
	private const uint StartupSplashBounceDownLength = 600;
	private const uint StartupSplashExitLength = 560;
	private const uint StartupSplashFadeOutLength = 440;
	private const uint StartupSplashBackgroundFadeOutLength = 180;

	private async Task HideStartupSplashAsync(bool includeBounce = true)
	{
		await MainThread.InvokeOnMainThreadAsync(async () =>
		{
			if (_startupSplashHiding || StartupSplashOverlay == null || !StartupSplashOverlay.IsVisible)
			{
				return;
			}

			try
			{
				_startupSplashHiding = true;
				StartupSplashOverlay.InputTransparent = false;
				SafeAnimation.CancelAnimations("MainPage.StartupSplash", StartupSplashOverlay, StartupSplashLogo);
				StartupSplashLogo.Opacity = 1;
				StartupSplashLogo.Scale = 1;
				StartupSplashLogo.TranslationY = 0;

				if (includeBounce)
				{
					await PlayStartupSplashBouncesAsync(StartupSplashBounceCount);
				}

				await Task.WhenAll(
					SafeAnimation.TranslateToAsync(StartupSplashLogo, 0, StartupSplashExitOffsetY, StartupSplashExitLength, Easing.CubicIn, "MainPage.StartupSplash"),
					SafeAnimation.FadeToAsync(StartupSplashLogo, 0, StartupSplashFadeOutLength, Easing.CubicIn, "MainPage.StartupSplash"));

				await SafeAnimation.FadeToAsync(StartupSplashOverlay, 0, StartupSplashBackgroundFadeOutLength, Easing.CubicInOut, "MainPage.StartupSplash");

				StartupSplashOverlay.IsVisible = false;
				StartupSplashOverlay.InputTransparent = true;
				StartupSplashLogo.Scale = 1;
				StartupSplashLogo.Opacity = 1;
				StartupSplashLogo.TranslationY = 0;
			}
			finally
			{
				_startupSplashHiding = false;
			}
		});
	}

	private async Task ShowStartupSplashAsync()
	{
		WriteServerBootSetupRouteTiming("Splash active route requested.");
		await MainThread.InvokeOnMainThreadAsync(() =>
		{
			if (StartupSplashOverlay == null)
			{
				return;
			}

			SafeAnimation.CancelAnimations("MainPage.StartupSplash", StartupSplashOverlay, StartupSplashLogo);
			StartupSplashOverlay.IsVisible = true;
			StartupSplashOverlay.Opacity = 1;
			StartupSplashOverlay.InputTransparent = false;
			StartupSplashLogo.Opacity = 1;
			StartupSplashLogo.Scale = 1;
			StartupSplashLogo.TranslationY = 0;
		});
		WriteServerBootSetupRouteTiming("Splash visual armed; yielding for first bounce frame.");

		await NexusUiFrame.AwaitDispatcherTurnAsync(this, "STARTUP:SPLASH_ENTRANCE_FRAME");
		WriteServerBootSetupRouteTiming("Splash first frame yielded; bounce can start.");
	}

	private void StartServerBootSetupRouteTiming()
	{
		_serverBootSetupRouteGeneration++;
		_serverBootSetupRouteStopwatch = System.Diagnostics.Stopwatch.StartNew();
	}

	private void WriteServerBootSetupRouteTiming(string message)
	{
		if (_serverBootSetupRouteStopwatch is not { } stopwatch)
		{
			NexusLog.Info($"[SETUP_ROUTE] {message}");
			return;
		}

		NexusLog.Info($"[SETUP_ROUTE #{_serverBootSetupRouteGeneration:00} +{stopwatch.ElapsedMilliseconds:00000}ms] {message}");
	}

	private async Task PlayStartupSplashBouncesUntilReadyAsync(Task readinessTask)
	{
		ArgumentNullException.ThrowIfNull(readinessTask);
		int bounceCount = 0;
		do
		{
			bounceCount++;
			WriteServerBootSetupRouteTiming($"Splash bounce {bounceCount} starting.");
			await PlayStartupSplashBounceAsync();
		}
		while (bounceCount < StartupSplashBounceCount || !readinessTask.IsCompleted);

		WriteServerBootSetupRouteTiming($"Splash bounce {bounceCount} completed after route readiness.");
	}

	private async Task PlayStartupSplashBouncesAsync(int count)
	{
		for (int index = 0; index < count; index++)
		{
			await PlayStartupSplashBounceAsync();
		}
	}

	private async Task PlayStartupSplashBounceAsync()
	{
		if (StartupSplashOverlay == null || !StartupSplashOverlay.IsVisible)
		{
			return;
		}

		await SafeAnimation.TranslateToAsync(
			StartupSplashLogo,
			0,
			-StartupSplashBounceHeight,
			StartupSplashBounceUpLength,
			Easing.CubicOut,
			"MainPage.StartupSplash");

		await SafeAnimation.TranslateToAsync(
			StartupSplashLogo,
			0,
			0,
			StartupSplashBounceDownLength,
			Easing.CubicIn,
			"MainPage.StartupSplash");
	}

	private void ResetLoadingVisualState()
	{
		if (LoadingOverlayControl == null) return;

		_loadingOverlayController.ResetVisualState("OK", LoadingSuccessColor);
	}

	private Task UpdateSystemLoadingStateAsync(bool isLoading)
		=> isLoading
			? UiThread.InvokeAsync(BeginSystemLoadingOnMainThreadAsync, "MAIN_PAGE:BEGIN_SYSTEM_LOADING")
			: PrepareSystemShellForStableRevealAsync();

	private Task PrepareSystemShellForStableRevealAsync()
		=> UiThread.InvokeAsync(PrepareSystemShellForStableRevealOnMainThreadAsync, "MAIN_PAGE:PREPARE_SHELL_REVEAL");

	private Task CompleteSystemLoadingRevealAsync()
		=> UiThread.InvokeAsync(CompleteSystemLoadingRevealOnMainThreadAsync, "MAIN_PAGE:COMPLETE_SHELL_REVEAL");

	private Task BeginSystemLoadingOnMainThreadAsync()
	{
		if (LoadingOverlayControl == null || HeaderControl == null || WorkspaceControl == null)
		{
			return Task.CompletedTask;
		}

		_loginSequence.Phase(BootPhase.LoadingOverlayStart);
		_isSystemLoading = true;
		_stabilizedVisualStateApplied = false;
		ApplyLeftChromeVisibilityState();

		_loadingOverlayController.SetBlockingState(isVisible: true, opacity: 1, inputTransparent: false);

		HeaderControl.SetRightPaneOpacity(1);
		WorkspaceControl.SetChromeOpacity(1);

		ResetLoadingVisualState();

		HeaderControl.SetWorkflowStatusBarState(true, inputTransparent: true);
		HeaderControl.SetLoadingHaloState(0.4, 1.0);
		HeaderControl.SetTabRowsOpacity(1);
		HeaderControl.SetLogoInputTransparent(true);
		_loadingOverlayController.Hold(
			LocalizationManager.Text("loading.preparing_nexus_title"),
			LocalizationManager.Text("loading.preparing_nexus_detail"),
			LocalizationManager.Text("loading.preparing_nexus_status"),
			LoadingInfoColor,
			progress: 0.08);

		StartOverlayAnimations();
		Log("[Nexus] Startup preparation started.");
		return Task.CompletedTask;
	}

	private async Task PrepareSystemShellForStableRevealOnMainThreadAsync()
	{
		if (LoadingOverlayControl == null || HeaderControl == null || WorkspaceControl == null)
		{
			return;
		}

		if (_stabilizedVisualStateApplied)
		{
			return;
		}

		_stabilizedVisualStateApplied = true;
		_loginSequence.Phase(BootPhase.LoadingOverlayStabilizing);

		InputBlockerOverlay.IsVisible = true;
		WorkspaceControl.SetBrowserSurfaceState(isVisible: true, opacity: 1);
		ApplyLeftChromeVisibilityState();

		await UiThread.YieldDispatcherFramesAsync(2, "MAIN_PAGE:SHELL_REVEAL_LAYOUT");
	}

	private async Task CompleteSystemLoadingRevealOnMainThreadAsync()
	{
		if (!_stabilizedVisualStateApplied || LoadingOverlayControl == null || HeaderControl == null)
		{
			return;
		}

		await _loadingOverlayController.CompleteStableRevealAsync();
		_loginSequence.Phase(BootPhase.LoadingOverlayHidden);
		HeaderControl.SetLogoInputTransparent(false);

		InputBlockerOverlay.IsVisible = false;
		_isSuccessSequenceActive = false;
		_isSystemLoading = false;

		ApplyLeftChromeVisibilityState();
		if (HeaderControl.GetRootHeight() > 0)
		{
			_lastMeasuredHeaderHeight = HeaderControl.GetRootHeight();
		}
		HeaderControl.SetWorkflowStatusBarState(true, inputTransparent: false);
		_ = AwakeWebViewKeyboardAfterStableAsync();
		Log("[Nexus] Workspace ready.");
		_loginSequence.End(BootPhase.StableCompleted);
	}

	/// <summary>
	/// Primes the WebView keyboard target once after the shell reveal so the first native relay reaches ComfyUI.
	/// </summary>
	private async Task AwakeWebViewKeyboardAfterStableAsync()
	{
		try
		{
			if (_isBooted && WorkspaceControl?.BrowserSurface?.IsVisible == true)
			{
				WorkspaceControl.BrowserSurface.FocusBrowserInput();
				await _webViewBridge.AwakeKeyboardRelayAsync();
			}
		}
		catch
		{
		}
	}

	private void StartOverlayAnimations()
	{
		if (LoadingOverlayControl == null || HeaderControl == null) return;
		_loadingOverlayController.StartLoopAnimations();
	}
}
