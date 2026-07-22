namespace ComfyUI_Nexus.Setup.Runtime;

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Setup.Services;

internal sealed class ComfyServerProcessRegistry
{
	private readonly TimeSpan _gpuUtilitySettleInterval = TimeSpan.FromMilliseconds(250);
	private const int RequiredGpuUtilityClearPasses = 2;
	private readonly object _gate = new();
	private readonly SetupSettingsService _settingsService;
	private string ProcessStatePath => ComfyInstallService.GetLocalRuntimePath("State/comfy-server-process.json");
	private Process? _serverProcess;
	private string? _serverLogPath;
	private int? _shuttingDownProcessId;

	internal ComfyServerProcessRegistry(SetupSettingsService settingsService)
	{
		_settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
	}

	internal void Register(Process process, string logPath)
	{
#if !WINDOWS
		return;
#else
		lock (_gate)
		{
			_serverProcess = process;
			_serverLogPath = logPath;
			_shuttingDownProcessId = null;
		}

		PersistProcess(process, logPath);
#endif
	}

	internal bool IsShuttingDown(Process process)
	{
		if (!TryGetProcessId(process, out int processId)) return false;

		lock (_gate)
		{
			return _shuttingDownProcessId == processId;
		}
	}

	internal ComfyServerProcessInfo? FindServerProcess()
	{
#if !WINDOWS
		return null;
#else
		Process? ownedProcess = GetOwnedProcess();
		if (ownedProcess != null && IsProcessRunning(ownedProcess))
		{
			return TryCreateInfo(ownedProcess, "Nexus-launched process", GetOwnedLogPath());
		}

		string? persistedLogPath;
		Process? persistedProcess = GetPersistedProcess(out persistedLogPath);
		if (persistedProcess != null && IsProcessRunning(persistedProcess))
		{
			return TryCreateInfo(persistedProcess, "Persisted Nexus launch process", persistedLogPath);
		}

		return TryFindProcessByPort(_settingsService.Settings.ServerPort);
#endif
	}

	internal Process? TryGetProcess(ComfyServerProcessInfo processInfo)
	{
#if !WINDOWS
		return null;
#else
		try
		{
			Process process = Process.GetProcessById(processInfo.ProcessId);
			return IsProcessRunning(process) ? process : null;
		}
		catch
		{
			return null;
		}
#endif
	}

	internal async Task ShutdownAsync(ComfyServerProcessInfo processInfo, TimeSpan timeout)
	{
#if !WINDOWS
		await Task.CompletedTask;
#else
		try
		{
			MarkShuttingDown(processInfo.ProcessId);
			await WaitForGpuUtilityChildrenToExitAsync(processInfo.ProcessId, timeout);

			bool exited = await KillAndWaitForExitAsync(processInfo.ProcessId, timeout);
			if (!exited)
			{
				throw new InvalidOperationException($"ComfyUI server process {processInfo.ProcessId} did not exit within {timeout.TotalSeconds:0.#} seconds.");
			}
		}
		finally
		{
			ClearIfSame(processInfo.ProcessId);
		}
#endif
	}

	internal async Task EnsureStoppedAsync(
		ComfyServerProcessInfo? expectedProcess,
		int port,
		TimeSpan timeout,
		TimeSpan pollingInterval,
		CancellationToken cancellationToken)
	{
#if !WINDOWS
		await Task.CompletedTask;
#else
		if (expectedProcess != null)
		{
			bool processExited = await WaitUntilProcessExitedAsync(
				expectedProcess.ProcessId,
				timeout,
				pollingInterval,
				cancellationToken);
			if (!processExited)
			{
				throw new InvalidOperationException($"ComfyUI server process {expectedProcess.ProcessId} is still running after shutdown.");
			}
		}

		bool portReleased = await LocalServerProbe.WaitUntilPortReleasedAsync(
			port,
			timeout,
			pollingInterval,
			cancellationToken);
		if (!portReleased)
		{
			throw new InvalidOperationException($"ComfyUI server port {port} is still listening after shutdown.");
		}

		if (FindServerProcess() is { } remainingProcess)
		{
			throw new InvalidOperationException($"ComfyUI server process {remainingProcess.ProcessId} is still running after shutdown.");
		}
#endif
	}

	private async Task<bool> KillAndWaitForExitAsync(int processId, TimeSpan timeout)
	{
		if (!IsProcessRunning(processId))
		{
			return true;
		}

#if WINDOWS
		IReadOnlyList<int> targetProcessIds = WindowsProcessTreeInspector.TerminateTree(
			processId,
			failure => NexusLog.Warning($"[LIFECYCLE] {failure}"));
		return await WaitUntilProcessesExitedAsync(targetProcessIds, timeout, _gpuUtilitySettleInterval);
#else
		return true;
#endif
	}

	private async Task<bool> WaitUntilProcessesExitedAsync(
		IReadOnlyList<int> processIds,
		TimeSpan timeout,
		TimeSpan pollingInterval)
	{
		using var pollTimer = new PeriodicTimer(pollingInterval);
		var stopwatch = Stopwatch.StartNew();

		while (stopwatch.Elapsed < timeout)
		{
			if (processIds.All(processId => !IsProcessRunning(processId)))
			{
				return true;
			}

			if (!await pollTimer.WaitForNextTickAsync())
			{
				break;
			}
		}

		return processIds.All(processId => !IsProcessRunning(processId));
	}

	private async Task WaitForGpuUtilityChildrenToExitAsync(int rootProcessId, TimeSpan timeout)
	{
#if !WINDOWS
		await Task.CompletedTask;
#else
		using var settleTimer = new PeriodicTimer(_gpuUtilitySettleInterval);
		var stopwatch = Stopwatch.StartNew();
		int clearPasses = 0;
		string? lastObservedProcesses = null;

		while (stopwatch.Elapsed < timeout)
		{
			if (!IsProcessRunning(rootProcessId))
			{
				return;
			}

			WindowsProcessTreeInspector.ProcessTreeEntry[] gpuUtilities = [.. WindowsProcessTreeInspector
				.GetDescendants(rootProcessId)
				.Where(entry => string.Equals(entry.ExecutableName, "nvidia-smi.exe", StringComparison.OrdinalIgnoreCase))];
			if (gpuUtilities.Length == 0)
			{
				clearPasses++;
				if (clearPasses >= RequiredGpuUtilityClearPasses)
				{
					return;
				}
			}
			else
			{
				clearPasses = 0;
				string observedProcesses = string.Join(", ", gpuUtilities.Select(entry => entry.ProcessId));
				if (!string.Equals(lastObservedProcesses, observedProcesses, StringComparison.Ordinal))
				{
					NexusLog.Info($"[LIFECYCLE] Waiting for ComfyUI child nvidia-smi.exe to exit: {observedProcesses}.");
					lastObservedProcesses = observedProcesses;
				}
			}

			if (!await settleTimer.WaitForNextTickAsync())
			{
				break;
			}
		}

		WindowsProcessTreeInspector.ProcessTreeEntry[] remainingUtilities = [.. WindowsProcessTreeInspector
			.GetDescendants(rootProcessId)
			.Where(entry => string.Equals(entry.ExecutableName, "nvidia-smi.exe", StringComparison.OrdinalIgnoreCase))];
		if (remainingUtilities.Length > 0)
		{
			string processIds = string.Join(", ", remainingUtilities.Select(entry => entry.ProcessId));
			throw new InvalidOperationException(
				$"ComfyUI child nvidia-smi.exe process is still running after {timeout.TotalSeconds:0.#} seconds: {processIds}.");
		}
#endif
	}

	private async Task<bool> WaitUntilProcessExitedAsync(
		int processId,
		TimeSpan timeout,
		TimeSpan pollingInterval,
		CancellationToken cancellationToken)
	{
		using var pollTimer = new PeriodicTimer(pollingInterval);
		var stopwatch = Stopwatch.StartNew();

		while (stopwatch.Elapsed < timeout)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!IsProcessRunning(processId))
			{
				return true;
			}

			if (!await pollTimer.WaitForNextTickAsync(cancellationToken))
			{
				break;
			}
		}

		return !IsProcessRunning(processId);
	}

	private Process? GetOwnedProcess()
	{
		lock (_gate)
		{
			return _serverProcess;
		}
	}

	private string? GetOwnedLogPath()
	{
		lock (_gate)
		{
			return _serverLogPath;
		}
	}

	private Process? GetPersistedProcess(out string? logPath)
	{
		logPath = null;
		ComfyServerProcessState? state = LoadProcessState();
		if (state == null) return null;

		try
		{
			Process process = Process.GetProcessById(state.ProcessId);
			if (!IsProcessRunning(process) || !TryHasStartTime(process, state.StartTimeUtcTicks))
			{
				ClearPersistedProcess(state.ProcessId);
				return null;
			}

			lock (_gate)
			{
				_serverProcess = process;
				_serverLogPath = state.LogPath;
			}

			logPath = state.LogPath;
			return process;
		}
		catch
		{
			ClearPersistedProcess(state.ProcessId);
			return null;
		}
	}

	private ComfyServerProcessInfo? TryFindProcessByPort(int port)
	{
		foreach (IPEndPointInfo listener in GetTcpListenersWithProcessId(port))
		{
			try
			{
				return TryCreateInfo(Process.GetProcessById(listener.ProcessId), $"Port {port}");
			}
			catch
			{
			}
		}

		return null;
	}

	private IEnumerable<IPEndPointInfo> GetTcpListenersWithProcessId(int port)
	{
#if WINDOWS
		return WindowsTcpProcessFinder.GetListeners(port);
#else
		return [];
#endif
	}

	private ComfyServerProcessInfo? TryCreateInfo(Process process, string source, string? logPath = null)
	{
		if (!TryGetProcessId(process, out int processId)) return null;

		return new ComfyServerProcessInfo(processId, TryGetProcessName(process), source, logPath);
	}

	private void ClearIfSame(int processId)
	{
		lock (_gate)
		{
			if (_serverProcess?.Id == processId)
			{
				_serverProcess = null;
				_serverLogPath = null;
			}

			ClearPersistedProcess(processId);

			if (_shuttingDownProcessId == processId)
			{
				_shuttingDownProcessId = null;
			}
		}
	}

	private void MarkShuttingDown(int processId)
	{
		lock (_gate)
		{
			_shuttingDownProcessId = processId;
		}
	}

	private void PersistProcess(Process process, string logPath)
	{
#if !WINDOWS
		return;
#else
		try
		{
			string? directory = Path.GetDirectoryName(ProcessStatePath);
			if (!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}

			var state = new ComfyServerProcessState(process.Id, process.StartTime.ToUniversalTime().Ticks, logPath);
			File.WriteAllText(ProcessStatePath, JsonSerializer.Serialize(state));
		}
		catch
		{
		}
#endif
	}

	private ComfyServerProcessState? LoadProcessState()
	{
		try
		{
			if (!File.Exists(ProcessStatePath)) return null;

			string json = File.ReadAllText(ProcessStatePath);
			return JsonSerializer.Deserialize<ComfyServerProcessState>(json);
		}
		catch
		{
			return null;
		}
	}

	private void ClearPersistedProcess(int processId)
	{
		try
		{
			ComfyServerProcessState? state = LoadProcessState();
			if (state?.ProcessId == processId && File.Exists(ProcessStatePath))
			{
				File.Delete(ProcessStatePath);
			}
		}
		catch
		{
		}
	}

	private static bool IsProcessRunning(Process? process)
	{
		if (process == null) return false;

		try
		{
			return !process.HasExited;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsProcessRunning(int processId)
	{
		try
		{
			using Process process = Process.GetProcessById(processId);
			return IsProcessRunning(process);
		}
		catch
		{
			return false;
		}
	}

	private static bool TryGetProcessId(Process process, out int processId)
	{
		try
		{
			processId = process.Id;
			return true;
		}
		catch
		{
			processId = -1;
			return false;
		}
	}

	private static bool TryHasStartTime(Process process, long expectedStartTimeUtcTicks)
	{
#if !WINDOWS
		return false;
#else
		try
		{
			return process.StartTime.ToUniversalTime().Ticks == expectedStartTimeUtcTicks;
		}
		catch
		{
			return false;
		}
#endif
	}

	private static string TryGetProcessName(Process process)
	{
		try
		{
			return process.ProcessName;
		}
		catch
		{
			return "process";
		}
	}

	internal sealed record IPEndPointInfo(int ProcessId);

	private sealed record ComfyServerProcessState(
		[property: JsonPropertyName("process_id")] int ProcessId,
		[property: JsonPropertyName("start_time_utc_ticks")] long StartTimeUtcTicks,
		[property: JsonPropertyName("log_path")] string? LogPath);
}
