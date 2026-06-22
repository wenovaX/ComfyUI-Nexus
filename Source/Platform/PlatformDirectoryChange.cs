namespace ComfyUI_Nexus.Platform;

public enum PlatformDirectoryChangeKind
{
	Created,
	Changed,
	Deleted,
	Renamed,
	Unknown,
}

public readonly record struct PlatformDirectoryChange(
	PlatformDirectoryChangeKind Kind,
	string? Path,
	string? OldPath = null);
