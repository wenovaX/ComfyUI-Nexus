namespace ComfyUI_Nexus.Ui;

/// <summary>
/// Keeps one pending UI action for each owner/key pair. The app manager owns this
/// coordinator so bridge and background work never retain a static dispatcher queue.
/// </summary>
internal sealed class NexusUiPostCoordinator : IDisposable
{
	private sealed class PendingPost
	{
		internal required long Generation { get; init; }
		internal required Action Action { get; init; }
		internal bool Scheduled { get; set; }
	}

	private readonly object _gate = new();
	private readonly Dictionary<string, PendingPost> _latestPosts = new(StringComparer.Ordinal);
	private bool _disposed;

	internal void PostLatest(string owner, string key, long generation, Action action)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		ArgumentException.ThrowIfNullOrWhiteSpace(owner);
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		ArgumentNullException.ThrowIfNull(action);

		string postKey = $"{owner}:{key}";
		bool schedule = false;
		lock (_gate)
		{
			if (!_latestPosts.TryGetValue(postKey, out PendingPost? pending))
			{
				_latestPosts.Add(postKey, new PendingPost
				{
					Generation = generation,
					Action = action,
					Scheduled = true,
				});
				schedule = true;
			}
			else
			{
				_latestPosts[postKey] = new PendingPost
				{
					Generation = generation,
					Action = action,
					Scheduled = pending.Scheduled,
				};
				schedule = !pending.Scheduled;
			}
		}

		if (schedule && !UiThread.TryBeginInvoke(() => FlushLatest(postKey), $"UI_DISPATCH:{postKey}"))
		{
			lock (_gate)
			{
				_latestPosts.Remove(postKey);
			}
		}
	}

	internal string GetSnapshot()
	{
		lock (_gate)
		{
			return _latestPosts.Count == 0
				? "none"
				: string.Join(", ", _latestPosts.Select(pair => $"{pair.Key}@{pair.Value.Generation}"));
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		lock (_gate)
		{
			_latestPosts.Clear();
		}
	}

	private void FlushLatest(string postKey)
	{
		PendingPost? pending;
		lock (_gate)
		{
			if (_disposed || !_latestPosts.Remove(postKey, out pending))
			{
				return;
			}
		}

		UiThread.TryBeginInvoke(pending.Action, $"UI_DISPATCH:{postKey}");
	}
}
