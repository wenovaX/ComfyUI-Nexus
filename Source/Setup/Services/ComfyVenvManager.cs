namespace ComfyUI_Nexus.Setup.Services;

using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;

internal sealed record VenvSeedReadiness(bool IsReady, string PythonExecutable, string Message);

internal sealed class ComfyVenvManager
{
	private const string RuntimeTag = "[Runtime]";
	private const string PipProgressArguments = "--progress-bar raw";
	private const string PyTorchRuntimeDependencies = "filelock fsspec jinja2 networkx numpy pillow setuptools sympy typing_extensions";
	private const string PipProgressEnvironmentVariableName = "PIP_PROGRESS_BAR";
	private const string PipProgressMode = "raw";
	private const string ShortComfyPathPrefix = "nx";
	private const string ShortComfyPathLinkName = "ComfyUI";
	private const string ShortComfyPathMarkerFileName = "README_NEXUS_TEMP.txt";
	private static readonly TimeSpan RequirementsIdleTimeout = TimeSpan.FromMinutes(45);
	private static readonly TimeSpan PyTorchIdleTimeout = TimeSpan.FromHours(3);
	private static readonly TimeSpan ReadinessIdleTimeout = TimeSpan.FromSeconds(30);
	private static string ActiveComfyPath => ComfyPathResolver.ResolveActiveComfyPath();
	private static string ActiveVenvPath => ComfyPathResolver.ResolveActiveVenvPath();
	private static string ActiveVenvPythonExe => ComfyPathResolver.ResolveActiveVenvPythonExe();
	private static string ActiveVenvRelativePythonExe => Path.Combine(".venv", "Scripts", "python.exe");

	private readonly Action<string> _log;
	private readonly Action<double, string> _progress;

	internal ComfyVenvManager(Action<string> log, Action<double, string> progress)
	{
		_log = log;
		_progress = progress;
	}

	internal async Task<SetupStepResult> EnsureOnlyAsync(CancellationToken cancellationToken)
	{
		try
		{
			Report(0.05, "Preparing ComfyUI virtual environment. This can take 10-20 minutes on a fresh setup.");
			ComfyInstallService.EnsureComfyWorkspaceDirectories(ActiveComfyPath);
			await EnsureAsync(RuntimeTag, cancellationToken);
		}
		catch (Exception ex)
		{
			return new SetupStepResult(false, $"Failed to create .venv: {ex.Message}", 0);
		}

		Report(0.30, "Installing ComfyUI Python dependencies into .venv. This is the long step.");
		var result = await RepairVenvDependenciesAsync(cancellationToken);
		if (result.IsSuccess)
		{
			var settings = SetupSettingsService.Instance.Settings;
			settings.ServerPythonMode = PythonExecutionModes.Venv;
			settings.PendingVenvDelete = false;
			SetupSettingsService.Instance.Save();
		}

		return result.IsSuccess
			? new SetupStepResult(true, "ComfyUI .venv is ready and runtime dependencies are repaired.", 1)
			: result;
	}

	internal async Task<SetupStepResult> RebuildOnlyAsync(CancellationToken cancellationToken)
	{
		try
		{
			ComfyInstallService.EnsureComfyWorkspaceDirectories(ActiveComfyPath);

			if (Directory.Exists(ActiveVenvPath))
			{
				if (!IsSafeComfyVenvPath(ActiveVenvPath))
				{
					return new SetupStepResult(false, $"Refusing to delete unsafe venv path: {ActiveVenvPath}", 0);
				}

				_log($"{RuntimeTag} Removing existing .venv...");
				Report(0.05, "Removing existing .venv before rebuilding it cleanly...");
				if (!await TryDeleteComfyVenvDirectoryAsync(cancellationToken))
				{
					return CreateVenvDeleteFailureResult("rebuild");
				}
			}

			var result = await EnsureOnlyAsync(cancellationToken);
			return result.IsSuccess
				? new SetupStepResult(true, "ComfyUI .venv was rebuilt and runtime dependencies are repaired.", 1)
				: result;
		}
		catch (Exception ex)
		{
			return new SetupStepResult(false, $"Failed to rebuild .venv: {ex.Message}", 0);
		}
	}

	internal async Task<SetupStepResult> DeleteOnlyAsync(CancellationToken cancellationToken)
	{
		try
		{
			if (!Directory.Exists(ActiveVenvPath))
			{
				SetupSettingsService.Instance.Settings.ServerPythonMode = PythonExecutionModes.ConfiguredPython;
				SetupSettingsService.Instance.Settings.PendingVenvDelete = false;
				SetupSettingsService.Instance.Save();
				_log($"{RuntimeTag} .venv does not exist. Direct Python mode is active.");
				return new SetupStepResult(true, ".venv is already absent.", 1);
			}

			if (await IsServerRunningAsync(cancellationToken))
			{
				return ScheduleDelete("ComfyUI server is running");
			}

			if (!IsSafeComfyVenvPath(ActiveVenvPath))
			{
				return new SetupStepResult(false, $"Refusing to delete unsafe venv path: {ActiveVenvPath}", 0);
			}

			_log($"{RuntimeTag} Deleting ComfyUI .venv...");
			if (!await TryDeleteComfyVenvDirectoryAsync(cancellationToken))
			{
				return CreateVenvDeleteFailureResult("delete");
			}

			SetupSettingsService.Instance.Settings.ServerPythonMode = PythonExecutionModes.ConfiguredPython;
			SetupSettingsService.Instance.Settings.PendingVenvDelete = false;
			SetupSettingsService.Instance.Save();

			_log($"{RuntimeTag} .venv deleted. Direct Python mode is active.");
			return new SetupStepResult(true, ".venv deleted. Direct Python mode is active.", 1);
		}
		catch (Exception ex)
		{
			return new SetupStepResult(false, $"Failed to delete .venv: {ex.Message}", 0);
		}
	}

	internal async Task<SetupStepResult> ApplyPendingDeleteAsync(CancellationToken cancellationToken)
	{
		var settings = SetupSettingsService.Instance.Settings;
		if (!settings.PendingVenvDelete || !IsDirectPythonMode(settings))
		{
			return new SetupStepResult(true, "No pending .venv delete.", 1);
		}

		try
		{
			if (!Directory.Exists(ActiveVenvPath))
			{
				settings.PendingVenvDelete = false;
				SetupSettingsService.Instance.Save();
				_log($"{RuntimeTag} Pending .venv delete cleared. .venv is already absent.");
				return new SetupStepResult(true, "Pending .venv delete cleared.", 1);
			}

			if (!IsSafeComfyVenvPath(ActiveVenvPath))
			{
				return new SetupStepResult(false, $"Refusing to delete unsafe venv path: {ActiveVenvPath}", 0);
			}

			_log($"{RuntimeTag} Applying pending .venv delete before server boot...");
			if (!await TryDeleteComfyVenvDirectoryAsync(cancellationToken))
			{
				return CreateVenvDeleteFailureResult("apply pending delete for");
			}

			settings.PendingVenvDelete = false;
			SetupSettingsService.Instance.Save();
			_log($"{RuntimeTag} Pending .venv delete applied. Direct Python mode is active.");
			return new SetupStepResult(true, "Pending .venv delete applied. Direct Python mode is active.", 1);
		}
		catch (Exception ex)
		{
			return new SetupStepResult(false, $"Failed to apply pending .venv delete: {ex.Message}", 0);
		}
	}

	private static bool IsDirectPythonMode(SetupSettings settings)
		=> string.Equals(settings.ServerPythonMode, PythonExecutionModes.ConfiguredPython, StringComparison.Ordinal);

	private static async Task<bool> IsServerRunningAsync(CancellationToken cancellationToken)
		=> await Task.Run(() => ComfyServerProcessRegistry.FindServerProcess() != null, cancellationToken);

	private SetupStepResult ScheduleDelete(string reason)
	{
		var settings = SetupSettingsService.Instance.Settings;
		settings.ServerPythonMode = PythonExecutionModes.ConfiguredPython;
		settings.PendingVenvDelete = true;
		SetupSettingsService.Instance.Save();

		_log($"{RuntimeTag} .venv delete scheduled. Reason: {reason}. It will run before the next server boot.");
		return new SetupStepResult(true, ".venv delete scheduled. Restart the server to remove it safely before the next boot.", 1);
	}

	internal async Task EnsureAsync(string tag, CancellationToken cancellationToken)
	{
		_log($"{tag} Ensuring virtual environment...");
		if (File.Exists(ActiveVenvPythonExe)) return;

		string seedPython = ResolveVenvSeedPython();
		string? seedWorkingDirectory = ResolveVenvSeedWorkingDirectory(seedPython);
		_log($"{tag} Venv seed Python: {seedPython}");
		Report(0.10, $"Checking seed Python for .venv support: {seedPython}");

		var readiness = await EnsureSeedPythonReadinessAsync(seedPython, seedWorkingDirectory, bootstrapPip: true, _log, cancellationToken);
		if (!readiness.IsReady)
		{
			throw new InvalidOperationException(readiness.Message);
		}

		if (Directory.Exists(ActiveVenvPath))
		{
			if (!IsSafeComfyVenvPath(ActiveVenvPath))
			{
				throw new InvalidOperationException($"Refusing to replace unsafe incomplete venv path: {ActiveVenvPath}");
			}

			_log($"{tag} Incomplete .venv detected. Recreating it before dependency repair...");
			if (!await TryDeleteComfyVenvDirectoryAsync(cancellationToken))
			{
				throw new InvalidOperationException(CreateVenvDeleteFailureResult("replace incomplete").Message);
			}
		}

		Report(0.18, "Creating ComfyUI .venv folder and Python launcher...");
		var pipEnvironment = CreatePipEnvironment(tag);
		var result = await ProcessRunner.RunAsync(
			seedPython,
			$"-m venv \"{ActiveVenvPath}\"",
			seedWorkingDirectory,
			_log,
			cancellationToken,
			environmentVariables: pipEnvironment);
		if (result.ExitCode != 0)
		{
			throw new InvalidOperationException($"Failed to create ComfyUI venv: {result.Error}");
		}

		ValidateActiveVenvLayout(seedPython);
		PythonRuntimeInfoService.Instance.Invalidate();
	}

	internal static async Task<VenvSeedReadiness> CheckSeedPythonReadinessAsync(CancellationToken cancellationToken)
	{
		string seedPython = ResolveVenvSeedPython();
		string? seedWorkingDirectory = ResolveVenvSeedWorkingDirectory(seedPython);
		return await EnsureSeedPythonReadinessAsync(seedPython, seedWorkingDirectory, bootstrapPip: false, onLog: null, cancellationToken);
	}

	internal async Task<SetupStepResult> RepairServerRuntimeDependenciesAsync(CancellationToken cancellationToken)
	{
		try
		{
			ComfyInstallService.EnsureComfyWorkspaceDirectories(ActiveComfyPath);
			string pythonExecutable = RuntimeRepairTarget.GetPythonExecutable();
			string runtimeLabel = RuntimeRepairTarget.GetLabel();

			if (RuntimeRepairTarget.IsUsingVenv())
			{
				await EnsureAsync(RuntimeTag, cancellationToken);
				pythonExecutable = ActiveVenvPythonExe;
			}

			_log($"{RuntimeTag} Repair target: {runtimeLabel}");
			_log($"{RuntimeTag} Repair Python: {pythonExecutable}");

			var cudaResult = await UpgradeToCudaAsync(pythonExecutable, cancellationToken);
			if (!cudaResult.IsSuccess) return cudaResult;

			await InstallComfyRequirementsAsync(RuntimeTag, pythonExecutable, cancellationToken);

			var verified = await VerifyCudaEnvironmentAsync(pythonExecutable, cancellationToken);
			if (!verified.IsSuccess) return verified;

			return new SetupStepResult(true, $"{runtimeLabel} dependencies and CUDA environment are ready.", 1);
		}
		catch (Exception ex)
		{
			return new SetupStepResult(false, $"Runtime dependency repair failed: {ex.Message}", 0);
		}
	}

	private async Task<SetupStepResult> RepairVenvDependenciesAsync(CancellationToken cancellationToken)
	{
		try
		{
			var cudaResult = await UpgradeToCudaAsync(ActiveVenvPythonExe, cancellationToken);
			if (!cudaResult.IsSuccess) return cudaResult;

			await InstallComfyRequirementsAsync(RuntimeTag, ActiveVenvPythonExe, cancellationToken);

			var verified = await VerifyCudaEnvironmentAsync(ActiveVenvPythonExe, cancellationToken);
			if (!verified.IsSuccess) return verified;

			return new SetupStepResult(true, "ComfyUI .venv dependencies and CUDA environment are ready.", 1);
		}
		catch (Exception ex)
		{
			return new SetupStepResult(false, $"ComfyUI .venv dependency repair failed: {ex.Message}", 0);
		}
	}

	private IReadOnlyDictionary<string, string>? CreatePipEnvironment(string tag)
	{
		var cacheEnvironment = PipCacheService.CreateEnvironment();
		var environment = CreatePipEnvironmentWithProgress(cacheEnvironment);
		_log(cacheEnvironment is null
			? $"{tag} pip cache: pip default"
			: $"{tag} pip cache: {cacheEnvironment[PipCacheService.EnvironmentVariableName]}");
		return environment;
	}

	private static IReadOnlyDictionary<string, string>? CreatePipEnvironment(Action<string>? onLog)
	{
		var cacheEnvironment = PipCacheService.CreateEnvironment();
		var environment = CreatePipEnvironmentWithProgress(cacheEnvironment);
		onLog?.Invoke(cacheEnvironment is null
			? $"{RuntimeTag} pip cache: pip default"
			: $"{RuntimeTag} pip cache: {cacheEnvironment[PipCacheService.EnvironmentVariableName]}");
		return environment;
	}

	private static IReadOnlyDictionary<string, string> CreatePipEnvironmentWithProgress(
		IReadOnlyDictionary<string, string>? cacheEnvironment)
	{
		var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			[PipProgressEnvironmentVariableName] = PipProgressMode
		};

		if (cacheEnvironment is not null)
		{
			foreach (var (key, value) in cacheEnvironment)
			{
				environment[key] = value;
			}
		}

		return environment;
	}

	private async Task InstallComfyRequirementsAsync(string tag, string pythonExecutable, CancellationToken cancellationToken)
	{
		string requirementsPath = Path.Combine(ActiveComfyPath, "requirements.txt");
		if (!File.Exists(requirementsPath))
		{
			_log($"{tag} requirements.txt was not found. Skipping dependency sync.");
			return;
		}

		_log($"{tag} Syncing ComfyUI requirements into: {pythonExecutable}");
		Report(0.38, "Installing ComfyUI requirements into .venv. A fresh setup may take 10-20 minutes.");
		var pipEnvironment = CreatePipEnvironment(tag);
		using var pipPath = await CreateShortComfyPathIfNeededAsync(tag, pythonExecutable, cancellationToken);
		string pipPython = pipPath?.PythonExecutable ?? pythonExecutable;
		string pipWorkingDirectory = pipPath?.ComfyPath ?? ActiveComfyPath;
		string pipRequirementsPath = pipPath == null
			? requirementsPath
			: Path.Combine(pipPath.ComfyPath, "requirements.txt");
		_log($"{tag} Requirements install Python: {pipPython}");
		var result = await ProcessRunner.RunAsync(
			pipPython,
			$"-m pip install {PipProgressArguments} -r \"{pipRequirementsPath}\"",
			pipWorkingDirectory,
			_log,
			cancellationToken,
			RequirementsIdleTimeout,
			pipEnvironment);

		if (result.ExitCode != 0)
		{
			string detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output.Trim() : result.Error.Trim();
			throw new InvalidOperationException($"Failed to sync ComfyUI requirements: {detail}");
		}
	}

	private async Task<SetupStepResult> UpgradeToCudaAsync(string pythonExecutable, CancellationToken cancellationToken)
	{
		_log($"{RuntimeTag} Verifying PyTorch CUDA environment...");
		Report(0.60, "Checking PyTorch CUDA support in the virtual environment...");

		var existing = await VerifyCudaEnvironmentAsync(pythonExecutable, cancellationToken);
		if (existing.IsSuccess)
		{
			return new SetupStepResult(true, "CUDA environment already verified.", 1);
		}

		var settings = SetupSettingsService.Instance.Settings;
		_log($"{RuntimeTag} CUDA environment is not ready. Installing PyTorch from {settings.PyTorchIndexUrl}...");
		Report(0.68, "Installing CUDA PyTorch packages. This can download and unpack several GB; progress appears in the setup log when pip reports it.");
		var pipEnvironment = CreatePipEnvironment(RuntimeTag);
		using var pipPath = await CreateShortComfyPathIfNeededAsync(RuntimeTag, pythonExecutable, cancellationToken);
		string pipPython = pipPath?.PythonExecutable ?? pythonExecutable;
		string pipWorkingDirectory = pipPath?.ComfyPath ?? ActiveComfyPath;
		_log($"{RuntimeTag} PyTorch install Python: {pipPython}");

		_log($"{RuntimeTag} Installing configured CUDA PyTorch packages in place.");

		var install = await ProcessRunner.RunAsync(
			pipPython,
			$"-m pip install {PipProgressArguments} --ignore-installed --no-deps {settings.TorchPackages} --index-url {settings.PyTorchIndexUrl}",
			pipWorkingDirectory,
			_log,
			cancellationToken,
			PyTorchIdleTimeout,
			pipEnvironment);
		if (install.ExitCode != 0)
		{
			return new SetupStepResult(false, $"Failed to install CUDA PyTorch packages: {install.Error}", 0);
		}

		_log($"{RuntimeTag} Installing required PyTorch runtime dependencies.");
		var dependencies = await ProcessRunner.RunAsync(
			pipPython,
			$"-m pip install {PipProgressArguments} --upgrade {PyTorchRuntimeDependencies}",
			pipWorkingDirectory,
			_log,
			cancellationToken,
			RequirementsIdleTimeout,
			pipEnvironment);
		if (dependencies.ExitCode != 0)
		{
			return new SetupStepResult(false, $"Failed to install PyTorch runtime dependencies: {GetProcessFailureDetail(dependencies)}", 0);
		}

		Report(0.90, "Verifying GPU runtime after PyTorch install...");
		_log($"{RuntimeTag} PyTorch verify Python: {pythonExecutable}");
		return await VerifyCudaEnvironmentAsync(pythonExecutable, cancellationToken);
	}

	private async Task<SetupStepResult> VerifyCudaEnvironmentAsync(string pythonExecutable, CancellationToken cancellationToken)
	{
		_log($"{RuntimeTag} Running GPU hardware verification...");
		const string verifyScript = @"
import importlib.util
import sys

if importlib.util.find_spec('torch') is None:
    print('PyTorch: not installed')
    sys.exit(2)

import torch

print(f'Torch Version: {torch.__version__}')
print(f'CUDA Build: {torch.version.cuda}')
print(f'CUDA Available: {torch.cuda.is_available()}')

if torch.cuda.is_available():
    count = torch.cuda.device_count()
    print(f'Total GPU(s) found: {count}')
    for i in range(count):
        props = torch.cuda.get_device_properties(i)
        print(f'  [GPU {i}] {torch.cuda.get_device_name(i)}')
        print(f'    Compute Capability: {torch.cuda.get_device_capability(i)}')
        print(f'    VRAM: {round(props.total_memory / (1024**3), 2)} GB')
    print(f'Current Active Device: {torch.cuda.current_device()}')
else:
    print('CRITICAL ERROR: CUDA is NOT available. Fallback to CPU occurred.')
    sys.exit(1)
";
		string scriptPath = Path.Combine(Path.GetTempPath(), $"nexus_gpu_check_{Guid.NewGuid():N}.py");
		try
		{
			await File.WriteAllTextAsync(scriptPath, verifyScript, cancellationToken);
			var (exitCode, output, error) = await ProcessRunner.RunAsync(pythonExecutable, $"\"{scriptPath}\"", ActiveComfyPath, _log, cancellationToken);

			if (exitCode == 0 && output.Contains("CUDA Available: True", StringComparison.Ordinal))
			{
				return new SetupStepResult(true, "CUDA environment verified.", 1);
			}

			string detail = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
			return new SetupStepResult(false, $"CUDA verification failed. ExitCode: {exitCode}. {detail}", 0);
		}
		finally
		{
			if (File.Exists(scriptPath)) File.Delete(scriptPath);
		}
	}

	private async Task<ShortComfyPathScope?> CreateShortComfyPathIfNeededAsync(
		string tag,
		string pythonExecutable,
		CancellationToken cancellationToken)
	{
		const int LongPathRiskThreshold = 40;
		if (ActiveComfyPath.Length < LongPathRiskThreshold)
		{
			return null;
		}

		CleanupStaleShortComfyPaths(tag);

		foreach (string containerRoot in GetShortComfyPathCandidates())
		{
			DeleteShortComfyPath(containerRoot);
			string shortComfyPath = Path.Combine(containerRoot, ShortComfyPathLinkName);
			Directory.CreateDirectory(containerRoot);
			WriteShortComfyPathMarker(containerRoot);

			var create = await ProcessRunner.RunAsync(
				"cmd.exe",
				$"/c mklink /J \"{shortComfyPath}\" \"{ActiveComfyPath}\"",
				null,
				null,
				cancellationToken,
				TimeSpan.FromSeconds(30));
			if (create.ExitCode != 0 || !Directory.Exists(shortComfyPath))
			{
				DeleteShortComfyPath(containerRoot);
				_log($"{tag} Short pip path candidate unavailable: {containerRoot}. {GetProcessFailureDetail(create)}");
				continue;
			}

			string pipPython = pythonExecutable;
			if (UsesActiveComfyVenv(pythonExecutable))
			{
				pipPython = Path.Combine(shortComfyPath, ActiveVenvRelativePythonExe);
				if (!File.Exists(pipPython))
				{
					DeleteShortComfyPath(containerRoot);
					_log($"{tag} Short pip path was created but .venv python was not found: {shortComfyPath}");
					continue;
				}
			}

			_log($"{tag} Using short pip install path to avoid Windows long-path extraction failures: {shortComfyPath}");
			return new ShortComfyPathScope(containerRoot, shortComfyPath, pipPython);
		}

		_log($"{tag} Short pip path unavailable. Continuing with normal path.");
		return null;
	}

	private static bool UsesActiveComfyVenv(string pythonExecutable)
	{
		try
		{
			string venvRoot = Path.GetFullPath(ActiveVenvPath)
				.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string resolvedPython = Path.GetFullPath(pythonExecutable);
			return resolvedPython.StartsWith(
				venvRoot + Path.DirectorySeparatorChar,
				StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private IEnumerable<string> GetShortComfyPathCandidates()
	{
		string suffix = Guid.NewGuid().ToString("N")[..8];
		string activePath = Path.GetFullPath(ActiveComfyPath);
		string? activeRoot = Path.GetPathRoot(activePath);
		if (!string.IsNullOrWhiteSpace(activeRoot))
		{
			yield return Path.Combine(activeRoot, $"{ShortComfyPathPrefix}{suffix}");

			string relative = Path.GetRelativePath(activeRoot, activePath);
			string? firstSegment = relative
				.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
				.FirstOrDefault();
			if (!string.IsNullOrWhiteSpace(firstSegment)
				&& firstSegment != "."
				&& firstSegment != "..")
			{
				yield return Path.Combine(activeRoot, firstSegment, $"{ShortComfyPathPrefix}{suffix}");
			}
		}

		string tempRoot = Path.GetTempPath();
		yield return Path.Combine(tempRoot, $"{ShortComfyPathPrefix}{suffix}");
	}

	private static void DeleteShortComfyPath(string linkPath)
	{
		try
		{
			if (Directory.Exists(linkPath))
			{
				DeleteShortComfyPathDirectory(linkPath);
			}
		}
		catch
		{
		}
	}

	internal void CleanupStaleShortComfyPaths(string tag)
	{
		foreach (string root in GetShortComfyPathCleanupRoots())
		{
			if (!Directory.Exists(root))
			{
				continue;
			}

			foreach (string candidate in EnumerateShortComfyPathCandidates(root))
			{
				if (!IsShortComfyPathCandidate(candidate))
				{
					continue;
				}

				if (!IsNexusShortComfyPathContainer(candidate))
				{
					continue;
				}

				try
				{
					DeleteShortComfyPathDirectory(candidate);
					_log($"{tag} Removed stale short pip install path: {candidate}");
				}
				catch (Exception ex)
				{
					_log($"{tag} Stale short pip install path cleanup skipped: {candidate}. {ex.Message}");
				}
			}
		}
	}

	private IEnumerable<string> EnumerateShortComfyPathCandidates(string root)
	{
		IEnumerable<string> candidates;
		try
		{
			candidates = Directory.EnumerateDirectories(root, $"{ShortComfyPathPrefix}????????", SearchOption.TopDirectoryOnly);
		}
		catch (Exception ex)
		{
			_log($"{RuntimeTag} Short pip path cleanup scan skipped for {root}: {ex.Message}");
			yield break;
		}

		foreach (string candidate in candidates)
		{
			yield return candidate;
		}
	}

	private IEnumerable<string> GetShortComfyPathCleanupRoots()
	{
		var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string candidate in GetShortComfyPathCandidateRoots())
		{
			if (Directory.Exists(candidate) && roots.Add(Path.GetFullPath(candidate)))
			{
				yield return candidate;
			}
		}
	}

	private IEnumerable<string> GetShortComfyPathCandidateRoots()
	{
		string activePath = Path.GetFullPath(ActiveComfyPath);
		string? activeRoot = Path.GetPathRoot(activePath);
		if (!string.IsNullOrWhiteSpace(activeRoot))
		{
			yield return activeRoot;

			string relative = Path.GetRelativePath(activeRoot, activePath);
			string? firstSegment = relative
				.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
				.FirstOrDefault();
			if (!string.IsNullOrWhiteSpace(firstSegment)
				&& firstSegment != "."
				&& firstSegment != "..")
			{
				yield return Path.Combine(activeRoot, firstSegment);
			}
		}

		yield return Path.GetTempPath();
	}

	private static bool IsShortComfyPathCandidate(string path)
	{
		string name = Path.GetFileName(path);
		if (name.Length != ShortComfyPathPrefix.Length + 8
			|| !name.StartsWith(ShortComfyPathPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		for (int i = ShortComfyPathPrefix.Length; i < name.Length; i++)
		{
			if (!Uri.IsHexDigit(name[i]))
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsNexusShortComfyPathContainer(string path)
	{
		if (IsReparsePoint(path))
		{
			return true;
		}

		string markerPath = Path.Combine(path, ShortComfyPathMarkerFileName);
		string shortComfyPath = Path.Combine(path, ShortComfyPathLinkName);
		return File.Exists(markerPath)
			&& Directory.Exists(shortComfyPath)
			&& IsReparsePoint(shortComfyPath);
	}

	private static void WriteShortComfyPathMarker(string containerRoot)
	{
		string markerPath = Path.Combine(containerRoot, ShortComfyPathMarkerFileName);
		string text = """
This folder was created temporarily by Nexus for ComfyUI.

It contains a short-path junction used while installing Python packages such as PyTorch.
The short path helps avoid Windows path-length extraction failures during pip installs.

Nexus removes this folder automatically after the install step finishes.
If Nexus or Windows terminated unexpectedly, it is safe to delete this folder when Nexus is not currently installing dependencies.
""";
		File.WriteAllText(markerPath, text);
	}

	private static void DeleteShortComfyPathDirectory(string path)
	{
		if (IsReparsePoint(path))
		{
			Directory.Delete(path);
			return;
		}

		foreach (string directory in Directory.EnumerateDirectories(path))
		{
			if (IsReparsePoint(directory))
			{
				Directory.Delete(directory);
			}
			else
			{
				Directory.Delete(directory, recursive: true);
			}
		}

		foreach (string file in Directory.EnumerateFiles(path))
		{
			File.SetAttributes(file, FileAttributes.Normal);
			File.Delete(file);
		}

		Directory.Delete(path);
	}

	private static bool IsReparsePoint(string path)
		=> (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

	private sealed class ShortComfyPathScope(string containerPath, string comfyPath, string pythonExecutable) : IDisposable
	{
		public string ContainerPath { get; } = containerPath;
		public string ComfyPath { get; } = comfyPath;
		public string PythonExecutable { get; } = pythonExecutable;

		public void Dispose()
		{
			DeleteShortComfyPath(ContainerPath);
		}
	}

	private static async Task<VenvSeedReadiness> EnsureSeedPythonReadinessAsync(
		string seedPython,
		string? seedWorkingDirectory,
		bool bootstrapPip,
		Action<string>? onLog,
		CancellationToken cancellationToken)
	{
		if (LooksLikePath(seedPython) && !File.Exists(seedPython))
		{
			return new VenvSeedReadiness(false, seedPython, $"Python executable was not found: {seedPython}");
		}

		var version = await ProcessRunner.RunAsync(
			seedPython,
			"--version",
			seedWorkingDirectory,
			onLog,
			cancellationToken,
			ReadinessIdleTimeout);
		if (version.ExitCode != 0)
		{
			return new VenvSeedReadiness(false, seedPython, $"Python is not executable: {GetProcessFailureDetail(version)}");
		}

		var modules = await ProcessRunner.RunAsync(
			seedPython,
			"-c \"import venv, ensurepip; print('venv-ready')\"",
			seedWorkingDirectory,
			onLog,
			cancellationToken,
			ReadinessIdleTimeout);
		if (modules.ExitCode != 0)
		{
			return new VenvSeedReadiness(
				false,
				seedPython,
				$"Python cannot create managed .venv because venv/ensurepip is unavailable: {GetProcessFailureDetail(modules)}");
		}

		var venvLayout = await VerifyWindowsVenvLayoutAsync(seedPython, seedWorkingDirectory, onLog, cancellationToken);
		if (!venvLayout.IsReady)
		{
			return venvLayout;
		}

		var pip = await ProcessRunner.RunAsync(
			seedPython,
			"-m pip --version",
			seedWorkingDirectory,
			onLog,
			cancellationToken,
			ReadinessIdleTimeout,
			CreatePipEnvironment(onLog));
		if (pip.ExitCode == 0)
		{
			return new VenvSeedReadiness(true, seedPython, "Python venv and pip are ready.");
		}

		if (!bootstrapPip)
		{
			return new VenvSeedReadiness(true, seedPython, "Python can create .venv; pip will be bootstrapped with ensurepip during setup.");
		}

		onLog?.Invoke($"{RuntimeTag} Seed Python pip is missing. Bootstrapping pip with ensurepip...");
		var ensurePip = await ProcessRunner.RunAsync(
			seedPython,
			"-m ensurepip --upgrade",
			seedWorkingDirectory,
			onLog,
			cancellationToken,
			ReadinessIdleTimeout,
			CreatePipEnvironment(onLog));
		if (ensurePip.ExitCode != 0)
		{
			return new VenvSeedReadiness(false, seedPython, $"Failed to bootstrap pip with ensurepip: {GetProcessFailureDetail(ensurePip)}");
		}

		var pipAfterBootstrap = await ProcessRunner.RunAsync(
			seedPython,
			"-m pip --version",
			seedWorkingDirectory,
			onLog,
			cancellationToken,
			ReadinessIdleTimeout,
			CreatePipEnvironment(onLog));
		return pipAfterBootstrap.ExitCode == 0
			? new VenvSeedReadiness(true, seedPython, "Python venv and pip are ready.")
			: new VenvSeedReadiness(false, seedPython, $"pip is still unavailable after ensurepip: {GetProcessFailureDetail(pipAfterBootstrap)}");
	}

	private static async Task<VenvSeedReadiness> VerifyWindowsVenvLayoutAsync(
		string seedPython,
		string? seedWorkingDirectory,
		Action<string>? onLog,
		CancellationToken cancellationToken)
	{
		string probePath = Path.Combine(Path.GetTempPath(), $"nexus-venv-probe-{Guid.NewGuid():N}");
		try
		{
			var probe = await ProcessRunner.RunAsync(
				seedPython,
				$"-m venv \"{probePath}\"",
				seedWorkingDirectory,
				onLog,
				cancellationToken,
				ReadinessIdleTimeout);
			if (probe.ExitCode != 0)
			{
				return new VenvSeedReadiness(false, seedPython, $"Python failed the managed .venv creation probe: {GetProcessFailureDetail(probe)}");
			}

			string windowsPython = Path.Combine(probePath, "Scripts", "python.exe");
			if (File.Exists(windowsPython))
			{
				return new VenvSeedReadiness(true, seedPython, "Python can create a Windows-compatible .venv.");
			}

			string posixPython = Path.Combine(probePath, "bin", "python.exe");
			string detail = File.Exists(posixPython)
				? "It created a POSIX/MSYS-style .venv with bin/python.exe instead of Scripts/python.exe."
				: "The expected Scripts/python.exe launcher was not created.";
			return new VenvSeedReadiness(
				false,
				seedPython,
				$"Selected Python is not compatible with Nexus managed Windows .venv. {detail} Use the bundled Python runtime or a Windows CPython installation.");
		}
		finally
		{
			if (Directory.Exists(probePath))
			{
				try
				{
					ClearReadOnlyAttributes(new DirectoryInfo(probePath));
					Directory.Delete(probePath, recursive: true);
				}
				catch
				{
				}
			}
		}
	}

	private static void ValidateActiveVenvLayout(string seedPython)
	{
		if (File.Exists(ActiveVenvPythonExe)) return;

		string posixPython = Path.Combine(ActiveVenvPath, "bin", "python.exe");
		string detail = File.Exists(posixPython)
			? "The selected Python created bin/python.exe instead of Scripts/python.exe."
			: "The expected Scripts/python.exe launcher was not created.";
		throw new InvalidOperationException(
			$"ComfyUI .venv was created with an unsupported layout. {detail} Seed Python: {seedPython}. Use the bundled Python runtime or a Windows CPython installation.");
	}

	private static string ResolveVenvSeedPython()
	{
		string configuredPython = SetupSettingsService.Instance.Settings.PythonPath;
		return string.IsNullOrWhiteSpace(configuredPython) ? ComfyInstallService.PythonExe : configuredPython;
	}

	private static string? ResolveVenvSeedWorkingDirectory(string pythonExecutable)
		=> File.Exists(pythonExecutable) ? Path.GetDirectoryName(pythonExecutable) : null;

	private static bool LooksLikePath(string pythonExecutable)
		=> pythonExecutable.Contains(Path.DirectorySeparatorChar)
			|| pythonExecutable.Contains(Path.AltDirectorySeparatorChar)
			|| Path.IsPathRooted(pythonExecutable);

	private static string GetProcessFailureDetail((int ExitCode, string Output, string Error) result)
	{
		string detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output.Trim() : result.Error.Trim();
		return string.IsNullOrWhiteSpace(detail)
			? $"ExitCode: {result.ExitCode}"
			: $"ExitCode: {result.ExitCode}. {detail}";
	}

	private void Report(double progress, string message)
	{
		_progress(Math.Clamp(progress, 0, 1), message);
		_log($"{RuntimeTag} {message}");
	}

	private static async Task<bool> TryDeleteComfyVenvDirectoryAsync(CancellationToken cancellationToken)
	{
		PythonRuntimeInfoService.Instance.Invalidate();
		int maxRetries = Math.Max(1, SetupSettingsService.Instance.Settings.PurgeRetryCount);
		for (int attempt = 0; attempt < maxRetries; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				return await Task.Run(() =>
				{
					if (!Directory.Exists(ActiveVenvPath)) return true;

					ClearReadOnlyAttributes(new DirectoryInfo(ActiveVenvPath));
					Directory.Delete(ActiveVenvPath, recursive: true);
					return true;
				}, cancellationToken);
			}
			catch (IOException) when (attempt < maxRetries - 1)
			{
				await Task.Delay(GetDeleteRetryDelay(), cancellationToken);
			}
			catch (UnauthorizedAccessException) when (attempt < maxRetries - 1)
			{
				await Task.Delay(GetDeleteRetryDelay(), cancellationToken);
			}
			catch (IOException)
			{
				return false;
			}
			catch (UnauthorizedAccessException)
			{
				return false;
			}
		}

		return !Directory.Exists(ActiveVenvPath);
	}

	private static SetupStepResult CreateVenvDeleteFailureResult(string action)
		=> new(
			false,
			$"Failed to {action} .venv because one or more files are locked. Stop the ComfyUI server and close Python processes, then retry.",
			0);

	private static TimeSpan GetDeleteRetryDelay()
		=> TimeSpan.FromMilliseconds(Math.Max(50, SetupSettingsService.Instance.Settings.PurgeRetryDelayMilliseconds));

	private static void ClearReadOnlyAttributes(DirectoryInfo directory)
	{
		if (!directory.Exists) return;

		foreach (var file in directory.GetFiles())
		{
			file.Attributes = FileAttributes.Normal;
		}

		foreach (var subDirectory in directory.GetDirectories())
		{
			ClearReadOnlyAttributes(subDirectory);
			subDirectory.Attributes = FileAttributes.Normal;
		}

		directory.Attributes = FileAttributes.Normal;
	}

	private static bool IsSafeComfyVenvPath(string path)
	{
		string fullVenvPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string fullComfyPath = Path.GetFullPath(ActiveComfyPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string expectedVenvName = Path.GetFileName(fullVenvPath);

		return string.Equals(expectedVenvName, ".venv", StringComparison.OrdinalIgnoreCase)
			&& fullVenvPath.StartsWith(fullComfyPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}
}
