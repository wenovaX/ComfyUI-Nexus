namespace ComfyUI_Nexus.Setup;

internal interface INexusAppEntry
{
	Task LaunchAsync(CancellationToken cancellationToken);
}

internal sealed class DelegateNexusAppEntry(Func<CancellationToken, Task> launchAsync) : INexusAppEntry
{
	public Task LaunchAsync(CancellationToken cancellationToken) => launchAsync(cancellationToken);
}
