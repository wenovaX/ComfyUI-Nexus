using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Views.Overlays;

internal enum SetupBackgroundMode
{
	Crossroads,
	VanguardHover,
	ArchitectHover,
	VanguardSelected,
	ArchitectSelected,
	Hidden,
}

internal enum SetupSceneMotionState
{
	Hidden,
	Crossroads,
	SelectionExit,
	Panel,
	Console,
}

/// <summary>
/// Owns the setup scene backdrop. Image placement remains in XAML; this class only
/// coordinates visibility, opacity, and the one selection transition contract.
/// </summary>
internal sealed class SetupBackgroundController
{
	private const double AmbientGlowOpacity = 0.15;
	private const double HoverGlowOpacity = 0.8;
	private const double SelectedGlowOpacity = 1.0;
	private const double SelectionScale = 1.04;
	private const uint GlowFadeLength = 400;
	private const uint SelectedGlowFadeLength = 500;
	private const uint SelectionScaleOutLength = 100;
	private const int SelectionIconExitFrameStep = 3;
	private const double VanguardSelectionBurstExitPlaybackRate = 0.825;
	private const double ArchitectSelectionBurstExitPlaybackRate = 1.125;
	private const double SelectionIconExitPlaybackRate = 10;

	private readonly VisualElement _baseLayer;
	private readonly Image _crossroadsAmbient;
	private readonly Image _vanguardGlow;
	private readonly Image _architectGlow;
	private readonly Image _vanguardSelectionBurst;
	private readonly Image _architectSelectionBurst;
	private readonly NexusAnimatedWebpClip _vanguardIconClip;
	private readonly NexusAnimatedWebpClip _architectIconClip;
	private readonly NexusAnimatedWebpClip _vanguardSelectionBurstClip;
	private readonly NexusAnimatedWebpClip _architectSelectionBurstClip;
	private long _selectionGeneration;
	private bool _isSelectionTransitionActive;
	private SetupBackgroundMode _mode = SetupBackgroundMode.Crossroads;

	internal SetupBackgroundController(
		NexusMotionController motion,
		NexusAnimatedWebpFrameCache frameCache,
		VisualElement baseLayer,
		Image crossroadsAmbient,
		Image vanguardGlow,
		Image architectGlow,
		Image vanguardSelectionBurst,
		Image architectSelectionBurst,
		NexusAnimatedWebpClip vanguardIconClip,
		NexusAnimatedWebpClip architectIconClip)
	{
		ArgumentNullException.ThrowIfNull(motion);
		ArgumentNullException.ThrowIfNull(frameCache);
		_baseLayer = baseLayer ?? throw new ArgumentNullException(nameof(baseLayer));
		_crossroadsAmbient = crossroadsAmbient ?? throw new ArgumentNullException(nameof(crossroadsAmbient));
		_vanguardGlow = vanguardGlow ?? throw new ArgumentNullException(nameof(vanguardGlow));
		_architectGlow = architectGlow ?? throw new ArgumentNullException(nameof(architectGlow));
		_vanguardSelectionBurst = vanguardSelectionBurst ?? throw new ArgumentNullException(nameof(vanguardSelectionBurst));
		_architectSelectionBurst = architectSelectionBurst ?? throw new ArgumentNullException(nameof(architectSelectionBurst));
		_vanguardIconClip = vanguardIconClip ?? throw new ArgumentNullException(nameof(vanguardIconClip));
		_architectIconClip = architectIconClip ?? throw new ArgumentNullException(nameof(architectIconClip));
		_vanguardSelectionBurstClip = new NexusAnimatedWebpClip(motion, frameCache, _vanguardSelectionBurst, "Setup.VanguardSelectionBurst", NexusAnimatedWebpCacheCatalog.SetupVanguardSelectionBurst);
		_architectSelectionBurstClip = new NexusAnimatedWebpClip(motion, frameCache, _architectSelectionBurst, "Setup.ArchitectSelectionBurst", NexusAnimatedWebpCacheCatalog.SetupArchitectSelectionBurst);
	}

	internal void SetBaseOpacity(double opacity)
		=> _baseLayer.Opacity = opacity;

	internal Task FadeBaseToAsync(double opacity, uint length, Easing easing)
		=> SafeAnimation.FadeToAsync(_baseLayer, opacity, length, easing, "Setup.Background");

	internal void SetCrossroadsAmbientVisible(bool isVisible)
		=> _crossroadsAmbient.Opacity = isVisible ? 1 : 0;

	internal Task PrepareSelectionBurstAsync(SetupBackgroundMode mode)
		=> mode switch
		{
			SetupBackgroundMode.VanguardHover or SetupBackgroundMode.VanguardSelected => _vanguardSelectionBurstClip.PrepareAsync(),
			SetupBackgroundMode.ArchitectHover or SetupBackgroundMode.ArchitectSelected => _architectSelectionBurstClip.PrepareAsync(),
			_ => Task.CompletedTask,
		};

	internal void ApplySceneState(SetupSceneMotionState state)
	{
		switch (state)
		{
			case SetupSceneMotionState.Crossroads:
				ResetCrossroads();
				break;
			case SetupSceneMotionState.Panel:
				_isSelectionTransitionActive = false;
				_vanguardSelectionBurstClip.Stop();
				_architectSelectionBurstClip.Stop();
				break;
			case SetupSceneMotionState.Console:
			case SetupSceneMotionState.Hidden:
				StopSelectionBursts("scene-state");
				SetMode(SetupBackgroundMode.Hidden);
				break;
		}
	}

	internal void SetMode(SetupBackgroundMode mode)
	{
		if (_isSelectionTransitionActive
			&& mode is SetupBackgroundMode.Crossroads or SetupBackgroundMode.VanguardHover or SetupBackgroundMode.ArchitectHover)
		{
			NexusLog.Trace($"[SETUP:BACKGROUND] Ignored hover mode during an active selection transition. requested={mode}.");
			return;
		}

		if (mode == _mode)
		{
			return;
		}

		if (mode is not SetupBackgroundMode.VanguardSelected and not SetupBackgroundMode.ArchitectSelected)
		{
			StopSelectionBursts("mode-change");
		}

		_mode = mode;

		switch (mode)
		{
			case SetupBackgroundMode.Crossroads:
				FadeGlows(AmbientGlowOpacity, AmbientGlowOpacity, GlowFadeLength);
				break;
			case SetupBackgroundMode.VanguardHover:
				FadeGlows(HoverGlowOpacity, AmbientGlowOpacity, GlowFadeLength);
				break;
			case SetupBackgroundMode.ArchitectHover:
				FadeGlows(AmbientGlowOpacity, HoverGlowOpacity, GlowFadeLength);
				break;
			case SetupBackgroundMode.VanguardSelected:
				FadeGlows(SelectedGlowOpacity, 0, SelectedGlowFadeLength);
				break;
			case SetupBackgroundMode.ArchitectSelected:
				FadeGlows(0, SelectedGlowOpacity, SelectedGlowFadeLength);
				break;
			case SetupBackgroundMode.Hidden:
				FadeGlows(0, 0, GlowFadeLength);
				break;
		}
	}

	internal async Task<bool> PlaySelectionAsync(SetupBackgroundMode selectedMode, VisualElement target, Func<bool> canRun)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(canRun);

		(Image selectedGlow, Image otherGlow, Image selectionBurst, NexusAnimatedWebpClip selectionBurstClip, NexusAnimatedWebpClip selectedIconClip, NexusAnimatedWebpClip otherIconClip) = selectedMode switch
		{
			SetupBackgroundMode.VanguardSelected => (_vanguardGlow, _architectGlow, _vanguardSelectionBurst, _vanguardSelectionBurstClip, _vanguardIconClip, _architectIconClip),
			SetupBackgroundMode.ArchitectSelected => (_architectGlow, _vanguardGlow, _architectSelectionBurst, _architectSelectionBurstClip, _architectIconClip, _vanguardIconClip),
			_ => throw new ArgumentOutOfRangeException(nameof(selectedMode), selectedMode, "A selected background mode is required."),
		};

		StopSelectionBursts("new-selection", clearTransition: false);
		long selectionGeneration = Interlocked.Increment(ref _selectionGeneration);
		_isSelectionTransitionActive = true;
		NexusLog.Trace($"[SETUP:BACKGROUND] Selection burst requested. mode={selectedMode}, generation={selectionGeneration}.");
		SafeAnimation.CancelAnimations("Setup.Selection", selectedGlow, otherGlow);
		selectedGlow.Opacity = SelectedGlowOpacity;
		otherGlow.Opacity = 0;
		SetMode(selectedMode);

		selectionBurstClip.Rewind();
		selectedIconClip.Rewind();
		selectionBurst.Opacity = 1;
		NexusLog.Trace($"[SETUP:BACKGROUND] Selection exit visuals starting. mode={selectedMode}, generation={selectionGeneration}, overlayHandler={selectionBurst.Handler is not null}, targetHandler={target.Handler is not null}.");
		Task<bool> selectionPlayback = selectionBurstClip.PlayOnceAsync(
			() => selectionGeneration == Volatile.Read(ref _selectionGeneration),
			selectedMode == SetupBackgroundMode.VanguardSelected
				? VanguardSelectionBurstExitPlaybackRate
				: ArchitectSelectionBurstExitPlaybackRate);
		Task<bool> selectedIconPlayback = selectedIconClip.PlayOnceAsync(
			canRun,
			SelectionIconExitPlaybackRate,
			SelectionIconExitFrameStep);
		_ = SafeAnimation.ScaleToAsync(target, SelectionScale, SelectionScaleOutLength, Easing.CubicOut, "Setup.Selection");

		bool[] playbackResults = await Task.WhenAll(selectionPlayback, selectedIconPlayback);
		bool playedToCompletion = playbackResults[0];
		bool selectedIconCompleted = playbackResults[1];
		otherIconClip.Stop();
		SafeAnimation.CancelAnimations(target, "Setup.Selection");
		target.Scale = 1;
		selectionBurst.Opacity = 0;
		if (!playedToCompletion)
		{
			NexusLog.Warning($"[SETUP:BACKGROUND] Selection burst did not reach its final frame; continuing the selection transition. mode={selectedMode}, generation={selectionGeneration}, overlayHandler={selectionBurst.Handler is not null}, targetHandler={target.Handler is not null}.");
		}

		bool isCurrentSelection = selectionGeneration == Volatile.Read(ref _selectionGeneration);
		NexusLog.Trace($"[SETUP:BACKGROUND] Selection exit visuals ended. mode={selectedMode}, generation={selectionGeneration}, burstCompleted={playedToCompletion}, selectedIconCompleted={selectedIconCompleted}, transitionAllowed={isCurrentSelection}, targetVisible={target.IsVisible}.");
		return isCurrentSelection;
	}

	internal void ResetCrossroads()
	{
		StopSelectionBursts("reset-crossroads");
		SafeAnimation.CancelAnimations("Setup.Background", _vanguardGlow, _architectGlow);
		_vanguardGlow.TranslationY = 0;
		_architectGlow.TranslationY = 0;
		_vanguardGlow.Opacity = AmbientGlowOpacity;
		_architectGlow.Opacity = AmbientGlowOpacity;
		_mode = SetupBackgroundMode.Crossroads;
	}

	internal void Dispose()
	{
		StopSelectionBursts("dispose");
		_vanguardSelectionBurstClip.Dispose();
		_architectSelectionBurstClip.Dispose();
	}

	private void StopSelectionBursts(string reason, bool clearTransition = true)
	{
		bool wasActive = _isSelectionTransitionActive;
		if (clearTransition)
		{
			_isSelectionTransitionActive = false;
		}

		Interlocked.Increment(ref _selectionGeneration);
		_vanguardSelectionBurstClip.Stop();
		_architectSelectionBurstClip.Stop();
		if (clearTransition && wasActive)
		{
			_vanguardIconClip.Stop();
			_architectIconClip.Stop();
		}
		_vanguardSelectionBurst.Opacity = 0;
		_architectSelectionBurst.Opacity = 0;
		if (wasActive)
		{
			NexusLog.Trace($"[SETUP:BACKGROUND] Selection burst stopped. reason={reason}, clearTransition={clearTransition}.");
		}
	}

	private void FadeGlows(double vanguardOpacity, double architectOpacity, uint length)
	{
		SafeAnimation.CancelAnimations("Setup.Background", _vanguardGlow, _architectGlow);
		_ = SafeAnimation.FadeToAsync(_vanguardGlow, vanguardOpacity, length, Easing.CubicOut, "Setup.Background");
		_ = SafeAnimation.FadeToAsync(_architectGlow, architectOpacity, length, Easing.CubicOut, "Setup.Background");
	}
}
