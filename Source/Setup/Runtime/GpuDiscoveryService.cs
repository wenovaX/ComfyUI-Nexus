using System.Diagnostics;
#if WINDOWS
using System.Runtime.InteropServices;
using System.Text;
#endif
using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Setup.Runtime;

internal sealed class GpuDiscoveryService
{
	private const string QueryArguments = "--query-gpu=index,name,memory.total --format=csv,noheader,nounits";
	private static readonly char[] LineSeparators = ['\r', '\n'];
	private readonly object _gate = new();
	private readonly SemaphoreSlim _shutdownGate = new(1, 1);
	private readonly List<Process> _activeProcesses = new();
	private IReadOnlyList<GpuDeviceInfo>? _cachedDevices;
	private Task<GpuDiscoveryResult>? _discoveryTask;
	private bool _isRunning;
	private bool _isShutdownInProgress;
	private bool _externalQueriesSealed;

	internal async Task<StartResult> StartAsync()
	{
		Task<GpuDiscoveryResult> discoveryTask;
		lock (_gate)
		{
			if (_isShutdownInProgress || (_isRunning && _externalQueriesSealed))
			{
				return StartResult.Failed(_cachedDevices ?? GetFallbackDevices(), "GPU discovery service is stopping.");
			}

			if (!_isRunning)
			{
				_isRunning = true;
				_externalQueriesSealed = false;
			}
			if (_cachedDevices != null)
			{
				NexusLog.Info("[GPU] Discovery start served from cache.");
				return StartResult.Succeeded(_cachedDevices);
			}

			if (_discoveryTask == null)
			{
				NexusLog.Info("[GPU] Discovery query started.");
				_discoveryTask = DiscoverUncachedAsync();
			}
			discoveryTask = _discoveryTask;
		}

		try
		{
			GpuDiscoveryResult result = await discoveryTask;
			lock (_gate)
			{
				if (ReferenceEquals(_discoveryTask, discoveryTask))
				{
					_discoveryTask = null;
					if (result.IsReliable)
					{
						_cachedDevices = result.Devices;
					}
				}
			}

			return result.IsReliable
				? StartResult.Succeeded(result.Devices)
				: StartResult.Failed(result.Devices, result.FailureMessage ?? "nvidia-smi did not return GPU data.");
		}
		catch (Exception ex)
		{
			lock (_gate)
			{
				if (ReferenceEquals(_discoveryTask, discoveryTask))
				{
					_discoveryTask = null;
				}
			}

			return StartResult.Failed(GetFallbackDevices(), ex.Message);
		}
	}

	internal IReadOnlyList<GpuDeviceInfo> GetCachedDevicesOrFallback()
	{
		lock (_gate)
		{
			return _cachedDevices ?? GetFallbackDevices();
		}
	}

	internal void BeginQuiesce()
	{
		lock (_gate)
		{
			if (!_externalQueriesSealed)
			{
				NexusLog.Info("[GPU] Discovery quiesce requested. New external queries are sealed.");
			}
			_isShutdownInProgress = true;
			_externalQueriesSealed = true;
		}
	}

	internal string? TryGetCachedDeviceName(string? id)
	{
		lock (_gate)
		{
			if (_cachedDevices is not { Count: > 0 } devices)
			{
				return null;
			}

			if (!string.IsNullOrWhiteSpace(id))
			{
				GpuDeviceInfo? matched = devices.FirstOrDefault(device =>
					string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase));
				if (matched != null && !string.IsNullOrWhiteSpace(matched.Name))
				{
					return matched.Name;
				}
			}

			return devices.Count == 1 && !string.IsNullOrWhiteSpace(devices[0].Name)
				? devices[0].Name
				: null;
		}
	}

	internal async Task<StopResult> StopAsync()
	{
		NexusLog.Info("[GPU] Discovery stop requested.");
		await _shutdownGate.WaitAsync();
		Task<GpuDiscoveryResult>? discoveryTask = null;
		try
		{
			// A server restart keeps this Nexus process alive. GPU topology is stable for
			// that lifetime, so never launch another nvidia-smi process after shutdown
			// begins. Subsequent callers receive the completed cache or generic fallback.
			BeginQuiesce();
			lock (_gate)
			{
				discoveryTask = _discoveryTask;
			}

			// nvidia-smi is a short-lived external probe. Killing it while its native
			// startup path is still active can make Windows surface an application-error dialog.
			// Block new discovery requests, then let the in-flight query finish normally.
			await AwaitDiscoveryCompletionAsync(discoveryTask);
			Process[] activeProcesses;
			lock (_gate)
			{
				activeProcesses = _activeProcesses.ToArray();
			}

			await WaitForProcessesToExitAsync(activeProcesses);

			lock (_gate)
			{
				_activeProcesses.RemoveAll(process => !IsProcessRunning(process));
				_isRunning = false;
				if (ReferenceEquals(_discoveryTask, discoveryTask))
				{
					_discoveryTask = null;
				}
			}

			NexusLog.Info($"[GPU] Discovery stop completed. waitedQueries={activeProcesses.Length}");
			return StopResult.Succeeded();
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"[GPU] Discovery stop completed with an error: {ex.Message}");
			return StopResult.Failed(ex.Message);
		}
		finally
		{
			lock (_gate)
			{
				_isShutdownInProgress = false;
			}

			_shutdownGate.Release();
		}
	}

	private async Task<GpuDiscoveryResult> DiscoverUncachedAsync()
	{
		var (exitCode, output) = await RunNvidiaSmiQueryAsync();
		if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
		{
			return GpuDiscoveryResult.Fallback(GetFallbackDevices(), $"nvidia-smi exited with code {exitCode}.");
		}

		var devices = ParseNvidiaSmiOutput(output);
		return devices.Count > 0
			? GpuDiscoveryResult.Reliable(devices)
			: GpuDiscoveryResult.Fallback(GetFallbackDevices(), "nvidia-smi returned no GPU devices.");
	}

	private async Task<(int ExitCode, string Output)> RunNvidiaSmiQueryAsync()
	{
#if !WINDOWS
		await Task.CompletedTask;
		return (-1, string.Empty);
#else
		var output = new StringBuilder();
		bool started = false;
		using var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = "nvidia-smi",
				Arguments = QueryArguments,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			}
		};

		process.OutputDataReceived += (s, e) =>
		{
			if (e.Data != null)
			{
				output.AppendLine(e.Data);
			}
		};

		try
		{
			using var errorDialogScope = WindowsErrorDialogScope.Suppress();
			process.Start();
			started = true;
			Register(process);
			NexusLog.Info($"[GPU] nvidia-smi started. pid={process.Id}");
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			await process.WaitForExitAsync();
			int exitCode = TryGetExitCode(process);
			NexusLog.Info($"[GPU] nvidia-smi exited. pid={process.Id}, exitCode={exitCode}");
			return (exitCode, output.ToString());
		}
		catch
		{
			if (started)
			{
				await WaitForProcessExitAsync(process);
			}

			return (-1, string.Empty);
		}
		finally
		{
			Unregister(process);
		}
#endif
	}

	private void Register(Process process)
	{
		lock (_gate)
		{
			_activeProcesses.Add(process);
		}
	}

	private static async Task AwaitDiscoveryCompletionAsync(Task<GpuDiscoveryResult>? discoveryTask)
	{
		if (discoveryTask is null)
		{
			return;
		}

		try
		{
			await discoveryTask;
		}
		catch
		{
			// Discovery failure falls back to generic GPU data and must not block shutdown.
		}
	}

	private static async Task WaitForProcessesToExitAsync(IEnumerable<Process> processes)
	{
		foreach (Process process in processes)
		{
			await WaitForProcessExitAsync(process);
		}
	}

	private static async Task WaitForProcessExitAsync(Process process)
	{
		try
		{
			if (!IsProcessRunning(process))
			{
				return;
			}

			await process.WaitForExitAsync();
		}
		catch (ObjectDisposedException)
		{
			// The query owner completed its cleanup before the shutdown checklist reached it.
		}
		catch (InvalidOperationException)
		{
			// The process was never started or has already completed.
		}

	}

	private void Unregister(Process process)
	{
		lock (_gate)
		{
			_activeProcesses.Remove(process);
		}
	}

	private static bool IsProcessRunning(Process process)
	{
		try
		{
			return !process.HasExited;
		}
		catch
		{
			return false;
		}
	}

	private static int TryGetExitCode(Process process)
	{
		try
		{
			return process.ExitCode;
		}
		catch
		{
			return -1;
		}
	}

	private static List<GpuDeviceInfo> ParseNvidiaSmiOutput(string output)
	{
		var devices = new List<GpuDeviceInfo>();
		foreach (string rawLine in output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
		{
			string[] parts = rawLine.Split(',', 3, StringSplitOptions.TrimEntries);
			if (parts.Length < 2) continue;

			string memory = parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2])
				? $"{parts[2]} MB"
				: "Unknown memory";

			devices.Add(new GpuDeviceInfo(parts[0], parts[1], memory));
		}

		return devices;
	}

	private static IReadOnlyList<GpuDeviceInfo> GetFallbackDevices()
		=> new[] { new GpuDeviceInfo("0", "GPU 0", "Detection unavailable") };

	internal sealed record StartResult(bool IsSuccess, IReadOnlyList<GpuDeviceInfo> Devices, string? FailureMessage)
	{
		internal static StartResult Succeeded(IReadOnlyList<GpuDeviceInfo> devices)
			=> new(true, devices, null);

		internal static StartResult Failed(IReadOnlyList<GpuDeviceInfo> devices, string failureMessage)
			=> new(false, devices, failureMessage);
	}

	internal sealed record StopResult(bool IsSuccess, string? FailureMessage)
	{
		internal static StopResult Succeeded()
			=> new(true, null);

		internal static StopResult Failed(string failureMessage)
			=> new(false, failureMessage);
	}

	private sealed record GpuDiscoveryResult(IReadOnlyList<GpuDeviceInfo> Devices, bool IsReliable, string? FailureMessage)
	{
		internal static GpuDiscoveryResult Reliable(IReadOnlyList<GpuDeviceInfo> devices)
			=> new(devices, true, null);

		internal static GpuDiscoveryResult Fallback(IReadOnlyList<GpuDeviceInfo> devices, string failureMessage)
			=> new(devices, false, failureMessage);
	}

	private sealed class WindowsErrorDialogScope : IDisposable
	{
		private const uint SemFailCriticalErrors = 0x0001;
		private const uint SemNoGpFaultErrorBox = 0x0002;
		private readonly uint _previousMode;
		private readonly bool _active;

		private WindowsErrorDialogScope(uint previousMode, bool active)
		{
			_previousMode = previousMode;
			_active = active;
		}

		internal static WindowsErrorDialogScope Suppress()
		{
#if WINDOWS
			uint previousMode = SetErrorMode(SemFailCriticalErrors | SemNoGpFaultErrorBox);
			return new WindowsErrorDialogScope(previousMode, active: true);
#else
			return new WindowsErrorDialogScope(0, active: false);
#endif
		}

		public void Dispose()
		{
#if WINDOWS
			if (_active)
			{
				_ = SetErrorMode(_previousMode);
			}
#endif
		}

#if WINDOWS
		[DllImport("kernel32.dll")]
		private static extern uint SetErrorMode(uint uMode);
#endif
	}
}
