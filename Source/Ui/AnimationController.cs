namespace ComfyUI_Nexus.Ui;

internal static class AnimationController
{
	private static int _lockCount;

	public static async Task RunLockedAsync(Grid blockerOverlay, Func<Task> animationTask)
	{
		try
		{
			if (Interlocked.Increment(ref _lockCount) == 1)
			{
				MainThread.BeginInvokeOnMainThread(() => blockerOverlay.IsVisible = true);
			}

			await animationTask();
		}
		finally
		{
			if (Interlocked.Decrement(ref _lockCount) == 0)
			{
				MainThread.BeginInvokeOnMainThread(() => blockerOverlay.IsVisible = false);
			}
		}
	}
}
