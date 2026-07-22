namespace ComfyUI_Nexus.Configuration;

using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;

/// <summary>
/// Grants setup tooling temporary short paths without changing app-wide storage paths.
/// </summary>
internal sealed class NexusToolingEnvironment
{
	private readonly SemaphoreSlim _toolingGate = new(1, 1);
	private readonly AsyncLocal<NexusRuntimeToolingLease?> _currentLease = new();
	private readonly SetupSettingsService _settingsService;
	private readonly NexusComfyRuntimePaths _paths;
	private readonly NexusToolingPathLeaseController _pathLeaseController = new();
	private readonly object _disposeGate = new();
	private Task _startupCleanupTask = Task.CompletedTask;
	private bool _disposeRequested;
	private bool _resourcesDisposed;
	private bool _startupCleanupStarted;
	private int _activeSessionCount;

	internal NexusToolingEnvironment(
		SetupSettingsService settingsService,
		NexusComfyRuntimePaths paths,
		NexusPreferenceStore preferences)
	{
		_settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
		PythonRuntimeInfo = new PythonRuntimeInfoService();
		NexusLegacyToolingPathMigration.RunOnce(_paths, preferences);
	}

	internal Task StartStartupCleanupAsync(NexusBackgroundWorkerPool backgroundWorkers)
	{
		ArgumentNullException.ThrowIfNull(backgroundWorkers);
		lock (_disposeGate)
		{
			if (_startupCleanupStarted)
			{
				return _startupCleanupTask;
			}

			_startupCleanupStarted = true;
			_startupCleanupTask = backgroundWorkers.RunAsync(
				"app-runtime",
				"tooling-path-stale-cleanup",
				NexusBackgroundLane.Maintenance,
				_ =>
				{
					try
					{
						NexusToolingPathLeaseController.ReleaseStaleOwnedMappings(NexusStorageLayout.LocalRuntimeRoot);
						return true;
					}
					catch (Exception ex)
					{
						NexusLog.Warning($"[TOOLING_PATH] Startup stale mapping cleanup failed: {ex.Message}");
						return false;
					}
				},
				CancellationToken.None);
			return _startupCleanupTask;
		}
	}

	internal async Task<T> RunToolingAsync<T>(
		Func<NexusRuntimeToolingLease, Task<T>> operation,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(operation);
		ThrowIfDisposeRequested();

		if (_currentLease.Value is { } existingLease)
		{
			existingLease.ThrowIfDisposed();
			return await operation(existingLease);
		}

		await _startupCleanupTask.WaitAsync(cancellationToken);
		await _toolingGate.WaitAsync(cancellationToken);
		NexusRuntimeToolingLease? lease = null;
		try
		{
			ThrowIfDisposeRequested();
			lease = new NexusRuntimeToolingLease(
				_pathLeaseController,
				_paths,
				ResolvePrimaryToolingRoot(),
				_settingsService.Settings);
			Interlocked.Increment(ref _activeSessionCount);
			_currentLease.Value = lease;
			return await operation(lease);
		}
		catch (NexusToolingPathLeaseUnavailableException ex)
		{
			NexusLog.Warning($"[TOOLING_PATH] Lease unavailable: {ex.Message}");
			throw;
		}
		finally
		{
			_currentLease.Value = null;
			lease?.Dispose();
			if (lease is not null)
			{
				Interlocked.Decrement(ref _activeSessionCount);
			}

			_toolingGate.Release();
			CompleteDisposeIfRequested();
		}
	}

	internal Task RunToolingAsync(
		Func<NexusRuntimeToolingLease, Task> operation,
		CancellationToken cancellationToken = default)
		=> RunToolingAsync(
			async lease =>
			{
				await operation(lease);
				return true;
			},
			cancellationToken);

	internal NexusRuntimeToolingLease? CurrentLease
	{
		get
		{
			if (_currentLease.Value is not { } lease)
			{
				return null;
			}

			lease.ThrowIfDisposed();
			return lease;
		}
	}

	internal PythonRuntimeInfoService PythonRuntimeInfo { get; }

	internal void Dispose()
	{
		lock (_disposeGate)
		{
			_disposeRequested = true;
		}

		CompleteDisposeIfRequested();
	}

	private void ThrowIfDisposeRequested()
	{
		lock (_disposeGate)
		{
			ObjectDisposedException.ThrowIf(_disposeRequested, this);
		}
	}

	private void CompleteDisposeIfRequested()
	{
		lock (_disposeGate)
		{
			if (!_disposeRequested || _resourcesDisposed || Volatile.Read(ref _activeSessionCount) != 0)
			{
				return;
			}

			_resourcesDisposed = true;
			_pathLeaseController.Dispose();
		}
	}

	private string ResolvePrimaryToolingRoot()
	{
		string activeComfyRoot = _paths.ActiveComfyPath;
		if (!string.IsNullOrWhiteSpace(activeComfyRoot)
			&& !NexusStorageLayout.AreEquivalentRuntimePaths(activeComfyRoot, _paths.ManagedComfyPath))
		{
			return activeComfyRoot;
		}

		string runtimeRoot = NexusStorageLayout.LocalRuntimeRoot;
		Directory.CreateDirectory(runtimeRoot);
		return runtimeRoot;
	}
}

/// <summary>
/// A scoped lease used only by setup tooling process launches and their working paths.
/// </summary>
internal sealed class NexusRuntimeToolingLease : IDisposable
{
	private readonly NexusToolingPathLeaseController _controller;
	private readonly NexusComfyRuntimePaths _paths;
	private readonly string _physicalToolingRoot;
	private readonly SetupSettings _settings;
	private int _disposed;

	internal NexusRuntimeToolingLease(
		NexusToolingPathLeaseController controller,
		NexusComfyRuntimePaths paths,
		string physicalToolingRoot,
		SetupSettings settings)
	{
		_controller = controller ?? throw new ArgumentNullException(nameof(controller));
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
		_physicalToolingRoot = Path.GetFullPath(physicalToolingRoot);
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_ = _controller.AcquirePrimaryRoot(_physicalToolingRoot);
	}

	internal string RuntimeRoot => GetToolingPath(NexusStorageLayout.LocalRuntimeRoot);
	internal string PhysicalRuntimeRoot => NexusStorageLayout.LocalRuntimeRoot;
	internal string PhysicalToolingRoot => _physicalToolingRoot;

	internal string GetRuntimePath(string relativePath)
		=> GetToolingPath(Path.Combine(NexusStorageLayout.LocalRuntimeRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

	internal string GetComfyRoot()
	{
		string comfyRoot = _paths.ActiveComfyPath;
		if (string.IsNullOrWhiteSpace(comfyRoot))
		{
			comfyRoot = _paths.ConfiguredComfyPath;
		}

		return string.IsNullOrWhiteSpace(comfyRoot) ? string.Empty : GetToolingPath(comfyRoot);
	}

	internal string GetPipCachePath()
	{
		string cacheRoot = PipCacheService.GetEffectiveCachePath(_settings);
		return string.IsNullOrWhiteSpace(cacheRoot) ? string.Empty : GetToolingPath(cacheRoot);
	}

	internal string GetToolingPath(string physicalPath)
	{
		ThrowIfDisposed();
		if (string.IsNullOrWhiteSpace(physicalPath))
		{
			return string.Empty;
		}

		string normalizedPath = Path.GetFullPath(NexusToolingPathLeaseController.ResolvePhysicalPath(physicalPath));
		string translatedPath = _controller.TranslatePath(normalizedPath);
		if (!string.Equals(translatedPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
		{
			return translatedPath;
		}

		return normalizedPath;
	}

	internal void ThrowIfDisposed()
		=> ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_controller.ReleasePrimaryRoot();
	}
}
