namespace ComfyUI_Nexus.Setup.Services;

using System.Text;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;

internal sealed class ManagedCustomNodeDependencyInstaller
{
	private const string DependencyCacheDirectory = "Cache/ManagedDependencies";

	private readonly Action<string> _log;

	internal ManagedCustomNodeDependencyInstaller(Action<string> log)
	{
		_log = log;
	}

	internal async Task<SetupStepResult> InstallAsync(
		IEnumerable<CustomNodeSetting> nodes,
		string customNodesPath,
		Action<ManagedCustomNodeDependencyProgress>? onProgress,
		CancellationToken cancellationToken)
	{
		var targets = nodes
			.Where(node => !string.Equals(node.EffectiveInstallMode, CustomNodeInstallModes.Repository, StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (targets.Count == 0)
		{
			return new SetupStepResult(true, "Managed custom node dependencies are ready.", 1);
		}

		onProgress?.Invoke(new ManagedCustomNodeDependencyProgress(
			"Python Runtime",
			ManagedCustomNodeDependencyStage.ResolvePythonRuntime));
		PythonRuntimeInfo? runtimeInfo = await PythonRuntimeInfoService.Instance.GetCurrentAsync(cancellationToken);
		if (runtimeInfo is null)
		{
			return new SetupStepResult(false, $"Python runtime is unavailable for managed custom node dependencies: {RuntimeRepairTarget.GetPythonExecutable()}", 0);
		}
		_log($"[Node Dependencies] Python runtime: {runtimeInfo.Version} ({runtimeInfo.PythonAbi}/{runtimeInfo.Platform})");

		foreach (CustomNodeSetting node in targets)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string installMode = node.EffectiveInstallMode;
			if (!CustomNodeInstallModes.IsKnown(installMode))
			{
				return new SetupStepResult(false, $"Managed custom node '{node.Folder}' has an unknown install mode: {installMode}", 0);
			}

			string nodePath = Path.Combine(customNodesPath, node.Folder);
			if (!Directory.Exists(nodePath))
			{
				return new SetupStepResult(false, $"Managed custom node directory is missing: {node.Folder}", 0);
			}

			if (string.Equals(installMode, CustomNodeInstallModes.Bootstrap, StringComparison.OrdinalIgnoreCase))
			{
				var dependencyResult = await InstallDependencyPlanAsync(node, nodePath, runtimeInfo, onProgress, cancellationToken);
				if (!dependencyResult.IsSuccess)
				{
					return dependencyResult;
				}
			}

			if (string.Equals(installMode, CustomNodeInstallModes.Requirements, StringComparison.OrdinalIgnoreCase)
				|| (string.Equals(installMode, CustomNodeInstallModes.Bootstrap, StringComparison.OrdinalIgnoreCase) && node.Dependencies?.Requirements == true))
			{
				var requirementsResult = await InstallRequirementsFileAsync(node, nodePath, runtimeInfo.ExecutablePath, onProgress, cancellationToken);
				if (!requirementsResult.IsSuccess)
				{
					return requirementsResult;
				}
			}

			if (string.Equals(installMode, CustomNodeInstallModes.Bootstrap, StringComparison.OrdinalIgnoreCase))
			{
				var verifyResult = await VerifyImportsAsync(node, nodePath, runtimeInfo.ExecutablePath, onProgress, cancellationToken);
				if (!verifyResult.IsSuccess)
				{
					return verifyResult;
				}
			}
		}

		return new SetupStepResult(true, "Managed custom node dependencies are ready.", 1);
	}

	internal static int GetProgressStepCount(IEnumerable<CustomNodeSetting> nodes)
	{
		var targets = nodes
			.Where(node => !string.Equals(node.EffectiveInstallMode, CustomNodeInstallModes.Repository, StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (targets.Count == 0)
		{
			return 0;
		}

		int count = 1; // Python runtime resolution.
		foreach (CustomNodeSetting node in targets)
		{
			if (string.Equals(node.EffectiveInstallMode, CustomNodeInstallModes.Requirements, StringComparison.OrdinalIgnoreCase))
			{
				count++;
				continue;
			}

			if (!string.Equals(node.EffectiveInstallMode, CustomNodeInstallModes.Bootstrap, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			CustomNodeDependencyPlan? plan = node.Dependencies;
			count += plan?.Wheels?
				.GroupBy(wheel => wheel.Package, StringComparer.OrdinalIgnoreCase)
				.Count() * 2 ?? 0;
			count += plan?.Files?.Count ?? 0;
			if (plan?.Requirements == true)
			{
				count++;
			}
			if (plan?.VerifyImports is { Count: > 0 })
			{
				count++;
			}
		}

		return count;
	}

	private async Task<SetupStepResult> InstallDependencyPlanAsync(
		CustomNodeSetting node,
		string nodePath,
		PythonRuntimeInfo runtimeInfo,
		Action<ManagedCustomNodeDependencyProgress>? onProgress,
		CancellationToken cancellationToken)
	{
		CustomNodeDependencyPlan? plan = node.Dependencies;
		if (plan is null)
		{
			return new SetupStepResult(true, "No custom dependency plan.", 1);
		}

		string tag = $"[Node Dependencies] [{node.Folder}]";
		var wheelResult = await InstallWheelsAsync(node, nodePath, plan.Wheels, runtimeInfo, tag, onProgress, cancellationToken);
		if (!wheelResult.IsSuccess)
		{
			return wheelResult;
		}

		return await InstallFilesAsync(node, nodePath, plan.Files, tag, onProgress, cancellationToken);
	}

	private async Task<SetupStepResult> InstallWheelsAsync(
		CustomNodeSetting node,
		string nodePath,
		IReadOnlyList<CustomNodeWheelSetting>? wheels,
		PythonRuntimeInfo runtimeInfo,
		string tag,
		Action<ManagedCustomNodeDependencyProgress>? onProgress,
		CancellationToken cancellationToken)
	{
		if (wheels is not { Count: > 0 })
		{
			return new SetupStepResult(true, "No wheel dependencies.", 1);
		}

		string cacheRoot = Path.Combine(
			ComfyInstallService.GetLocalRuntimePath(DependencyCacheDirectory),
			SanitizePathSegment(node.Folder));
		Directory.CreateDirectory(cacheRoot);

		foreach (IGrouping<string, CustomNodeWheelSetting> packageWheels in wheels.GroupBy(wheel => wheel.Package, StringComparer.OrdinalIgnoreCase))
		{
			CustomNodeWheelSetting? wheel = packageWheels.SingleOrDefault(candidate =>
				string.Equals(candidate.PythonAbi, runtimeInfo.PythonAbi, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(candidate.Platform, runtimeInfo.Platform, StringComparison.OrdinalIgnoreCase));
			if (wheel is null)
			{
				string supported = string.Join(", ", packageWheels.Select(candidate => $"{candidate.PythonAbi}/{candidate.Platform}").Distinct(StringComparer.OrdinalIgnoreCase));
				return new SetupStepResult(
					false,
					$"Managed custom node '{node.Folder}' has no {packageWheels.Key} wheel for Python {runtimeInfo.PythonAbi} {runtimeInfo.Platform}. Supported: {supported}.",
					0);
			}

			if (!IsSafeCacheFileName(wheel.CacheFile))
			{
				return new SetupStepResult(false, $"Managed custom node '{node.Folder}' has an unsafe wheel cache filename: {wheel.CacheFile}", 0);
			}

			string wheelPath = Path.Combine(cacheRoot, wheel.CacheFile);
			try
			{
				onProgress?.Invoke(new ManagedCustomNodeDependencyProgress(node.Folder, ManagedCustomNodeDependencyStage.DownloadWheel, wheel.Package));
				_log($"{tag} Downloading {wheel.Package} wheel for {runtimeInfo.PythonAbi}/{runtimeInfo.Platform}...");
				await DownloadService.DownloadFileAsync(
					wheel.Url,
					wheelPath,
					onProgress: null,
					Math.Max(1024, SetupSettingsService.Instance.Settings.DownloadBufferSize),
					cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				return new SetupStepResult(false, $"Failed to download {wheel.Package} wheel for managed custom node '{node.Folder}': {ex.Message}", 0);
			}

			if (!IsNonEmptyFile(wheelPath))
			{
				return new SetupStepResult(false, $"Downloaded {wheel.Package} wheel is missing or empty for managed custom node '{node.Folder}'.", 0);
			}

			onProgress?.Invoke(new ManagedCustomNodeDependencyProgress(node.Folder, ManagedCustomNodeDependencyStage.InstallWheel, wheel.Package));
			_log($"{tag} Installing precompiled {wheel.Package} wheel...");
			var result = await ProcessRunner.RunAsync(
				runtimeInfo.ExecutablePath,
				$"-m pip install --no-deps --force-reinstall \"{wheelPath}\"",
				nodePath,
				_log,
				cancellationToken,
				environmentVariables: CreatePipEnvironment(),
				outputEncoding: Encoding.UTF8);
			if (result.ExitCode != 0)
			{
				return new SetupStepResult(false, $"Failed to install precompiled {wheel.Package} for managed custom node '{node.Folder}': {GetFailureDetail(result)}", 0);
			}
		}

		return new SetupStepResult(true, "Wheel dependencies are ready.", 1);
	}

	private async Task<SetupStepResult> InstallFilesAsync(
		CustomNodeSetting node,
		string nodePath,
		IReadOnlyList<CustomNodeFileSetting>? files,
		string tag,
		Action<ManagedCustomNodeDependencyProgress>? onProgress,
		CancellationToken cancellationToken)
	{
		if (files is not { Count: > 0 })
		{
			return new SetupStepResult(true, "No file dependencies.", 1);
		}

		for (int index = 0; index < files.Count; index++)
		{
			CustomNodeFileSetting file = files[index];
			if (!TryResolveNodeRelativePath(nodePath, file.Path, out string? destinationPath))
			{
				return new SetupStepResult(false, $"Managed custom node '{node.Folder}' has an unsafe dependency path: {file.Path}", 0);
			}

			try
			{
				onProgress?.Invoke(new ManagedCustomNodeDependencyProgress(
					node.Folder,
					ManagedCustomNodeDependencyStage.DownloadFile,
					file.Path,
					index + 1,
					files.Count));
				_log($"{tag} Downloading {file.Path}...");
				await DownloadService.DownloadFileAsync(
					file.Url,
					destinationPath!,
					onProgress: null,
					Math.Max(1024, SetupSettingsService.Instance.Settings.DownloadBufferSize),
					cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				return new SetupStepResult(false, $"Failed to download dependency file '{file.Path}' for managed custom node '{node.Folder}': {ex.Message}", 0);
			}

			if (!IsNonEmptyFile(destinationPath!))
			{
				return new SetupStepResult(false, $"Downloaded dependency file '{file.Path}' is missing or empty for managed custom node '{node.Folder}'.", 0);
			}
		}

		return new SetupStepResult(true, "File dependencies are ready.", 1);
	}

	private async Task<SetupStepResult> InstallRequirementsFileAsync(
		CustomNodeSetting node,
		string nodePath,
		string pythonExecutable,
		Action<ManagedCustomNodeDependencyProgress>? onProgress,
		CancellationToken cancellationToken)
	{
		string requirementsPath = Path.Combine(nodePath, "requirements.txt");
		onProgress?.Invoke(new ManagedCustomNodeDependencyProgress(node.Folder, ManagedCustomNodeDependencyStage.InstallRequirements));
		if (!File.Exists(requirementsPath))
		{
			return new SetupStepResult(true, "No requirements.txt file.", 1);
		}

		string tag = $"[Node Dependencies] [{node.Folder}]";
		_log($"{tag} Installing requirements.txt...");
		var pipEnvironment = CreatePipEnvironment();
		_log(pipEnvironment.TryGetValue(PipCacheService.EnvironmentVariableName, out string? cachePath)
			? $"{tag} pip cache: {cachePath}"
			: $"{tag} pip cache: pip default");
		var result = await ProcessRunner.RunAsync(
			pythonExecutable,
			$"-m pip install -r \"{requirementsPath}\"",
			nodePath,
			_log,
			cancellationToken,
			environmentVariables: pipEnvironment,
			outputEncoding: Encoding.UTF8);
		return result.ExitCode == 0
			? new SetupStepResult(true, "requirements.txt is ready.", 1)
			: new SetupStepResult(false, $"Failed to install requirements for managed custom node '{node.Folder}': {GetFailureDetail(result)}", 0);
	}

	private async Task<SetupStepResult> VerifyImportsAsync(
		CustomNodeSetting node,
		string nodePath,
		string pythonExecutable,
		Action<ManagedCustomNodeDependencyProgress>? onProgress,
		CancellationToken cancellationToken)
	{
		IReadOnlyList<string>? imports = node.Dependencies?.VerifyImports;
		if (imports is not { Count: > 0 })
		{
			return new SetupStepResult(true, "No dependency import verification.", 1);
		}

		if (imports.Any(importName => !IsValidImportName(importName)))
		{
			return new SetupStepResult(false, $"Managed custom node '{node.Folder}' has an invalid dependency import verification entry.", 0);
		}

		string tag = $"[Node Dependencies] [{node.Folder}]";
		onProgress?.Invoke(new ManagedCustomNodeDependencyProgress(node.Folder, ManagedCustomNodeDependencyStage.VerifyImports));
		_log($"{tag} Verifying installed imports...");
		var result = await ProcessRunner.RunAsync(
			pythonExecutable,
			$"-c \"import {string.Join(", ", imports)}\"",
			nodePath,
			_log,
			cancellationToken,
			environmentVariables: CreatePipEnvironment(),
			outputEncoding: Encoding.UTF8);
		return result.ExitCode == 0
			? new SetupStepResult(true, "Dependency imports are ready.", 1)
			: new SetupStepResult(false, $"Managed custom node dependency import verification failed for '{node.Folder}': {GetFailureDetail(result)}", 0);
	}

	private static bool TryResolveNodeRelativePath(string nodePath, string relativePath, out string? destinationPath)
	{
		destinationPath = null;
		if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
		{
			return false;
		}

		string root = Path.GetFullPath(nodePath);
		string candidate = Path.GetFullPath(Path.Combine(root, relativePath));
		string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
		if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		destinationPath = candidate;
		return true;
	}

	private static bool IsSafeCacheFileName(string fileName)
		=> !string.IsNullOrWhiteSpace(fileName)
			&& !Path.IsPathRooted(fileName)
			&& string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
			&& fileName.EndsWith(".whl", StringComparison.OrdinalIgnoreCase);

	private static bool IsNonEmptyFile(string path)
		=> File.Exists(path) && new FileInfo(path).Length > 0;

	private static bool IsValidImportName(string importName)
		=> !string.IsNullOrWhiteSpace(importName)
			&& importName.All(character => char.IsLetterOrDigit(character) || character is '_' or '.');

	private static string SanitizePathSegment(string value)
		=> string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

	private static string GetFailureDetail((int ExitCode, string Output, string Error) result)
	{
		string detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output.Trim() : result.Error.Trim();
		return string.IsNullOrWhiteSpace(detail) ? $"ExitCode: {result.ExitCode}" : detail;
	}

	private static IReadOnlyDictionary<string, string> CreatePipEnvironment()
	{
		var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			// Native build metadata must not inherit the Windows ANSI code page.
			["PYTHONUTF8"] = "1",
			["PYTHONIOENCODING"] = "utf-8"
		};

		if (PipCacheService.CreateEnvironment() is { } cacheEnvironment)
		{
			foreach (var (key, value) in cacheEnvironment)
			{
				environment[key] = value;
			}
		}

		return environment;
	}
}

internal enum ManagedCustomNodeDependencyStage
{
	ResolvePythonRuntime,
	DownloadWheel,
	InstallWheel,
	InstallRequirements,
	DownloadFile,
	VerifyImports
}

internal sealed record ManagedCustomNodeDependencyProgress(
	string NodeFolder,
	ManagedCustomNodeDependencyStage Stage,
	string? ItemName = null,
	int ItemIndex = 0,
	int ItemCount = 0);
