namespace ComfyUI_Nexus.Setup.Services;

using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;

internal sealed class NexusAppEntryService
{
	private INexusAppEntry? _appEntry;

	internal Action<string>? OnMessage { get; set; }

	internal void SetEntry(INexusAppEntry appEntry) => _appEntry = appEntry;

	internal async Task<SetupStepResult> LaunchAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (_appEntry == null)
		{
			var settings = SetupSettingsService.Instance.Settings;
			string host = settings.ListenAddress;
			int port = settings.ServerPort;
			var probeInterval = TimeSpan.FromMilliseconds(Math.Max(50, settings.PortProbeIntervalMilliseconds));
			OnMessage?.Invoke($"[Launch] Nexus App Entry not connected. Checking port {port} fallback...");

			if (await PortProbeService.WaitUntilOpenAsync(host, port, probeInterval, probeInterval, cancellationToken))
			{
				return new SetupStepResult(true, $"[Launch] Backend is already active on port {port}. Hand-off bypassed.", 1);
			}

			return new SetupStepResult(false, "Nexus App Entry is not connected and backend port is not responding.", 0);
		}

		OnMessage?.Invoke("[Launch] Handing off to Nexus App Entry...");

		try
		{
			await _appEntry.LaunchAsync(cancellationToken);
			return new SetupStepResult(true, "[Launch] Nexus App Entry accepted the boot request.", 1);
		}
		catch (Exception ex)
		{
			return new SetupStepResult(false, $"[Launch] Nexus App Entry failed: {ex.Message}", 0);
		}
	}
}
