namespace ComfyUI_Nexus.Settings;

using System.Text.Json.Serialization;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Services;

internal sealed record ServerLaunchSettingsSnapshot(
	[property: JsonPropertyName("comfy_path")] string ComfyPath,
	[property: JsonPropertyName("python_path")] string PythonPath,
	[property: JsonPropertyName("server_python_mode")] string ServerPythonMode,
	[property: JsonPropertyName("pip_cache_mode")] string PipCacheMode,
	[property: JsonPropertyName("pip_cache_path")] string PipCachePath,
	[property: JsonPropertyName("listen_address")] string ListenAddress,
	[property: JsonPropertyName("server_port")] int ServerPort,
	[property: JsonPropertyName("gpu_id")] string GpuId)
{
	internal static ServerLaunchSettingsSnapshot FromSettings(SetupSettings settings, string effectiveComfyPath)
		=> new(
			Normalize(effectiveComfyPath),
			Normalize(settings.PythonPath),
			Normalize(settings.ServerPythonMode),
			Normalize(PipCacheService.GetMode(settings)),
			Normalize(PipCacheService.GetEffectiveCachePath(settings)),
			Normalize(settings.ListenAddress),
			settings.ServerPort,
			Normalize(settings.GpuId));

	private static string Normalize(string? value)
		=> (value ?? string.Empty).Trim();
}
