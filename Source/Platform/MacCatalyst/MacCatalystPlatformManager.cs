#if MACCATALYST
namespace ComfyUI_Nexus.Platform.MacCatalyst;

public sealed class MacCatalystPlatformManager : UnsupportedPlatformManager
{
	public override NexusPlatformKind Kind => NexusPlatformKind.MacCatalyst;

	public override IPlatformShellService Shell { get; } = new MacCatalystShellService();

	public override IPlatformDirectoryWatcherService DirectoryWatcher { get; } = new MacCatalystDirectoryWatcherService();
}
#endif
