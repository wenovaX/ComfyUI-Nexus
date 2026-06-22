namespace ComfyUI_Nexus.Setup.Services;

using System.IO;
using System.Threading.Tasks;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;

internal sealed class ComfyInstallService
{
	internal static ComfyInstallService Instance { get; private set; } = null!;

	// Settings access
	private static SetupSettings Settings => SetupSettingsService.Instance.Settings;

	private const string CoreTag = "[Core]";

	// Paths
	internal static string RootPath => Settings.RootPath;
	internal static bool PortableOnly => Settings.PortableOnly;

	internal Action<string>? OnMessage { get; set; }
	internal Action<double, string>? OnProgress { get; set; }
	private readonly RuntimePackageService _packageService;
	private readonly RuntimePurgeService _purgeService;
	private readonly RuntimeBackupService _backupService;
	private readonly GitRepositoryService _gitRepositoryService;
	private readonly ComfyVenvManager _venvManager;
	private readonly ModelResourceInstaller _modelResourceInstaller;
	private readonly ExtensionInstaller _extensionInstaller;
	private readonly HudBridgeInstaller _hudBridgeInstaller;
	private readonly DependencyInstaller _dependencyInstaller;

	internal static string LocalRuntimePath => Path.Combine(RootPath, "LocalRuntime");
	internal static string RuntimeBackupsPath => RuntimeBackupService.GetConfiguredBackupRoot();
	internal static string InstalledPath => Path.Combine(LocalRuntimePath, "Installed");
	internal static string DefaultComfyPath => Path.Combine(InstalledPath, "ComfyUI");
	internal static string ComfyPath => string.IsNullOrWhiteSpace(Settings.ComfyPath) ? DefaultComfyPath : Settings.ComfyPath;
	internal static string PythonPath => Path.Combine(InstalledPath, "Python");
	internal static string PythonExe => Path.Combine(PythonPath, "python.exe");
	internal static string ComfyVenvPath => Path.Combine(ComfyPath, ".venv");
	internal static string ComfyVenvPythonExe => Path.Combine(ComfyVenvPath, "Scripts", "python.exe");

	internal static string GetLocalRuntimePath(string relativePath)
		=> Path.Combine(LocalRuntimePath, relativePath.Replace('/', Path.DirectorySeparatorChar));

	internal ComfyInstallService()
	{
		Instance = this;
		_packageService = new RuntimePackageService(Log);
		_purgeService = new RuntimePurgeService(Log);
		_backupService = new RuntimeBackupService(Log, ReportProgressOnly);
		_backupService.CleanupPendingRestoreTemps();
		_gitRepositoryService = new GitRepositoryService(Log);
		_venvManager = new ComfyVenvManager(Log, ReportProgressOnly);
		_modelResourceInstaller = new ModelResourceInstaller(Log, ReportProgressOnly);
		_extensionInstaller = new ExtensionInstaller(Log, Progress, _gitRepositoryService);
		_hudBridgeInstaller = new HudBridgeInstaller(Log, _gitRepositoryService);
		_dependencyInstaller = new DependencyInstaller(_venvManager);
	}

	private void Log(string message)
	{
		NexusLog.Info(message);
		OnMessage?.Invoke(message);
	}
	private void ReportProgressOnly(double progress, string message)
		=> OnProgress?.Invoke(Math.Clamp(progress, 0, 1), message);

	private void Progress(double progress, string message)
	{
		ReportProgressOnly(progress, message);
		Log(message);
	}

	internal async Task ExtractGitPackageAsync(CancellationToken cancellationToken)
		=> await _packageService.ExtractGitPackageAsync(cancellationToken);

	internal async Task ExtractPythonPackageAsync(CancellationToken cancellationToken)
		=> await _packageService.ExtractPythonPackageAsync(cancellationToken);

	internal async Task<SetupStepResult> PurgeRuntimeAsync(CancellationToken cancellationToken)
		=> await _purgeService.PurgeRuntimeAsync(cancellationToken);

	internal async Task<RuntimeBackupAnalysis> AnalyzeRuntimeBackupAsync(
		IEnumerable<string> backupTargets,
		CancellationToken cancellationToken)
		=> await _backupService.AnalyzeBackupAsync(backupTargets, cancellationToken);

	internal RuntimeBackupAnalysis RefreshRuntimeBackupSpace(RuntimeBackupAnalysis analysis)
		=> _backupService.RefreshAvailableSpace(analysis);

	internal async Task<SetupStepResult> BackupRuntimeDataAsync(
		IEnumerable<string> backupTargets,
		string format,
		CancellationToken cancellationToken)
		=> await _backupService.BackupRuntimeDataAsync(backupTargets, format, cancellationToken);

	internal async Task<SetupStepResult> BackupRuntimeDataAsync(
		RuntimeBackupAnalysis analysis,
		string format,
		CancellationToken cancellationToken)
		=> await _backupService.BackupRuntimeDataAsync(analysis, format, cancellationToken);

	internal async Task<RuntimeRestoreAnalysis> AnalyzeRuntimeRestoreAsync(
		string backupPath,
		CancellationToken cancellationToken)
		=> await _backupService.AnalyzeRestoreAsync(backupPath, cancellationToken);

	internal async Task<RuntimeRestoreResult> RestoreRuntimeBackupAsync(
		RuntimeRestoreAnalysis analysis,
		CancellationToken cancellationToken)
		=> await _backupService.RestoreRuntimeBackupAsync(analysis, cancellationToken);

	internal async Task<SetupStepResult> DeleteRuntimeBackupAsync(string backupPath, CancellationToken cancellationToken)
		=> await _backupService.DeleteRuntimeBackupAsync(backupPath, cancellationToken);

	internal IReadOnlyList<RuntimeBackupEntry> GetRuntimeBackups(bool includeIncomplete)
		=> _backupService.GetManagedBackups(includeIncomplete);

	internal async Task<SetupStepResult> InstallCoreAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			string? gitExe = (await _gitRepositoryService.ResolveConfiguredGitAsync(cancellationToken)).Exe;
			if (gitExe == null) return new SetupStepResult(false, "Git is required for ComfyUI installation.", 0);

			Log($"{CoreTag} Attempting Git sync for ComfyUI...");
			var gitResult = await _gitRepositoryService.SyncComfyRepoAsync(gitExe, CoreTag, cancellationToken);

			if (gitResult.IsSuccess)
			{
				EnsureComfyWorkspaceDirectories();

				await _venvManager.EnsureAsync(CoreTag, cancellationToken);

				return new SetupStepResult(true, "ComfyUI Core and Venv are ready.", 1);
			}

			Log($"{CoreTag} Git sync failed. Falling back to local Zip package...");
			var packageSpec = RuntimePackageSpecService.Load();
			var comfyPackage = packageSpec.Comfy ?? new RuntimeOptionalPackageSpec("ComfyUI", "ComfyUI-0.20.2.zip");
			string comfyZip = Path.Combine(LocalRuntimePath, "Packages", comfyPackage.Folder, comfyPackage.File);

			if (File.Exists(comfyZip))
			{
				await _packageService.ExtractComfyZipDirectlyAsync(comfyZip, ComfyPath, cancellationToken);
				EnsureComfyWorkspaceDirectories();

				await _venvManager.EnsureAsync(CoreTag, cancellationToken);

				return new SetupStepResult(true, "ComfyUI Core and Venv are ready.", 1);
			}

			return new SetupStepResult(false, "Both Git sync and Zip fallback failed.", 0);
		}
		catch (Exception ex)
		{
			return new SetupStepResult(false, $"Core install failed: {ex.Message}", 0);
		}
	}

	private static void EnsureComfyWorkspaceDirectories()
		=> EnsureComfyWorkspaceDirectories(ComfyPath);

	internal static void EnsureComfyWorkspaceDirectories(string comfyPath)
	{
		string[] dirs = {
			Path.Combine(comfyPath, "input"),
			Path.Combine(comfyPath, "output"),
			Path.Combine(comfyPath, "models"),
			Path.Combine(comfyPath, "user", "default", "workflows")
		};
		foreach (var dir in dirs) Directory.CreateDirectory(dir);
	}

	internal async Task<SetupStepResult> DownloadDefaultModelAsync(CancellationToken cancellationToken)
		=> await _modelResourceInstaller.DownloadDefaultModelAsync(cancellationToken);

	internal async Task<SetupStepResult> InstallManagerAsync(CancellationToken cancellationToken)
		=> await _extensionInstaller.InstallManagerAsync(cancellationToken);

	internal async Task<SetupStepResult> InstallManagedExtensionsAsync(
		IEnumerable<string>? targetFolders,
		bool forceSyncExisting,
		bool reinstallExisting,
		CancellationToken cancellationToken,
		bool installNodeDependencies = true)
		=> await _extensionInstaller.InstallManagedExtensionsAsync(
			targetFolders,
			forceSyncExisting,
			reinstallExisting,
			cancellationToken,
			installNodeDependencies);

	internal async Task<SetupStepResult> InstallHudBridgeAsync(CancellationToken cancellationToken)
		=> await _hudBridgeInstaller.InstallHudBridgeAsync(cancellationToken);

	internal async Task<SetupStepResult> InstallHudBridgeAsync(bool overwriteExisting, CancellationToken cancellationToken)
		=> await _hudBridgeInstaller.InstallHudBridgeAsync(overwriteExisting, cancellationToken);

	internal async Task<SetupStepResult> PatchLocalHudProjectAsync(string localHudPath, CancellationToken cancellationToken)
		=> await _hudBridgeInstaller.PatchLocalHudProjectAsync(localHudPath, cancellationToken);

	internal async Task<SetupStepResult> PatchNexusBridgeAsync(CancellationToken cancellationToken)
		=> await _hudBridgeInstaller.PatchNexusBridgeAsync(cancellationToken);

	internal async Task<SetupStepResult> RestoreHudSamplesAsync(
		bool overwriteExisting,
		CancellationToken cancellationToken)
		=> await _hudBridgeInstaller.RestoreHudSamplesAsync(overwriteExisting, cancellationToken);

	internal async Task<SetupStepResult> UpdateComfyRepositoryAsync(CancellationToken cancellationToken)
	{
		try
		{
			string comfyPath = ComfyPath;
			if (!Directory.Exists(Path.Combine(comfyPath, ".git")))
			{
				return new SetupStepResult(true, "ComfyUI is not a git checkout. Repository update skipped.", 1);
			}

			string gitExe = ResolveConfiguredGitExecutable();
			Log("[ComfyUI] Updating ComfyUI repository...");
			return await _gitRepositoryService.PullFastForwardAsync(gitExe, comfyPath, "[ComfyUI]", cancellationToken);
		}
		catch (Exception ex)
		{
			return new SetupStepResult(false, $"ComfyUI update failed: {ex.Message}", 0);
		}
	}

	internal async Task<SetupStepResult> InstallDependenciesAsync(CancellationToken cancellationToken)
		=> await _dependencyInstaller.InstallDependenciesAsync(cancellationToken);

	internal async Task<SetupStepResult> RepairRuntimeDependenciesAsync(CancellationToken cancellationToken)
		=> await _dependencyInstaller.RepairRuntimeDependenciesAsync(cancellationToken);

	internal async Task<SetupStepResult> EnsureComfyVenvOnlyAsync(CancellationToken cancellationToken)
		=> await _venvManager.EnsureOnlyAsync(cancellationToken);

	internal async Task<SetupStepResult> RebuildComfyVenvOnlyAsync(CancellationToken cancellationToken)
		=> await _venvManager.RebuildOnlyAsync(cancellationToken);

	internal async Task<SetupStepResult> DeleteComfyVenvOnlyAsync(CancellationToken cancellationToken)
		=> await _venvManager.DeleteOnlyAsync(cancellationToken);

	internal async Task<SetupStepResult> ApplyPendingComfyVenvDeleteAsync(CancellationToken cancellationToken)
		=> await _venvManager.ApplyPendingDeleteAsync(cancellationToken);

	private static string ResolveConfiguredGitExecutable()
	{
		var settings = SetupSettingsService.Instance.Settings;
		if (!string.IsNullOrWhiteSpace(settings.GitPath)) return settings.GitPath;
		return Path.Combine(InstalledPath, "Git", "cmd", "git.exe");
	}

}
