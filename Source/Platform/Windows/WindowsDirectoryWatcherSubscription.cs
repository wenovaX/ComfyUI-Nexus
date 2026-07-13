#if WINDOWS
namespace ComfyUI_Nexus.Platform.Windows;

internal sealed class WindowsDirectoryWatcherSubscription : IPlatformDirectoryWatcherSubscription
{
	private readonly FileSystemWatcher _watcher;
	private readonly DirectoryWatcherOptions _options;
	private readonly Action<PlatformDirectoryChange> _onChange;
	private readonly Action<Exception?> _onError;
	private bool _disposed;

	internal WindowsDirectoryWatcherSubscription(
		FileSystemWatcher watcher,
		DirectoryWatcherOptions options,
		Action<PlatformDirectoryChange> onChange,
		Action<Exception?> onError)
	{
		_watcher = watcher;
		_options = options;
		_onChange = onChange;
		_onError = onError;
		_watcher.Created += OnChanged;
		_watcher.Changed += OnChanged;
		_watcher.Deleted += OnChanged;
		_watcher.Renamed += OnRenamed;
		_watcher.Error += OnError;
	}

	public void Start()
	{
		if (!_disposed)
		{
			_watcher.EnableRaisingEvents = true;
		}
	}

	public void Stop()
	{
		if (!_disposed)
		{
			_watcher.EnableRaisingEvents = false;
		}
	}

	private void OnChanged(object sender, FileSystemEventArgs e)
	{
		if (ShouldIgnorePath(e.FullPath))
		{
			return;
		}

		var kind = e.ChangeType switch
		{
			WatcherChangeTypes.Created => PlatformDirectoryChangeKind.Created,
			WatcherChangeTypes.Changed => PlatformDirectoryChangeKind.Changed,
			WatcherChangeTypes.Deleted => PlatformDirectoryChangeKind.Deleted,
			_ => PlatformDirectoryChangeKind.Unknown,
		};

		_onChange(new PlatformDirectoryChange(kind, e.FullPath));
	}

	private void OnRenamed(object sender, RenamedEventArgs e)
	{
		if (ShouldIgnorePath(e.FullPath) && ShouldIgnorePath(e.OldFullPath))
		{
			return;
		}

		_onChange(new PlatformDirectoryChange(PlatformDirectoryChangeKind.Renamed, e.FullPath, e.OldFullPath));
	}

	private void OnError(object sender, ErrorEventArgs e)
		=> _onError(e.GetException());

	private bool ShouldIgnorePath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return true;
		}

		string fileName = Path.GetFileName(path);
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return false;
		}

		if (_options.IgnoredFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
		{
			return true;
		}

		if (_options.IgnoredSuffixes.Any(suffix =>
			!string.IsNullOrWhiteSpace(suffix) &&
			fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
		{
			return true;
		}

		string extension = Path.GetExtension(fileName);
		return _options.IgnoredExtensions.Any(ignored =>
			!string.IsNullOrWhiteSpace(ignored) &&
			extension.Equals(ignored, StringComparison.OrdinalIgnoreCase));
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_watcher.EnableRaisingEvents = false;
		_watcher.Created -= OnChanged;
		_watcher.Changed -= OnChanged;
		_watcher.Deleted -= OnChanged;
		_watcher.Renamed -= OnRenamed;
		_watcher.Error -= OnError;
		_watcher.Dispose();
	}
}
#endif
