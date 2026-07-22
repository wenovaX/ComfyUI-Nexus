namespace ComfyUI_Nexus.Setup.Services;

using System.Text;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Setup.Runtime;

internal sealed record PythonRuntimeInfo(
	string ExecutablePath,
	string Version,
	string PythonAbi,
	string Platform);

internal sealed class PythonRuntimeInfoService
{
	private readonly Dictionary<string, PythonRuntimeInfo> _cache = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _cacheGate = new();
	private readonly SemaphoreSlim _probeGate = new(1, 1);
	private long _generation;

	internal async Task<PythonRuntimeInfo?> GetAsync(
		string executablePath,
		NexusRuntimeToolingLease? lease,
		CancellationToken cancellationToken)
	{
		if (!File.Exists(executablePath))
		{
			return null;
		}

		string fullPath = Path.GetFullPath(executablePath);
		string cacheKey = CreateCacheKey(fullPath);
		lock (_cacheGate)
		{
			if (_cache.TryGetValue(cacheKey, out PythonRuntimeInfo? cached))
			{
				return cached;
			}
		}

		await _probeGate.WaitAsync(cancellationToken);
		try
		{
			lock (_cacheGate)
			{
				if (_cache.TryGetValue(cacheKey, out PythonRuntimeInfo? cached))
				{
					return cached;
				}
			}

			long generation = Volatile.Read(ref _generation);
			string toolingExecutablePath = lease?.GetToolingPath(fullPath) ?? fullPath;
			string? toolingWorkingDirectory = Path.GetDirectoryName(toolingExecutablePath);
			string[]? values = await PythonRuntimeProbe.ReadAsync(
				toolingExecutablePath,
				toolingWorkingDirectory,
				cancellationToken);
			if (values is null
				|| values.Length != 3
				|| !TryNormalizeAbi(values[1], out string? pythonAbi)
				|| !TryNormalizePlatform(values[2], out string? platform))
			{
				return null;
			}

			var runtimeInfo = new PythonRuntimeInfo(fullPath, values[0], pythonAbi, platform);
			lock (_cacheGate)
			{
				if (generation == _generation)
				{
					_cache[cacheKey] = runtimeInfo;
				}
			}
			return runtimeInfo;
		}
		finally
		{
			_probeGate.Release();
		}
	}

	internal void Invalidate()
	{
		lock (_cacheGate)
		{
			_cache.Clear();
			_generation++;
		}
	}

	private static string CreateCacheKey(string executablePath)
	{
		long stamp = File.GetLastWriteTimeUtc(executablePath).Ticks;
		return $"{executablePath}|{stamp}";
	}

	private static bool TryNormalizeAbi(string cacheTag, out string pythonAbi)
	{
		pythonAbi = string.Empty;
		const string prefix = "cpython-";
		if (!cacheTag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string version = cacheTag[prefix.Length..];
		if (version.Length < 2 || !version.All(char.IsDigit))
		{
			return false;
		}

		pythonAbi = $"cp{version}";
		return true;
	}

	private static bool TryNormalizePlatform(string architecture, out string platform)
	{
		platform = architecture.Trim().ToUpperInvariant() switch
		{
			"AMD64" or "X86_64" => "win_amd64",
			"ARM64" or "AARCH64" => "win_arm64",
			_ => string.Empty
		};
		return platform.Length > 0;
	}
}

internal static class PythonRuntimeProbe
{
	internal static async Task<string[]?> ReadAsync(
		string toolingExecutablePath,
		string? toolingWorkingDirectory,
		CancellationToken cancellationToken)
	{
		var result = await ProcessRunner.RunAsync(
			toolingExecutablePath,
			"-c \"import platform, sys; print(f'{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}|{sys.implementation.cache_tag}|{platform.machine()}')\"",
			toolingWorkingDirectory,
			cancellationToken: cancellationToken,
			outputEncoding: Encoding.UTF8);
		if (result.ExitCode != 0)
		{
			return null;
		}

		return result.Output
			.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.LastOrDefault()
			?.Split('|', StringSplitOptions.TrimEntries);
	}
}
