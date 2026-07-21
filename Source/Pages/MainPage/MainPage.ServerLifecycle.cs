using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private Task<ServerLifecycleResult> RunServerLifecycleFromLoadingAsync(ServerLifecycleRequest request)
		=> _serverLifecycle.RunAsync(request, CancellationToken.None);

	private void OnServerLifecycleStateChanged(ServerLifecycleSnapshot snapshot)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			_controlDeckServerStatus = GetControlDeckServerStatus(snapshot);
			RefreshControlDeckWebPulse(force: true);

			if (!_isShuttingDown)
			{
				LoadingOverlayControl.ApplyServerLifecycleSnapshot(snapshot);
			}
		});
	}

	private NexusControlDeckServerStatus GetControlDeckServerStatus(ServerLifecycleSnapshot snapshot)
		=> snapshot.State switch
		{
			ServerLifecycleState.ServerReady => NexusControlDeckServerStatus.Ready,
			ServerLifecycleState.Completed when snapshot.Mode is ServerLifecycleMode.Shutdown or ServerLifecycleMode.KillServerAndExit => NexusControlDeckServerStatus.Offline,
			ServerLifecycleState.Preparing or
			ServerLifecycleState.QuiescingServices or
			ServerLifecycleState.ServicesEnded or
			ServerLifecycleState.StoppingServer or
			ServerLifecycleState.VerifyingServerStopped or
			ServerLifecycleState.BootingServer or
			ServerLifecycleState.RunningMaintenance => NexusControlDeckServerStatus.Transitioning,
			ServerLifecycleState.Failed => NexusControlDeckServerStatus.Offline,
			_ => _controlDeckServerStatus,
		};

	private void OnServerLifecycleLogEmitted(string message)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			if (!_isShuttingDown)
			{
				LoadingOverlayControl.AppendServerLifecycleLog(message);
			}
		});
	}
}
