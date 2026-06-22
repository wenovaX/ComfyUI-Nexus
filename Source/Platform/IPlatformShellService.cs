namespace ComfyUI_Nexus.Platform;

public interface IPlatformShellService
{
	Task<PlatformFeatureResult<bool>> OpenPathAsync(string path);

	Task<PlatformFeatureResult<bool>> RevealInFileManagerAsync(string path);
}
