namespace ComfyUI_Nexus.Setup.Services;

using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Setup.Models;

internal sealed class HudBridgeInstaller
{
	private const string BridgeTag = "[Bridge]";
	private const int HudFolderOperationRetries = 3;
	private const int HudFolderOperationRetryDelayMs = 350;

	private readonly Action<string> _log;
	private readonly GitRepositoryService _gitRepositoryService;

	internal HudBridgeInstaller(Action<string> log, GitRepositoryService gitRepositoryService)
	{
		_log = log;
		_gitRepositoryService = gitRepositoryService;
	}

	internal Task<SetupStepResult> InstallHudBridgeAsync(CancellationToken cancellationToken)
		=> InstallHudBridgeAsync(overwriteExisting: false, cancellationToken);

	internal async Task<SetupStepResult> RestoreHudSamplesAsync(
		bool overwriteExisting,
		CancellationToken cancellationToken)
	{
		string sourcePath = Path.Combine(ComfyPathResolver.ResolveActiveCustomNodesPath(), "ComfyUI-HUD", "hud_sample");
		string targetPath = Path.Combine(ComfyPathResolver.ResolveActiveWorkflowsPath(), "hud_sample");
		try
		{
			int copied = await Task.Run(
				() => CopyHudSamples(sourcePath, targetPath, overwriteExisting, cancellationToken),
				cancellationToken);
			_log($"{BridgeTag} HUD samples restored. copied={copied}, overwrite={overwriteExisting}");
			return new SetupStepResult(true, copied == 0
				? "HUD samples are already up to date."
				: $"HUD sample restore completed. copied={copied}", 1);
		}
		catch (Exception ex)
		{
			_log($"{BridgeTag} HUD sample restore failed: {ex.Message}");
			return new SetupStepResult(false, $"HUD sample restore failed: {ex.Message}", 0);
		}
	}

	private static int CopyHudSamples(
		string sourcePath,
		string targetPath,
		bool overwriteExisting,
		CancellationToken cancellationToken)
	{
		if (!Directory.Exists(sourcePath))
		{
			throw new DirectoryNotFoundException($"HUD sample folder was not found: {sourcePath}");
		}

		string[] sourceFiles = Directory.GetFiles(sourcePath, "*.json", SearchOption.AllDirectories);
		if (sourceFiles.Length == 0)
		{
			throw new InvalidOperationException($"HUD sample folder contains no workflow JSON files: {sourcePath}");
		}

		Directory.CreateDirectory(targetPath);
		int copied = 0;
		foreach (string sourceFile in sourceFiles)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string relativePath = Path.GetRelativePath(sourcePath, sourceFile);
			string destinationPath = Path.Combine(targetPath, relativePath);
			if (!overwriteExisting && File.Exists(destinationPath))
			{
				continue;
			}

			string? destinationDirectory = Path.GetDirectoryName(destinationPath);
			if (!string.IsNullOrWhiteSpace(destinationDirectory))
			{
				Directory.CreateDirectory(destinationDirectory);
			}

			File.Copy(sourceFile, destinationPath, overwrite: overwriteExisting);
			copied++;
		}

		return copied;
	}

	internal Task<SetupStepResult> PatchLocalHudProjectAsync(string localHudPath, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			string validationError = ValidateLocalHudProject(localHudPath);
			if (!string.IsNullOrWhiteSpace(validationError))
			{
				return Task.FromResult(new SetupStepResult(false, validationError, 0));
			}

			string hudPath = Path.Combine(ComfyPathResolver.ResolveActiveCustomNodesPath(), "ComfyUI-HUD");
			Directory.CreateDirectory(hudPath);

			int copied = CopyWorktreeFiles(localHudPath, hudPath, cancellationToken);
			_log($"{BridgeTag} Local ComfyUI-HUD project patched. copied={copied}");

			var bridgeResult = PatchNexusBridgePayload(hudPath, cancellationToken);
			if (!bridgeResult.IsSuccess) return Task.FromResult(bridgeResult);

			return Task.FromResult(new SetupStepResult(
				true,
				$"Local ComfyUI-HUD patched. Restart ComfyUI or reload the WebView to use updated HUD files. copied={copied}",
				1));
		}
		catch (Exception ex)
		{
			return Task.FromResult(new SetupStepResult(false, $"Local HUD patch failed: {ex.Message}", 0));
		}
	}

	internal Task<SetupStepResult> PatchNexusBridgeAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			string hudPath = Path.Combine(ComfyPathResolver.ResolveActiveCustomNodesPath(), "ComfyUI-HUD");
			string hudInitPath = Path.Combine(hudPath, "__init__.py");
			if (!Directory.Exists(hudPath) || !File.Exists(hudInitPath))
			{
				return Task.FromResult(new SetupStepResult(
					false,
					$"ComfyUI-HUD is not installed or incomplete: {hudPath}",
					0));
			}

			return Task.FromResult(PatchNexusBridgePayload(hudPath, cancellationToken));
		}
		catch (Exception ex)
		{
			return Task.FromResult(new SetupStepResult(false, $"Nexus bridge patch failed: {ex.Message}", 0));
		}
	}

	internal async Task<SetupStepResult> InstallHudBridgeAsync(bool overwriteExisting, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			string hudPath = Path.Combine(ComfyPathResolver.ResolveActiveCustomNodesPath(), "ComfyUI-HUD");
			string hudInitPath = Path.Combine(hudPath, "__init__.py");
			string hudGitPath = Path.Combine(hudPath, ".git");
			_log($"{BridgeTag} Deploying Nexus Agent...");

			var (gitExe, _) = await _gitRepositoryService.ResolveConfiguredGitAsync(cancellationToken);
			if (gitExe == null) return new SetupStepResult(false, "Git is required for ComfyUI-HUD installation.", 0);

			string hudRepoUrl = SetupSettingsService.Instance.Settings.HudRepoUrl;

			if (File.Exists(hudInitPath) && !overwriteExisting)
			{
				_log($"{BridgeTag} Existing ComfyUI-HUD detected. Keeping current copy.");
				return PatchNexusBridgePayload(hudPath, cancellationToken);
			}

			if (Directory.Exists(hudPath))
			{
				var hudRepo = await _gitRepositoryService.InspectRepositoryAsync(gitExe, hudRepoUrl, hudPath, cancellationToken);
				if (!hudRepo.IsValid)
				{
					_log($"{BridgeTag} Existing ComfyUI-HUD is not a valid HUD repository. {hudRepo.Reason}");
					bool moved = await TryBackupExistingHudFolderAsync(hudPath, cancellationToken);
					if (!moved)
					{
						return await InstallHudByOverlayCloneAsync(gitExe, hudRepoUrl, hudPath, hudInitPath, cancellationToken);
					}
				}
			}

			if (Directory.Exists(hudPath) && !Directory.Exists(hudGitPath))
			{
				bool moved = await TryBackupExistingHudFolderAsync(hudPath, cancellationToken);
				if (!moved)
				{
					return await InstallHudByOverlayCloneAsync(gitExe, hudRepoUrl, hudPath, hudInitPath, cancellationToken);
				}
			}

			if (Directory.Exists(hudPath) && Directory.Exists(hudGitPath) && !File.Exists(hudInitPath))
			{
				_log($"{BridgeTag} Incomplete ComfyUI-HUD git folder detected. Moving it aside before clone.");
				bool moved = await TryBackupExistingHudFolderAsync(hudPath, cancellationToken);
				if (!moved)
				{
					return await InstallHudByOverlayCloneAsync(gitExe, hudRepoUrl, hudPath, hudInitPath, cancellationToken);
				}
			}

			var syncResult = await _gitRepositoryService.EnsureRepoSyncedAsync(
				gitExe,
				hudRepoUrl,
				hudPath,
				BridgeTag,
				forceSyncExisting: overwriteExisting,
				cancellationToken);
			if (!syncResult.IsSuccess) return syncResult;

			if (!File.Exists(hudInitPath))
			{
				return new SetupStepResult(false, "ComfyUI-HUD sync finished, but __init__.py is still missing.", 0);
			}

			return PatchNexusBridgePayload(hudPath, cancellationToken);
		}
		catch (Exception ex)
		{
			return new SetupStepResult(false, $"Bridge deployment failed: {ex.Message}", 0);
		}
	}

	private async Task<bool> TryBackupExistingHudFolderAsync(string hudPath, CancellationToken cancellationToken)
	{
		if (!Directory.Exists(hudPath)) return true;

		string parent = Directory.GetParent(hudPath)?.FullName ?? Path.GetDirectoryName(hudPath) ?? ComfyPathResolver.ResolveActiveComfyPath();
		for (int attempt = 1; attempt <= HudFolderOperationRetries; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string backupPath = Path.Combine(parent, $"ComfyUI-HUD.broken-{DateTime.Now:yyyyMMdd-HHmmss}-{attempt}");
			try
			{
				_log($"{BridgeTag} Moving existing ComfyUI-HUD folder aside: {backupPath}");
				ClearReadOnlyAttributes(new DirectoryInfo(hudPath));
				Directory.Move(hudPath, backupPath);
				return true;
			}
			catch (IOException ex) when (attempt < HudFolderOperationRetries)
			{
				_log($"{BridgeTag} HUD folder is locked. Retrying move ({attempt}/{HudFolderOperationRetries}): {ex.Message}");
				await Task.Delay(HudFolderOperationRetryDelayMs, cancellationToken);
			}
			catch (UnauthorizedAccessException ex) when (attempt < HudFolderOperationRetries)
			{
				_log($"{BridgeTag} HUD folder access denied. Retrying move ({attempt}/{HudFolderOperationRetries}): {ex.Message}");
				await Task.Delay(HudFolderOperationRetryDelayMs, cancellationToken);
			}
			catch (Exception ex)
			{
				_log($"{BridgeTag} HUD folder move failed: {ex.Message}");
				return false;
			}
		}

		_log($"{BridgeTag} HUD folder could not be moved. Falling back to non-destructive overlay install.");
		return false;
	}

	private async Task<SetupStepResult> InstallHudByOverlayCloneAsync(
		string gitExe,
		string hudRepoUrl,
		string hudPath,
		string hudInitPath,
		CancellationToken cancellationToken)
	{
		string tempHudPath = Path.Combine(
			ComfyInstallService.LocalRuntimePath,
			"Cache",
			$"ComfyUI-HUD-clone-{Guid.NewGuid():N}");

		try
		{
			_log($"{BridgeTag} Cloning ComfyUI-HUD to temporary cache for overlay repair...");
			var cloneResult = await _gitRepositoryService.EnsureRepoSyncedAsync(
				gitExe,
				hudRepoUrl,
				tempHudPath,
				BridgeTag,
				forceSyncExisting: true,
				cancellationToken);
			if (!cloneResult.IsSuccess) return cloneResult;

			Directory.CreateDirectory(hudPath);
			int copied = CopyWorktreeFiles(tempHudPath, hudPath, cancellationToken);
			_log($"{BridgeTag} Overlay repair copied {copied} HUD file(s). Locked git remnants were left untouched.");

			if (!File.Exists(hudInitPath))
			{
				return new SetupStepResult(false, "ComfyUI-HUD overlay repair finished, but __init__.py is still missing.", 0);
			}

			var patchResult = PatchNexusBridgePayload(hudPath, cancellationToken);
			if (!patchResult.IsSuccess) return patchResult;

			return new SetupStepResult(true, "Nexus Agent repaired from GitHub. Restart ComfyUI if the old folder was locked.", 1);
		}
		finally
		{
			TryDeleteDirectory(tempHudPath);
		}
	}

	private SetupStepResult PatchNexusBridgePayload(string hudPath, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		string sourceJsDir = Path.Combine(ComfyInstallService.LocalRuntimePath, "Packages", "NexusBridge", "js");
		string targetJsDir = Path.Combine(hudPath, "js");
		string bridgeEntryPath = Path.Combine(targetJsDir, "nexus_bridge.js");
		string bridgeModulePath = Path.Combine(targetJsDir, "nexus", "index.js");

		if (!Directory.Exists(sourceJsDir))
		{
			return new SetupStepResult(false, $"NexusBridge package is missing: {sourceJsDir}", 0);
		}

		Directory.CreateDirectory(targetJsDir);
		int copied = CopyWorktreeFiles(sourceJsDir, targetJsDir, cancellationToken);
		_log($"{BridgeTag} Nexus bridge overlay patched into ComfyUI-HUD. copied={copied}");

		if (!File.Exists(bridgeEntryPath))
		{
			return new SetupStepResult(false, "Nexus bridge patch finished, but js/nexus_bridge.js is missing.", 0);
		}

		if (!File.Exists(bridgeModulePath))
		{
			return new SetupStepResult(false, "Nexus bridge patch finished, but js/nexus/index.js is missing.", 0);
		}

		return new SetupStepResult(true, "Nexus Agent successfully synced and patched.", copied);
	}

	private int CopyWorktreeFiles(string sourceDir, string targetDir, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Directory.CreateDirectory(targetDir);

		int copied = 0;
		foreach (string file in Directory.GetFiles(sourceDir))
		{
			cancellationToken.ThrowIfCancellationRequested();
			string targetPath = Path.Combine(targetDir, Path.GetFileName(file));
			try
			{
				File.Copy(file, targetPath, overwrite: true);
				copied++;
			}
			catch (IOException ex)
			{
				_log($"{BridgeTag} Skipped locked HUD file: {targetPath} ({ex.Message})");
			}
			catch (UnauthorizedAccessException ex)
			{
				_log($"{BridgeTag} Skipped protected HUD file: {targetPath} ({ex.Message})");
			}
		}

		foreach (string subDir in Directory.GetDirectories(sourceDir))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (string.Equals(Path.GetFileName(subDir), ".git", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			copied += CopyWorktreeFiles(
				subDir,
				Path.Combine(targetDir, Path.GetFileName(subDir)),
				cancellationToken);
		}

		return copied;
	}

	private static string ValidateLocalHudProject(string localHudPath)
	{
		if (string.IsNullOrWhiteSpace(localHudPath))
		{
			return "Local HUD project path is empty.";
		}

		if (!Directory.Exists(localHudPath))
		{
			return $"Local HUD project was not found: {localHudPath}";
		}

		string initPath = Path.Combine(localHudPath, "__init__.py");
		if (!File.Exists(initPath))
		{
			return "Local HUD project validation failed: __init__.py is missing.";
		}

		string jsPath = Path.Combine(localHudPath, "js");
		if (!Directory.Exists(jsPath))
		{
			return "Local HUD project validation failed: js folder is missing.";
		}

		string manifestPath = Path.Combine(localHudPath, "hud.manifest.json");
		if (!File.Exists(manifestPath))
		{
			return "Local HUD project validation failed: hud.manifest.json is missing.";
		}

		try
		{
			using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
			var root = doc.RootElement;
			string id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
			string name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
			string type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? string.Empty : string.Empty;

			if (!string.Equals(id, "comfyui-hud", StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(name, "ComfyUI-HUD", StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(type, "comfyui-custom-node", StringComparison.OrdinalIgnoreCase))
			{
				return "Local HUD project validation failed: manifest does not identify ComfyUI-HUD.";
			}
		}
		catch (JsonException ex)
		{
			return $"Local HUD project validation failed: manifest is invalid JSON. {ex.Message}";
		}
		catch (IOException ex)
		{
			return $"Local HUD project validation failed: manifest could not be read. {ex.Message}";
		}

		return string.Empty;
	}

	private void TryDeleteDirectory(string path)
	{
		if (!Directory.Exists(path)) return;
		try
		{
			ClearReadOnlyAttributes(new DirectoryInfo(path));
			Directory.Delete(path, recursive: true);
		}
		catch (Exception ex)
		{
			_log($"{BridgeTag} Temporary HUD clone cleanup skipped: {ex.Message}");
		}
	}

	private static void ClearReadOnlyAttributes(DirectoryInfo directory)
	{
		if (!directory.Exists) return;

		foreach (var file in directory.GetFiles())
		{
			file.Attributes = FileAttributes.Normal;
		}

		foreach (var subDir in directory.GetDirectories())
		{
			ClearReadOnlyAttributes(subDir);
			subDir.Attributes = FileAttributes.Normal;
		}

		directory.Attributes = FileAttributes.Normal;
	}

}
