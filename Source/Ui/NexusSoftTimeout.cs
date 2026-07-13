namespace ComfyUI_Nexus.Ui;

/// <summary>
/// Observes a logical timeout without cancelling the operation being observed.
/// The operation remains single-flight and is always awaited to natural completion.
/// </summary>
internal static class NexusSoftTimeout
{
	internal static async Task<T> AwaitAsync<T>(
		Task<T> operation,
		TimeSpan threshold,
		Action? onPending = null)
	{
		ArgumentNullException.ThrowIfNull(operation);
		await ObserveThresholdAsync(operation, threshold, onPending);
		return await operation;
	}

	internal static async Task AwaitAsync(
		Task operation,
		TimeSpan threshold,
		Action? onPending = null)
	{
		ArgumentNullException.ThrowIfNull(operation);
		await ObserveThresholdAsync(operation, threshold, onPending);
		await operation;
	}

	private static async Task ObserveThresholdAsync(Task operation, TimeSpan threshold, Action? onPending)
	{
		if (!operation.IsCompleted && threshold > TimeSpan.Zero)
		{
			using var timer = new PeriodicTimer(threshold);
			Task<bool> thresholdTask = timer.WaitForNextTickAsync().AsTask();
			if (await Task.WhenAny(operation, thresholdTask) == thresholdTask && !operation.IsCompleted)
			{
				onPending?.Invoke();
			}
		}
	}
}
