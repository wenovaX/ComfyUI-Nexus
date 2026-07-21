using System.Runtime.InteropServices;
using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Ui;

internal static class UiThread
{
	private sealed class PendingPost
	{
		internal required long Generation { get; init; }
		internal required Action Action { get; init; }
		internal bool Scheduled { get; set; }
	}

	private static readonly object LatestPostGate = new();
	private static readonly Dictionary<string, PendingPost> LatestPosts = new(StringComparer.Ordinal);

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

	/// <summary>
	/// Schedules only the newest UI update for an owner/key pair. The caller never
	/// waits for dispatcher execution, so a stalled UI dispatcher cannot block a
	/// bridge, lifecycle, or background worker lane.
	/// </summary>
	internal static void PostLatest(string owner, string key, long generation, Action action)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(owner);
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		ArgumentNullException.ThrowIfNull(action);

		string postKey = $"{owner}:{key}";
		bool schedule = false;
		lock (LatestPostGate)
		{
			if (!LatestPosts.TryGetValue(postKey, out PendingPost? pending))
			{
				pending = new PendingPost
				{
					Generation = generation,
					Action = action,
					Scheduled = true,
				};
				LatestPosts.Add(postKey, pending);
				schedule = true;
			}
			else
			{
				LatestPosts[postKey] = new PendingPost
				{
					Generation = generation,
					Action = action,
					Scheduled = pending.Scheduled,
				};
				schedule = !pending.Scheduled;
			}
		}

		if (schedule)
		{
			if (!TryBeginInvoke(() => FlushLatest(postKey), $"UI_DISPATCH:{postKey}"))
			{
				lock (LatestPostGate)
				{
					LatestPosts.Remove(postKey);
				}
			}
		}
	}

	internal static string GetPostSnapshot()
	{
		lock (LatestPostGate)
		{
			return LatestPosts.Count == 0
				? "none"
				: string.Join(", ", LatestPosts.Select(pair => $"{pair.Key}@{pair.Value.Generation}"));
		}
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

	private static void FlushLatest(string postKey)
	{
		PendingPost? pending;
		lock (LatestPostGate)
		{
			if (!LatestPosts.Remove(postKey, out pending))
			{
				return;
			}
		}

		TryRun(pending.Action, $"UI_DISPATCH:{postKey}");
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
