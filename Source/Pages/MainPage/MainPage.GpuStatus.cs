using System.Text.Json;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private string? _lastGpuModelName;
	private GpuStatsSnapshot? _lastVisibleGpuStatsSnapshot;

	private Task HandleGpuStatsAsync(JsonElement data, NexusOperationLease lease)
	{
		if (!_shellRuntimeServicesActive || !lease.IsCurrent)
		{
			return Task.CompletedTask;
		}

		GpuStatsSnapshot snapshot = CreateGpuStatsSnapshot(data);
		UiThread.PostLatest("main-page-bridge", "gpu-stats", lease.Generation, () =>
		{
			if (_shellRuntimeServicesActive)
			{
				ApplyGpuStatsSnapshot(snapshot);
			}
		});
		return Task.CompletedTask;
	}

	private void ApplyGpuStatsSnapshot(GpuStatsSnapshot snapshot)
	{
		try
		{
			if (!snapshot.IsVisible)
			{
				_lastVisibleGpuStatsSnapshot = null;
				HeaderControl.SetGpuVisibility(false);
				return;
			}

			_lastVisibleGpuStatsSnapshot = snapshot;

			Color accent = Color.FromArgb(snapshot.AccentHex);
			Color accentSoft = Color.FromArgb(snapshot.AccentSoftHex);
			string modelName = ResolveDisplayGpuModelName(snapshot.ReportedModelName, snapshot.DeviceId);

			HeaderControl.UpdateGpuSummary(
				modelName,
				snapshot.LoadPercent,
				snapshot.ActivePercent,
				snapshot.UsedGb,
				snapshot.ReservedGb,
				snapshot.TotalGb,
				snapshot.UsedOverflow,
				snapshot.ReservedOverflow,
				accent,
				accentSoft);

			_gpuStatusController.UpdateGpuUsage(snapshot.LoadPercent, snapshot.ActivePercent, snapshot.CachePercent);
#if WINDOWS
			HeaderControl.UpdateSystemUsageSummary(_lastSystemCpuPercent);
			_gpuStatusController.UpdateCpuUsage(_lastSystemCpuPercent);
#else
			HeaderControl.UpdateSystemUsageSummary(0);
			_gpuStatusController.UpdateCpuUsage(0);
#endif
			_gpuStatusController.UpdateExecutionState(snapshot.IsRunning);
			HeaderControl.SetExecutionState(snapshot.IsRunning);
			_pulseIsRunning = snapshot.IsRunning;
			CurrentControlDeck?.SetPulseRun(_pulseIsRunning, _pulseInstantStop);
		}
		catch
		{
			HeaderControl.SetGpuVisibility(false);
		}
	}

	private void RestoreGpuStatusAfterShellReveal()
	{
		if (_lastVisibleGpuStatsSnapshot is { } snapshot)
		{
			ApplyGpuStatsSnapshot(snapshot);
			return;
		}

		_gpuStatusController.RestoreAfterSurfaceAttach();
	}

	private string ResolveDisplayGpuModelName(string reportedModelName, string? deviceId)
	{
		string resolvedModelName = ResolveGpuModelName(reportedModelName, deviceId);
		if (!IsGenericGpuName(resolvedModelName))
		{
			_lastGpuModelName = resolvedModelName;
			return resolvedModelName;
		}

		return !string.IsNullOrWhiteSpace(_lastGpuModelName)
			? _lastGpuModelName
			: "GPU";
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
		string reportedModelName = gpu.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
		string? deviceId = TryReadGpuId(gpu);
		double used = gpu.TryGetProperty("used_vram", out var usedProp) ? usedProp.GetDouble() : 0;
		double reserved = gpu.TryGetProperty("reserved_vram", out var reservedProp) ? reservedProp.GetDouble() : 0;
		double total = gpu.TryGetProperty("total_vram", out var totalProp) ? totalProp.GetDouble() : 0;
		double loadPercent = gpu.TryGetProperty("utilization", out var loadProp) ? Math.Clamp(loadProp.GetDouble(), 0, 100) : 0;
		bool isRunning = data.TryGetProperty("is_running", out var runningProp) && runningProp.GetBoolean();
		double activePercent = CalculateVramPercent(used, total);
		double cachePercent = CalculateVramPercent(reserved, total);
		bool usedOverflow = total > 0 && used > total;
		double displayUsed = total > 0 ? Math.Min(used, total) : used;
		(string accent, string accentSoft) = GetGpuAccent(activePercent);

		return new GpuStatsSnapshot(
			IsVisible: true,
			ReportedModelName: reportedModelName,
			DeviceId: deviceId,
			LoadPercent: loadPercent,
			ActivePercent: activePercent,
			CachePercent: cachePercent,
			UsedGb: displayUsed / (1024d * 1024d * 1024d),
			ReservedGb: reserved / (1024d * 1024d * 1024d),
			TotalGb: total / (1024d * 1024d * 1024d),
			UsedOverflow: usedOverflow,
			ReservedOverflow: false,
			AccentHex: accent,
			AccentSoftHex: accentSoft,
			IsRunning: isRunning);
	}

	private static string ResolveGpuModelName(string rawModelName, string? gpuId)
	{
		if (!IsGenericGpuName(rawModelName))
		{
			return rawModelName.Trim();
		}

		string? discoveredName = GpuDiscoveryService.TryGetCachedDeviceName(gpuId);
		return !IsGenericGpuName(discoveredName)
			? discoveredName!
			: "GPU";
	}

	private static string? TryReadGpuId(JsonElement gpu)
	{
		foreach (string propertyName in new[] { "index", "id", "device_id", "cuda_device" })
		{
			if (!gpu.TryGetProperty(propertyName, out var property))
			{
				continue;
			}

			return property.ValueKind switch
			{
				JsonValueKind.Number when property.TryGetInt32(out int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
				JsonValueKind.String => property.GetString(),
				_ => null,
			};
		}

		return null;
	}

	private static bool IsGenericGpuName(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return true;
		}

		string normalized = value.Trim();
		return string.Equals(normalized, "GPU", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(normalized, "GPU 0", StringComparison.OrdinalIgnoreCase);
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
		string ReportedModelName,
		string? DeviceId,
		double LoadPercent,
		double ActivePercent,
		double CachePercent,
		double UsedGb,
		double ReservedGb,
		double TotalGb,
		bool UsedOverflow,
		bool ReservedOverflow,
		string AccentHex,
		string AccentSoftHex,
		bool IsRunning)
	{
		internal static readonly GpuStatsSnapshot Hidden = new(
			IsVisible: false,
			ReportedModelName: string.Empty,
			DeviceId: null,
			LoadPercent: 0,
			ActivePercent: 0,
			CachePercent: 0,
			UsedGb: 0,
			ReservedGb: 0,
			TotalGb: 0,
			UsedOverflow: false,
			ReservedOverflow: false,
			AccentHex: "#22d3ee",
			AccentSoftHex: "#a7f3ff",
			IsRunning: false);
	}
}
