using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus.Views.Rail.Tools;

internal sealed class RailSearchVisualController
{
	private static readonly Color NormalBackgroundColor = Color.FromArgb("#0D141D");
	private static readonly Color HoverBackgroundColor = Color.FromArgb("#151E2B");
	private static readonly Color FocusedBackgroundColor = Color.FromArgb("#132A40");
	private static readonly Color NormalStrokeColor = Colors.Transparent;
	private static readonly Color FocusedStrokeColor = NexusColors.Accent;

	private readonly Border _border;
	private readonly Entry _entry;
	private bool _isHovered;
	private bool _isFocused;

	internal RailSearchVisualController(Border border, Entry entry)
	{
		_border = border;
		_entry = entry;
		_entry.HandlerChanged += OnEntryHandlerChanged;
		Apply();
	}

	internal void SetHovered(bool isHovered)
	{
		_isHovered = isHovered;
		Apply();
	}

	internal void SetFocused(bool isFocused)
	{
		_isFocused = isFocused;
		Apply();
		ApplyNativeSelectionColors();
	}

	private void Apply()
	{
		_border.BackgroundColor = _isFocused
			? FocusedBackgroundColor
			: _isHovered ? HoverBackgroundColor : NormalBackgroundColor;
		_border.Stroke = _isFocused ? FocusedStrokeColor : NormalStrokeColor;
		_border.StrokeThickness = _isFocused ? 1 : 0;
	}

	private void ApplyNativeSelectionColors()
	{
#if WINDOWS
		var platformView = _entry.Handler?.PlatformView;
		var property = platformView?.GetType().GetProperty("SelectionHighlightColor");
		if (property == null)
		{
			return;
		}

		var selectionBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
			Microsoft.UI.ColorHelper.FromArgb(255, 49, 216, 255));
		if (property.PropertyType.IsAssignableFrom(selectionBrush.GetType()))
		{
			property.SetValue(platformView, selectionBrush);
		}
#endif
	}

	private void OnEntryHandlerChanged(object? sender, EventArgs e)
		=> ApplyNativeSelectionColors();
}

internal static class RailSearchClearButtonVisuals
{
	private static readonly Color NormalBackgroundColor = Colors.Transparent;
	private static readonly Color NormalStrokeColor = Colors.Transparent;
	private static readonly Color NormalTextColor = Color.FromArgb("#8FAFC3");
	private static readonly Color HoverTextColor = Color.FromArgb("#F2FBFF");

	internal static void Attach(Border button, Label label)
	{
		ArgumentNullException.ThrowIfNull(button);
		ArgumentNullException.ThrowIfNull(label);
		Apply(button, label, isHovered: false);

		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (_, _) => Apply(button, label, isHovered: true);
		pointer.PointerExited += (_, _) => Apply(button, label, isHovered: false);
		button.GestureRecognizers.Add(pointer);
	}

	private static void Apply(Border button, Label label, bool isHovered)
	{
		button.BackgroundColor = NormalBackgroundColor;
		button.Stroke = NormalStrokeColor;
		button.StrokeThickness = 0;
		label.TextColor = isHovered ? HoverTextColor : NormalTextColor;
	}
}
