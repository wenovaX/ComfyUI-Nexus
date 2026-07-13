namespace ComfyUI_Nexus.AssetHub;

internal enum AssetHubItemType
{
	File,
	Directory,
}

internal sealed record AssetHubItem(
	string Name,
	string FullPath,
	AssetHubItemType Type,
	long SizeBytes,
	DateTimeOffset? ModifiedAtUtc,
	bool IsImage,
	bool IsVideo,
	string? RelativePath = null);
