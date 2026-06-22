namespace ComfyUI_Nexus.Platform;

public interface IPlatformFileClipboardService
{
	Task<PlatformFeatureResult<bool>> SetFilesAsync(IReadOnlyList<string> paths, bool cut);
}
