namespace ComfyUI_Nexus.Setup.Runtime;

using System.Diagnostics;
using ComfyUI_Nexus.Setup.Services;

/// <summary>
/// App-lifetime boundary for ComfyUI process ownership and shutdown verification.
/// Callers receive process facts through this controller instead of accessing
/// registry implementation state directly.
/// </summary>
internal sealed class NexusServerProcessController
{
	private readonly ComfyServerProcessRegistry _registry;

	internal NexusServerProcessController(SetupSettingsService settingsService)
	{
		_registry = new ComfyServerProcessRegistry(settingsService);
	}

	internal void Register(Process process, string logPath)
		=> _registry.Register(process, logPath);

	internal bool IsShuttingDown(Process process)
		=> _registry.IsShuttingDown(process);

	internal ComfyServerProcessInfo? FindServerProcess()
		=> _registry.FindServerProcess();

	internal Process? TryGetProcess(ComfyServerProcessInfo processInfo)
		=> _registry.TryGetProcess(processInfo);

	internal Task ShutdownAsync(ComfyServerProcessInfo processInfo, TimeSpan timeout)
		=> _registry.ShutdownAsync(processInfo, timeout);

	internal Task EnsureStoppedAsync(
		ComfyServerProcessInfo? expectedProcess,
		int port,
		TimeSpan timeout,
		TimeSpan pollingInterval,
		CancellationToken cancellationToken)
		=> _registry.EnsureStoppedAsync(
			expectedProcess,
			port,
			timeout,
			pollingInterval,
			cancellationToken);
}
