using ComfyUI_Nexus.Setup.Models;

namespace ComfyUI_Nexus.Setup.Services;

internal static class PipCacheService
{
	internal const string EnvironmentVariableName = "PIP_CACHE_DIR";

	internal static string GetDefaultCachePath()
		=> ComfyInstallService.GetLocalRuntimePath("Cache/pip");

	internal static string GetMode(SetupSettings? settings = null)
	{
		string mode = settings?.PipCacheMode ?? SetupSettingsService.Instance.Settings.PipCacheMode;
		return PipCacheModes.IsKnown(mode) ? mode : PipCacheModes.NexusDefault;
	}

	internal static string GetEffectiveCachePath(SetupSettings? settings = null)
	{
		if (string.Equals(GetMode(settings), PipCacheModes.PipDefault, StringComparison.Ordinal))
		{
			return string.Empty;
		}

		string configuredPath = settings?.PipCachePath ?? SetupSettingsService.Instance.Settings.PipCachePath;
		string path = string.Equals(GetMode(settings), PipCacheModes.Custom, StringComparison.Ordinal)
			&& !string.IsNullOrWhiteSpace(configuredPath)
			? configuredPath.Trim()
			: GetDefaultCachePath();
		return Path.GetFullPath(path);
	}

	internal static IReadOnlyDictionary<string, string>? CreateEnvironment(SetupSettings? settings = null)
	{
		if (string.Equals(GetMode(settings), PipCacheModes.PipDefault, StringComparison.Ordinal))
		{
			return null;
		}

		string cachePath = GetEffectiveCachePath(settings);
		Directory.CreateDirectory(cachePath);
		return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			[EnvironmentVariableName] = cachePath
		};
	}

	internal static void ClearCache(SetupSettings? settings = null)
	{
		if (string.Equals(GetMode(settings), PipCacheModes.PipDefault, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("The bundled pip cache is managed by pip and cannot be cleared by Nexus.");
		}

		string cachePath = GetEffectiveCachePath(settings);
		ValidateSafeCachePath(cachePath);

		if (Directory.Exists(cachePath))
		{
			Directory.Delete(cachePath, recursive: true);
		}

		Directory.CreateDirectory(cachePath);
	}

	internal static void ClearDefaultCache()
	{
		string cachePath = GetDefaultCachePath();
		ValidateSafeCachePath(cachePath);

		if (Directory.Exists(cachePath))
		{
			Directory.Delete(cachePath, recursive: true);
		}

		Directory.CreateDirectory(cachePath);
	}

	private static void ValidateSafeCachePath(string cachePath)
	{
		string fullPath = Path.GetFullPath(cachePath)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string? root = Path.GetPathRoot(fullPath);
		if (string.IsNullOrWhiteSpace(fullPath)
			|| string.Equals(fullPath, root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
			|| fullPath.Length < 6)
		{
			throw new InvalidOperationException($"Refusing to clear unsafe pip cache path: {cachePath}");
		}

		string localRuntimeRoot = Path.GetFullPath(ComfyInstallService.LocalRuntimePath)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (string.Equals(fullPath, localRuntimeRoot, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"Refusing to clear LocalRuntime root as pip cache: {cachePath}");
		}
	}
}
