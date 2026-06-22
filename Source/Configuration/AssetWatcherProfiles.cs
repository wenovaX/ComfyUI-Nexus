using ComfyUI_Nexus.Platform;

namespace ComfyUI_Nexus.Configuration;

internal static class AssetWatcherProfiles
{
	internal static DirectoryWatcherOptions Output { get; } = new()
	{
		DebounceIntervalMs = 350,
		StableDelayMs = 150,
	};

	internal static DirectoryWatcherOptions Input { get; } = new()
	{
		DebounceIntervalMs = 700,
		StableDelayMs = 350,
	};

	internal static DirectoryWatcherOptions Models { get; } = new()
	{
		DebounceIntervalMs = 1800,
		StableDelayMs = 2200,
	};

	internal static DirectoryWatcherOptions Workflows { get; } = new()
	{
		DebounceIntervalMs = 450,
		StableDelayMs = 180,
	};
}
