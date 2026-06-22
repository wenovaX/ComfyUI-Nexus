namespace ComfyUI_Nexus.Setup.Services;

using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;

internal sealed record VenvSeedReadiness(bool IsReady, string PythonExecutable, string Message);

internal sealed class ComfyVenvManager
{
	private const string RuntimeTag = "[Runtime]";
	private static readonly TimeSpan DependencyIdleTimeout = TimeSpan.FromMinutes(10);
	private static readonly TimeSpan ReadinessIdleTimeout = TimeSpan.FromSeconds(30);

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
			ComfyInstallService.EnsureComfyWorkspaceDirectories(ComfyInstallService.ComfyPath);
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
			ComfyInstallService.EnsureComfyWorkspaceDirectories(ComfyInstallService.ComfyPath);

			if (Directory.Exists(ComfyInstallService.ComfyVenvPath))
			{
				if (!IsSafeComfyVenvPath(ComfyInstallService.ComfyVenvPath))
				{
					return new SetupStepResult(false, $"Refusing to delete unsafe venv path: {ComfyInstallService.ComfyVenvPath}", 0);
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
			if (!Directory.Exists(ComfyInstallService.ComfyVenvPath))
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

			if (!IsSafeComfyVenvPath(ComfyInstallService.ComfyVenvPath))
			{
				return new SetupStepResult(false, $"Refusing to delete unsafe venv path: {ComfyInstallService.ComfyVenvPath}", 0);
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
			if (!Directory.Exists(ComfyInstallService.ComfyVenvPath))
			{
				settings.PendingVenvDelete = false;
				SetupSettingsService.Instance.Save();
				_log($"{RuntimeTag} Pending .venv delete cleared. .venv is already absent.");
				return new SetupStepResult(true, "Pending .venv delete cleared.", 1);
			}

			if (!IsSafeComfyVenvPath(ComfyInstallService.ComfyVenvPath))
			{
				return new SetupStepResult(false, $"Refusing to delete unsafe venv path: {ComfyInstallService.ComfyVenvPath}", 0);
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
		if (File.Exists(ComfyInstallService.ComfyVenvPythonExe)) return;

		string seedPython = ResolveVenvSeedPython();
		string? seedWorkingDirectory = ResolveVenvSeedWorkingDirectory(seedPython);
		_log($"{tag} Venv seed Python: {seedPython}");
		Report(0.10, $"Checking seed Python for .venv support: {seedPython}");

		var readiness = await EnsureSeedPythonReadinessAsync(seedPython, seedWorkingDirectory, bootstrapPip: true, _log, cancellationToken);
		if (!readiness.IsReady)
		{
			throw new InvalidOperationException(readiness.Message);
		}

		Report(0.18, "Creating ComfyUI .venv folder and Python launcher...");
		var pipEnvironment = CreatePipEnvironment(tag);
		var result = await ProcessRunner.RunAsync(
			seedPython,
			$"-m venv \"{ComfyInstallService.ComfyVenvPath}\"",
			seedWorkingDirectory,
			_log,
			cancellationToken,
			environmentVariables: pipEnvironment);
		if (result.ExitCode != 0)
		{
			throw new InvalidOperationException($"Failed to create ComfyUI venv: {result.Error}");
		}
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
			ComfyInstallService.EnsureComfyWorkspaceDirectories(ComfyInstallService.ComfyPath);
			string pythonExecutable = RuntimeRepairTarget.GetPythonExecutable();
			string runtimeLabel = RuntimeRepairTarget.GetLabel();

			if (RuntimeRepairTarget.IsUsingVenv())
			{
				await EnsureAsync(RuntimeTag, cancellationToken);
				pythonExecutable = ComfyInstallService.ComfyVenvPythonExe;
			}

			_log($"{RuntimeTag} Repair target: {runtimeLabel}");
			_log($"{RuntimeTag} Repair Python: {pythonExecutable}");

			await InstallComfyRequirementsAsync(RuntimeTag, pythonExecutable, cancellationToken);

			var cudaResult = await UpgradeToCudaAsync(pythonExecutable, cancellationToken);
			if (!cudaResult.IsSuccess) return cudaResult;

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
			await InstallComfyRequirementsAsync(RuntimeTag, ComfyInstallService.ComfyVenvPythonExe, cancellationToken);

			var cudaResult = await UpgradeToCudaAsync(ComfyInstallService.ComfyVenvPythonExe, cancellationToken);
			if (!cudaResult.IsSuccess) return cudaResult;

			return new SetupStepResult(true, "ComfyUI .venv dependencies and CUDA environment are ready.", 1);
		}
		catch (Exception ex)
		{
			return new SetupStepResult(false, $"ComfyUI .venv dependency repair failed: {ex.Message}", 0);
		}
	}

	private IReadOnlyDictionary<string, string>? CreatePipEnvironment(string tag)
	{
		var environment = PipCacheService.CreateEnvironment();
		_log(environment is null
			? $"{tag} pip cache: pip default"
			: $"{tag} pip cache: {environment[PipCacheService.EnvironmentVariableName]}");
		return environment;
	}

	private static IReadOnlyDictionary<string, string>? CreatePipEnvironment(Action<string>? onLog)
	{
		var environment = PipCacheService.CreateEnvironment();
		onLog?.Invoke(environment is null
			? $"{RuntimeTag} pip cache: pip default"
			: $"{RuntimeTag} pip cache: {environment[PipCacheService.EnvironmentVariableName]}");
		return environment;
	}

	private async Task InstallComfyRequirementsAsync(string tag, string pythonExecutable, CancellationToken cancellationToken)
	{
		string requirementsPath = Path.Combine(ComfyInstallService.ComfyPath, "requirements.txt");
		if (!File.Exists(requirementsPath))
		{
			_log($"{tag} requirements.txt was not found. Skipping dependency sync.");
			return;
		}

		_log($"{tag} Syncing ComfyUI requirements into: {pythonExecutable}");
		Report(0.38, "Installing ComfyUI requirements into .venv. A fresh setup may take 10-20 minutes.");
		var pipEnvironment = CreatePipEnvironment(tag);
		var result = await ProcessRunner.RunAsync(
			pythonExecutable,
			$"-m pip install -r \"{requirementsPath}\"",
			ComfyInstallService.ComfyPath,
			_log,
			cancellationToken,
			DependencyIdleTimeout,
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
		Report(0.68, "Installing CUDA PyTorch packages. This can download and unpack several GB.");
		var pipEnvironment = CreatePipEnvironment(RuntimeTag);

		var uninstall = await ProcessRunner.RunAsync(
			pythonExecutable,
			$"-m pip uninstall {settings.TorchPackages} -y",
			ComfyInstallService.ComfyPath,
			_log,
			cancellationToken,
			environmentVariables: pipEnvironment);
		if (uninstall.ExitCode != 0)
		{
			return new SetupStepResult(false, $"Failed to remove existing PyTorch packages: {uninstall.Error}", 0);
		}

		var install = await ProcessRunner.RunAsync(
			pythonExecutable,
			$"-m pip install {settings.TorchPackages} --index-url {settings.PyTorchIndexUrl}",
			ComfyInstallService.ComfyPath,
			_log,
			cancellationToken,
			DependencyIdleTimeout,
			pipEnvironment);
		if (install.ExitCode != 0)
		{
			return new SetupStepResult(false, $"Failed to install CUDA PyTorch packages: {install.Error}", 0);
		}

		Report(0.90, "Verifying GPU runtime after PyTorch install...");
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
			var (exitCode, output, error) = await ProcessRunner.RunAsync(pythonExecutable, $"\"{scriptPath}\"", ComfyInstallService.ComfyPath, _log, cancellationToken);

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
		int maxRetries = Math.Max(1, SetupSettingsService.Instance.Settings.PurgeRetryCount);
		for (int attempt = 0; attempt < maxRetries; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				return await Task.Run(() =>
				{
					if (!Directory.Exists(ComfyInstallService.ComfyVenvPath)) return true;

					ClearReadOnlyAttributes(new DirectoryInfo(ComfyInstallService.ComfyVenvPath));
					Directory.Delete(ComfyInstallService.ComfyVenvPath, recursive: true);
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

		return !Directory.Exists(ComfyInstallService.ComfyVenvPath);
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
		string fullComfyPath = Path.GetFullPath(ComfyInstallService.ComfyPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string expectedVenvName = Path.GetFileName(fullVenvPath);

		return string.Equals(expectedVenvName, ".venv", StringComparison.OrdinalIgnoreCase)
			&& fullVenvPath.StartsWith(fullComfyPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}
}
