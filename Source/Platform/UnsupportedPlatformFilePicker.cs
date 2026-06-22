namespace ComfyUI_Nexus.Platform;

public sealed class UnsupportedPlatformFilePicker : IPlatformFilePicker
{
	public Task<PlatformFeatureResult<string>> PickFolderAsync(string? title = null)
		=> Task.FromResult(PlatformFeatureResult<string>.NotSupported(
			"Folder selection is not supported on this platform yet."));

	public Task<PlatformFeatureResult<string>> PickFileAsync(string? title = null, IReadOnlyList<string>? fileTypes = null)
		=> Task.FromResult(PlatformFeatureResult<string>.NotSupported(
			"File selection is not supported on this platform yet."));
}
