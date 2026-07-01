namespace ComfyUI_Nexus.Setup.Startup;

using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Services;

internal sealed record StartupReadinessResult(bool IsUsable, string Reason)
{
	internal static StartupReadinessResult Usable { get; } = new(true, "Setup runtime is ready.");
}

internal sealed class StartupReadinessProbe
{
	internal async Task<StartupReadinessResult> CheckAsync(SetupSettings settings, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(settings.ListenAddress))
		{
			return NotUsable("Listen address is missing.");
		}

		if (settings.ServerPort is <= 0 or > 65535)
		{
			return NotUsable($"Server port is invalid: {settings.ServerPort}.");
		}

		string comfyPath = ComfyPathResolver.ResolveActiveComfyPath();
		if (string.IsNullOrWhiteSpace(comfyPath) || !Directory.Exists(comfyPath))
		{
			return NotUsable($"ComfyUI path is missing or unavailable: {comfyPath}");
		}

		if (!File.Exists(Path.Combine(comfyPath, "main.py")))
		{
			return NotUsable($"ComfyUI entry point is missing: {Path.Combine(comfyPath, "main.py")}");
		}

		StartupReadinessResult packageResult = CheckBuiltInPackageAvailability(settings);
		if (!packageResult.IsUsable)
		{
			return packageResult;
		}

		string pythonExecutable = RuntimeRepairTarget.GetPythonExecutable(settings);
		if (string.IsNullOrWhiteSpace(pythonExecutable))
		{
			return NotUsable("Python runtime is missing or unavailable.");
		}

		if (RuntimeRepairTarget.IsUsingVenv(settings) && !File.Exists(pythonExecutable))
		{
			return NotUsable($"Python runtime is missing or unavailable: {pythonExecutable}");
		}

		if (!await CanRunPythonAsync(pythonExecutable, cancellationToken))
		{
			return NotUsable($"Python runtime is missing or unavailable: {pythonExecutable}");
		}

		return StartupReadinessResult.Usable;
	}

	private static StartupReadinessResult CheckBuiltInPackageAvailability(SetupSettings settings)
	{
		var packageSpec = RuntimePackageSpecService.Load();
		string pythonPackagePath = packageSpec.GetPythonPackagePath(ComfyInstallService.RootPath);
		if (UsesBuiltInPython(settings)
			&& !File.Exists(ComfyInstallService.PythonExe)
			&& !File.Exists(pythonPackagePath))
		{
			return NotUsable($"Built-in Python package is missing: {pythonPackagePath}");
		}

		string builtInGitExe = Path.Combine(ComfyInstallService.InstalledPath, "Git", "cmd", "git.exe");
		string gitPackagePath = packageSpec.GetGitPackagePath(ComfyInstallService.RootPath);
		if (UsesBuiltInGit(settings)
			&& !File.Exists(builtInGitExe)
			&& !File.Exists(gitPackagePath))
		{
			return NotUsable($"Built-in Git package is missing: {gitPackagePath}");
		}

		return StartupReadinessResult.Usable;
	}

	private static bool UsesBuiltInPython(SetupSettings settings)
		=> string.Equals(settings.PythonSource, "builtin", StringComparison.Ordinal)
			|| IsLocalRuntimePythonPath(settings.PythonPath);

	private static bool UsesBuiltInGit(SetupSettings settings)
		=> string.Equals(settings.GitSource, "builtin", StringComparison.Ordinal);

	private static bool IsLocalRuntimePythonPath(string pythonPath)
	{
		if (string.IsNullOrWhiteSpace(pythonPath)) return false;

		try
		{
			string fullPythonPath = Path.GetFullPath(pythonPath);
			string localPythonRoot = Path.GetFullPath(ComfyInstallService.PythonPath)
				.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

			return fullPythonPath.StartsWith(
				localPythonRoot + Path.DirectorySeparatorChar,
				StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private static async Task<bool> CanRunPythonAsync(string pythonExecutable, CancellationToken cancellationToken)
	{
		try
		{
			var (code, stdout, stderr) = await ProcessRunner.RunAsync(
				pythonExecutable,
				"--version",
				null,
				null,
				cancellationToken);

			return code == 0 && (!string.IsNullOrWhiteSpace(stdout) || !string.IsNullOrWhiteSpace(stderr));
		}
		catch
		{
			return false;
		}
	}

	private static StartupReadinessResult NotUsable(string reason)
		=> new(false, reason);
}
