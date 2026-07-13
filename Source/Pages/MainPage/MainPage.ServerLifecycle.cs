using ComfyUI_Nexus.Setup.Runtime;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private Task<ServerLifecycleResult> RunServerLifecycleFromLoadingAsync(ServerLifecycleRequest request)
		=> _serverLifecycle.RunAsync(request, CancellationToken.None);

	private void OnServerLifecycleStateChanged(ServerLifecycleSnapshot snapshot)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			if (!_isShuttingDown)
			{
				LoadingOverlayControl.ApplyServerLifecycleSnapshot(snapshot);
			}
		});
	}

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
