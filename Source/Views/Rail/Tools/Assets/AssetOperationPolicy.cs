namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

/// <summary>
/// Selection policy used to enable or disable asset file operations per root profile.
/// </summary>
internal enum AssetOperationPolicy
{
	All,
	FileOnly,
	DirectoryOnly,
	None,
}
