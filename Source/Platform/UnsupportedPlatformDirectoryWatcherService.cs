namespace ComfyUI_Nexus.Platform;

public sealed class UnsupportedPlatformDirectoryWatcherService : IPlatformDirectoryWatcherService
{
	public PlatformFeatureResult<IPlatformDirectoryWatcherSubscription> TryWatch(
		string rootPath,
		DirectoryWatcherOptions options,
		Action<PlatformDirectoryChange> onChange,
		Action<Exception?> onError)
	{
		return PlatformFeatureResult<IPlatformDirectoryWatcherSubscription>.NotSupported(
			"Directory watching is not supported on this platform.");
	}
}
