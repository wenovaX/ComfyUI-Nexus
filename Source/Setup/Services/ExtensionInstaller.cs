namespace ComfyUI_Nexus.Setup.Services;

using System.Linq;
using System.Threading.Tasks;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;

internal sealed class ExtensionInstaller
{
	private const string ExtensionStepPrefix = "Extensions";

	private readonly Action<string> _log;
	private readonly Action<double, string> _progress;
	private readonly GitRepositoryService _gitRepositoryService;
	private readonly ManagedCustomNodeDependencyInstaller _dependencyInstaller;
	private readonly NexusToolingEnvironment _tooling;
	private readonly ComfyInstallService _comfyInstall;
	private readonly SetupSettingsService _settingsService;
	private readonly NexusComfyRuntimePaths _paths;

	internal ExtensionInstaller(
		Action<string> log,
		Action<double, string> progress,
		GitRepositoryService gitRepositoryService,
		NexusToolingEnvironment tooling,
		ComfyInstallService comfyInstall,
		SetupSettingsService settingsService,
		NexusComfyRuntimePaths paths)
	{
		_log = log;
		_progress = progress;
		_gitRepositoryService = gitRepositoryService;
		_tooling = tooling;
		_comfyInstall = comfyInstall;
		_settingsService = settingsService;
		_paths = paths;
		_dependencyInstaller = new ManagedCustomNodeDependencyInstaller(log, tooling, settingsService, paths);
	}

	internal async Task<SetupStepResult> InstallManagerAsync(CancellationToken cancellationToken)
		=> await InstallManagedExtensionsAsync(null, forceSyncExisting: false, reinstallExisting: false, cancellationToken);

	internal async Task<SetupStepResult> InstallManagedExtensionsAsync(
		IEnumerable<string>? targetFolders,
		bool forceSyncExisting,
		bool reinstallExisting,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			var settings = _settingsService.Settings;
			var (gitExe, _) = await _gitRepositoryService.ResolveConfiguredGitAsync(settings.GitPath, cancellationToken);
			if (gitExe == null) return new SetupStepResult(false, "Git is required for ComfyUI-Manager installation.", 0);

			var targets = targetFolders?
				.Where(target => !string.IsNullOrWhiteSpace(target))
				.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			bool installAll = targets.Count == 0;
			NexusRuntimeToolingLease? lease = _tooling.CurrentLease;
			string toolingComfyPath = lease?.GetComfyRoot() ?? _paths.ActiveComfyPath;
			string customNodesPath = Path.Combine(toolingComfyPath, "custom_nodes");
			string managerPath = Path.Combine(customNodesPath, HudBridgeInstaller.ManagerExtensionFolderName);
			var selectedNodes = settings.EssentialNodes
				.Where(node => installAll || targets.Contains(node.Folder))
				.ToList();
			bool shouldInstallHud = installAll || targets.Contains(HudBridgeInstaller.HudExtensionFolderName);
			bool shouldPatchBridgeOnly = !shouldInstallHud && targets.Contains(HudBridgeInstaller.NexusBridgeExtensionFolderName);
			int dependencySteps = ManagedCustomNodeDependencyInstaller.GetProgressStepCount(selectedNodes);
			int totalItems =
				(installAll || targets.Contains(HudBridgeInstaller.ManagerExtensionFolderName) ? 1 : 0) +
				selectedNodes.Count +
				(shouldInstallHud || shouldPatchBridgeOnly ? 1 : 0) +
				dependencySteps;
			totalItems = Math.Max(1, totalItems);
			int completedItems = 0;
			GitRecoveryMode recoveryMode = ResolveRecoveryMode(forceSyncExisting, reinstallExisting);

			if (installAll || targets.Contains(HudBridgeInstaller.ManagerExtensionFolderName))
			{
				string managerTag = CreateStepTag(++completedItems, totalItems, "Manager");
				_progress(ProgressBefore(completedItems, totalItems), $"{managerTag} Syncing ComfyUI-Manager...");
				var managerResult = await _gitRepositoryService.EnsureRepositoryAsync(
					gitExe,
					settings.ManagerRepoUrl,
					managerPath,
					managerTag,
					recoveryMode,
					cancellationToken);
				if (!managerResult.IsSuccess) return managerResult;
				_progress(ProgressAfter(completedItems, totalItems), $"{managerTag} Ready.");
			}

			foreach (var node in selectedNodes)
			{
				string nodeTag = CreateStepTag(++completedItems, totalItems, node.Folder);
				string targetPath = Path.Combine(customNodesPath, node.Folder);

				_progress(ProgressBefore(completedItems, totalItems), $"{nodeTag} Checking {node.Folder}...");
				var nodeResult = await _gitRepositoryService.EnsureRepositoryAsync(
					gitExe,
					node.Url,
					targetPath,
					nodeTag,
					recoveryMode,
					cancellationToken);
				if (!nodeResult.IsSuccess) return nodeResult;

				_progress(ProgressAfter(completedItems, totalItems), $"{nodeTag} Ready.");
			}

			if (selectedNodes.Count > 0)
			{
				var dependencyResult = await _dependencyInstaller.InstallAsync(
					selectedNodes,
					customNodesPath,
					progress =>
					{
						string dependencyTag = CreateStepTag(++completedItems, totalItems, progress.NodeFolder);
						_progress(ProgressBefore(completedItems, totalItems), $"{dependencyTag} {FormatDependencyProgress(progress)}");
					},
					cancellationToken);
				if (!dependencyResult.IsSuccess) return dependencyResult;
				if (dependencySteps > 0)
				{
					_progress(ProgressAfter(completedItems, totalItems), $"{CreateStepTag(completedItems, totalItems, "Dependencies")} Ready.");
				}
			}

			if (shouldInstallHud)
			{
				string hudTag = CreateStepTag(++completedItems, totalItems, "HUD / Nexus Bridge");
				_progress(ProgressBefore(completedItems, totalItems), $"{hudTag} Syncing HUD and Nexus Bridge...");
				var bridgeResult = await _comfyInstall.InstallHudBridgeAsync(recoveryMode, cancellationToken);
				if (!bridgeResult.IsSuccess) return bridgeResult;
				_progress(ProgressAfter(completedItems, totalItems), $"{hudTag} Ready.");
			}
			else if (shouldPatchBridgeOnly)
			{
				string bridgeTag = CreateStepTag(++completedItems, totalItems, "Nexus Bridge");
				_progress(ProgressBefore(completedItems, totalItems), $"{bridgeTag} Patching Nexus Bridge...");
				var bridgeResult = await _comfyInstall.PatchNexusBridgeAsync(cancellationToken);
				if (!bridgeResult.IsSuccess) return bridgeResult;
				_progress(ProgressAfter(completedItems, totalItems), $"{bridgeTag} Ready.");
			}

			return new SetupStepResult(true, "Nexus-managed extensions are ready.", 1);
		}
		catch (Exception ex)
		{
			return new SetupStepResult(false, $"Manager install failed: {ex.Message}", 0);
		}
	}

	private static GitRecoveryMode ResolveRecoveryMode(bool forceSyncExisting, bool reinstallExisting)
		=> reinstallExisting
			? GitRecoveryMode.FreshClone
			: forceSyncExisting
				? GitRecoveryMode.SyncExisting
				: GitRecoveryMode.PresentOnly;

	private static string CreateStepTag(int itemNumber, int totalItems, string label)
		=> $"[{ExtensionStepPrefix} {itemNumber}/{Math.Max(1, totalItems)}] [{label}]";

	private static double ProgressBefore(int itemNumber, int totalItems)
		=> Math.Clamp((Math.Max(1, itemNumber) - 1) / (double)Math.Max(1, totalItems), 0, 1);

	private static double ProgressAfter(int itemNumber, int totalItems)
		=> Math.Clamp(Math.Max(1, itemNumber) / (double)Math.Max(1, totalItems), 0, 1);

	private static string FormatDependencyProgress(ManagedCustomNodeDependencyProgress progress)
		=> progress.Stage switch
		{
			ManagedCustomNodeDependencyStage.ResolvePythonRuntime => "Preparing the Python runtime for extension dependencies...",
			ManagedCustomNodeDependencyStage.DownloadWheel => $"Downloading the prebuilt {progress.ItemName} wheel...",
			ManagedCustomNodeDependencyStage.InstallWheel => $"Installing the prebuilt {progress.ItemName} wheel...",
			ManagedCustomNodeDependencyStage.InstallRequirements => "Installing upstream Python requirements...",
			ManagedCustomNodeDependencyStage.DownloadFile => $"Downloading required file {progress.ItemIndex}/{progress.ItemCount}: {Path.GetFileName(progress.ItemName)}...",
			ManagedCustomNodeDependencyStage.VerifyImports => "Verifying installed Python dependencies...",
			_ => "Preparing extension dependencies..."
		};

}
