namespace ComfyUI_Nexus.Ui;

/// <summary>
/// Common visual mode for the system loading overlay.
/// </summary>
internal enum LoadingOverlayState
{
	Show,
	Hold,
	Message,
	Error,
	Hide,
}

/// <summary>
/// Immutable display payload consumed by <see cref="LoadingOverlayController"/>.
/// </summary>
/// <param name="State">Overlay mode such as show, hold, message, error, or hide.</param>
/// <param name="Title">Primary title text.</param>
/// <param name="Description">Secondary descriptive text.</param>
/// <param name="Status">Short status line or phase label.</param>
/// <param name="AccentColor">Accent color used by the overlay ring and text highlights.</param>
/// <param name="CenterGlyph">Optional center glyph for error or special states.</param>
/// <param name="Progress">Optional normalized progress from 0 to 1.</param>
internal sealed record LoadingOverlayDisplay(
	LoadingOverlayState State,
	string Title,
	string Description,
	string Status,
	Color AccentColor,
	string? CenterGlyph = null,
	double? Progress = null)
{
	internal bool HasProgress => Progress is >= 0 and <= 1;
}
