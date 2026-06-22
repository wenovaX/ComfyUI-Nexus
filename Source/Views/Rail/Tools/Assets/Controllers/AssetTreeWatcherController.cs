using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

internal sealed class AssetTreeWatcherController
{
	private readonly Func<string> _getRootPath;
	private readonly Func<DirectoryWatcherOptions> _getWatcherOptions;
	private readonly Func<bool> _isRefreshBlocked;
	private readonly Action<IReadOnlyList<string>> _applyPendingChanges;

	private readonly object _stateLock = new();
	private readonly HashSet<string> _dirtyDirectories = new(StringComparer.OrdinalIgnoreCase);
	private IPlatformDirectoryWatcherSubscription? _watcherSubscription;
	private CancellationTokenSource? _refreshLoopCts;
	private volatile bool _treeDirty;
	private DateTime _lastExternalChangeUtc = DateTime.MinValue;

	internal AssetTreeWatcherController(
		Func<string> getRootPath,
		Func<DirectoryWatcherOptions> getWatcherOptions,
		Func<bool> isRefreshBlocked,
		Action<IReadOnlyList<string>> applyPendingChanges)
	{
		_getRootPath = getRootPath;
		_getWatcherOptions = getWatcherOptions;
		_isRefreshBlocked = isRefreshBlocked;
		_applyPendingChanges = applyPendingChanges;
	}

	internal bool IsDirty
	{
		get
		{
			lock (_stateLock)
			{
				return _treeDirty;
			}
		}
	}

	internal void ResetDirtyTracking()
	{
		lock (_stateLock)
		{
			_treeDirty = false;
			_dirtyDirectories.Clear();
		}
	}

	internal void Restart()
	{
		DisposeWatcher();

		string rootPath = _getRootPath();
		if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
		{
			return;
		}

		var watchResult = PlatformManager.Current.DirectoryWatcher.TryWatch(
			rootPath,
			_getWatcherOptions(),
			OnDirectoryChanged,
			OnDirectoryError);

		if (!watchResult.IsSuccess || watchResult.Value is null)
		{
			return;
		}

		_watcherSubscription = watchResult.Value;
		_watcherSubscription.Start();
	}

	internal void Start()
	{
		_refreshLoopCts?.Cancel();
		_refreshLoopCts = new CancellationTokenSource();
		var token = _refreshLoopCts.Token;
		var options = _getWatcherOptions();
		int debounceMs = Math.Max(100, options.DebounceIntervalMs);
		int stableDelayMs = Math.Max(0, options.StableDelayMs);

		_ = Task.Run(async () =>
		{
			using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(debounceMs));
			try
			{
				while (await timer.WaitForNextTickAsync(token))
				{
					if (!IsRefreshReady(stableDelayMs))
					{
						continue;
					}

					if (!TryTakePendingChanges(out var dirtyDirectories))
					{
						continue;
					}

					DispatchPendingChanges(dirtyDirectories);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				NexusLog.Exception(ex, "[ASSET_WATCHER] Refresh loop failed");
			}
		}, token);
	}

	internal void Dispose()
	{
		_refreshLoopCts?.Cancel();
		DisposeWatcher();
	}

	internal void MarkDirty(string? changedPath = null)
	{
		string? dirtyDirectory = ResolveDirtyDirectory(changedPath);
		lock (_stateLock)
		{
			if (!string.IsNullOrWhiteSpace(dirtyDirectory))
			{
				_dirtyDirectories.Add(dirtyDirectory);
				string? parent = Directory.GetParent(dirtyDirectory)?.FullName;
				if (!string.IsNullOrWhiteSpace(parent))
				{
					_dirtyDirectories.Add(parent);
				}
			}

			_treeDirty = true;
			_lastExternalChangeUtc = DateTime.UtcNow;
		}
	}

	internal void RefreshImmediately(IEnumerable<string?> dirtyPaths)
	{
		var paths = dirtyPaths.ToArray();
		if (paths.All(string.IsNullOrWhiteSpace))
		{
			MarkDirty();
		}

		foreach (string? path in paths)
		{
			MarkDirty(path);
		}

		RequestPendingRefresh();
	}

	internal void RequestPendingRefresh()
	{
		if (_isRefreshBlocked() || !TryTakePendingChanges(out var dirtyDirectories))
		{
			return;
		}

		DispatchPendingChanges(dirtyDirectories);
	}

	private bool IsRefreshReady(int stableDelayMs)
	{
		lock (_stateLock)
		{
			return _treeDirty &&
				!_isRefreshBlocked() &&
				(DateTime.UtcNow - _lastExternalChangeUtc).TotalMilliseconds >= stableDelayMs;
		}
	}

	private bool TryTakePendingChanges(out List<string> dirtyDirectories)
	{
		lock (_stateLock)
		{
			if (!_treeDirty)
			{
				dirtyDirectories = [];
				return false;
			}

			dirtyDirectories = _dirtyDirectories.ToList();
			_dirtyDirectories.Clear();
			_treeDirty = false;
			return true;
		}
	}

	private void RequeuePendingChanges(IEnumerable<string> dirtyDirectories)
	{
		lock (_stateLock)
		{
			foreach (string directory in dirtyDirectories)
			{
				_dirtyDirectories.Add(directory);
			}

			_treeDirty = true;
		}
	}

	private void DispatchPendingChanges(IReadOnlyList<string> dirtyDirectories)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			if (string.IsNullOrWhiteSpace(_getRootPath()))
			{
				return;
			}

			if (_isRefreshBlocked())
			{
				RequeuePendingChanges(dirtyDirectories);
				return;
			}

			NexusLog.Info($"[ASSET_WATCHER] Applying {dirtyDirectories.Count} dirty director{(dirtyDirectories.Count == 1 ? "y" : "ies")}.");
			_applyPendingChanges(dirtyDirectories);
		});
	}

	private void DisposeWatcher()
	{
		if (_watcherSubscription is null)
		{
			return;
		}

		_watcherSubscription.Stop();
		_watcherSubscription.Dispose();
		_watcherSubscription = null;
	}

	private void OnDirectoryChanged(PlatformDirectoryChange change)
	{
		if (change.Kind == PlatformDirectoryChangeKind.Renamed)
		{
			MarkDirty(change.OldPath);
			MarkDirty(change.Path);
			return;
		}

		MarkDirty(change.Path);
	}

	private void OnDirectoryError(Exception? exception)
	{
		MarkDirty();
	}

	private string? ResolveDirtyDirectory(string? changedPath)
	{
		string rootPath = _getRootPath();
		if (string.IsNullOrWhiteSpace(rootPath))
		{
			return null;
		}

		if (string.IsNullOrWhiteSpace(changedPath))
		{
			return rootPath;
		}

		if (Directory.Exists(changedPath))
		{
			return changedPath;
		}

		string? parent = Path.GetDirectoryName(changedPath);
		return string.IsNullOrWhiteSpace(parent) ? rootPath : parent;
	}
}
