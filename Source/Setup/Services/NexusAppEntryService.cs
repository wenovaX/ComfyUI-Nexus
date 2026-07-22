namespace ComfyUI_Nexus.Setup.Services;

using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Ui;

internal sealed class NexusAppEntryService
{
	private readonly SetupSettingsService _settingsService;
	private INexusAppEntry? _appEntry;

	internal NexusAppEntryService(SetupSettingsService settingsService)
	{
		_settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
	}

	internal Action<string>? OnMessage { get; set; }

	internal void SetEntry(INexusAppEntry appEntry) => _appEntry = appEntry;

	internal async Task<SetupStepResult> LaunchAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (_appEntry == null)
		{
			OnMessage?.Invoke("[Launch] Nexus App Entry not connected. Checking ComfyUI API readiness fallback...");

			Uri endpoint = new(ComfyApiOptions.GetObjectInfoUrl(_settingsService.Settings));
			Task<LocalHttpProbeResult> probeTask = LocalServerProbe.TryGetAsync(endpoint, cancellationToken);
			LocalHttpProbeResult probeResult = await NexusSoftTimeout.AwaitAsync(
				probeTask,
				TimeSpan.FromSeconds(2),
				() => OnMessage?.Invoke($"[Launch] ComfyUI API response pending at {endpoint}; continuing to wait."));
			if (probeResult.StatusCode == System.Net.HttpStatusCode.OK)
			{
				return new SetupStepResult(true, "[Launch] ComfyUI API is already ready. Hand-off bypassed.", 1);
			}

			return new SetupStepResult(false, "Nexus App Entry is not connected and ComfyUI API is not ready.", 0);
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
