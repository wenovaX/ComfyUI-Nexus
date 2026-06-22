namespace ComfyUI_Nexus.Setup.Services;

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;

internal sealed class ExtensionInstaller
{
	private const string ManagerTag = "[Manager]";

	private readonly Action<string> _log;
	private readonly Action<double, string> _progress;
	private readonly GitRepositoryService _gitRepositoryService;

	internal ExtensionInstaller(
		Action<string> log,
		Action<double, string> progress,
		GitRepositoryService gitRepositoryService)
	{
		_log = log;
		_progress = progress;
		_gitRepositoryService = gitRepositoryService;
	}

	internal async Task<SetupStepResult> InstallManagerAsync(CancellationToken cancellationToken)
		=> await InstallManagedExtensionsAsync(null, forceSyncExisting: false, reinstallExisting: false, cancellationToken);

	internal async Task<SetupStepResult> InstallManagedExtensionsAsync(
		IEnumerable<string>? targetFolders,
		bool forceSyncExisting,
		bool reinstallExisting,
		CancellationToken cancellationToken,
		bool installNodeDependencies = true)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			var settings = SetupSettingsService.Instance.Settings;
			var (gitExe, _) = await _gitRepositoryService.ResolveConfiguredGitAsync(cancellationToken);
			if (gitExe == null) return new SetupStepResult(false, "Git is required for ComfyUI-Manager installation.", 0);

			var targets = targetFolders?
				.Where(target => !string.IsNullOrWhiteSpace(target))
				.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			bool installAll = targets.Count == 0;
			string managerPath = Path.Combine(ComfyInstallService.ComfyPath, "custom_nodes", "ComfyUI-Manager");
			if (installAll || targets.Contains("ComfyUI-Manager"))
			{
				DeleteExistingExtensionIfRequested(managerPath, reinstallExisting);
				_progress(0.1, $"{ManagerTag} Syncing ComfyUI-Manager...");
				var managerResult = await _gitRepositoryService.EnsureRepoSyncedAsync(
					gitExe,
					settings.ManagerRepoUrl,
					managerPath,
					ManagerTag,
					forceSyncExisting,
					cancellationToken);
				if (!managerResult.IsSuccess) return managerResult;
			}

			int nodeCount = settings.EssentialNodes.Count(node => installAll || targets.Contains(node.Folder));
			int i = 0;
			foreach (var node in settings.EssentialNodes)
			{
				if (!installAll && !targets.Contains(node.Folder))
				{
					continue;
				}

				string nodeTag = $"[Node:{i + 1}/{nodeCount}]";
				string targetPath = Path.Combine(ComfyInstallService.ComfyPath, "custom_nodes", node.Folder);
				bool shouldInstallScripts = !Directory.Exists(targetPath);
				DeleteExistingExtensionIfRequested(targetPath, reinstallExisting);
				shouldInstallScripts = shouldInstallScripts || reinstallExisting;

				_progress(0.2 + (i * 0.1), $"{nodeTag} Checking {node.Folder}...");
				var nodeResult = await _gitRepositoryService.EnsureRepoSyncedAsync(
					gitExe,
					node.Url,
					targetPath,
					nodeTag,
					forceSyncExisting,
					cancellationToken);
				if (!nodeResult.IsSuccess) return nodeResult;

				if (installNodeDependencies && (shouldInstallScripts || forceSyncExisting))
				{
					await ExecuteNodeInstallScriptAsync(targetPath, nodeTag, cancellationToken);
				}

				i++;
			}

			if (installAll || targets.Contains("ComfyUI-HUD"))
			{
				var bridgeResult = await ComfyInstallService.Instance.InstallHudBridgeAsync(forceSyncExisting || reinstallExisting, cancellationToken);
				if (!bridgeResult.IsSuccess) return bridgeResult;
			}

			return new SetupStepResult(true, "Nexus-managed extensions are ready.", 1);
		}
		catch (Exception ex)
		{
			return new SetupStepResult(false, $"Manager install failed: {ex.Message}", 0);
		}
	}

	private void DeleteExistingExtensionIfRequested(string path, bool reinstallExisting)
	{
		if (!reinstallExisting || !Directory.Exists(path))
		{
			return;
		}

		_log($"[Extension] Reinstall requested. Removing existing folder: {path}");
		Directory.Delete(path, recursive: true);
	}

	private async Task ExecuteNodeInstallScriptAsync(string nodePath, string tag, CancellationToken cancellationToken)
	{
		string requirementsPath = Path.Combine(nodePath, "requirements.txt");
		string installScriptPath = Path.Combine(nodePath, "install.py");
		string pythonExecutable = RuntimeRepairTarget.GetPythonExecutable();

		if (File.Exists(requirementsPath))
		{
			_log($"{tag} Installing node dependencies (requirements.txt)...");
			var pipEnvironment = PipCacheService.CreateEnvironment();
			_log(pipEnvironment is null
				? $"{tag} pip cache: pip default"
				: $"{tag} pip cache: {pipEnvironment[PipCacheService.EnvironmentVariableName]}");
			await ProcessRunner.RunAsync(
				pythonExecutable,
				$"-m pip install -r \"{requirementsPath}\"",
				nodePath,
				_log,
				cancellationToken,
				environmentVariables: pipEnvironment);
		}

		if (File.Exists(installScriptPath))
		{
			_log($"{tag} Running node install script (install.py)...");
			await ProcessRunner.RunAsync(
				pythonExecutable,
				$"\"{installScriptPath}\"",
				nodePath,
				_log,
				cancellationToken);
		}
	}
}
