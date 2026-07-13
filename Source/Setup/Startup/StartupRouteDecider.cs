namespace ComfyUI_Nexus.Setup.Startup;

using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;

internal sealed class StartupRouteDecider
{
	private readonly StartupReadinessProbe _readinessProbe = new();

	internal async Task<StartupRouteDecision> DecideAsync(CancellationToken cancellationToken)
	{
		SetupSettingsService settingsService = SetupSettingsService.Instance;
		SetupSettings settings = settingsService.Settings;
		if (settings.PendingRuntimePurge || settings.RuntimePurgeInProgress)
		{
			if (Directory.Exists(ComfyInstallService.InstalledPath))
			{
				return new StartupRouteDecision(StartupRouteKind.MaintenanceRecovery, "Runtime purge is pending or was interrupted.");
			}

			settingsService.CompleteRuntimePurgeAndResetSetup();
			settings = settingsService.Settings;
		}

		var pendingTasks = settingsService.GetRunnableBootTasks();
		if (pendingTasks.Any(IsMaintenanceRecoveryTask))
		{
			return new StartupRouteDecision(StartupRouteKind.MaintenanceRecovery, "Maintenance task is pending or was interrupted.");
		}

		if (pendingTasks.Count > 0)
		{
			return new StartupRouteDecision(StartupRouteKind.ServerLaunchOnly, "Pending boot task(s) must run before loading Nexus.");
		}

		StartupReadinessResult readiness = await _readinessProbe.CheckAsync(settings, cancellationToken);
		if (!readiness.IsUsable)
		{
			return new StartupRouteDecision(StartupRouteKind.FullSetup, readiness.Reason);
		}

		if (await IsComfyApiReadyAsync(cancellationToken))
		{
			return new StartupRouteDecision(StartupRouteKind.DirectLoading, "ComfyUI API is already responding.");
		}

		if (ComfyServerProcessRegistry.FindServerProcess() != null)
		{
			return new StartupRouteDecision(StartupRouteKind.ServerStartupPending, "A previous server launch process is still starting.");
		}

		if (!settings.LastLaunchSuccessful)
		{
			return new StartupRouteDecision(StartupRouteKind.ServerLaunchOnly, "Setup is usable, but no previous successful launch was recorded.");
		}

		if (settings.LastActivePort is { } lastPort && lastPort != settings.ServerPort)
		{
			return new StartupRouteDecision(StartupRouteKind.ServerLaunchOnly, $"Last successful port was {lastPort}, current port is {settings.ServerPort}.");
		}

		return new StartupRouteDecision(StartupRouteKind.ServerLaunchOnly, "Previous setup is valid, but the server is offline.");
	}

	private static async Task<bool> IsComfyApiReadyAsync(CancellationToken cancellationToken)
	{
		Uri endpoint = new(ComfyApiOptions.ObjectInfoUrl);
		Task<LocalHttpProbeResult> probeTask = LocalServerProbe.TryGetAsync(endpoint, cancellationToken);
		LocalHttpProbeResult result = await NexusSoftTimeout.AwaitAsync(
			probeTask,
			TimeSpan.FromSeconds(2),
			() => NexusLog.Trace($"[STARTUP] ComfyUI API response pending at {endpoint}."));
		return result.StatusCode == System.Net.HttpStatusCode.OK;
	}

	private static bool IsMaintenanceRecoveryTask(PendingBootTask task)
		=> task.Id is PendingBootTaskIds.RuntimePurge or PendingBootTaskIds.ResetSetup or PendingBootTaskIds.ResetAll;
}
