using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Platform;

namespace ComfyUI_Nexus.Views.Rail;

internal readonly record struct RailDirectoryWatchBatch(
	string RootPath,
	long Generation,
	bool RequiresFullRefresh,
	IReadOnlyList<string> DirtyDirectories);

/// <summary>
/// Owns one rail tool's active directory watch session. File system callbacks only
/// collect paths; debounce and UI application always run on the UI dispatcher.
/// </summary>
internal sealed class RailDirectoryWatchController : IDisposable
{
	private readonly object _gate = new();
	private readonly string _logSource;
	private readonly IDispatcher _dispatcher;
	private readonly Func<RailDirectoryWatchBatch, bool> _canApply;
	private readonly Func<RailDirectoryWatchBatch, bool> _apply;
	private readonly HashSet<string> _dirtyDirectories = new(StringComparer.OrdinalIgnoreCase);
	private IPlatformDirectoryWatcherSubscription? _subscription;
	private IDispatcherTimer? _debounceTimer;
	private string _rootPath = string.Empty;
	private DirectoryWatcherOptions _options = DirectoryWatcherOptions.Default;
	private long _generation;
	private bool _isActive;
	private bool _needsReconcile;
	private bool _pendingChanges;
	private bool _requiresFullRefresh;
	private bool _watcherErrorReported;
	private DateTime _lastChangeUtc = DateTime.MinValue;

	internal RailDirectoryWatchController(
		string logSource,
		IDispatcher dispatcher,
		Func<RailDirectoryWatchBatch, bool> canApply,
		Func<RailDirectoryWatchBatch, bool> apply)
	{
		_logSource = logSource;
		_dispatcher = dispatcher;
		_canApply = canApply;
		_apply = apply;
	}

	internal bool NeedsRefresh
	{
		get
		{
			lock (_gate)
			{
				return _needsReconcile || _pendingChanges;
			}
		}
	}

	internal bool IsActive
	{
		get
		{
			lock (_gate)
			{
				return _isActive;
			}
		}
	}

	internal void ConfigureRoot(string? rootPath, DirectoryWatcherOptions? options)
	{
		string normalizedRoot = NormalizePath(rootPath);
		bool wasActive;
		lock (_gate)
		{
			DirectoryWatcherOptions resolvedOptions = options ?? DirectoryWatcherOptions.Default;
			if (string.Equals(_rootPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) && HasSameOptions(_options, resolvedOptions))
			{
				return;
			}

			wasActive = _isActive;
			_isActive = false;
			_generation++;
			_rootPath = normalizedRoot;
			_options = resolvedOptions;
			_dirtyDirectories.Clear();
			_pendingChanges = false;
			_requiresFullRefresh = !string.IsNullOrWhiteSpace(normalizedRoot);
			_needsReconcile = !string.IsNullOrWhiteSpace(normalizedRoot);
			_watcherErrorReported = false;
		}

		StopSubscription();
		StopDebounceTimer();
		if (wasActive)
		{
			NexusLog.Info($"[{_logSource}] Watch root changed; waiting for active view reconciliation. root='{normalizedRoot}'");
		}
	}

	internal void SetActive(bool active)
	{
		if (!active)
		{
			bool wasActive;
			lock (_gate)
			{
				wasActive = _isActive;
				_isActive = false;
				if (!string.IsNullOrWhiteSpace(_rootPath))
				{
					_needsReconcile = true;
					_requiresFullRefresh = true;
				}
				_dirtyDirectories.Clear();
				_pendingChanges = false;
			}

			StopSubscription();
			StopDebounceTimer();
			if (wasActive)
			{
				NexusLog.Info($"[{_logSource}] Watch deactivated.");
			}
			return;
		}

		string rootPath;
		long generation;
		lock (_gate)
		{
			if (_isActive || string.IsNullOrWhiteSpace(_rootPath) || !Directory.Exists(_rootPath))
			{
				return;
			}

			_isActive = true;
			rootPath = _rootPath;
			generation = _generation;
		}

		StartSubscription(rootPath, generation);
		NexusLog.Info($"[{_logSource}] Watch activated. root='{rootPath}', generation={generation}");
		ScheduleDebounce();
	}

	internal void MarkReconciled()
	{
		lock (_gate)
		{
			_needsReconcile = false;
			_requiresFullRefresh = false;
		}
	}

	internal void NotifyMutation(IEnumerable<string?> paths)
	{
		bool added = false;
		foreach (string? path in paths)
		{
			added |= RecordChangedPath(path, requireActive: false);
		}

		if (!added)
		{
			MarkFullRefreshIfConfigured();
		}

		ScheduleDebounce();
	}

	internal bool IsCurrent(string rootPath, long generation)
	{
		lock (_gate)
		{
			return _isActive
				&& _generation == generation
				&& string.Equals(_rootPath, NormalizePath(rootPath), StringComparison.OrdinalIgnoreCase);
		}
	}

	public void Dispose()
	{
		SetActive(false);
		lock (_gate)
		{
			_generation++;
			_rootPath = string.Empty;
			_dirtyDirectories.Clear();
			_needsReconcile = false;
			_requiresFullRefresh = false;
		}
	}

	private void StartSubscription(string rootPath, long generation)
	{
		DirectoryWatcherOptions options;
		lock (_gate)
		{
			if (!_isActive || generation != _generation || !string.Equals(rootPath, _rootPath, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}

			options = _options;
		}

		var result = NexusAppManager.Instance.Platform.DirectoryWatcher.TryWatch(
			rootPath,
			options,
			change => OnDirectoryChanged(generation, change),
			exception => OnDirectoryError(generation, exception));
		if (!result.IsSuccess || result.Value is null)
		{
			if (!string.IsNullOrWhiteSpace(result.Message))
			{
				NexusLog.Warning($"[{_logSource}] Watch unavailable: {result.Message}");
			}

			MarkWatcherUnavailable(generation);
			return;
		}

		lock (_gate)
		{
			if (!_isActive || generation != _generation)
			{
				result.Value.Dispose();
				return;
			}

			_subscription = result.Value;
		}

		result.Value.Start();
	}

	private void OnDirectoryChanged(long generation, PlatformDirectoryChange change)
	{
		if (change.Kind == PlatformDirectoryChangeKind.Renamed)
		{
			bool added = RecordChangedPath(change.OldPath, requireActive: true, expectedGeneration: generation);
			added |= RecordChangedPath(change.Path, requireActive: true, expectedGeneration: generation);
			if (added)
			{
				ScheduleDebounce();
			}
			return;
		}

		if (RecordChangedPath(change.Path, requireActive: true, expectedGeneration: generation))
		{
			ScheduleDebounce();
		}
	}

	private void OnDirectoryError(long generation, Exception? exception)
	{
		bool shouldLog;
		lock (_gate)
		{
			shouldLog = _isActive && _generation == generation && !_watcherErrorReported;
			_watcherErrorReported = true;
		}

		if (shouldLog)
		{
			NexusLog.Warning($"[{_logSource}] Watch stopped after platform error: {exception?.Message ?? "unknown error"}");
		}

		MarkWatcherUnavailable(generation);
	}

	private bool RecordChangedPath(string? changedPath, bool requireActive, long? expectedGeneration = null)
	{
		lock (_gate)
		{
			if (requireActive && !_isActive)
			{
				return false;
			}

			if (expectedGeneration.HasValue && expectedGeneration.Value != _generation)
			{
				return false;
			}

			if (string.IsNullOrWhiteSpace(_rootPath))
			{
				return false;
			}

			string? dirtyDirectory = ResolveDirtyDirectory(_rootPath, changedPath);
			if (string.IsNullOrWhiteSpace(dirtyDirectory))
			{
				return false;
			}

			_dirtyDirectories.Add(dirtyDirectory);
			string? parent = Directory.GetParent(dirtyDirectory)?.FullName;
			if (!string.IsNullOrWhiteSpace(parent) && IsPathWithinRoot(_rootPath, parent))
			{
				_dirtyDirectories.Add(parent);
			}

			_pendingChanges = true;
			_lastChangeUtc = DateTime.UtcNow;
			return true;
		}
	}

	private void MarkFullRefreshIfConfigured()
	{
		lock (_gate)
		{
			if (string.IsNullOrWhiteSpace(_rootPath))
			{
				return;
			}

			_requiresFullRefresh = true;
			_pendingChanges = true;
			_lastChangeUtc = DateTime.UtcNow;
		}
	}

	private void MarkWatcherUnavailable(long generation)
	{
		lock (_gate)
		{
			if (generation != _generation)
			{
				return;
			}

			_isActive = false;
			_needsReconcile = true;
			_requiresFullRefresh = true;
			_dirtyDirectories.Clear();
			_pendingChanges = false;
		}

		StopSubscription();
		StopDebounceTimer();
	}

	private void ScheduleDebounce()
	{
		if (_dispatcher.IsDispatchRequired)
		{
			_dispatcher.Dispatch(ScheduleDebounceOnUiThread);
			return;
		}

		ScheduleDebounceOnUiThread();
	}

	private void ScheduleDebounceOnUiThread()
	{
		lock (_gate)
		{
			if (!_isActive || !_pendingChanges)
			{
				return;
			}

			_debounceTimer ??= CreateDebounceTimer();
			_debounceTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, _options.DebounceIntervalMs));
			if (!_debounceTimer.IsRunning)
			{
				_debounceTimer.Start();
			}
		}
	}

	private IDispatcherTimer CreateDebounceTimer()
	{
		IDispatcherTimer timer = _dispatcher.CreateTimer();
		timer.Tick += OnDebounceTimerTick;
		return timer;
	}

	private void OnDebounceTimerTick(object? sender, EventArgs e)
	{
		RailDirectoryWatchBatch batch;
		lock (_gate)
		{
			if (!_isActive || !_pendingChanges)
			{
				StopDebounceTimerLocked();
				return;
			}

			if ((DateTime.UtcNow - _lastChangeUtc).TotalMilliseconds < Math.Max(0, _options.StableDelayMs))
			{
				return;
			}

			batch = new RailDirectoryWatchBatch(
				_rootPath,
				_generation,
				_requiresFullRefresh,
				_dirtyDirectories.OrderBy(path => path.Length).ToArray());
			_dirtyDirectories.Clear();
			_pendingChanges = false;
			_requiresFullRefresh = false;
			StopDebounceTimerLocked();
		}

		if (!_canApply(batch))
		{
			MarkFullRefreshIfConfigured();
			return;
		}

		try
		{
			if (_apply(batch))
			{
				string message = $"[{_logSource}] Applied batch. root='{batch.RootPath}', generation={batch.Generation}, dirty={batch.DirtyDirectories.Count}, full={batch.RequiresFullRefresh}";
				if (batch.RequiresFullRefresh)
				{
					NexusLog.Info(message);
				}
				else
				{
					NexusLog.Trace(message);
				}

				return;
			}
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"[{_logSource}] Batch application failed: {ex.Message}");
		}

		MarkFullRefreshIfConfigured();
	}

	private void StopSubscription()
	{
		IPlatformDirectoryWatcherSubscription? subscription;
		lock (_gate)
		{
			subscription = _subscription;
			_subscription = null;
		}

		if (subscription is null)
		{
			return;
		}

		subscription.Stop();
		subscription.Dispose();
	}

	private void StopDebounceTimer()
	{
		if (_dispatcher.IsDispatchRequired)
		{
			_dispatcher.Dispatch(StopDebounceTimerOnUiThread);
			return;
		}

		StopDebounceTimerOnUiThread();
	}

	private void StopDebounceTimerOnUiThread()
	{
		lock (_gate)
		{
			StopDebounceTimerLocked();
		}
	}

	private void StopDebounceTimerLocked()
	{
		_debounceTimer?.Stop();
	}

	private static bool HasSameOptions(DirectoryWatcherOptions left, DirectoryWatcherOptions right)
	{
		return left.IncludeSubdirectories == right.IncludeSubdirectories
			&& left.DebounceIntervalMs == right.DebounceIntervalMs
			&& left.StableDelayMs == right.StableDelayMs
			&& left.IgnoredFileNames.SequenceEqual(right.IgnoredFileNames, StringComparer.OrdinalIgnoreCase)
			&& left.IgnoredExtensions.SequenceEqual(right.IgnoredExtensions, StringComparer.OrdinalIgnoreCase)
			&& left.IgnoredSuffixes.SequenceEqual(right.IgnoredSuffixes, StringComparer.OrdinalIgnoreCase);
	}

	private static string NormalizePath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return string.Empty;
		}

		try
		{
			string fullPath = Path.GetFullPath(path);
			string root = Path.GetPathRoot(fullPath) ?? string.Empty;
			return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
				? fullPath
				: fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string? ResolveDirtyDirectory(string rootPath, string? changedPath)
	{
		if (string.IsNullOrWhiteSpace(changedPath))
		{
			return rootPath;
		}

		string normalizedPath = NormalizePath(changedPath);
		if (!IsPathWithinRoot(rootPath, normalizedPath))
		{
			return null;
		}

		if (string.Equals(rootPath, normalizedPath, StringComparison.OrdinalIgnoreCase) || Directory.Exists(normalizedPath))
		{
			return normalizedPath;
		}

		string? parent = Path.GetDirectoryName(normalizedPath);
		return !string.IsNullOrWhiteSpace(parent) && IsPathWithinRoot(rootPath, parent)
			? NormalizePath(parent)
			: rootPath;
	}

	private static bool IsPathWithinRoot(string rootPath, string? candidatePath)
	{
		if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(candidatePath))
		{
			return false;
		}

		string normalizedRoot = NormalizePath(rootPath);
		string normalizedCandidate = NormalizePath(candidatePath);
		if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return normalizedCandidate.StartsWith(
			normalizedRoot + Path.DirectorySeparatorChar,
			StringComparison.OrdinalIgnoreCase);
	}
}
