namespace ComfyUI_Nexus.Platform;

public sealed class DirectoryWatcherOptions
{
	public static DirectoryWatcherOptions Default { get; } = new();

	public bool IncludeSubdirectories { get; init; } = true;

	public int DebounceIntervalMs { get; init; } = 700;

	public int StableDelayMs { get; init; } = 350;

	public IReadOnlyList<string> IgnoredFileNames { get; init; } =
	[
		".DS_Store",
		"Thumbs.db",
	];

	public IReadOnlyList<string> IgnoredExtensions { get; init; } =
	[
		".tmp",
		".swp",
		".temp",
	];

	public IReadOnlyList<string> IgnoredSuffixes { get; init; } =
	[
		"~",
	];
}
