using ComfyUI_Nexus.Views;
using ComfyUI_Nexus.Views.Overlays;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Setup;
using ComfyUI_Nexus.Setup.Runtime;
using Microsoft.Maui.Controls;

namespace ComfyUI_Nexus.Ui;

internal enum ServerBootEntryKind
{
	Idle,
	ResumePending,
	Restart,
	MaintenanceRecovery,
}

internal readonly record struct ServerBootEntryRequest(ServerBootEntryKind Kind);

internal sealed class LoadingOverlayController
{
	private readonly LoadingOverlayView _overlay;
	private readonly HeaderView _header;
	private readonly NexusMotionController _motion;
	private readonly NexusAnimatedWebpClip _processClip;
	private readonly NexusAnimatedWebpClip _successClip;
	private NexusAnimatedWebpCacheLease? _animationCacheLease;
	private Task<NexusAnimatedWebpCacheLease>? _animationCacheAcquireTask;
	private bool _loopAnimationsActive;

	internal LoadingOverlayController(LoadingOverlayView overlay, HeaderView header)
	{
		_overlay = overlay;
		_header = header;
		_motion = new NexusMotionController("loading-overlay", "LOADING_CONTROLLER", overlay.Dispatcher);
		_processClip = new NexusAnimatedWebpClip(_motion, _overlay.LoadingProcessAnimationSurface, "Loading.Process", NexusAnimatedWebpCacheCatalog.LoadingProcess);
		_successClip = new NexusAnimatedWebpClip(_motion, _overlay.SuccessAnimationSurface, "Loading.SuccessGate", NexusAnimatedWebpCacheCatalog.LoadingSuccess);
	}

	internal void SetBlockingState(bool isVisible, double opacity, bool inputTransparent)
	{
		RunOnMainThread(() =>
		{
			_overlay.IsVisible = isVisible;
			_overlay.Opacity = opacity;
			_overlay.InputTransparent = inputTransparent;
		});
	}

	internal void Show(LoadingOverlayDisplay display)
	{
		SetBlockingState(isVisible: true, opacity: 1, inputTransparent: false);
		Apply(display);
	}

	internal void Hold(string title, string description, string status, Color accentColor, double? progress = null)
	{
		Show(new LoadingOverlayDisplay(
			LoadingOverlayState.Hold,
			title,
			description,
			status,
			accentColor,
			Progress: progress));
	}

	internal void Message(string title, string description, string status, Color accentColor, double? progress = null)
	{
		Apply(new LoadingOverlayDisplay(
			LoadingOverlayState.Message,
			title,
			description,
			status,
			accentColor,
			Progress: progress));
	}

	internal void Error(string title, string description, string status, Color accentColor)
	{
		Show(new LoadingOverlayDisplay(
			LoadingOverlayState.Error,
			title,
			description,
			status,
			accentColor,
			CenterGlyph: "X"));
	}

	internal void Hide()
	{
		Apply(new LoadingOverlayDisplay(
			LoadingOverlayState.Hide,
			string.Empty,
			string.Empty,
			string.Empty,
			Colors.Transparent));
	}

	internal void Apply(LoadingOverlayDisplay display)
	{
		if (display.State == LoadingOverlayState.Hide)
		{
			HideBlockingSurface();
			return;
		}

		RunOnMainThread(() => _overlay.ApplyDisplay(display));
	}

	internal void HideBlockingSurface()
	{
		StopLoopAnimations();
		RunOnMainThread(HideBlockingSurfaceOnMainThread);
	}

	/// <summary>
	/// Completes the loading-to-shell hand-off before callers resume shell services.
	/// </summary>
	internal async Task CompleteStableRevealAsync()
	{
		await FadeOutForStableAsync();
		await UiThread.InvokeAsync(() =>
		{
			HideBlockingSurfaceOnMainThread();
			return Task.CompletedTask;
		}, "LOADING_CONTROLLER:COMPLETE_STABLE_REVEAL");
	}

	internal void SetConfigOverlayState(bool isVisible, double opacity, bool inputTransparent)
	{
		RunOnMainThread(() => _overlay.SetConfigSurfaceState(isVisible, opacity, inputTransparent));
	}

	internal void ResetVisualState(string successText, Color successColor)
	{
		RunOnMainThread(() => _overlay.ResetVisualState(successText, successColor));
	}

	internal void SetStatus(string text, Color textColor)
	{
		RunOnMainThread(() => _overlay.SetStatus(text, textColor));
	}

	internal void SetMode(bool showConfig)
	{
		if (showConfig)
		{
			StopLoopAnimations();
		}

		RunOnMainThread(() => _overlay.SetMode(showConfig));
	}

	internal async Task PrepareProductSetupForRevealAsync()
	{
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		StopLoopAnimations();
		NexusLog.Trace("[SETUP_ROUTE] Animated WebP cache prewarm starting.");
		await _overlay.PrewarmProductSetupAnimationsAsync().ConfigureAwait(false);
		NexusLog.Trace($"[SETUP_ROUTE] Animated WebP cache prewarm completed in {stopwatch.ElapsedMilliseconds}ms; requesting UI reveal.");
		await UiThread.InvokeAsync(async () =>
		{
			_overlay.IsVisible = true;
			_overlay.Opacity = 1;
			_overlay.InputTransparent = false;
			await _overlay.RevealPreparedProductSetupAsync();
		}, "LOADING_CONTROLLER:PREPARE_PRODUCT_SETUP");
		NexusLog.Trace($"[SETUP_ROUTE] Product setup UI reveal completed in {stopwatch.ElapsedMilliseconds}ms.");
	}

	internal Task EnterServerBootAsync(ServerBootEntryRequest request)
	{
		StopLoopAnimations();
		return UiThread.InvokeAsync(() =>
		{
			_overlay.IsVisible = true;
			_overlay.Opacity = 1;
			_overlay.InputTransparent = false;
			_overlay.EnterServerBoot(request);
			return Task.CompletedTask;
		}, $"LOADING_CONTROLLER:ENTER_SERVER_BOOT:{request.Kind}");
	}

	internal void SetNexusAppEntry(INexusAppEntry appEntry)
	{
		RunOnMainThread(() => _overlay.SetNexusAppEntry(appEntry));
	}

	internal void SetServerLifecycleRunner(Func<ServerLifecycleRequest, Task<ServerLifecycleResult>> runner)
	{
		RunOnMainThread(() => _overlay.SetServerLifecycleRunner(runner));
	}

	internal void SetFailureGlyph(string glyph, Color color)
	{
		RunOnMainThread(() =>
		{
			_overlay.SetSuccessGlyph(glyph, color);
			_overlay.SetSuccessGlyphVisualState(0, 0, -10);
		});
	}

	internal async Task FadeOutForStableAsync()
	{
		await UiThread.InvokeAsync(async () =>
		{
			StopLoopAnimations(preserveSuccessVisual: true);

			var fadeTasks = new List<Task>
			{
				_header.FadeLoadingHaloAsync(0, 260),
				SafeAnimation.FadeToAsync(_overlay, 0, 360, Easing.CubicInOut, "LoadingOverlay.FadeOut")
			};

			await Task.WhenAll(fadeTasks);
		}, "LOADING_CONTROLLER:FADE_OUT");
	}

	private void HideBlockingSurfaceOnMainThread()
	{
		StopLoopAnimations();
		_overlay.IsVisible = false;
		_overlay.Opacity = 0;
		_overlay.InputTransparent = true;
	}

	internal void StartLoopAnimations()
	{
		RunOnMainThread(StartLoopAnimationsOnMainThread);
	}

	internal void Stop()
		=> StopLoopAnimations();

	internal async Task PlaySuccessVisualsAsync(string text, Color color)
	{
		await UiThread.InvokeAsync(async () =>
		{
			_processClip.Stop();
			_overlay.SetLoadingProcessVisible(false);
			_overlay.SetSuccessGlyphVisualState(0, 0, 0);
			_overlay.SetStatusVisualState(1, 1.05, -1);
			_overlay.SetSuccessAnimationVisible(true);

			bool completed = await _successClip.PlayOnceAsync(
				CanRunSuccessVisual,
				finalFrameBehavior: NexusAnimatedWebpFinalFrameBehavior.HoldFinalFrame);
			if (completed)
			{
				_overlay.SetSuccessGlyphVisualState(0, 0, 0);
			}
			else
			{
				_overlay.SetSuccessAnimationVisible(false);
				_overlay.SetSuccessGlyph(text, color);
				_overlay.SetSuccessGlyphVisualState(1, 1, 0);
				NexusLog.Trace("[LOADING_CONTROLLER] Success gate clip did not complete; applied the static success state.");
			}
		}, "LOADING_CONTROLLER:SUCCESS_VISUALS");
	}

	private void StartLoopAnimationsOnMainThread()
	{
		_ = EnsureAnimationCacheAsync();
		StopLoopAnimations();
		_loopAnimationsActive = true;
		_overlay.SetSuccessGlyphVisualState(0, 0, 0);
		_overlay.SetSuccessAnimationVisible(false);
		_overlay.SetLoadingProcessVisible(true);
		_processClip.PlayLoop(CanRunLoopAnimations);
		_ = _successClip.PrepareAsync();
	}
	private static void RunOnMainThread(Action action)
	{
		if (MainThread.IsMainThread)
		{
			action();
			return;
		}

		UiThread.TryBeginInvoke(action, "LOADING_CONTROLLER:UI");
	}

	private bool CanRunLoopAnimations()
		=> _loopAnimationsActive
			&& _overlay.Handler is not null
			&& _overlay.IsVisible;

	private bool CanRunSuccessVisual()
		=> _overlay.Handler is not null
			&& _overlay.IsVisible;

	private void StopLoopAnimations(bool preserveSuccessVisual = false)
	{
		_loopAnimationsActive = false;
		_processClip.Stop();
		_overlay.SetLoadingProcessVisible(false);
		if (!preserveSuccessVisual)
		{
			_successClip.Stop();
			_overlay.SetSuccessAnimationVisible(false);
		}
		_motion.StopAll();
		if (!preserveSuccessVisual)
		{
			ReleaseAnimationCache();
		}
	}

	private async Task EnsureAnimationCacheAsync()
	{
		if (_animationCacheLease is not null)
		{
			return;
		}

		_animationCacheAcquireTask ??= NexusAnimatedWebpFrameCache.AcquireAsync(NexusAnimatedWebpCacheGroup.Shell);
		_animationCacheLease = await _animationCacheAcquireTask;
	}

	private void ReleaseAnimationCache()
	{
		_animationCacheLease?.Dispose();
		_animationCacheLease = null;
		_animationCacheAcquireTask = null;
	}

}
