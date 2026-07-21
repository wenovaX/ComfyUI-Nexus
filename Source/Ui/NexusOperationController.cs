using System.Threading.Channels;
using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Ui;

/// <summary>
/// Owns latest, ordered serial, and bounded background work for one UI or service surface.
/// Superseded latest requests never cancel running work; they only make its result stale.
/// </summary>
internal sealed class NexusOperationController : IDisposable
{
	private enum OperationMode
	{
		Latest,
		Serial,
	}

	private sealed class Slot
	{
		internal required CancellationTokenSource Lifecycle { get; init; }
		internal Func<NexusOperationLease, Task>? LatestPending { get; set; }
		internal Queue<SerialOperation> SerialPending { get; } = new();
		internal long Generation { get; set; }
		internal long SerialEpoch { get; set; }
		internal bool Running { get; set; }
		internal TaskCompletionSource<bool>? IdleCompletion { get; set; }
		internal OperationMode Mode { get; set; }
	}

	private sealed record SerialOperation(
		Func<NexusOperationLease, Task> Callback,
		long Generation,
		long Epoch);

	private readonly object _gate = new();
	private readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
	private readonly string _owner;
	private bool _disposed;

	internal NexusOperationController(string owner)
	{
		_owner = owner;
		NexusConcurrencyDiagnostics.RegisterOwner(owner, GetSnapshot);
	}

	internal void RequestLatest(string key, Func<NexusOperationLease, Task> operation)
		=> _ = RequestLatestAsync(key, operation);

	internal Task<bool> RequestLatestAsync(string key, Func<NexusOperationLease, Task> operation)
		=> Request(key, operation, OperationMode.Latest);

	internal void RequestSerial(string key, Func<NexusOperationLease, Task> operation)
		=> Request(key, operation, OperationMode.Serial);

	internal Task<T> RunBackgroundAsync<T>(
		NexusBackgroundLane lane,
		string key,
		Func<CancellationToken, T> operation,
		CancellationToken lifecycleToken = default)
	{
		ArgumentNullException.ThrowIfNull(operation);
		return NexusBackgroundWorkers.RunAsync(_owner, key, lane, operation, lifecycleToken);
	}

	internal Task RunBackgroundAsync(
		NexusBackgroundLane lane,
		string key,
		Action<CancellationToken> operation,
		CancellationToken lifecycleToken = default)
		=> RunBackgroundAsync<object?>(lane, key, token =>
		{
			operation(token);
			return null;
		}, lifecycleToken);

	internal void Invalidate(string key)
	{
		lock (_gate)
		{
			if (_slots.TryGetValue(key, out Slot? slot))
			{
				slot.Generation++;
				slot.LatestPending = null;
				slot.SerialPending.Clear();
				slot.SerialEpoch++;
			}
		}
	}

	internal void InvalidateAll()
	{
		lock (_gate)
		{
			foreach (Slot slot in _slots.Values)
			{
				slot.Generation++;
				slot.LatestPending = null;
				slot.SerialPending.Clear();
				slot.SerialEpoch++;
			}
		}
	}

	internal void Stop(string key)
	{
		Slot? slot;
		lock (_gate)
		{
			if (!_slots.Remove(key, out slot))
			{
				return;
			}
		}

		StopSlot(slot);
	}

	internal void StopAll()
	{
		List<Slot> slots;
		lock (_gate)
		{
			slots = _slots.Values.ToList();
			_slots.Clear();
		}

		foreach (Slot slot in slots)
		{
			StopSlot(slot);
		}

		NexusConcurrencyDiagnostics.RecordLifecycleStop(_owner);
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
		}

		StopAll();
		NexusConcurrencyDiagnostics.UnregisterOwner(_owner);
	}
	private Task<bool> Request(string key, Func<NexusOperationLease, Task> operation, OperationMode mode)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		ArgumentNullException.ThrowIfNull(operation);

		Slot slot;
		bool start = false;
		lock (_gate)
		{
			if (_disposed)
			{
				return Task.FromResult(false);
			}

			if (!_slots.TryGetValue(key, out slot!))
			{
				slot = new Slot
				{
					Lifecycle = new CancellationTokenSource(),
					Mode = mode,
				};
				_slots.Add(key, slot);
			}
			else if (slot.Mode != mode)
			{
				throw new InvalidOperationException($"Operation key '{key}' cannot mix {slot.Mode} and {mode} requests.");
			}

			slot.Generation++;
			if (mode == OperationMode.Latest)
			{
				slot.LatestPending = operation;
			}
			else
			{
				slot.SerialPending.Enqueue(new SerialOperation(operation, slot.Generation, slot.SerialEpoch));
			}
			if (!slot.Running)
			{
				slot.Running = true;
				slot.IdleCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				start = true;
			}
		}

		if (start)
		{
			_ = RunSlotAsync(key, slot);
		}

		return slot.IdleCompletion?.Task ?? Task.FromResult(true);
	}
	private async Task RunSlotAsync(string key, Slot slot)
	{
		try
		{
			while (true)
			{
				Func<NexusOperationLease, Task>? operation;
				long generation;
				long serialEpoch;
				lock (_gate)
				{
					if (_disposed || !_slots.TryGetValue(key, out Slot? current) || !ReferenceEquals(current, slot))
					{
						return;
					}

					if (slot.Mode == OperationMode.Latest)
					{
						operation = slot.LatestPending;
						slot.LatestPending = null;
						generation = slot.Generation;
						serialEpoch = 0;
					}
					else if (slot.SerialPending.TryDequeue(out SerialOperation? serialOperation))
					{
						operation = serialOperation.Callback;
						generation = serialOperation.Generation;
						serialEpoch = serialOperation.Epoch;
					}
					else
					{
						operation = null;
						generation = slot.Generation;
						serialEpoch = slot.SerialEpoch;
					}
					if (operation is null)
					{
						slot.Running = false;
						slot.IdleCompletion?.TrySetResult(true);
						return;
					}
				}

				var lease = new NexusOperationLease(
					key,
					generation,
					slot.Lifecycle.Token,
					() => slot.Mode == OperationMode.Serial
						? IsSerialCurrent(key, slot, serialEpoch)
						: IsCurrent(key, slot, generation));
				try
				{
					await operation(lease).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (slot.Lifecycle.IsCancellationRequested)
				{
					return;
				}
				catch (Exception ex)
				{
					NexusConcurrencyDiagnostics.RecordFault(_owner, key, generation, ex);
					NexusLog.Warning($"[OPERATION] {_owner}/{key} generation {generation} failed: {ex.GetType().Name}: {ex.Message}");
				}
			}
		}
		finally
		{
			bool removed;
			lock (_gate)
			{
				removed = !_slots.TryGetValue(key, out Slot? current) || !ReferenceEquals(current, slot);
			}

			if (removed)
			{
				slot.IdleCompletion?.TrySetResult(false);
				slot.Lifecycle.Dispose();
			}
		}
	}

	private static void StopSlot(Slot slot)
	{
		slot.IdleCompletion?.TrySetResult(false);
		slot.Lifecycle.Cancel();
		if (!slot.Running)
		{
			slot.Lifecycle.Dispose();
		}
	}
	private bool IsCurrent(string key, Slot slot, long generation)
	{
		lock (_gate)
		{
			return !_disposed
				&& _slots.TryGetValue(key, out Slot? current)
				&& ReferenceEquals(current, slot)
				&& !slot.Lifecycle.IsCancellationRequested
				&& slot.Generation == generation;
		}
	}

	private bool IsSerialCurrent(string key, Slot slot, long epoch)
	{
		lock (_gate)
		{
			return !_disposed
				&& _slots.TryGetValue(key, out Slot? current)
				&& ReferenceEquals(current, slot)
				&& !slot.Lifecycle.IsCancellationRequested
				&& slot.SerialEpoch == epoch;
		}
	}

	private string GetSnapshot()
	{
		lock (_gate)
		{
			if (_slots.Count == 0)
			{
				return "idle";
			}

			return string.Join(", ", _slots.Select(pair =>
				$"{pair.Key}(generation={pair.Value.Generation}, running={pair.Value.Running}, pending={GetPendingCount(pair.Value)}, mode={pair.Value.Mode})"));
		}
	}

	private static int GetPendingCount(Slot slot)
		=> slot.Mode == OperationMode.Latest
			? slot.LatestPending is null ? 0 : 1
			: slot.SerialPending.Count;
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
internal enum NexusBackgroundLane
{
	FileIo,
	Cpu,
	Maintenance,
}

internal static class NexusBackgroundWorkers
{
	private sealed record WorkItem(string Owner, string Key, Func<object?> Operation, TaskCompletionSource<object?> Completion, CancellationToken Token);

	private static readonly IReadOnlyDictionary<NexusBackgroundLane, Channel<WorkItem>> Queues = new Dictionary<NexusBackgroundLane, Channel<WorkItem>>
	{
		[NexusBackgroundLane.FileIo] = CreateQueue(NexusBackgroundLane.FileIo, workerCount: 2),
		[NexusBackgroundLane.Cpu] = CreateQueue(NexusBackgroundLane.Cpu, workerCount: 1),
		[NexusBackgroundLane.Maintenance] = CreateQueue(NexusBackgroundLane.Maintenance, workerCount: 1),
	};

	internal static async Task<T> RunAsync<T>(string owner, string key, NexusBackgroundLane lane, Func<CancellationToken, T> operation, CancellationToken token)
	{
		if (token.IsCancellationRequested)
		{
			return await Task.FromCanceled<T>(token).ConfigureAwait(false);
		}

		var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var item = new WorkItem(owner, key, () => operation(token), completion, token);
		await Queues[lane].Writer.WriteAsync(item, token).ConfigureAwait(false);
		object? result = await completion.Task.ConfigureAwait(false);
		return (T)result!;
	}

	internal static string GetSnapshot()
		=> string.Join(", ", Queues.Select(pair => $"{pair.Key}=queued:{pair.Value.Reader.Count}"));

	private static Channel<WorkItem> CreateQueue(NexusBackgroundLane lane, int workerCount)
	{
		var queue = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(32)
		{
			FullMode = BoundedChannelFullMode.Wait,
			SingleReader = false,
			SingleWriter = false,
		});

		for (int index = 0; index < workerCount; index++)
		{
			_ = Task.Factory.StartNew(
				async () => await RunWorkerAsync(lane, queue.Reader).ConfigureAwait(false),
				CancellationToken.None,
				TaskCreationOptions.LongRunning,
				TaskScheduler.Default).Unwrap();
		}

		return queue;
	}

	private static async Task RunWorkerAsync(NexusBackgroundLane lane, ChannelReader<WorkItem> reader)
	{
		await foreach (WorkItem item in reader.ReadAllAsync())
		{
			if (item.Token.IsCancellationRequested)
			{
				item.Completion.TrySetCanceled(item.Token);
				continue;
			}

			try
			{
				object? result = item.Operation();
				item.Completion.TrySetResult(result);
			}
			catch (Exception ex)
			{
				NexusConcurrencyDiagnostics.RecordWorkerFault(item.Owner, item.Key, lane, ex);
				item.Completion.TrySetException(ex);
			}
		}
	}
}
