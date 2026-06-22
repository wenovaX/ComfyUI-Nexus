using Microsoft.Maui.Controls;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Platform;

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

	private async Task HideStartupSplashAsync()
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
				StartupSplashOverlay.AbortAnimation("FadeTo");
				StartupSplashLogo.AbortAnimation("TranslateTo");
				StartupSplashLogo.Opacity = 1;
				StartupSplashLogo.Scale = 1;
				StartupSplashLogo.TranslationY = 0;

				await PlayStartupSplashBounceAsync();

				await Task.WhenAll(
					StartupSplashLogo.TranslateToAsync(0, StartupSplashExitOffsetY, StartupSplashExitLength, Easing.CubicIn),
					StartupSplashLogo.FadeToAsync(0, StartupSplashFadeOutLength, Easing.CubicIn));

				await StartupSplashOverlay.FadeToAsync(0, StartupSplashBackgroundFadeOutLength, Easing.CubicInOut);

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

	private async Task PlayStartupSplashBounceAsync()
	{
		for (int i = 0; i < StartupSplashBounceCount; i++)
		{
			if (StartupSplashOverlay == null || !StartupSplashOverlay.IsVisible)
			{
				return;
			}

			await StartupSplashLogo.TranslateToAsync(
				0,
				-StartupSplashBounceHeight,
				StartupSplashBounceUpLength,
				Easing.CubicOut);

			await StartupSplashLogo.TranslateToAsync(
				0,
				0,
				StartupSplashBounceDownLength,
				Easing.CubicIn);
		}
	}

	private void SetLoadingStatus(string text, Color? textColor = null)
	{
		if (LoadingOverlayControl == null) return;
		_loadingOverlayController.Message(string.Empty, string.Empty, text, textColor ?? LoadingInfoColor);
	}

	private void ResetLoadingVisualState()
	{
		if (LoadingOverlayControl == null) return;

		_loadingOverlayController.ResetVisualState("OK", LoadingSuccessColor, LoadingInfoColor);
	}

	private void UpdateSystemLoadingState(bool isLoading)
	{
		MainThread.BeginInvokeOnMainThread(async () =>
		{
			if (LoadingOverlayControl == null || HeaderControl == null || WorkspaceControl == null) return;

			if (isLoading)
			{
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
					"Nexus Shell Startup",
					"Preparing local workspace and connecting the WebView bridge.",
					"INITIALIZING NEXUS SHELL...",
					LoadingInfoColor,
					progress: 0.08);

				StartOverlayAnimations();
				Log("NEXUS SYSTEM: Neural Link Sync Started.");
			}
			else
			{
				if (_stabilizedVisualStateApplied) return;
				_stabilizedVisualStateApplied = true;
				_loginSequence.Phase(BootPhase.LoadingOverlayStabilizing);

				InputBlockerOverlay.IsVisible = true;
				WorkspaceControl.SetBrowserSurfaceState(isVisible: true, opacity: 1);
				ApplyLeftChromeVisibilityState();

				await Task.Delay(80);

				await _loadingOverlayController.FadeOutForStableAsync();

				_loadingOverlayController.HideBlockingSurface();
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
				Log("NEXUS SYSTEM: Neural Link Stabilized.");
				_loginSequence.End(BootPhase.StableCompleted);
			}
		});
	}

	/// <summary>
	/// Primes the WebView keyboard target once after the shell reveal so the first native relay reaches ComfyUI.
	/// </summary>
	private async Task AwakeWebViewKeyboardAfterStableAsync()
	{
		try
		{
			if (_isBooted && WorkspaceControl?.BrowserView?.IsVisible == true)
			{
				PlatformManager.Current.WebView.Focus(WorkspaceControl.BrowserView);
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
		_loadingOverlayController.StartLoopAnimations(this.Height);
	}
}
