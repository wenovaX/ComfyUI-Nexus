#if WINDOWS
using System.Runtime.InteropServices;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private IDispatcherTimer? _systemTelemetryTimer;
	private ulong _lastCpuIdleTicks;
	private ulong _lastCpuKernelTicks;
	private ulong _lastCpuUserTicks;
	private bool _hasCpuSample;

	partial void InitializeNativeSystemTelemetry()
	{
		_systemTelemetryTimer = Dispatcher.CreateTimer();
		_systemTelemetryTimer.Interval = TimeSpan.FromSeconds(1);
		_systemTelemetryTimer.Tick += OnSystemTelemetryTimerTick;
		Unloaded += OnSystemTelemetryUnloaded;
	}

	partial void StartNativeSystemTelemetry()
	{
		if (_systemTelemetryTimer is null)
		{
			return;
		}

		_hasCpuSample = false;
		if (!_systemTelemetryTimer.IsRunning)
		{
			_systemTelemetryTimer.Start();
		}
	}

	partial void StopNativeSystemTelemetry()
	{
		_systemTelemetryTimer?.Stop();
		_hasCpuSample = false;
	}

	private void OnSystemTelemetryUnloaded(object? sender, EventArgs e)
	{
		if (_systemTelemetryTimer is null)
		{
			return;
		}

		StopNativeSystemTelemetry();
		_systemTelemetryTimer.Tick -= OnSystemTelemetryTimerTick;
		_systemTelemetryTimer = null;
	}

	private void OnSystemTelemetryTimerTick(object? sender, EventArgs e)
	{
		try
		{
			ApplyNativeSystemTelemetry(new SystemTelemetrySnapshot(ReadCpuUsagePercent()));
		}
		catch
		{
		}
	}

	private void ApplyNativeSystemTelemetry(SystemTelemetrySnapshot snapshot)
	{
		_lastSystemCpuPercent = snapshot.CpuPercent;
		HeaderControl.UpdateSystemUsageSummary(snapshot.CpuPercent);
		_gpuStatusController.UpdateCpuUsage(snapshot.CpuPercent);
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

	private readonly record struct SystemTelemetrySnapshot(double CpuPercent);
}
#endif
