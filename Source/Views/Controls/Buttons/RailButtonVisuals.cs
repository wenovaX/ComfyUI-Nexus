using ComfyUI_Nexus.Ui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ComfyUI_Nexus.Views.Controls.Buttons;

internal static class RailButtonVisuals
{
	internal static void ApplyTooltip(BindableObject target, string? tooltipText)
	{
		string text = string.IsNullOrWhiteSpace(tooltipText) ? string.Empty : tooltipText;
		ToolTipProperties.SetText(target, text);
		SemanticProperties.SetDescription(target, text);
	}

	internal static void ApplyIconPair(Image normalIcon, Image activeIcon, bool isSelected, ImageSource? activeIconSource)
	{
		bool showActive = isSelected && activeIconSource != null;
		normalIcon.Opacity = showActive ? 0 : 1;
		activeIcon.Opacity = showActive ? 1 : 0;
	}

	internal static void TweenOpacity(
		IAnimatable owner,
		string animationName,
		VisualElement target,
		double targetOpacity,
		uint length,
		Easing easing,
		string source,
		double snapThreshold)
	{
		SafeAnimation.TweenTo(
			owner,
			animationName,
			() => target.Opacity,
			value => target.Opacity = value,
			targetOpacity,
			length: length,
			easing: easing,
			snapThreshold: snapThreshold,
			source: source);
	}

	internal static void FlashOpacity(
		IAnimatable owner,
		string animationName,
		VisualElement target,
		double targetOpacity,
		double flashOpacity,
		uint length,
		Easing easing,
		string source,
		double snapThreshold)
	{
		SafeAnimation.AbortAnimation(owner, animationName, source);
		target.Opacity = flashOpacity;
		TweenOpacity(owner, animationName, target, targetOpacity, length, easing, source, snapThreshold);
	}
}
