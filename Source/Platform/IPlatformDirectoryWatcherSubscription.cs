namespace ComfyUI_Nexus.Platform;

public interface IPlatformDirectoryWatcherSubscription : IDisposable
{
	void Start();
	void Stop();
}
