#if WINDOWS
using System.Runtime.InteropServices;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private CancellationTokenSource? _systemTelemetryCts;
	private ulong _lastCpuIdleTicks;
	private ulong _lastCpuKernelTicks;
	private ulong _lastCpuUserTicks;
	private bool _hasCpuSample;

	partial void InitializeNativeSystemTelemetry()
	{
		_systemTelemetryCts = new CancellationTokenSource();
		Unloaded += (_, _) => _systemTelemetryCts?.Cancel();
		_ = RunNativeSystemTelemetryLoopAsync(_systemTelemetryCts.Token);
	}

	private async Task RunNativeSystemTelemetryLoopAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				var snapshot = await ReadNativeSystemTelemetryAsync(cancellationToken);
				if (snapshot is not null)
				{
					await MainThread.InvokeOnMainThreadAsync(() => ApplyNativeSystemTelemetry(snapshot.Value));
				}
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch
			{
			}

			try
			{
				await Task.Delay(1000, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private Task<SystemTelemetrySnapshot?> ReadNativeSystemTelemetryAsync(CancellationToken cancellationToken)
	{
		double cpuPercent = ReadCpuUsagePercent();
		return Task.FromResult<SystemTelemetrySnapshot?>(new SystemTelemetrySnapshot(cpuPercent, null));
	}

	private void ApplyNativeSystemTelemetry(SystemTelemetrySnapshot snapshot)
	{
		_lastSystemCpuPercent = snapshot.CpuPercent;
		if (snapshot.GpuPercent.HasValue)
		{
			_lastSystemGpuPercent = snapshot.GpuPercent.Value;
		}

		double gpuPercent = snapshot.GpuPercent ?? _lastSystemGpuPercent;
		var accent = GetTelemetryAccent(gpuPercent);
		HeaderControl.UpdateSystemUsageSummary(snapshot.CpuPercent, gpuPercent, accent);
		_gpuStatusController.AnimateMiniUsage(snapshot.CpuPercent, gpuPercent, HeaderControl.GetMiniUsageTrackWidth());
	}

	private static Color GetTelemetryAccent(double usagePercent)
	{
		if (usagePercent >= 85)
		{
			return Color.FromArgb("#ff7a8f");
		}

		if (usagePercent >= 55)
		{
			return Color.FromArgb("#ffca6f");
		}

		return Color.FromArgb("#22d3ee");
	}

	private double ReadCpuUsagePercent()
	{
		if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
		{
			return 0;
		}

		ulong idleTicks = ToUInt64(idleTime);
		ulong kernelTicks = ToUInt64(kernelTime);
		ulong userTicks = ToUInt64(userTime);

		if (!_hasCpuSample)
		{
			_lastCpuIdleTicks = idleTicks;
			_lastCpuKernelTicks = kernelTicks;
			_lastCpuUserTicks = userTicks;
			_hasCpuSample = true;
			return 0;
		}

		ulong idleDelta = idleTicks - _lastCpuIdleTicks;
		ulong kernelDelta = kernelTicks - _lastCpuKernelTicks;
		ulong userDelta = userTicks - _lastCpuUserTicks;
		ulong totalDelta = kernelDelta + userDelta;

		_lastCpuIdleTicks = idleTicks;
		_lastCpuKernelTicks = kernelTicks;
		_lastCpuUserTicks = userTicks;

		if (totalDelta == 0)
		{
			return 0;
		}

		double activeDelta = totalDelta - idleDelta;
		return Math.Clamp(activeDelta * 100d / totalDelta, 0, 100);
	}

	private static ulong ToUInt64(FILETIME fileTime)
	{
		return ((ulong)fileTime.dwHighDateTime << 32) | fileTime.dwLowDateTime;
	}

	[DllImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

	[StructLayout(LayoutKind.Sequential)]
	private struct FILETIME
	{
		public uint dwLowDateTime;
		public uint dwHighDateTime;
	}

	private readonly record struct SystemTelemetrySnapshot(double CpuPercent, double? GpuPercent);
}
#endif
