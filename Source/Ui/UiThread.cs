using System.Runtime.InteropServices;
using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Ui;

internal static class UiThread
{
	public static bool TryBeginInvoke(Action action, string source)
	{
		try
		{
			if (MainThread.IsMainThread)
			{
				return TryRun(action, source);
			}

			MainThread.BeginInvokeOnMainThread(() => TryRun(action, source));
			return true;
		}
		catch (Exception ex) when (IsShutdownDispatchException(ex))
		{
			NexusLog.Trace($"[{source}] UI dispatch skipped during shutdown: {ex.Message}");
			return false;
		}
	}

	public static Task InvokeAsync(Func<Task> action, string source)
	{
		try
		{
			if (MainThread.IsMainThread)
			{
				return InvokeDirectAsync(action, source);
			}
		}
		catch (Exception ex) when (IsShutdownDispatchException(ex))
		{
			NexusLog.Trace($"[{source}] UI async dispatch skipped during shutdown: {ex.Message}");
			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		try
		{
			MainThread.BeginInvokeOnMainThread(async () =>
			{
				try
				{
					await action();
					completion.TrySetResult();
				}
				catch (Exception ex) when (IsShutdownDispatchException(ex))
				{
					NexusLog.Trace($"[{source}] UI async dispatch skipped during shutdown: {ex.Message}");
					completion.TrySetResult();
				}
				catch (Exception ex)
				{
					completion.TrySetException(ex);
				}
			});
		}
		catch (Exception ex) when (IsShutdownDispatchException(ex))
		{
			NexusLog.Trace($"[{source}] UI async dispatch skipped during shutdown: {ex.Message}");
			completion.TrySetResult();
		}

		return completion.Task;
	}

	internal static Task YieldDispatcherFramesAsync(int frameCount, string source)
		=> InvokeAsync(() => YieldFramesOnUiThreadAsync(frameCount), source);

	private static bool TryRun(Action action, string source)
	{
		try
		{
			action();
			return true;
		}
		catch (Exception ex) when (IsShutdownDispatchException(ex))
		{
			NexusLog.Trace($"[{source}] UI update skipped during shutdown: {ex.Message}");
			return false;
		}
	}

	private static async Task InvokeDirectAsync(Func<Task> action, string source)
	{
		try
		{
			await action();
		}
		catch (Exception ex) when (IsShutdownDispatchException(ex))
		{
			NexusLog.Trace($"[{source}] UI async update skipped during shutdown: {ex.Message}");
		}
	}

	private static async Task YieldFramesOnUiThreadAsync(int frameCount)
	{
		for (int frame = 0; frame < Math.Max(1, frameCount); frame++)
		{
			await Task.Yield();
		}
	}

	private static bool IsShutdownDispatchException(Exception ex)
		=> ex is InvalidOperationException
			|| ex is ObjectDisposedException
			|| ex is COMException;
}
