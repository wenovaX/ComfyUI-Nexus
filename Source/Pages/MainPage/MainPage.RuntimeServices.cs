using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Setup.Runtime;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private bool _shellRuntimeServicesActive;

	private async Task StartShellRuntimeServicesAsync()
	{
		if (_isShuttingDown || _shellRuntimeServicesActive)
		{
			return;
		}

		_shellRuntimeServicesActive = true;
		GpuDiscoveryService.StartResult gpuStart = await GpuDiscoveryService.StartAsync();
		if (!gpuStart.IsSuccess)
		{
			NexusLog.Warning($"[RUNTIME] GPU discovery service completed without device data: {gpuStart.FailureMessage}");
		}

		StartNativeSystemTelemetry();
		NexusLog.Info("[RUNTIME] Shell telemetry and indicators started.");
	}

	internal async Task QuiesceShellRuntimeServicesAsync(string reason)
	{
		bool wasActive = _shellRuntimeServicesActive;
		_shellRuntimeServicesActive = false;
		StopNativeSystemTelemetry();
		_gpuStatusController.Stop();
		HeaderControl.SetGpuVisibility(false);

		if (wasActive)
		{
			Log($"[RUNTIME] Shell telemetry and indicators stopped: {reason}");
		}

		GpuDiscoveryService.StopResult gpuStop = await GpuDiscoveryService.StopAsync();
		if (!gpuStop.IsSuccess)
		{
			Log($"[RUNTIME] GPU discovery service stop completed with an error: {gpuStop.FailureMessage}");
		}
	}

	private void StopShellRuntimeServicesForUnload()
	{
		_shellRuntimeServicesActive = false;
		StopNativeSystemTelemetry();
		_gpuStatusController.Stop();
	}
}
