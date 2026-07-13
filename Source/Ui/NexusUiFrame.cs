using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Ui;

internal static class NexusUiFrame
{
	private const int MaxLayoutDrainAttempts = 4;

	internal static async Task AwaitShellReadyAsync(VisualElement root, string source)
	{
		for (int attempt = 0; attempt < MaxLayoutDrainAttempts; attempt++)
		{
			await AwaitDispatcherTurnAsync(root, source);
			if (IsLayoutReady(root))
			{
				return;
			}
		}

		NexusLog.Trace($"[{source}] Popup shell layout was not ready after dispatcher drain; continuing show animation.");
	}

	internal static Task AwaitDispatcherTurnAsync(VisualElement root, string source)
	{
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		try
		{
			IDispatcher? dispatcher = root.Dispatcher ?? Application.Current?.Dispatcher;
			if (dispatcher != null)
			{
				if (!dispatcher.Dispatch(() => completion.TrySetResult()))
				{
					NexusLog.Trace($"[{source}] Dispatcher turn was unavailable; continuing without a layout drain.");
					completion.TrySetResult();
				}

				return completion.Task;
			}

			MainThread.BeginInvokeOnMainThread(() => completion.TrySetResult());
		}
		catch (Exception ex) when (IsShutdownSafe(ex))
		{
			NexusLog.Trace($"[POPUP] Shell frame wait skipped during shutdown: {ex.Message}");
			completion.TrySetResult();
		}

		return completion.Task;
	}

	private static bool IsLayoutReady(VisualElement root)
		=> root.IsVisible
			&& root.Handler is not null
			&& root.Width > 0
			&& root.Height > 0;

	private static bool IsShutdownSafe(Exception ex)
		=> ex is ObjectDisposedException
			or InvalidOperationException
			or TaskCanceledException
			or OperationCanceledException;
}
