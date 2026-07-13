using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Ui;

/// <summary>
/// Serializes latest-wins work for one owner without cancelling a superseded operation.
/// </summary>
internal sealed class NexusLatestOperationCoordinator : IDisposable
{
	private sealed class OperationSlot
	{
		internal required CancellationTokenSource Lifecycle { get; init; }
		internal required TaskCompletionSource<bool> IdleCompletion { get; init; }
		internal Func<NexusOperationLease, Task>? PendingOperation { get; set; }
		internal long Generation { get; set; }
	}

	private readonly object _gate = new();
	private readonly Dictionary<string, OperationSlot> _slots = new(StringComparer.Ordinal);
	private readonly string _owner;

	internal NexusLatestOperationCoordinator(string owner)
	{
		_owner = owner;
	}

	internal Task<bool> RequestAsync(string key, Func<NexusOperationLease, Task> operation)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		ArgumentNullException.ThrowIfNull(operation);

		OperationSlot slot;
		bool startWorker = false;
		lock (_gate)
		{
			if (!_slots.TryGetValue(key, out slot!))
			{
				slot = new OperationSlot
				{
					Lifecycle = new CancellationTokenSource(),
					IdleCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
				};
				_slots.Add(key, slot);
				startWorker = true;
			}

			slot.Generation++;
			slot.PendingOperation = operation;
		}

		if (startWorker)
		{
			_ = RunSlotAsync(key, slot);
		}

		return slot.IdleCompletion.Task;
	}

	internal void Request(string key, Func<NexusOperationLease, Task> operation)
		=> _ = RequestAsync(key, operation);

	/// <summary>
	/// Invalidates an in-flight result without cancelling its work. This is used when
	/// an owner hides or replaces a surface but remains alive for future requests.
	/// </summary>
	internal void Invalidate(string key)
	{
		lock (_gate)
		{
			if (_slots.TryGetValue(key, out OperationSlot? slot))
			{
				slot.Generation++;
				slot.PendingOperation = null;
			}
		}
	}

	internal void InvalidateAll()
	{
		lock (_gate)
		{
			foreach (OperationSlot slot in _slots.Values)
			{
				slot.Generation++;
				slot.PendingOperation = null;
			}
		}
	}

	internal void Stop(string key)
	{
		OperationSlot? slot;
		lock (_gate)
		{
			if (!_slots.Remove(key, out slot))
			{
				return;
			}
		}

		slot.Lifecycle.Cancel();
		slot.IdleCompletion.TrySetResult(false);
	}

	internal void StopAll()
	{
		string[] keys;
		lock (_gate)
		{
			keys = _slots.Keys.ToArray();
		}

		foreach (string key in keys)
		{
			Stop(key);
		}
	}

	public void Dispose()
		=> StopAll();

	private async Task RunSlotAsync(string key, OperationSlot slot)
	{
		try
		{
			while (true)
			{
				Func<NexusOperationLease, Task>? operation;
				long generation;
				lock (_gate)
				{
					if (!_slots.TryGetValue(key, out OperationSlot? current) || !ReferenceEquals(current, slot))
					{
						return;
					}

					operation = slot.PendingOperation;
					slot.PendingOperation = null;
					generation = slot.Generation;
				}

				if (operation is null)
				{
					if (CompleteSlot(key, slot, completed: true))
					{
						return;
					}

					continue;
				}

				var lease = new NexusOperationLease(key, generation, slot.Lifecycle.Token, () => IsCurrent(key, slot, generation));
				try
				{
					await operation(lease);
				}
				catch (OperationCanceledException) when (slot.Lifecycle.IsCancellationRequested)
				{
					return;
				}
				catch (Exception ex)
				{
					NexusLog.Warning($"[LATEST] {_owner}/{key} generation {generation} failed: {ex.GetType().Name}: {ex.Message}");
				}
			}
		}
		finally
		{
			_ = CompleteSlot(key, slot, completed: !slot.Lifecycle.IsCancellationRequested);
		}
	}

	private bool IsCurrent(string key, OperationSlot slot, long generation)
	{
		lock (_gate)
		{
			return _slots.TryGetValue(key, out OperationSlot? current)
				&& ReferenceEquals(current, slot)
				&& !slot.Lifecycle.IsCancellationRequested
				&& slot.Generation == generation;
		}
	}

	private bool CompleteSlot(string key, OperationSlot slot, bool completed)
	{
		bool disposeLifecycle = false;
		lock (_gate)
		{
			if (!_slots.TryGetValue(key, out OperationSlot? current) || !ReferenceEquals(current, slot))
			{
				slot.Lifecycle.Dispose();
				return true;
			}

			if (slot.PendingOperation is not null && !slot.Lifecycle.IsCancellationRequested)
			{
				return false;
			}

			_slots.Remove(key);
			disposeLifecycle = true;
		}

		slot.IdleCompletion.TrySetResult(completed);
		if (disposeLifecycle)
		{
			slot.Lifecycle.Dispose();
		}

		return true;
	}
}

internal sealed class NexusOperationLease
{
	private readonly Func<bool> _isCurrent;

	internal NexusOperationLease(string key, long generation, CancellationToken lifecycleToken, Func<bool> isCurrent)
	{
		Key = key;
		Generation = generation;
		LifecycleToken = lifecycleToken;
		_isCurrent = isCurrent;
	}

	internal string Key { get; }
	internal long Generation { get; }
	internal CancellationToken LifecycleToken { get; }
	internal bool IsCurrent => _isCurrent();

	internal async Task<bool> WaitForAsync(TimeSpan interval)
	{
		if (interval <= TimeSpan.Zero)
		{
			return IsCurrent;
		}

		using var timer = new PeriodicTimer(interval);
		try
		{
			return await timer.WaitForNextTickAsync(LifecycleToken) && IsCurrent;
		}
		catch (OperationCanceledException) when (LifecycleToken.IsCancellationRequested)
		{
			return false;
		}
	}
}
