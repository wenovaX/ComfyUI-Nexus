using System.Diagnostics;

namespace ComfyUI_Nexus.Diagnostics;

internal static class RailPerformanceDiagnostics
{
#if DEBUG
	private const bool EnableRailPerformanceLogs = false;
	internal static bool IsEnabled => EnableRailPerformanceLogs;
#else
	internal static bool IsEnabled => false;
#endif

	internal static long Start()
		=> IsEnabled ? Stopwatch.GetTimestamp() : 0;

	internal static void Mark(string stage, long startTimestamp, string? detail = null)
	{
		if (!IsEnabled)
		{
			return;
		}

		string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" - {detail}";
		long elapsedMilliseconds = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
		NexusLog.Info($"[RAIL_PERF] +{elapsedMilliseconds:D4}ms {stage}{suffix}");
	}
}
