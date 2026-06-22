namespace ComfyUI_Nexus.Setup.Runtime;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

internal static class GpuDiscoveryService
{
	private const string QueryArguments = "--query-gpu=index,name,memory.total --format=csv,noheader,nounits";
	private static readonly object Gate = new();
	private static readonly List<Process> ActiveProcesses = new();
	private static CancellationTokenSource _shutdown = new();
	private static IReadOnlyList<GpuDeviceInfo>? _cachedDevices;
	private static Task<GpuDiscoveryResult>? _discoveryTask;

	internal static async Task<IReadOnlyList<GpuDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
	{
		Task<GpuDiscoveryResult> discoveryTask;
		lock (Gate)
		{
			if (_cachedDevices != null)
			{
				return _cachedDevices;
			}

			_discoveryTask ??= DiscoverUncachedAsync();
			discoveryTask = _discoveryTask;
		}

		try
		{
			GpuDiscoveryResult result = await discoveryTask.WaitAsync(cancellationToken);
			lock (Gate)
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

			return result.Devices;
		}
		catch (OperationCanceledException)
		{
			lock (Gate)
			{
				if (ReferenceEquals(_discoveryTask, discoveryTask) && discoveryTask.IsCompleted)
				{
					_discoveryTask = null;
				}
			}

			throw;
		}
		catch
		{
			lock (Gate)
			{
				if (ReferenceEquals(_discoveryTask, discoveryTask))
				{
					_discoveryTask = null;
				}
			}

			return GetFallbackDevices();
		}
	}

	internal static void Prewarm()
		=> _ = PrewarmAsync();

	internal static async Task ShutdownAsync()
	{
		CancellationTokenSource shutdown;
		lock (Gate)
		{
			shutdown = _shutdown;
			_shutdown = new CancellationTokenSource();
		}

		shutdown.Cancel();
		KillActiveProcesses();

		await Task.Delay(50);
		lock (Gate)
		{
			ActiveProcesses.RemoveAll(process => !IsProcessRunning(process));
			_discoveryTask = null;
		}

		shutdown.Dispose();
	}

	private static async Task<GpuDiscoveryResult> DiscoverUncachedAsync()
	{
		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(GetShutdownToken());
		var (exitCode, output) = await RunNvidiaSmiQueryAsync(linkedCancellation.Token);
		if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
		{
			return GpuDiscoveryResult.Fallback(GetFallbackDevices());
		}

		var devices = ParseNvidiaSmiOutput(output);
		return devices.Count > 0
			? GpuDiscoveryResult.Reliable(devices)
			: GpuDiscoveryResult.Fallback(GetFallbackDevices());
	}

	private static async Task PrewarmAsync()
	{
		try
		{
			await DiscoverAsync();
		}
		catch (OperationCanceledException)
		{
		}
	}

	private static CancellationToken GetShutdownToken()
	{
		lock (Gate)
		{
			return _shutdown.Token;
		}
	}

	private static async Task<(int ExitCode, string Output)> RunNvidiaSmiQueryAsync(CancellationToken cancellationToken)
	{
#if !WINDOWS
		await Task.CompletedTask;
		return (-1, string.Empty);
#else
		var output = new StringBuilder();
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

		Register(process);
		try
		{
			using var errorDialogScope = WindowsErrorDialogScope.Suppress();
			process.Start();
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			await process.WaitForExitAsync(cancellationToken);
			return (TryGetExitCode(process), output.ToString());
		}
		catch (OperationCanceledException)
		{
			TryKillProcessTree(process);
			throw;
		}
		catch
		{
			return (-1, string.Empty);
		}
		finally
		{
			Unregister(process);
		}
#endif
	}

	private static void Register(Process process)
	{
		lock (Gate)
		{
			ActiveProcesses.Add(process);
		}
	}

	private static void KillActiveProcesses()
	{
		Process[] processes;
		lock (Gate)
		{
			processes = ActiveProcesses.ToArray();
		}

		foreach (Process process in processes)
		{
			TryKillProcessTree(process);
		}
	}

	private static void Unregister(Process process)
	{
		lock (Gate)
		{
			ActiveProcesses.Remove(process);
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

	private static void TryKillProcessTree(Process process)
	{
#if WINDOWS
		try
		{
			if (!IsProcessRunning(process)) return;

			process.Kill(entireProcessTree: true);
		}
		catch
		{
		}
#endif
	}

	private static List<GpuDeviceInfo> ParseNvidiaSmiOutput(string output)
	{
		var devices = new List<GpuDeviceInfo>();
		foreach (string rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
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

	private sealed record GpuDiscoveryResult(IReadOnlyList<GpuDeviceInfo> Devices, bool IsReliable)
	{
		internal static GpuDiscoveryResult Reliable(IReadOnlyList<GpuDeviceInfo> devices)
			=> new(devices, true);

		internal static GpuDiscoveryResult Fallback(IReadOnlyList<GpuDeviceInfo> devices)
			=> new(devices, false);
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
				SetErrorMode(_previousMode);
			}
#endif
		}

#if WINDOWS
		[DllImport("kernel32.dll")]
		private static extern uint SetErrorMode(uint uMode);
#endif
	}
}
