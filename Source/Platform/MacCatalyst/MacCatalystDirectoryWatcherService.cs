#if MACCATALYST
using System.Runtime.Versioning;

namespace ComfyUI_Nexus.Platform.MacCatalyst;

[SupportedOSPlatform("maccatalyst15.0")]
public sealed class MacCatalystDirectoryWatcherService : IPlatformDirectoryWatcherService
{
	public PlatformFeatureResult<IPlatformDirectoryWatcherSubscription> TryWatch(
		string rootPath,
		DirectoryWatcherOptions options,
		Action<PlatformDirectoryChange> onChange,
		Action<Exception?> onError)
	{
		ArgumentNullException.ThrowIfNull(options);

		if (string.IsNullOrWhiteSpace(rootPath))
		{
			return PlatformFeatureResult<IPlatformDirectoryWatcherSubscription>.Failed("Root path is required.");
		}

		if (!Directory.Exists(rootPath))
		{
			return PlatformFeatureResult<IPlatformDirectoryWatcherSubscription>.Failed($"Directory not found: {rootPath}");
		}

		FileSystemWatcher? watcher = null;
		try
		{
			watcher = new FileSystemWatcher(rootPath)
			{
				IncludeSubdirectories = options.IncludeSubdirectories,
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
				EnableRaisingEvents = false,
			};

			var subscription = new MacCatalystDirectoryWatcherSubscription(watcher, options, onChange, onError);
			watcher = null;
			return PlatformFeatureResult<IPlatformDirectoryWatcherSubscription>.Success(subscription);
		}
		catch (Exception ex)
		{
			watcher?.Dispose();
			return PlatformFeatureResult<IPlatformDirectoryWatcherSubscription>.Failed(ex.Message);
		}
	}
}
#endif
