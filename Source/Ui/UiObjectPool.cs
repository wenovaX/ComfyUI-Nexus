namespace ComfyUI_Nexus.Ui;

/// <summary>
/// Lightweight reusable object pool for UI rows or other short-lived reference objects.
/// </summary>
/// <typeparam name="T">Reference type created by the pool and returned after use.</typeparam>
internal sealed class UiObjectPool<T> where T : class
{
	private readonly Func<T> _factory;
	private readonly Action<T>? _reset;
	private readonly Stack<T> _items = new();

	/// <summary>
	/// Creates a pool around a factory and optional reset callback.
	/// </summary>
	/// <param name="factory">Creates a new item when the pool is empty or being prewarmed.</param>
	/// <param name="reset">Optional cleanup invoked before an item is returned to the pool.</param>
	internal UiObjectPool(Func<T> factory, Action<T>? reset = null)
	{
		_factory = factory;
		_reset = reset;
	}

	/// <summary>
	/// Synchronously creates items ahead of time.
	/// </summary>
	/// <param name="count">Number of items to create and store in the pool.</param>
	internal void Prewarm(int count)
	{
		for (int i = 0; i < count; i++)
		{
			_items.Push(_factory());
		}
	}

	/// <summary>
	/// Creates items ahead of time while yielding between batches to keep the UI responsive.
	/// </summary>
	/// <param name="count">Total number of items to create.</param>
	/// <param name="batchSize">Number of items to create before yielding back to the scheduler.</param>
	internal async Task PrewarmAsync(int count, int batchSize, CancellationToken cancellationToken = default)
	{
		int safeBatchSize = Math.Max(1, batchSize);

		for (int i = 0; i < count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			_items.Push(_factory());

			if ((i + 1) % safeBatchSize == 0)
			{
				await Task.Yield();
				cancellationToken.ThrowIfCancellationRequested();
			}
		}
	}

	/// <summary>
	/// Gets an existing pooled item or creates a new one when the pool is empty.
	/// </summary>
	/// <returns>A ready-to-bind item owned by the caller until returned.</returns>
	internal T Rent()
	{
		return _items.Count > 0
			? _items.Pop()
			: _factory();
	}

	/// <summary>
	/// Resets and stores an item for later reuse.
	/// </summary>
	/// <param name="item">Item previously rented from this pool.</param>
	internal void Return(T item)
	{
		_reset?.Invoke(item);
		_items.Push(item);
	}

	/// <summary>
	/// Drops all currently pooled items.
	/// </summary>
	internal void Clear()
	{
		_items.Clear();
	}
}
