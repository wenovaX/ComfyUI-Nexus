namespace ComfyUI_Nexus.Configuration;

internal static class RunModeOptions
{
	internal const string Default = "Run";
	internal const string OnChange = "Run (On Change)";
	internal const string Instant = "Run (Instant)";

	internal static readonly string[] All =
	[
		Default,
		OnChange,
		Instant,
	];
}
