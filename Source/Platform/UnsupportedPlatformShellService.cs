namespace ComfyUI_Nexus.Platform;

public sealed class UnsupportedPlatformShellService : IPlatformShellService
{
	public Task<PlatformFeatureResult<bool>> OpenPathAsync(string path)
		=> Task.FromResult(PlatformFeatureResult<bool>.NotSupported(
			"Opening paths in the operating system shell is not supported on this platform yet."));

	public Task<PlatformFeatureResult<bool>> RevealInFileManagerAsync(string path)
		=> Task.FromResult(PlatformFeatureResult<bool>.NotSupported(
			"Revealing paths in the operating system file manager is not supported on this platform yet."));
}
