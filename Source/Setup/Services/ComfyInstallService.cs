namespace ComfyUI_Nexus.Setup.Services;

using System.IO;
using System.Threading.Tasks;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;

internal sealed class ComfyInstallService
{
	private const string CoreTag = "[Core]";
	private static readonly string[] PreservedComfySourceDirectories =
	[
		".venv",
		"custom_nodes",
		"input",
		"logs",
		"models",
		"output",
		"temp",
		"user"
	];
	private static readonly string[] PreservedComfySourceFiles =
	[
		"extra_model_paths.yaml",
		"extra_model_paths.yml"
	];

	// Paths
	internal static string RootPath => NexusStorageLayout.DataRoot;
	internal static string PackageRoot => NexusStorageLayout.PackageRoot;

	internal Action<string>? OnMessage { get; set; }
	internal Action<double, string>? OnProgress { get; set; }
	private readonly RuntimePackageService _packageService;
	internal SetupSettingsService SettingsService { get; }
	private readonly NexusToolingEnvironment _tooling;
	private readonly RuntimePurgeService _purgeService;
	private readonly RuntimeBackupService _backupService;
	private readonly GitRepositoryService _gitRepositoryService;
	private readonly ComfyVenvManager _venvManager;
	private readonly ModelResourceInstaller _modelResourceInstaller;
	private readonly ExtensionInstaller _extensionInstaller;
	private readonly HudBridgeInstaller _hudBridgeInstaller;
	private readonly DependencyInstaller _dependencyInstaller;
	internal NexusComfyRuntimePaths Paths { get; }
	private SetupSettings Settings => SettingsService.Settings;

	internal static string LocalRuntimePath => NexusStorageLayout.LocalRuntimeRoot;
	internal static string RuntimePackagesPath => NexusStorageLayout.RuntimePackagesRoot;
	internal string RuntimeBackupsPath => RuntimeBackupService.GetConfiguredBackupRoot(SettingsService.Settings);
	internal static string InstalledPath => Path.Combine(LocalRuntimePath, "Installed");
	internal static string DefaultComfyPath => Path.Combine(InstalledPath, "ComfyUI");
	internal static string PythonPath => Path.Combine(InstalledPath, "Python");
	internal static string PythonExe => Path.Combine(PythonPath, "python.exe");
	internal bool PortableOnly => Settings.PortableOnly;
	internal string ComfyPath => Paths.ActiveComfyPath;
	internal string ComfyVenvPath => Paths.ActiveVenvPath;
	internal string ComfyVenvPythonExe => Paths.ActiveVenvPythonExe;

	internal static string GetLocalRuntimePath(string relativePath)
		=> Path.Combine(LocalRuntimePath, relativePath.Replace('/', Path.DirectorySeparatorChar));

	internal static string GetRuntimePackagePath(string relativePath)
		=> NexusStorageLayout.GetRuntimePackagePath(relativePath);

	internal ComfyInstallService(
		NexusToolingEnvironment tooling,
		NexusServerProcessController serverProcesses,
		SetupSettingsService settingsService,
		NexusComfyRuntimePaths paths)
	{
		_tooling = tooling ?? throw new ArgumentNullException(nameof(tooling));
		SettingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
		Paths = paths ?? throw new ArgumentNullException(nameof(paths));
		_packageService = new RuntimePackageService(Log, _tooling, SettingsService);
		_purgeService = new RuntimePurgeService(Log, SettingsService);
		_backupService = new RuntimeBackupService(Log, ReportProgressOnly, SettingsService, Paths);
		_backupService.CleanupPendingRestoreTemps();
		_gitRepositoryService = new GitRepositoryService(new GitRepositoryOperationContext(
			Log,
			message => ReportProgressOnly(0, message),
			_packageService.KillInstalledRuntimeProcesses));
		_venvManager = new ComfyVenvManager(Log, ReportProgressOnly, _tooling, serverProcesses, SettingsService, Paths);
		_modelResourceInstaller = new ModelResourceInstaller(Log, ReportProgressOnly, SettingsService, Paths);
		_extensionInstaller = new ExtensionInstaller(Log, Progress, _gitRepositoryService, _tooling, this, SettingsService, Paths);
		_hudBridgeInstaller = new HudBridgeInstaller(Log, _gitRepositoryService, _tooling, SettingsService, Paths);
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
			if (IsExternalExistingComfyPath())
			{
				Log($"{CoreTag} Existing external ComfyUI detected. Source files will not be replaced: {Path.GetFullPath(ComfyPath)}");
				EnsureComfyWorkspaceDirectories();

				await _venvManager.EnsureAsync(CoreTag, cancellationToken);

				return new SetupStepResult(true, "Existing ComfyUI Core and Venv are ready.", 1);
			}

			if (string.Equals(Settings.ComfyCoreSource, ComfyCoreSources.BuiltIn, StringComparison.Ordinal))
			{
				Log($"{CoreTag} Using built-in ComfyUI source package.");
				return await InstallCoreFromLocalSourcePackageAsync(cancellationToken);
			}

			string? gitExe = (await _gitRepositoryService.ResolveConfiguredGitAsync(Settings.GitPath, cancellationToken)).Exe;
			if (gitExe == null) return new SetupStepResult(false, "Git is required for ComfyUI installation.", 0);
			string toolingComfyPath = GetToolingComfyPath();

			var inspection = await _gitRepositoryService.InspectRepositoryAsync(gitExe, Settings.ComfyRepoUrl, toolingComfyPath, cancellationToken);
			if (inspection.Exists && !inspection.IsValid)
			{
				if (!IsManagedComfyPath(ComfyPath))
				{
					return new SetupStepResult(false, $"External ComfyUI path is not the expected git repository. {inspection.Reason}", 0);
				}

				Log($"{CoreTag} Managed ComfyUI folder is not a git repository. Replacing source tree with a fresh remote clone...");
				var replaceResult = await ReplaceManagedComfySourceWithRemoteRepoAsync(gitExe, cancellationToken);
				if (replaceResult.IsSuccess)
				{
					EnsureComfyWorkspaceDirectories();

					await _venvManager.EnsureAsync(CoreTag, cancellationToken);

					return new SetupStepResult(true, "ComfyUI Core and Venv are ready.", 1);
				}

				Log($"{CoreTag} Fresh remote clone replacement failed. Falling back to local source package: {replaceResult.Message}");
			}

			Log($"{CoreTag} Attempting Git sync for ComfyUI...");
			var gitResult = await _gitRepositoryService.EnsureRepositoryAsync(
				gitExe,
				Settings.ComfyRepoUrl,
				toolingComfyPath,
				CoreTag,
				GitRecoveryMode.SyncExisting,
				cancellationToken);

			if (gitResult.IsSuccess)
			{
				EnsureComfyWorkspaceDirectories();

				await _venvManager.EnsureAsync(CoreTag, cancellationToken);

				return new SetupStepResult(true, "ComfyUI Core and Venv are ready.", 1);
			}

			Log($"{CoreTag} Git sync failed. Falling back to local source package...");
			return await InstallCoreFromLocalSourcePackageAsync(cancellationToken, "Both Git sync and ComfyUI source fallback failed.");
		}
		catch (Exception ex)
		{
			return new SetupStepResult(false, $"Core install failed: {ex.Message}", 0);
		}
	}

	private async Task<SetupStepResult> InstallCoreFromLocalSourcePackageAsync(
		CancellationToken cancellationToken,
		string failurePrefix = "ComfyUI source package installation failed.")
	{
		var packageSpec = RuntimePackageSpecService.Load();
		var comfyPackage = packageSpec.Comfy ?? new RuntimeOptionalPackageSpec(
			"ComfyUI",
			"ComfyUI-v0.27.0-source.zip",
			"62A2E70E143869B1C5B768F874622D402FE53B9D69F99A14CF83E6C2A97BB09C",
			"v0.27.0",
			"bb131be",
			"https://github.com/Comfy-Org/ComfyUI/releases/tag/v0.27.0");
		string comfyZip = GetRuntimePackagePath(Path.Combine(comfyPackage.Folder, comfyPackage.File));

		if (!File.Exists(comfyZip))
		{
			return new SetupStepResult(false, $"{failurePrefix} Missing package: {comfyZip}", 0);
		}

		await CleanManagedComfySourceTreeBeforePackageExtractAsync(cancellationToken);
		string toolingComfyPath = GetToolingComfyPath();
		await _packageService.ExtractComfyZipDirectlyAsync(comfyZip, toolingComfyPath, comfyPackage, cancellationToken);
		EnsureComfyWorkspaceDirectories();

		await _venvManager.EnsureAsync(CoreTag, cancellationToken);

		return new SetupStepResult(true, "ComfyUI Core and Venv are ready.", 1);
	}

	private async Task<SetupStepResult> ReplaceManagedComfySourceWithRemoteRepoAsync(
		string gitExe,
		CancellationToken cancellationToken)
	{
		string targetPath = GetToolingComfyPath()
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string clonePath = GetToolingPath(GetUniqueManagedComfyRemoteClonePath());
		bool cloneCreated = false;

		try
		{
			var cloneResult = await _gitRepositoryService.EnsureRepositoryAsync(
				gitExe,
				Settings.ComfyRepoUrl,
				clonePath,
				CoreTag,
				GitRecoveryMode.FreshClone,
				cancellationToken);
			if (!cloneResult.IsSuccess)
			{
				return cloneResult;
			}

			cloneCreated = true;
			await ApplyRemoteCloneToManagedComfySourceAsync(clonePath, targetPath, cancellationToken);
			await DeleteDirectoryWithRuntimeFallbackAsync(clonePath, cancellationToken);
			cloneCreated = false;

			return cloneResult;
		}
		catch (Exception ex)
		{
			if (cloneCreated)
			{
				try
				{
					await DeleteDirectoryWithRuntimeFallbackAsync(clonePath, CancellationToken.None);
				}
				catch (Exception cleanupEx)
				{
					Log($"{CoreTag} Temporary remote clone cleanup failed: {cleanupEx.Message}");
				}
			}

			return new SetupStepResult(false, $"Fresh remote clone replacement failed: {ex.Message}", 0);
		}
	}

	private async Task ApplyRemoteCloneToManagedComfySourceAsync(
		string clonePath,
		string targetPath,
		CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(targetPath);
		await CleanManagedComfySourceTreeBeforePackageExtractAsync(cancellationToken);

		var preservedDirectories = new HashSet<string>(PreservedComfySourceDirectories, StringComparer.OrdinalIgnoreCase);
		var preservedFiles = new HashSet<string>(PreservedComfySourceFiles, StringComparer.OrdinalIgnoreCase);

		foreach (string directory in Directory.EnumerateDirectories(clonePath).ToArray())
		{
			cancellationToken.ThrowIfCancellationRequested();
			string name = Path.GetFileName(directory);
			string destination = Path.Combine(targetPath, name);
			if (preservedDirectories.Contains(name) && Directory.Exists(destination))
			{
				Log($"{CoreTag} Preserved runtime directory kept during remote source merge: {name}");
				continue;
			}

			if (Directory.Exists(destination))
			{
				await DeleteDirectoryWithRuntimeFallbackAsync(destination, cancellationToken);
			}

			await MoveDirectoryWithRuntimeFallbackAsync(directory, destination, cancellationToken);
		}

		foreach (string file in Directory.EnumerateFiles(clonePath).ToArray())
		{
			cancellationToken.ThrowIfCancellationRequested();
			string name = Path.GetFileName(file);
			string destination = Path.Combine(targetPath, name);
			if (preservedFiles.Contains(name) && File.Exists(destination))
			{
				Log($"{CoreTag} Preserved runtime file kept during remote source merge: {name}");
				continue;
			}

			MoveFileWithRuntimeFallback(file, destination);
		}

		Log($"{CoreTag} Applied remote ComfyUI source while preserving runtime data.");
	}

	private static string GetUniqueManagedComfyRemoteClonePath()
	{
		for (int i = 0; i < 100; i++)
		{
			string suffix = i == 0 ? string.Empty : $"-{i}";
			string candidate = Path.Combine(
				InstalledPath,
				$"ComfyUI.remote-clone-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{suffix}");
			if (!Directory.Exists(candidate) && !File.Exists(candidate))
			{
				return candidate;
			}
		}

		throw new IOException("Unable to allocate a unique ComfyUI remote clone path.");
	}

	private async Task CleanManagedComfySourceTreeBeforePackageExtractAsync(CancellationToken cancellationToken)
	{
		string physicalTargetPath = Paths.ConfiguredComfyPath;
		if (!IsManagedComfyPath(physicalTargetPath))
		{
			Log($"{CoreTag} Skipping source cleanup for external ComfyUI path: {Path.GetFullPath(physicalTargetPath)}");
			return;
		}

		string targetPath = GetToolingComfyPath()
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		if (!Directory.Exists(targetPath))
		{
			return;
		}

		Log($"{CoreTag} Cleaning managed ComfyUI source files before source merge...");
		await Task.Run(() => CleanManagedComfySourceTree(targetPath, cancellationToken), cancellationToken);
	}

	private void CleanManagedComfySourceTree(string targetPath, CancellationToken cancellationToken)
	{
		var preservedDirectories = new HashSet<string>(PreservedComfySourceDirectories, StringComparer.OrdinalIgnoreCase);
		var preservedFiles = new HashSet<string>(PreservedComfySourceFiles, StringComparer.OrdinalIgnoreCase);
		int removedDirectories = 0;
		int removedFiles = 0;

		foreach (string directory in Directory.EnumerateDirectories(targetPath))
		{
			cancellationToken.ThrowIfCancellationRequested();
			string name = Path.GetFileName(directory);
			if (preservedDirectories.Contains(name))
			{
				continue;
			}

			DeleteDirectoryWithRuntimeFallback(directory);
			removedDirectories++;
		}

		foreach (string file in Directory.EnumerateFiles(targetPath))
		{
			cancellationToken.ThrowIfCancellationRequested();
			string name = Path.GetFileName(file);
			if (preservedFiles.Contains(name))
			{
				continue;
			}

			DeleteFileWithRuntimeFallback(file);
			removedFiles++;
		}

		Log($"{CoreTag} Cleaned managed ComfyUI source tree. Removed {removedDirectories} directories and {removedFiles} files; preserved models, custom_nodes, user data, IO folders, .venv, temp/logs, and extra_model_paths.");
	}

	private void DeleteDirectoryWithRuntimeFallback(string path)
	{
		try
		{
			ClearFileSystemAttributes(path);
			Directory.Delete(path, recursive: true);
		}
		catch (Exception) when (IsInstalledRuntimePath(path))
		{
			Log($"{CoreTag} Locked source directory detected. Terminating local runtime processes before retry: {Path.GetFileName(path)}");
			_packageService.KillInstalledRuntimeProcesses();
			Thread.Sleep(250);
			ClearFileSystemAttributes(path);
			Directory.Delete(path, recursive: true);
		}
	}

	private async Task DeleteDirectoryWithRuntimeFallbackAsync(string path, CancellationToken cancellationToken)
	{
		await Task.Run(() =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			DeleteDirectoryWithRuntimeFallback(path);
		}, cancellationToken);
	}

	private async Task MoveDirectoryWithRuntimeFallbackAsync(
		string sourcePath,
		string destinationPath,
		CancellationToken cancellationToken)
	{
		await Task.Run(() =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			const int MaxAttempts = 5;
			Exception? lastException = null;
			bool cleanupAttempted = false;

			for (int attempt = 1; attempt <= MaxAttempts; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					ClearFileSystemAttributes(sourcePath);
					Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? InstalledPath);
					Directory.Move(sourcePath, destinationPath);
					return;
				}
				catch (Exception ex) when (IsInstalledRuntimePath(sourcePath) || IsInstalledRuntimePath(destinationPath))
				{
					lastException = ex;
					if (!cleanupAttempted)
					{
						cleanupAttempted = true;
						Log($"{CoreTag} Locked source tree detected. Terminating local runtime processes before retry: {Path.GetFileName(sourcePath)}");
						_packageService.KillInstalledRuntimeProcesses();
					}

					Thread.Sleep(160 * attempt);
				}
			}

			throw lastException ?? new IOException($"Unable to move directory: {sourcePath}");
		}, cancellationToken);
	}

	private void DeleteFileWithRuntimeFallback(string path)
	{
		try
		{
			File.SetAttributes(path, FileAttributes.Normal);
			File.Delete(path);
		}
		catch (Exception) when (IsInstalledRuntimePath(path))
		{
			Log($"{CoreTag} Locked source file detected. Terminating local runtime processes before retry: {Path.GetFileName(path)}");
			_packageService.KillInstalledRuntimeProcesses();
			Thread.Sleep(250);
			File.SetAttributes(path, FileAttributes.Normal);
			File.Delete(path);
		}
	}

	private void MoveFileWithRuntimeFallback(string sourcePath, string destinationPath)
	{
		try
		{
			File.SetAttributes(sourcePath, FileAttributes.Normal);
			Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? InstalledPath);
			File.Move(sourcePath, destinationPath, overwrite: true);
		}
		catch (Exception) when (IsInstalledRuntimePath(sourcePath) || IsInstalledRuntimePath(destinationPath))
		{
			Log($"{CoreTag} Locked source file detected. Terminating local runtime processes before retry: {Path.GetFileName(sourcePath)}");
			_packageService.KillInstalledRuntimeProcesses();
			Thread.Sleep(250);
			File.SetAttributes(sourcePath, FileAttributes.Normal);
			Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? InstalledPath);
			File.Move(sourcePath, destinationPath, overwrite: true);
		}
	}

	private static bool IsInstalledRuntimePath(string path)
	{
		string fullPath = NexusToolingPathLeaseController.ResolvePhysicalPath(path);
		string installedPath = Path.GetFullPath(InstalledPath)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		return fullPath.StartsWith(
			installedPath + Path.DirectorySeparatorChar,
			StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsManagedComfyPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		string targetPath = Path.GetFullPath(path)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string managedPath = Path.GetFullPath(DefaultComfyPath)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		return string.Equals(targetPath, managedPath, StringComparison.OrdinalIgnoreCase);
	}

	private bool IsExternalExistingComfyPath()
		=> !string.IsNullOrWhiteSpace(Settings.ComfyPath)
			&& !IsManagedComfyPath(ComfyPath)
			&& File.Exists(Path.Combine(ComfyPath, "main.py"));

	private static void ClearFileSystemAttributes(string path)
	{
		if (File.Exists(path))
		{
			File.SetAttributes(path, FileAttributes.Normal);
			return;
		}

		if (!Directory.Exists(path))
		{
			return;
		}

		foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
		{
			File.SetAttributes(file, FileAttributes.Normal);
		}

		foreach (string directory in Directory.GetDirectories(path, "*", SearchOption.AllDirectories))
		{
			File.SetAttributes(directory, FileAttributes.Normal);
		}

		File.SetAttributes(path, FileAttributes.Normal);
	}

	private void EnsureComfyWorkspaceDirectories()
		=> EnsureComfyWorkspaceDirectories(Paths.ActiveComfyPath);

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
		CancellationToken cancellationToken)
		=> await _extensionInstaller.InstallManagedExtensionsAsync(
			targetFolders,
			forceSyncExisting,
			reinstallExisting,
			cancellationToken);

	internal async Task<SetupStepResult> InstallHudBridgeAsync(CancellationToken cancellationToken)
		=> await _hudBridgeInstaller.InstallHudBridgeAsync(cancellationToken);

	internal async Task<SetupStepResult> InstallHudBridgeAsync(bool overwriteExisting, CancellationToken cancellationToken)
		=> await _hudBridgeInstaller.InstallHudBridgeAsync(overwriteExisting, cancellationToken);

	internal async Task<SetupStepResult> InstallHudBridgeAsync(GitRecoveryMode recoveryMode, CancellationToken cancellationToken)
		=> await _hudBridgeInstaller.InstallHudBridgeAsync(recoveryMode, cancellationToken);

	internal async Task<SetupStepResult> PatchLocalHudProjectAsync(string localHudPath, CancellationToken cancellationToken)
		=> await _hudBridgeInstaller.PatchLocalHudProjectAsync(localHudPath, cancellationToken);

	internal async Task<SetupStepResult> PatchNexusBridgeAsync(CancellationToken cancellationToken)
		=> await _hudBridgeInstaller.PatchNexusBridgeAsync(cancellationToken);

	internal bool IsNexusBridgeExtensionHealthy()
		=> _hudBridgeInstaller.IsNexusBridgeExtensionHealthy();

	internal bool IsPackagedNexusBridgeAvailable()
		=> _hudBridgeInstaller.IsPackagedNexusBridgeAvailable();

	internal async Task<SetupStepResult> RestoreHudSamplesAsync(
		bool overwriteExisting,
		CancellationToken cancellationToken)
		=> await _hudBridgeInstaller.RestoreHudSamplesAsync(overwriteExisting, cancellationToken);

	internal async Task<SetupStepResult> UpdateComfyRepositoryAsync(CancellationToken cancellationToken)
	{
		try
		{
			string comfyPath = ComfyPath;
			if (string.Equals(Settings.ComfyCoreSource, ComfyCoreSources.BuiltIn, StringComparison.Ordinal))
			{
				if (!IsManagedComfyPath(comfyPath))
				{
					return new SetupStepResult(true, "Built-in compatible source is only applied to the Nexus managed runtime. External ComfyUI path skipped.", 1);
				}

				Log("[ComfyUI] Applying built-in compatible ComfyUI source...");
				return await InstallCoreFromLocalSourcePackageAsync(cancellationToken);
			}

			string gitExe = ResolveConfiguredGitExecutable();
			string toolingComfyPath = GetToolingComfyPath();
			var inspection = await _gitRepositoryService.InspectRepositoryAsync(gitExe, Settings.ComfyRepoUrl, toolingComfyPath, cancellationToken);
			if (!inspection.IsValid)
			{
				if (!IsManagedComfyPath(comfyPath))
				{
					return new SetupStepResult(true, "ComfyUI is not a git checkout. Repository update skipped.", 1);
				}

				Log("[ComfyUI] Managed ComfyUI source is missing or not a valid git checkout. Replacing it with a fresh remote clone...");
				var replaceResult = inspection.Exists
					? await ReplaceManagedComfySourceWithRemoteRepoAsync(gitExe, cancellationToken)
					: await _gitRepositoryService.EnsureRepositoryAsync(
						gitExe,
						Settings.ComfyRepoUrl,
						toolingComfyPath,
						"[ComfyUI]",
						GitRecoveryMode.RecoverIfBroken,
						cancellationToken);
				if (!replaceResult.IsSuccess)
				{
					return replaceResult;
				}

				EnsureComfyWorkspaceDirectories();
				return replaceResult;
			}

			Log("[ComfyUI] Updating ComfyUI repository...");
			var pullResult = await _gitRepositoryService.EnsureRepositoryAsync(
				gitExe,
				Settings.ComfyRepoUrl,
				toolingComfyPath,
				"[ComfyUI]",
				GitRecoveryMode.SyncExisting,
				cancellationToken);
			if (pullResult.IsSuccess)
			{
				EnsureComfyWorkspaceDirectories();
			}

			return pullResult;
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

	private string GetToolingComfyPath()
	{
		string? toolingPath = _tooling.CurrentLease?.GetComfyRoot();
		if (!string.IsNullOrWhiteSpace(toolingPath))
		{
			return toolingPath;
		}

		// ActiveComfyPath intentionally remains empty until an installation target exists.
		// Core installation must instead use the configured physical target on first run.
		return Paths.ConfiguredComfyPath;
	}

	private string GetToolingPath(string physicalPath)
	{
		NexusRuntimeToolingLease? lease = _tooling.CurrentLease;
		if (lease is null)
		{
			return physicalPath;
		}

		_ = lease.GetComfyRoot();
		return lease.GetToolingPath(physicalPath);
	}

	private string ResolveConfiguredGitExecutable()
	{
		var settings = SettingsService.Settings;
		if (!string.IsNullOrWhiteSpace(settings.GitPath)) return settings.GitPath;
		return Path.Combine(InstalledPath, "Git", "cmd", "git.exe");
	}

}
