namespace ComfyUI_Nexus.Setup.Services;

using System.Text;
using ComfyUI_Nexus.Setup.Runtime;

internal sealed record PythonRuntimeInfo(
	string ExecutablePath,
	string Version,
	string PythonAbi,
	string Platform);

internal sealed class PythonRuntimeInfoService
{
	internal static PythonRuntimeInfoService Instance { get; } = new();

	private readonly Dictionary<string, PythonRuntimeInfo> _cache = new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _gate = new(1, 1);

	internal async Task<PythonRuntimeInfo?> GetCurrentAsync(CancellationToken cancellationToken)
	{
		string executablePath = RuntimeRepairTarget.GetPythonExecutable();
		if (!File.Exists(executablePath))
		{
			return null;
		}

		string fullPath = Path.GetFullPath(executablePath);
		string cacheKey = CreateCacheKey(fullPath);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (_cache.TryGetValue(cacheKey, out PythonRuntimeInfo? cached))
			{
				return cached;
			}

			var result = await ProcessRunner.RunAsync(
				fullPath,
				"-c \"import platform, sys; print(f'{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}|{sys.implementation.cache_tag}|{platform.machine()}')\"",
				Path.GetDirectoryName(fullPath),
				cancellationToken: cancellationToken,
				outputEncoding: Encoding.UTF8);
			if (result.ExitCode != 0)
			{
				return null;
			}

			string[] values = result.Output
				.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.LastOrDefault()
				?.Split('|', StringSplitOptions.TrimEntries) ?? [];
			if (values.Length != 3 || !TryNormalizeAbi(values[1], out string? pythonAbi) || !TryNormalizePlatform(values[2], out string? platform))
			{
				return null;
			}

			var runtimeInfo = new PythonRuntimeInfo(fullPath, values[0], pythonAbi, platform);
			_cache[cacheKey] = runtimeInfo;
			return runtimeInfo;
		}
		finally
		{
			_gate.Release();
		}
	}

	internal void Invalidate()
	{
		_gate.Wait();
		try
		{
			_cache.Clear();
		}
		finally
		{
			_gate.Release();
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
