namespace ComfyUI_Nexus.Platform;

public interface IPlatformDirectoryWatcherService
{
	PlatformFeatureResult<IPlatformDirectoryWatcherSubscription> TryWatch(
		string rootPath,
		DirectoryWatcherOptions options,
		Action<PlatformDirectoryChange> onChange,
		Action<Exception?> onError);
}
