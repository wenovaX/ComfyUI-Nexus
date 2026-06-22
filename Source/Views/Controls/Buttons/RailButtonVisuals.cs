using Microsoft.Maui.Controls;

namespace ComfyUI_Nexus.Views.Controls.Buttons;

internal static class RailButtonVisuals
{
	internal static void ApplyTooltip(BindableObject target, string? tooltipText)
	{
		ToolTipProperties.SetText(target, string.IsNullOrWhiteSpace(tooltipText) ? string.Empty : tooltipText);
	}

	internal static void ApplyIconPair(Image normalIcon, Image activeIcon, bool isSelected, ImageSource? activeIconSource)
	{
		bool showActive = isSelected && activeIconSource != null;
		normalIcon.Opacity = showActive ? 0 : 1;
		activeIcon.Opacity = showActive ? 1 : 0;
	}
}
