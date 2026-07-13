using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace ComfyUI_Nexus.Ui;

internal sealed class HeaderGpuStatusController
{
	private const string GpuCacheBarScaleAnimation = "GpuCacheBarScale";
	private const string GpuUtilBarScaleAnimation = "GpuUtilBarScale";
	private const string GpuUtilPulseAnimation = "GpuUtilPulse";
	private const string GpuRunningPulseAnimation = "GpuRunningPulse";
	private const string GpuRunningScaleAnimation = "GpuRunningScale";
	private const string GpuIndicatorBlobScaleAnimation = "HeaderControl.GpuIndicatorBlobScale";
	private const string GpuIndicatorBlobOpacityAnimation = "HeaderControl.GpuIndicatorBlobOpacity";
	private const string CpuUsageBarScaleAnimation = "CpuUsageBarScale";
	private const string CpuUsageFillOpacityAnimation = "CpuUsageFillOpacity";
	private const uint GaugeAnimationLength = 180;
	private const uint GpuUtilPulseLength = 900;
	private const int AnimationRate = 16;
	private const double ScaleSnapThreshold = 0.002;
	private const double CacheWarningThreshold = 90;
	private const double ActiveWarningThreshold = 85;

	private readonly object _stateLock = new();
	private readonly VisualElement _animationHost;
	private readonly HeaderView _header;
	private readonly NexusMotionController _motion;
	private readonly GaugeState _gpuCacheScale = new();
	private readonly GaugeState _gpuUtilScale = new();
	private readonly GaugeState _cpuUsageScale = new();
	private readonly GaugeState _cpuFillOpacity = new(GetCpuFillOpacity(0));
	private bool? _lastVramWarningState;
	private bool? _lastRunningState;
	private Color? _lastGpuBarAccent;
	private Color? _lastGpuBarAccentSoft;

	private sealed class GaugeState
	{
		internal GaugeState(double current = 0)
		{
			Current = current;
		}

		internal double Current { get; set; }
		internal double LastTarget { get; set; } = double.NaN;
		internal long Generation { get; set; }
	}

	internal HeaderGpuStatusController(VisualElement animationHost, HeaderView header)
	{
		_animationHost = animationHost;
		_header = header;
		_motion = new NexusMotionController("header-gpu", "HeaderGpuStatus", animationHost.Dispatcher);
	}

	internal void AnimateVramUsage(double activePercent, double cachePercent, Color accent, Color accentSoft)
	{
		double activeClamped = Math.Clamp(activePercent, 0, 100);
		double cacheClamped = Math.Clamp(Math.Max(cachePercent, activeClamped), 0, 100);
		double activeTargetScale = activeClamped / 100d;
		double cacheTargetScale = cacheClamped / 100d;

		UpdateGpuBarBackground(accent, accentSoft);

		TweenGaugeScale(GpuCacheBarScaleAnimation, _gpuCacheScale, _header.SetGpuCacheBarScale, cacheTargetScale);
		TweenGaugeScale(GpuUtilBarScaleAnimation, _gpuUtilScale, _header.SetGpuBarScale, activeTargetScale);

		bool isWarning = cacheClamped >= CacheWarningThreshold || activeClamped >= ActiveWarningThreshold;
		lock (_stateLock)
		{
			if (_lastVramWarningState == isWarning)
			{
				return;
			}

			_lastVramWarningState = isWarning;
		}

		if (isWarning)
		{
			CommitTimeline(
				GpuUtilPulseAnimation,
				GpuUtilPulseLength,
				Easing.Linear,
				repeat: CanRepeatHeaderAnimation,
				reset: () => _header.SetGpuBarOpacity(1),
				new SafeAnimation.TimelineSegment(0, 0.5, _header.SetGpuBarOpacity, 1, 0.55),
				new SafeAnimation.TimelineSegment(0.5, 1, _header.SetGpuBarOpacity, 0.55, 1));
			return;
		}

		_motion.Stop(GpuUtilPulseAnimation);
		_header.SetGpuBarOpacity(1);
	}

	internal void AnimateRunningState(bool isRunning)
	{
		lock (_stateLock)
		{
			if (_lastRunningState == isRunning)
			{
				return;
			}

			_lastRunningState = isRunning;
		}

		_motion.Stop(GpuRunningPulseAnimation);
		_motion.Stop(GpuRunningScaleAnimation);
		_motion.Stop(GpuIndicatorBlobScaleAnimation);
		_motion.Stop(GpuIndicatorBlobOpacityAnimation);

		if (isRunning)
		{
			_header.ApplyGpuIndicatorPalette(
				Color.FromArgb("#ff5d73"),
				Color.FromArgb("#ff9cac"),
				Color.FromArgb("#24131a"));
			_header.SetGpuIndicatorVisualState(1, 1, 0.4, 1, 1);

			CommitTimeline(
				GpuRunningPulseAnimation,
				860,
				Easing.CubicInOut,
				repeat: CanRepeatHeaderAnimation,
				reset: ResetGpuIndicatorMotion,
				new SafeAnimation.TimelineSegment(0, 0.2, _header.SetGpuIndicatorCoreScale, 1.0, 0.9),
				new SafeAnimation.TimelineSegment(0.2, 0.52, _header.SetGpuIndicatorCoreScale, 0.9, 1.16),
				new SafeAnimation.TimelineSegment(0.52, 0.78, _header.SetGpuIndicatorCoreScale, 1.16, 0.98),
				new SafeAnimation.TimelineSegment(0.78, 1, _header.SetGpuIndicatorCoreScale, 0.98, 1.08));
			CommitTimeline(
				GpuRunningScaleAnimation,
				980,
				Easing.CubicInOut,
				repeat: CanRepeatHeaderAnimation,
				reset: ResetGpuIndicatorMotion,
				new SafeAnimation.TimelineSegment(0, 0.3, _header.SetGpuIndicatorShellScale, 1, 1.08),
				new SafeAnimation.TimelineSegment(0.3, 0.62, _header.SetGpuIndicatorShellScale, 1.08, 1.22),
				new SafeAnimation.TimelineSegment(0.62, 1, _header.SetGpuIndicatorShellScale, 1.22, 1));
			CommitTimeline(
				GpuIndicatorBlobScaleAnimation,
				1100,
				Easing.CubicInOut,
				repeat: CanRepeatHeaderAnimation,
				reset: ResetGpuIndicatorMotion,
				new SafeAnimation.TimelineSegment(0, 0.28, _header.SetGpuIndicatorBlobScale, 0.9, 1.18),
				new SafeAnimation.TimelineSegment(0.28, 0.7, _header.SetGpuIndicatorBlobScale, 1.18, 1.34),
				new SafeAnimation.TimelineSegment(0.7, 1, _header.SetGpuIndicatorBlobScale, 1.34, 0.96));
			CommitTimeline(
				GpuIndicatorBlobOpacityAnimation,
				1040,
				Easing.CubicInOut,
				repeat: CanRepeatHeaderAnimation,
				reset: ResetGpuIndicatorMotion,
				new SafeAnimation.TimelineSegment(0, 0.36, _header.SetGpuIndicatorBlobOpacity, 0.32, 0.64),
				new SafeAnimation.TimelineSegment(0.36, 0.7, _header.SetGpuIndicatorBlobOpacity, 0.64, 0.46),
				new SafeAnimation.TimelineSegment(0.7, 1, _header.SetGpuIndicatorBlobOpacity, 0.46, 0.28));
			return;
		}

		_header.ApplyGpuIndicatorPalette(
			Color.FromArgb("#2ecbff"),
			Color.FromArgb("#73e7ff"),
			Color.FromArgb("#102232"));
		_header.SetGpuIndicatorVisualState(1, 1, 0.32, 1, 1);

		CommitTimeline(
			GpuRunningPulseAnimation,
			2000,
			Easing.CubicInOut,
			repeat: CanRepeatHeaderAnimation,
			reset: ResetGpuIndicatorMotion,
			new SafeAnimation.TimelineSegment(0, 0.5, _header.SetGpuIndicatorCoreScale, 0.96, 1.05),
			new SafeAnimation.TimelineSegment(0.5, 1, _header.SetGpuIndicatorCoreScale, 1.05, 0.96));
		CommitTimeline(
			GpuRunningScaleAnimation,
			2200,
			Easing.CubicInOut,
			repeat: CanRepeatHeaderAnimation,
			reset: ResetGpuIndicatorMotion,
			new SafeAnimation.TimelineSegment(0, 0.5, _header.SetGpuIndicatorShellScale, 1.0, 1.05),
			new SafeAnimation.TimelineSegment(0.5, 1, _header.SetGpuIndicatorShellScale, 1.05, 1.0));
		CommitTimeline(
			GpuIndicatorBlobScaleAnimation,
			2400,
			Easing.CubicInOut,
			repeat: CanRepeatHeaderAnimation,
			reset: ResetGpuIndicatorMotion,
			new SafeAnimation.TimelineSegment(0, 0.4, _header.SetGpuIndicatorBlobScale, 0.92, 1.08),
			new SafeAnimation.TimelineSegment(0.4, 1, _header.SetGpuIndicatorBlobScale, 1.08, 0.92));
		CommitTimeline(
			GpuIndicatorBlobOpacityAnimation,
			2100,
			Easing.CubicInOut,
			repeat: CanRepeatHeaderAnimation,
			reset: ResetGpuIndicatorMotion,
			new SafeAnimation.TimelineSegment(0, 0.5, _header.SetGpuIndicatorBlobOpacity, 0.24, 0.42),
			new SafeAnimation.TimelineSegment(0.5, 1, _header.SetGpuIndicatorBlobOpacity, 0.42, 0.24));
	}

	internal void AnimateCpuUsage(double cpuUsage)
	{
		TweenGaugeScale(CpuUsageBarScaleAnimation, _cpuUsageScale, _header.SetCpuUsageBarScale, PercentToScale(cpuUsage));
		TweenGaugeValue(CpuUsageFillOpacityAnimation, _cpuFillOpacity, _header.SetCpuLoadFillOpacity, GetCpuFillOpacity(cpuUsage));
	}

	internal void Stop()
	{
		_motion.StopAll();
		SafeAnimation.AbortAnimation(_animationHost, GpuCacheBarScaleAnimation, "HeaderGpuStatus.Stop");
		SafeAnimation.AbortAnimation(_animationHost, GpuUtilBarScaleAnimation, "HeaderGpuStatus.Stop");
		SafeAnimation.AbortAnimation(_animationHost, GpuUtilPulseAnimation, "HeaderGpuStatus.Stop");
		SafeAnimation.AbortAnimation(_animationHost, CpuUsageBarScaleAnimation, "HeaderGpuStatus.Stop");
		SafeAnimation.AbortAnimation(_animationHost, CpuUsageFillOpacityAnimation, "HeaderGpuStatus.Stop");

		lock (_stateLock)
		{
			ResetGaugeState(_gpuCacheScale);
			ResetGaugeState(_gpuUtilScale);
			ResetGaugeState(_cpuUsageScale);
			ResetGaugeState(_cpuFillOpacity, GetCpuFillOpacity(0));
			_lastVramWarningState = null;
			_lastRunningState = null;
		}

		_header.SetGpuCacheBarScale(0);
		_header.SetGpuBarScale(0);
		_header.SetGpuBarOpacity(1);
		ResetGpuIndicatorMotion();
		_header.SetCpuUsageBarScale(0);
		_header.SetCpuLoadFillOpacity(GetCpuFillOpacity(0));
	}

	private void TweenGaugeScale(string animationName, GaugeState state, Action<double> apply, double targetScale)
		=> TweenGaugeValue(animationName, state, apply, targetScale);

	private void UpdateGpuBarBackground(Color accent, Color accentSoft)
	{
		bool shouldUpdate;
		lock (_stateLock)
		{
			shouldUpdate = _lastGpuBarAccent is not Color lastAccent
				|| _lastGpuBarAccentSoft is not Color lastAccentSoft
				|| !lastAccent.Equals(accent)
				|| !lastAccentSoft.Equals(accentSoft);
			if (shouldUpdate)
			{
				_lastGpuBarAccent = accent;
				_lastGpuBarAccentSoft = accentSoft;
			}
		}

		if (!shouldUpdate)
		{
			return;
		}

		_header.SetGpuBarBackground(new LinearGradientBrush(
			new GradientStopCollection
			{
				new GradientStop(accent, 0.0f),
				new GradientStop(accentSoft, 0.58f),
				new GradientStop(Color.FromArgb("#f8feff"), 1.0f)
			},
			Point.Zero,
			new Point(1, 0)));
	}

	private void TweenGaugeValue(string animationName, GaugeState state, Action<double> apply, double targetValue)
	{
		double safeTargetValue = Math.Clamp(targetValue, 0, 1);
		double startValue;
		long generation;
		bool shouldAnimate;
		bool shouldSnap;
		lock (_stateLock)
		{
			if (!double.IsNaN(state.LastTarget) && Math.Abs(state.LastTarget - safeTargetValue) <= ScaleSnapThreshold)
			{
				return;
			}

			state.LastTarget = safeTargetValue;
			state.Generation++;
			generation = state.Generation;
			startValue = state.Current;
			shouldSnap = Math.Abs(startValue - safeTargetValue) <= ScaleSnapThreshold;
			shouldAnimate = !shouldSnap;
			if (shouldSnap)
			{
				state.Current = safeTargetValue;
			}
		}

		if (shouldSnap)
		{
			ApplyGaugeValueIfCurrent(state, generation, apply, safeTargetValue);
			return;
		}

		if (XamlLifetimeDiagnostics.AreTransformAnimationsDisabled)
		{
			ApplyGaugeValueIfCurrent(state, generation, apply, safeTargetValue);
			return;
		}

		SafeAnimation.AbortAnimation(_animationHost, animationName, "HeaderGpuStatus.Gauge");
		if (!shouldAnimate)
		{
			return;
		}

		SafeAnimation.Tween(
			_animationHost,
			animationName,
			value => ApplyGaugeValueIfCurrent(state, generation, apply, value),
			startValue,
			safeTargetValue,
			AnimationRate,
			GaugeAnimationLength,
			Easing.CubicOut,
			source: "HeaderGpuStatus.Gauge");
	}

	private void ApplyGaugeValueIfCurrent(GaugeState state, long generation, Action<double> apply, double value)
	{
		lock (_stateLock)
		{
			if (state.Generation != generation)
			{
				return;
			}

			state.Current = value;
		}

		apply(value);
	}

	private void CommitTimeline(
		string animationName,
		uint length,
		Easing easing,
		Func<bool> repeat,
		Action reset,
		params SafeAnimation.TimelineSegment[] segments)
	{
		_motion.StartTimeline(animationName, _animationHost, AnimationRate, length, easing, repeat, reset, segments);
	}

	private void ResetGpuIndicatorMotion()
		=> _header.SetGpuIndicatorVisualState(1, 1, 0.32, 1, 1);

	private static double PercentToScale(double usagePercent)
		=> Math.Clamp(usagePercent, 0, 100) / 100d;

	private static void ResetGaugeState(GaugeState state, double current = 0)
	{
		state.Current = current;
		state.LastTarget = double.NaN;
		state.Generation++;
	}

	private bool CanRepeatHeaderAnimation()
		=> _header.IsVisible && _header.Handler is not null;

	private static double GetCpuFillOpacity(double usagePercent)
		=> 0.16 + (PercentToScale(usagePercent) * 0.36);
}
