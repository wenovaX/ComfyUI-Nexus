namespace ComfyUI_Nexus.Setup.Runtime;

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ComfyUI_Nexus.Setup.Services;

internal static class ComfyServerProcessRegistry
{
	private static readonly object Gate = new();
	private static string ProcessStatePath => ComfyInstallService.GetLocalRuntimePath("State/comfy-server-process.json");
	private static Process? _serverProcess;
	private static string? _serverLogPath;
	private static int? _shuttingDownProcessId;

	internal static void Register(Process process, string logPath)
	{
#if !WINDOWS
		return;
#else
		lock (Gate)
		{
			_serverProcess = process;
			_serverLogPath = logPath;
			_shuttingDownProcessId = null;
		}

		PersistProcess(process, logPath);
#endif
	}

	internal static bool IsShuttingDown(Process process)
	{
		if (!TryGetProcessId(process, out int processId)) return false;

		lock (Gate)
		{
			return _shuttingDownProcessId == processId;
		}
	}

	internal static ComfyServerProcessInfo? FindServerProcess()
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

		return TryFindProcessByPort(SetupSettingsService.Instance.Settings.ServerPort);
#endif
	}

	internal static Process? TryGetProcess(ComfyServerProcessInfo processInfo)
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

	internal static async Task ShutdownAsync(ComfyServerProcessInfo processInfo, TimeSpan timeout)
	{
#if !WINDOWS
		await Task.CompletedTask;
#else
		try
		{
			MarkShuttingDown(processInfo.ProcessId);

			await Task.Run(() =>
			{
				using Process process = Process.GetProcessById(processInfo.ProcessId);
				if (!IsProcessRunning(process))
				{
					return;
				}

				TryKillProcessTree(process);
				process.WaitForExit(GetWaitTimeoutMilliseconds(timeout));
			});
		}
		catch (OperationCanceledException)
		{
		}
		catch
		{
		}
		finally
		{
			ClearIfSame(processInfo.ProcessId);
		}
#endif
	}

	private static int GetWaitTimeoutMilliseconds(TimeSpan timeout)
	{
		if (timeout <= TimeSpan.Zero) return 0;
		return timeout.TotalMilliseconds >= int.MaxValue ? int.MaxValue : (int)timeout.TotalMilliseconds;
	}

	private static Process? GetOwnedProcess()
	{
		lock (Gate)
		{
			return _serverProcess;
		}
	}

	private static string? GetOwnedLogPath()
	{
		lock (Gate)
		{
			return _serverLogPath;
		}
	}

	private static Process? GetPersistedProcess(out string? logPath)
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

			lock (Gate)
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

	private static ComfyServerProcessInfo? TryFindProcessByPort(int port)
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

	private static IEnumerable<IPEndPointInfo> GetTcpListenersWithProcessId(int port)
	{
#if WINDOWS
		return WindowsTcpProcessFinder.GetListeners(port);
#else
		return [];
#endif
	}

	private static ComfyServerProcessInfo? TryCreateInfo(Process process, string source, string? logPath = null)
	{
		if (!TryGetProcessId(process, out int processId)) return null;

		return new ComfyServerProcessInfo(processId, TryGetProcessName(process), source, logPath);
	}

	private static void ClearIfSame(int processId)
	{
		lock (Gate)
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

	private static void MarkShuttingDown(int processId)
	{
		lock (Gate)
		{
			_shuttingDownProcessId = processId;
		}
	}

	private static void PersistProcess(Process process, string logPath)
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

	private static ComfyServerProcessState? LoadProcessState()
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

	private static void ClearPersistedProcess(int processId)
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

	private static void TryKillProcessTree(Process process)
	{
#if WINDOWS
		try
		{
			process.Kill(entireProcessTree: true);
		}
		catch
		{
		}
#endif
	}

	internal sealed record IPEndPointInfo(int ProcessId);

	private sealed record ComfyServerProcessState(
		[property: JsonPropertyName("process_id")] int ProcessId,
		[property: JsonPropertyName("start_time_utc_ticks")] long StartTimeUtcTicks,
		[property: JsonPropertyName("log_path")] string? LogPath);
}
