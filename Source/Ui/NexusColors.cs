namespace ComfyUI_Nexus.Ui;

/// <summary>
/// Shared Nexus chrome colors. Keep role names stable so individual views can
/// expose their own semantic aliases without repeating raw hex values.
/// </summary>
internal static class NexusColors
{
	internal static readonly Color Transparent = Color.FromArgb("#00000000");
	internal static readonly Color White = Color.FromArgb("#ffffff");
	internal static readonly Color TextPrimary = Color.FromArgb("#f3fbff");
	internal static readonly Color TextStrong = Color.FromArgb("#f4feff");
	internal static readonly Color TextSoft = Color.FromArgb("#dff8ff");
	internal static readonly Color TextMuted = Color.FromArgb("#9bc4df");
	internal static readonly Color TextDim = Color.FromArgb("#7fa0b8");
	internal static readonly Color TextFaint = Color.FromArgb("#6688aa");

	internal static readonly Color Accent = Color.FromArgb("#31d8ff");
	internal static readonly Color AccentBright = Color.FromArgb("#7ee7ff");
	internal static readonly Color AccentHover = Color.FromArgb("#8fefff");
	internal static readonly Color AccentText = Color.FromArgb("#88e4ff");
	internal static readonly Color AccentDeep = Color.FromArgb("#00e5ff");
	internal static readonly Color AccentWash = Color.FromArgb("#0A31d8ff");
	internal static readonly Color AccentSoft = Color.FromArgb("#1A31d8ff");
	internal static readonly Color AccentHoverSoft = Color.FromArgb("#3331d8ff");
	internal static readonly Color AccentStroke = Color.FromArgb("#2231d8ff");
	internal static readonly Color AccentStrokeStrong = Color.FromArgb("#6631d8ff");
	internal static readonly Color AccentGlow = Color.FromArgb("#4D31d8ff");

	internal static readonly Color Surface = Color.FromArgb("#121826");
	internal static readonly Color SurfaceDark = Color.FromArgb("#0b0f19");
	internal static readonly Color SurfaceDarkTranslucent = Color.FromArgb("#E6081018");
	internal static readonly Color SurfaceHover = Color.FromArgb("#161d2d");
	internal static readonly Color SurfaceRaised = Color.FromArgb("#182a3a");
	internal static readonly Color SurfaceSubtle = Color.FromArgb("#0Affffff");
	internal static readonly Color SurfaceSubtleHover = Color.FromArgb("#26ffffff");
	internal static readonly Color SurfaceOverlay = Color.FromArgb("#12224430");
	internal static readonly Color StrokeSubtle = Color.FromArgb("#1a2235");
	internal static readonly Color Stroke = Color.FromArgb("#222e44");
	internal static readonly Color StrokeHover = Color.FromArgb("#2f4e73");

	internal static readonly Color Warning = Color.FromArgb("#ffaa00");
	internal static readonly Color WarningSoft = Color.FromArgb("#1Affaa00");
	internal static readonly Color WarningHover = Color.FromArgb("#33ffaa00");
	internal static readonly Color WarningText = Color.FromArgb("#ffb84d");
	internal static readonly Color Success = Color.FromArgb("#31ffb1");
	internal static readonly Color Danger = Color.FromArgb("#ff6d8f");
	internal static readonly Color DangerSoft = Color.FromArgb("#22ff6d8f");
	internal static readonly Color DangerHover = Color.FromArgb("#38ff6d8f");
	internal static readonly Color DangerText = Color.FromArgb("#ff8aa0");
	internal static readonly Color DeleteSoft = Color.FromArgb("#14ff4d6d");
	internal static readonly Color DeleteHover = Color.FromArgb("#28ff4d6d");
	internal static readonly Color DeleteText = Color.FromArgb("#ff8aa0");
}
