namespace ComfyUI_Nexus.Platform;

public sealed class UnsupportedPlatformFileClipboardService : IPlatformFileClipboardService
{
	public Task<PlatformFeatureResult<bool>> SetFilesAsync(IReadOnlyList<string> paths, bool cut)
		=> Task.FromResult(PlatformFeatureResult<bool>.NotSupported(
			"Copying files to the operating system clipboard is not supported on this platform yet."));
}
