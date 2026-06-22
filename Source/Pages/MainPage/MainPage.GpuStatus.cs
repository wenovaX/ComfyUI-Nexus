using System.Text.Json;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private const double GpuUtilTrackWidth = 120;

	private async Task HandleGpuStatsAsync(JsonElement data)
	{
		GpuStatsSnapshot snapshot = await Task.Run(() => CreateGpuStatsSnapshot(data));
		MainThread.BeginInvokeOnMainThread(() => ApplyGpuStatsSnapshot(snapshot));
	}

	private void ApplyGpuStatsSnapshot(GpuStatsSnapshot snapshot)
	{
		try
		{
			if (!snapshot.IsVisible)
			{
				HeaderControl.SetGpuVisibility(false);
				return;
			}

			Color accent = Color.FromArgb(snapshot.AccentHex);
			Color accentSoft = Color.FromArgb(snapshot.AccentSoftHex);

			HeaderControl.UpdateGpuSummary(
				snapshot.ModelName,
				snapshot.ActivePercent,
				snapshot.AllocatedGb,
				snapshot.CachedGb,
				snapshot.TotalGb,
				accent,
				accentSoft);

			_gpuStatusController.AnimateVramUsage(snapshot.ActivePercent, snapshot.CachePercent, accent, accentSoft, GpuUtilTrackWidth);
#if WINDOWS
			_lastSystemGpuPercent = snapshot.ActivePercent;
			HeaderControl.UpdateSystemUsageSummary(_lastSystemCpuPercent, snapshot.ActivePercent, accent);
			_gpuStatusController.AnimateMiniUsage(_lastSystemCpuPercent, snapshot.ActivePercent, HeaderControl.GetMiniUsageTrackWidth());
#else
			HeaderControl.UpdateSystemUsageSummary(0, snapshot.ActivePercent, accent);
			_gpuStatusController.AnimateMiniUsage(0, snapshot.ActivePercent, HeaderControl.GetMiniUsageTrackWidth());
#endif
			_gpuStatusController.AnimateRunningState(snapshot.IsRunning);
			HeaderControl.SetExecutionState(snapshot.IsRunning);
			_pulseIsRunning = snapshot.IsRunning;
			ControlDeckControl.SetPulseRun(_pulseIsRunning, _pulseInstantStop);
		}
		catch
		{
			HeaderControl.SetGpuVisibility(false);
		}
	}

	private static GpuStatsSnapshot CreateGpuStatsSnapshot(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object ||
			!data.TryGetProperty("devices", out var devices) ||
			devices.ValueKind != JsonValueKind.Array)
		{
			return GpuStatsSnapshot.Hidden;
		}

		JsonElement? activeGpu = null;
		foreach (var device in devices.EnumerateArray())
		{
			if (device.TryGetProperty("utilization", out var utilProp) && utilProp.GetDouble() > 5)
			{
				activeGpu = device;
				break;
			}

			activeGpu ??= device;
		}

		if (activeGpu is null)
		{
			return GpuStatsSnapshot.Hidden;
		}

		var gpu = activeGpu.Value;
		string modelName = gpu.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "GPU" : "GPU";
		double allocated = gpu.TryGetProperty("allocated_vram", out var allocatedProp) ? allocatedProp.GetDouble() : 0;
		double cached = gpu.TryGetProperty("reserved_vram", out var cachedProp) ? cachedProp.GetDouble() : 0;
		double total = gpu.TryGetProperty("total_vram", out var totalProp) ? totalProp.GetDouble() : 0;
		bool isRunning = data.TryGetProperty("is_running", out var runningProp) && runningProp.GetBoolean();
		double activePercent = CalculateVramPercent(allocated, total);
		double cachePercent = CalculateVramPercent(cached, total);
		(string accent, string accentSoft) = GetGpuAccent(activePercent);

		return new GpuStatsSnapshot(
			IsVisible: true,
			ModelName: modelName,
			ActivePercent: activePercent,
			CachePercent: cachePercent,
			AllocatedGb: allocated / (1024d * 1024d * 1024d),
			CachedGb: cached / (1024d * 1024d * 1024d),
			TotalGb: total / (1024d * 1024d * 1024d),
			AccentHex: accent,
			AccentSoftHex: accentSoft,
			IsRunning: isRunning);
	}

	private static (string Accent, string AccentSoft) GetGpuAccent(double activePercent)
	{
		if (activePercent >= 85)
		{
			return ("#ff6b7d", "#ffb4bf");
		}

		if (activePercent >= 50)
		{
			return ("#ffcc7a", "#ffe2a8");
		}

		return ("#22d3ee", "#a7f3ff");
	}

	private static double CalculateVramPercent(double usedBytes, double totalBytes)
	{
		if (totalBytes <= 0)
		{
			return 0;
		}

		return Math.Clamp((usedBytes / totalBytes) * 100d, 0, 100);
	}

	private sealed record GpuStatsSnapshot(
		bool IsVisible,
		string ModelName,
		double ActivePercent,
		double CachePercent,
		double AllocatedGb,
		double CachedGb,
		double TotalGb,
		string AccentHex,
		string AccentSoftHex,
		bool IsRunning)
	{
		internal static readonly GpuStatsSnapshot Hidden = new(
			IsVisible: false,
			ModelName: string.Empty,
			ActivePercent: 0,
			CachePercent: 0,
			AllocatedGb: 0,
			CachedGb: 0,
			TotalGb: 0,
			AccentHex: "#22d3ee",
			AccentSoftHex: "#a7f3ff",
			IsRunning: false);
	}
}
