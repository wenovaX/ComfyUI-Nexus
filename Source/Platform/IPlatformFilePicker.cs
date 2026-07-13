namespace ComfyUI_Nexus.Platform;

public interface IPlatformFilePicker
{
	Task<PlatformFeatureResult<string>> PickFolderAsync(string? title = null);
	Task<PlatformFeatureResult<string>> PickFileAsync(string? title = null, IReadOnlyList<string>? fileTypes = null);
}
