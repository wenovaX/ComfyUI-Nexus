using ComfyUI_Nexus.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace ComfyUI_Nexus.Ui;

internal sealed class HeaderGpuStatusController
{
	private const string GpuCacheBarWidthAnimation = "GpuCacheBarWidth";
	private const string GpuUtilBarWidthAnimation = "GpuUtilBarWidth";
	private const string GpuUtilPulseAnimation = "GpuUtilPulse";
	private const string GpuRunningPulseAnimation = "GpuRunningPulse";
	private const string GpuRunningScaleAnimation = "GpuRunningScale";
	private const string GpuIndicatorBlobScaleAnimation = "HeaderControl.GpuIndicatorBlobScale";
	private const string GpuIndicatorBlobOpacityAnimation = "HeaderControl.GpuIndicatorBlobOpacity";
	private const string CpuUsageBarWidthAnimation = "CpuUsageBarWidth";
	private const string GpuMiniUsageBarWidthAnimation = "GpuMiniUsageBarWidth";
	private const uint WidthAnimationLength = 180;
	private const uint GpuUtilPulseLength = 900;
	private const int AnimationRate = 16;
	private const double CacheWarningThreshold = 90;
	private const double ActiveWarningThreshold = 85;

	private readonly VisualElement _animationHost;
	private readonly HeaderView _header;

	internal HeaderGpuStatusController(VisualElement animationHost, HeaderView header)
	{
		_animationHost = animationHost;
		_header = header;
	}

	internal void AnimateVramUsage(double activePercent, double cachePercent, Color accent, Color accentSoft, double trackWidth)
	{
		double activeClamped = Math.Clamp(activePercent, 0, 100);
		double cacheClamped = Math.Clamp(Math.Max(cachePercent, activeClamped), 0, 100);
		double activeTargetWidth = trackWidth * (activeClamped / 100d);
		double cacheTargetWidth = trackWidth * (cacheClamped / 100d);

		_header.SetGpuBarBackground(new LinearGradientBrush(
			new GradientStopCollection
			{
				new GradientStop(accent, 0.0f),
				new GradientStop(accentSoft, 0.58f),
				new GradientStop(Color.FromArgb("#f8feff"), 1.0f)
			},
			Point.Zero,
			new Point(1, 0)));

		_animationHost.AbortAnimation(GpuCacheBarWidthAnimation);
		var cacheWidthAnimation = new Animation(v => _header.SetGpuCacheBarWidth(v), _header.GetGpuCacheBarWidth(), cacheTargetWidth);
		cacheWidthAnimation.Commit(_animationHost, GpuCacheBarWidthAnimation, AnimationRate, WidthAnimationLength, Easing.CubicOut);

		_animationHost.AbortAnimation(GpuUtilBarWidthAnimation);
		var widthAnimation = new Animation(v => _header.SetGpuBarWidth(v), _header.GetGpuBarWidth(), activeTargetWidth);
		widthAnimation.Commit(_animationHost, GpuUtilBarWidthAnimation, AnimationRate, WidthAnimationLength, Easing.CubicOut);

		if (cacheClamped >= CacheWarningThreshold || activeClamped >= ActiveWarningThreshold)
		{
			_animationHost.AbortAnimation(GpuUtilPulseAnimation);
			var pulseAnimation = new Animation();
			pulseAnimation.Add(0, 0.5, new Animation(v => _header.SetGpuBarOpacity(v), 1, 0.55));
			pulseAnimation.Add(0.5, 1, new Animation(v => _header.SetGpuBarOpacity(v), 0.55, 1));
			pulseAnimation.Commit(_animationHost, GpuUtilPulseAnimation, AnimationRate, GpuUtilPulseLength, Easing.CubicInOut, repeat: () => true);
			return;
		}

		_animationHost.AbortAnimation(GpuUtilPulseAnimation);
		_header.SetGpuBarOpacity(1);
	}

	internal void AnimateRunningState(bool isRunning)
	{
		_animationHost.AbortAnimation(GpuRunningPulseAnimation);
		_animationHost.AbortAnimation(GpuRunningScaleAnimation);
		_animationHost.AbortAnimation(GpuIndicatorBlobScaleAnimation);
		_animationHost.AbortAnimation(GpuIndicatorBlobOpacityAnimation);

		if (isRunning)
		{
			_header.ApplyGpuIndicatorPalette(
				Color.FromArgb("#ff5d73"),
				Color.FromArgb("#ff9cac"),
				Color.FromArgb("#24131a"),
				Color.FromArgb("#ff6b7d"),
				0.58f,
				16);
			_header.SetGpuIndicatorVisualState(1, 1, 0.4, 1, 1);

			var pulseAnimation = new Animation();
			pulseAnimation.Add(0, 0.2, new Animation(v => _header.SetGpuIndicatorCoreScale(v), 1.0, 0.9));
			pulseAnimation.Add(0.2, 0.52, new Animation(v => _header.SetGpuIndicatorCoreScale(v), 0.9, 1.16));
			pulseAnimation.Add(0.52, 0.78, new Animation(v => _header.SetGpuIndicatorCoreScale(v), 1.16, 0.98));
			pulseAnimation.Add(0.78, 1, new Animation(v => _header.SetGpuIndicatorCoreScale(v), 0.98, 1.08));
			pulseAnimation.Commit(_animationHost, GpuRunningPulseAnimation, AnimationRate, 860, Easing.CubicInOut, repeat: () => true);

			var scaleAnimation = new Animation();
			scaleAnimation.Add(0, 0.3, new Animation(v => _header.SetGpuIndicatorShellScale(v), 1, 1.08));
			scaleAnimation.Add(0.3, 0.62, new Animation(v => _header.SetGpuIndicatorShellScale(v), 1.08, 1.22));
			scaleAnimation.Add(0.62, 1, new Animation(v => _header.SetGpuIndicatorShellScale(v), 1.22, 1));
			scaleAnimation.Commit(_animationHost, GpuRunningScaleAnimation, AnimationRate, 980, Easing.CubicInOut, repeat: () => true);

			var blobScaleAnimation = new Animation();
			blobScaleAnimation.Add(0, 0.28, new Animation(v => _header.SetGpuIndicatorBlobScale(v), 0.9, 1.18));
			blobScaleAnimation.Add(0.28, 0.7, new Animation(v => _header.SetGpuIndicatorBlobScale(v), 1.18, 1.34));
			blobScaleAnimation.Add(0.7, 1, new Animation(v => _header.SetGpuIndicatorBlobScale(v), 1.34, 0.96));
			blobScaleAnimation.Commit(_animationHost, GpuIndicatorBlobScaleAnimation, AnimationRate, 1100, Easing.CubicInOut, repeat: () => true);

			var blobOpacityAnimation = new Animation();
			blobOpacityAnimation.Add(0, 0.36, new Animation(v => _header.SetGpuIndicatorBlobOpacity(v), 0.32, 0.64));
			blobOpacityAnimation.Add(0.36, 0.7, new Animation(v => _header.SetGpuIndicatorBlobOpacity(v), 0.64, 0.46));
			blobOpacityAnimation.Add(0.7, 1, new Animation(v => _header.SetGpuIndicatorBlobOpacity(v), 0.46, 0.28));
			blobOpacityAnimation.Commit(_animationHost, GpuIndicatorBlobOpacityAnimation, AnimationRate, 1040, Easing.CubicInOut, repeat: () => true);
			return;
		}

		_header.ApplyGpuIndicatorPalette(
			Color.FromArgb("#2ecbff"),
			Color.FromArgb("#73e7ff"),
			Color.FromArgb("#102232"),
			Color.FromArgb("#2ecbff"),
			0.24f,
			11);
		_header.SetGpuIndicatorVisualState(1, 1, 0.32, 1, 1);

		var idlePulse = new Animation();
		idlePulse.Add(0, 0.5, new Animation(v => _header.SetGpuIndicatorCoreScale(v), 0.96, 1.05));
		idlePulse.Add(0.5, 1, new Animation(v => _header.SetGpuIndicatorCoreScale(v), 1.05, 0.96));
		idlePulse.Commit(_animationHost, GpuRunningPulseAnimation, AnimationRate, 2000, Easing.CubicInOut, repeat: () => true);

		var idleShellAnimation = new Animation();
		idleShellAnimation.Add(0, 0.5, new Animation(v => _header.SetGpuIndicatorShellScale(v), 1.0, 1.05));
		idleShellAnimation.Add(0.5, 1, new Animation(v => _header.SetGpuIndicatorShellScale(v), 1.05, 1.0));
		idleShellAnimation.Commit(_animationHost, GpuRunningScaleAnimation, AnimationRate, 2200, Easing.CubicInOut, repeat: () => true);

		var idleBlobAnimation = new Animation();
		idleBlobAnimation.Add(0, 0.4, new Animation(v => _header.SetGpuIndicatorBlobScale(v), 0.92, 1.08));
		idleBlobAnimation.Add(0.4, 1, new Animation(v => _header.SetGpuIndicatorBlobScale(v), 1.08, 0.92));
		idleBlobAnimation.Commit(_animationHost, GpuIndicatorBlobScaleAnimation, AnimationRate, 2400, Easing.CubicInOut, repeat: () => true);

		var idleBlobOpacity = new Animation();
		idleBlobOpacity.Add(0, 0.5, new Animation(v => _header.SetGpuIndicatorBlobOpacity(v), 0.24, 0.42));
		idleBlobOpacity.Add(0.5, 1, new Animation(v => _header.SetGpuIndicatorBlobOpacity(v), 0.42, 0.24));
		idleBlobOpacity.Commit(_animationHost, GpuIndicatorBlobOpacityAnimation, AnimationRate, 2100, Easing.CubicInOut, repeat: () => true);
	}

	internal void AnimateMiniUsage(double cpuUsage, double gpuUsage, double trackWidth)
	{
		AnimateMiniBar(CpuUsageBarWidthAnimation, _header.GetCpuUsageBarWidth(), cpuUsage, trackWidth, _header.SetCpuUsageBarWidth);
		AnimateMiniBar(GpuMiniUsageBarWidthAnimation, _header.GetGpuMiniUsageBarWidth(), gpuUsage, trackWidth, _header.SetGpuMiniUsageBarWidth);
	}

	private void AnimateMiniBar(string animationName, double startWidth, double usagePercent, double trackWidth, Action<double> applyWidth)
	{
		double clamped = Math.Clamp(usagePercent, 0, 100);
		double targetWidth = trackWidth * (clamped / 100d);

		_animationHost.AbortAnimation(animationName);
		var widthAnimation = new Animation(v => applyWidth(v), startWidth, targetWidth);
		widthAnimation.Commit(_animationHost, animationName, AnimationRate, WidthAnimationLength, Easing.CubicOut);
	}
}
