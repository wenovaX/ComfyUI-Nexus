using ComfyUI_Nexus.Views;
using ComfyUI_Nexus.Views.Overlays;
using ComfyUI_Nexus.Setup;
using Microsoft.Maui.Controls;

namespace ComfyUI_Nexus.Ui;

internal sealed class LoadingOverlayController
{
	private const int AnimationRate = 16;
	private const double PulseMidpoint = 0.5;
	private const double LoadingRotationStart = -12;
	private const double LoadingRotationEnd = 348;
	private const double DefaultScanLineHeight = 800;
	private const string LoadingRingAnimationName = "LoadingRingAnim";
	private const string LoadingRingPulseAnimationName = "LoadingRingPulse";
	private const string LoadingOrbitMorphAnimationName = "LoadingOrbitMorph";
	private const string ScanLineAnimationName = "ScanLineAnim";
	private const string HaloPulseAnimationName = "HaloPulse";
	private const string HaloScaleAnimationName = "HaloScale";
	private const string LoadingLogoFloatAnimationName = "LoadingLogoFloat";
	private const string LoadingStatusPulseAnimationName = "LoadingStatusPulse";
	private const string TopLogoLoadingRotateAnimationName = "TopLogoLoadingRotate";

	private readonly VisualElement _animationHost;
	private readonly LoadingOverlayView _overlay;
	private readonly HeaderView _header;

	internal LoadingOverlayController(VisualElement animationHost, LoadingOverlayView overlay, HeaderView header)
	{
		_animationHost = animationHost;
		_overlay = overlay;
		_header = header;
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
		RunOnMainThread(() =>
		{
			_overlay.IsVisible = false;
			_overlay.Opacity = 0;
			_overlay.InputTransparent = true;
		});
	}

	internal void SetConfigOverlayState(bool isVisible, double opacity, bool inputTransparent)
	{
		RunOnMainThread(() => _overlay.SetConfigSurfaceState(isVisible, opacity, inputTransparent));
	}

	internal void ResetVisualState(string successText, Color successColor, Color ringColor)
	{
		RunOnMainThread(() => _overlay.ResetVisualState(successText, successColor, ringColor));
	}

	internal void SetStatus(string text, Color textColor)
	{
		RunOnMainThread(() => _overlay.SetStatus(text, textColor));
	}

	internal void SetMode(bool showConfig)
	{
		RunOnMainThread(() => _overlay.SetMode(showConfig));
	}

	internal void ShowProductSetup()
	{
		RunOnMainThread(() =>
		{
			_overlay.IsVisible = true;
			_overlay.Opacity = 1;
			_overlay.InputTransparent = false;
			_overlay.ShowProductSetup();
		});
	}

	internal void ShowServerLaunchOnly()
	{
		RunOnMainThread(() =>
		{
			_overlay.IsVisible = true;
			_overlay.Opacity = 1;
			_overlay.InputTransparent = false;
			_overlay.ShowServerLaunchOnly();
		});
	}

	internal void ShowServerStartupPending()
	{
		RunOnMainThread(() =>
		{
			_overlay.IsVisible = true;
			_overlay.Opacity = 1;
			_overlay.InputTransparent = false;
			_overlay.ShowServerStartupPending();
		});
	}

	internal void ShowMaintenanceRecovery()
	{
		RunOnMainThread(() =>
		{
			_overlay.IsVisible = true;
			_overlay.Opacity = 1;
			_overlay.InputTransparent = false;
			_overlay.ShowMaintenanceRecovery();
		});
	}

	internal void RestartServerLaunch(bool repairRuntimeBeforeBoot = false)
	{
		RunOnMainThread(() =>
		{
			_overlay.IsVisible = true;
			_overlay.Opacity = 1;
			_overlay.InputTransparent = false;
			_overlay.RestartServerLaunch(repairRuntimeBeforeBoot);
		});
	}

	internal void SetNexusAppEntry(INexusAppEntry appEntry)
	{
		RunOnMainThread(() => _overlay.SetNexusAppEntry(appEntry));
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
			AbortLoopAnimations();
			_header.SetLoadingLogoRotation(0);

			var fadeTasks = new List<Task>
			{
				_header.FadeLoadingHaloAsync(0, 260),
				_overlay.FadeToAsync(0, 360, Easing.CubicInOut)
			};

			await Task.WhenAll(fadeTasks);
		}, "LOADING_CONTROLLER:FADE_OUT");
	}

	internal void StartLoopAnimations(double viewportHeight)
	{
		RunOnMainThread(() => StartLoopAnimationsOnMainThread(viewportHeight));
	}

	internal async Task PlaySuccessVisualsAsync(string text, Color color)
	{
		await UiThread.InvokeAsync(async () =>
		{
			await Task.WhenAll(
				_overlay.ScaleStatusAsync(1.05, 110, Easing.CubicOut),
				_overlay.TranslateStatusAsync(0, -1, 110, Easing.CubicOut),
				_overlay.FadeLoadingLogoAsync(0, 160, Easing.CubicInOut),
				_overlay.ScaleLoadingLogoAsync(0.82, 160, Easing.CubicInOut),
				RevealSuccessGlyphOnMainThreadAsync(text, color),
				EmphasizeSuccessRingOnMainThreadAsync(color));
		}, "LOADING_CONTROLLER:SUCCESS_VISUALS");
	}

	private void StartLoopAnimationsOnMainThread(double viewportHeight)
	{
		AbortLoopAnimations();

		var ringAnim = new Animation(v => _overlay.SetLoadingRingRotation(v), LoadingRotationStart, LoadingRotationEnd);
		ringAnim.Commit(_animationHost, LoadingRingAnimationName, AnimationRate, 12000, Easing.Linear, repeat: () => true);

		var ringPulse = new Animation();
		ringPulse.Add(0, PulseMidpoint, new Animation(v => _overlay.SetLoadingOrbitOpacity(v), 0.015, 0.07, Easing.CubicInOut));
		ringPulse.Add(PulseMidpoint, 1, new Animation(v => _overlay.SetLoadingOrbitOpacity(v), 0.07, 0.015, Easing.CubicInOut));
		ringPulse.Commit(_animationHost, LoadingRingPulseAnimationName, AnimationRate, 2200, Easing.Linear, repeat: () => true);

		var orbitMorph = new Animation();
		orbitMorph.Add(0, PulseMidpoint, new Animation(v => _overlay.SetLoadingOrbitWidth(v), 122, 158, Easing.CubicInOut));
		orbitMorph.Add(0, PulseMidpoint, new Animation(v => _overlay.SetLoadingOrbitHeight(v), 112, 102, Easing.CubicInOut));
		orbitMorph.Add(PulseMidpoint, 1, new Animation(v => _overlay.SetLoadingOrbitWidth(v), 158, 122, Easing.CubicInOut));
		orbitMorph.Add(PulseMidpoint, 1, new Animation(v => _overlay.SetLoadingOrbitHeight(v), 102, 112, Easing.CubicInOut));
		orbitMorph.Commit(_animationHost, LoadingOrbitMorphAnimationName, AnimationRate, 5200, Easing.Linear, repeat: () => true);

		var scanAnim = new Animation(v => _overlay.SetScanLineTranslation(v), 0, viewportHeight > 0 ? viewportHeight : DefaultScanLineHeight);
		scanAnim.Commit(_animationHost, ScanLineAnimationName, AnimationRate, 3000, Easing.Linear, repeat: () => true);

		var haloAnim = new Animation();
		haloAnim.Add(0, PulseMidpoint, new Animation(v => _header.SetLoadingHaloOpacity(v), 0.2, 0.7, Easing.CubicInOut));
		haloAnim.Add(PulseMidpoint, 1, new Animation(v => _header.SetLoadingHaloOpacity(v), 0.7, 0.2, Easing.CubicInOut));
		haloAnim.Commit(_animationHost, HaloPulseAnimationName, AnimationRate, 1200, Easing.Linear, repeat: () => true);

		var haloScale = new Animation();
		haloScale.Add(0, PulseMidpoint, new Animation(v => _header.SetLoadingHaloScale(v), 1.0, 1.3, Easing.CubicInOut));
		haloScale.Add(PulseMidpoint, 1, new Animation(v => _header.SetLoadingHaloScale(v), 1.3, 1.0, Easing.CubicInOut));
		haloScale.Commit(_animationHost, HaloScaleAnimationName, AnimationRate, 1200, Easing.Linear, repeat: () => true);

		var logoFloat = new Animation();
		logoFloat.Add(0, PulseMidpoint, new Animation(v => _overlay.SetLoadingLogoScale(v), 0.98, 1.04, Easing.CubicInOut));
		logoFloat.Add(PulseMidpoint, 1, new Animation(v => _overlay.SetLoadingLogoScale(v), 1.04, 0.98, Easing.CubicInOut));
		logoFloat.Commit(_animationHost, LoadingLogoFloatAnimationName, AnimationRate, 1500, Easing.Linear, repeat: () => true);

		var statusPulse = new Animation();
		statusPulse.Add(0, PulseMidpoint, new Animation(v => _overlay.SetStatusOpacity(v), 0.8, 1, Easing.CubicInOut));
		statusPulse.Add(PulseMidpoint, 1, new Animation(v => _overlay.SetStatusOpacity(v), 1, 0.8, Easing.CubicInOut));
		statusPulse.Commit(_animationHost, LoadingStatusPulseAnimationName, AnimationRate, 1400, Easing.Linear, repeat: () => true);

		var topRotate = new Animation(v => _header.SetLoadingLogoRotation(v), 0, 360);
		topRotate.Commit(_animationHost, TopLogoLoadingRotateAnimationName, AnimationRate, 5000, Easing.Linear, repeat: () => true);
	}

	private async Task RevealSuccessGlyphOnMainThreadAsync(string text, Color color)
	{
		_overlay.SetSuccessGlyph(text, color);
		_overlay.SetSuccessGlyphVisualState(1, 0, -10);
		await Task.WhenAll(
			_overlay.FadeSuccessGlyphAsync(1, 120, Easing.CubicOut),
			_overlay.ScaleSuccessGlyphAsync(1, 180, Easing.CubicOut),
			_overlay.RotateSuccessGlyphAsync(0, 200, Easing.CubicOut));
	}

	private async Task EmphasizeSuccessRingOnMainThreadAsync(Color color)
	{
		_overlay.SetLoadingRingStyle(color, 4);
		await _overlay.ScaleLoadingRingAsync(1.05, 120, Easing.CubicOut);
	}

	private static void RunOnMainThread(Action action)
	{
		UiThread.TryBeginInvoke(action, "LOADING_CONTROLLER:UI");
	}

	private void AbortLoopAnimations()
	{
		_animationHost.AbortAnimation(LoadingRingAnimationName);
		_animationHost.AbortAnimation(LoadingRingPulseAnimationName);
		_animationHost.AbortAnimation(LoadingOrbitMorphAnimationName);
		_animationHost.AbortAnimation(ScanLineAnimationName);
		_animationHost.AbortAnimation(HaloPulseAnimationName);
		_animationHost.AbortAnimation(HaloScaleAnimationName);
		_animationHost.AbortAnimation(LoadingLogoFloatAnimationName);
		_animationHost.AbortAnimation(LoadingStatusPulseAnimationName);
		_animationHost.AbortAnimation(TopLogoLoadingRotateAnimationName);
	}
}
