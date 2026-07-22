namespace ComfyUI_Nexus.Setup.Services;

using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Models;

internal sealed class HudBridgeInstaller
{
	internal const string ManagerExtensionFolderName = "ComfyUI-Manager";
	internal const string HudExtensionFolderName = "ComfyUI-HUD";
	internal const string NexusBridgeExtensionFolderName = "ComfyUI-NexusBridge";
	internal const string NexusBridgePackageFolderName = "NexusBridge";

	private const string BridgeTag = "[Bridge]";
	private const string HudTag = "[HUD]";
	private static readonly string[] NexusBridgeRequiredFiles =
	[
		"__init__.py",
		Path.Combine("js", "nexus_bridge.js"),
		Path.Combine("js", "nexus", "index.js"),
		Path.Combine("js", "nexus", "actions.js"),
		Path.Combine("js", "nexus", "asset_modules.js"),
		Path.Combine("js", "nexus", "auto_queue_mode.js"),
		Path.Combine("js", "nexus", "canvas_mode.js"),
		Path.Combine("js", "nexus", "current_run_cancel.js"),
		Path.Combine("js", "nexus", "settings_dialog.js"),
		Path.Combine("js", "nexus", "settings_store.js"),
		Path.Combine("js", "nexus", "workflow_context_menu.js"),
		Path.Combine("js", "nexus", "workflow_action_menu.js")
	];

	private readonly Action<string> _log;
	private readonly GitRepositoryService _gitRepositoryService;
	private readonly NexusToolingEnvironment _tooling;
	private readonly SetupSettingsService _settingsService;
	private readonly NexusComfyRuntimePaths _paths;

	internal HudBridgeInstaller(
		Action<string> log,
		GitRepositoryService gitRepositoryService,
		NexusToolingEnvironment tooling,
		SetupSettingsService settingsService,
		NexusComfyRuntimePaths paths)
	{
		_log = log;
		_gitRepositoryService = gitRepositoryService;
		_tooling = tooling;
		_settingsService = settingsService;
		_paths = paths;
	}

	internal Task<SetupStepResult> InstallHudBridgeAsync(CancellationToken cancellationToken)
		=> InstallHudBridgeAsync(overwriteExisting: false, cancellationToken);

	internal async Task<SetupStepResult> RestoreHudSamplesAsync(
		bool overwriteExisting,
		CancellationToken cancellationToken)
	{
		string sourcePath = Path.Combine(_paths.ActiveCustomNodesPath, HudExtensionFolderName, "hud_sample");
		string targetPath = Path.Combine(_paths.ActiveWorkflowsPath, "hud_sample");
		try
		{
			int copied = await Task.Run(
				() => CopyHudSamples(sourcePath, targetPath, overwriteExisting, cancellationToken),
				cancellationToken);
			_log($"{HudTag} HUD samples restored. copied={copied}, overwrite={overwriteExisting}");
			return new SetupStepResult(true, copied == 0
				? "HUD samples are already up to date."
				: $"HUD sample restore completed. copied={copied}", 1);
		}
		catch (Exception ex)
		{
			_log($"{HudTag} HUD sample restore failed: {ex.Message}");
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

			string hudPath = Path.Combine(_paths.ActiveCustomNodesPath, HudExtensionFolderName);
			Directory.CreateDirectory(hudPath);

			int copied = CopyWorktreeFiles(localHudPath, hudPath, cancellationToken);
			_log($"{HudTag} Local ComfyUI-HUD project patched. copied={copied}");

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
			string hudPath = Path.Combine(_paths.ActiveCustomNodesPath, HudExtensionFolderName);
			return Task.FromResult(PatchNexusBridgePayload(hudPath, cancellationToken));
		}
		catch (Exception ex)
		{
			return Task.FromResult(new SetupStepResult(false, $"Nexus bridge patch failed: {ex.Message}", 0));
		}
	}

	internal bool IsNexusBridgeExtensionHealthy()
		=> IsNexusBridgeExtensionHealthy(_paths.ActiveCustomNodesPath);

	internal static bool IsNexusBridgeExtensionHealthy(string customNodesPath)
	{
		if (string.IsNullOrWhiteSpace(customNodesPath)) return false;

		string sourceBridgeDir = Path.Combine(ComfyInstallService.RuntimePackagesPath, NexusBridgePackageFolderName);
		string bridgePath = Path.Combine(customNodesPath, NexusBridgeExtensionFolderName);
		return IsNexusBridgePayloadCurrent(sourceBridgeDir, bridgePath);
	}

	internal bool IsPackagedNexusBridgeAvailable()
	{
		string sourceBridgeDir = Path.Combine(ComfyInstallService.RuntimePackagesPath, NexusBridgePackageFolderName);
		return HasRequiredNexusBridgeFiles(sourceBridgeDir);
	}

	internal async Task<SetupStepResult> InstallHudBridgeAsync(bool overwriteExisting, CancellationToken cancellationToken)
		=> await InstallHudBridgeAsync(
			overwriteExisting ? GitRecoveryMode.SyncExisting : GitRecoveryMode.PresentOnly,
			cancellationToken);

	internal async Task<SetupStepResult> InstallHudBridgeAsync(GitRecoveryMode recoveryMode, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			NexusRuntimeToolingLease? lease = _tooling.CurrentLease;
			string toolingComfyPath = lease?.GetComfyRoot() ?? _paths.ActiveComfyPath;
			string hudPath = Path.Combine(toolingComfyPath, "custom_nodes", HudExtensionFolderName);
			string hudInitPath = Path.Combine(hudPath, "__init__.py");
			_log($"{HudTag} Deploying ComfyUI-HUD...");

			var (gitExe, _) = await _gitRepositoryService.ResolveConfiguredGitAsync(_settingsService.Settings.GitPath, cancellationToken);
			if (gitExe == null) return new SetupStepResult(false, "Git is required for ComfyUI-HUD installation.", 0);

			string hudRepoUrl = _settingsService.Settings.HudRepoUrl;
			var syncResult = await _gitRepositoryService.EnsureRepositoryAsync(
				gitExe,
				hudRepoUrl,
				hudPath,
				HudTag,
				recoveryMode,
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
			return new SetupStepResult(false, $"HUD deployment failed: {ex.Message}", 0);
		}
	}

	private SetupStepResult PatchNexusBridgePayload(string? hudPath, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		string sourceBridgeDir = Path.Combine(ComfyInstallService.RuntimePackagesPath, NexusBridgePackageFolderName);
		string customNodesPath = _paths.ActiveCustomNodesPath;
		ComfyFrontendCompatibilityService.DeleteLegacyHudBackups(customNodesPath, _log);

		string targetBridgeDir = Path.Combine(customNodesPath, NexusBridgeExtensionFolderName);

		if (!HasRequiredNexusBridgeFiles(sourceBridgeDir))
		{
			return new SetupStepResult(false, $"NexusBridge package is missing or incomplete: {sourceBridgeDir}", 0);
		}

		Directory.CreateDirectory(targetBridgeDir);
		int copied = CopyWorktreeFiles(sourceBridgeDir, targetBridgeDir, cancellationToken);
		ComfyFrontendCompatibilityService.CleanupLegacyHudBridgeOverlay(hudPath, _log);
		int patchedHudFiles = ComfyFrontendCompatibilityService.PatchHudDuplicateExtensionGuard(hudPath, cancellationToken);
		_log($"{BridgeTag} Nexus bridge extension patched. target={targetBridgeDir}, copied={copied}");
		if (patchedHudFiles > 0)
		{
			_log($"{HudTag} HUD duplicate extension guard patched. files={patchedHudFiles}");
		}

		if (!IsNexusBridgePayloadCurrent(sourceBridgeDir, targetBridgeDir))
		{
			return new SetupStepResult(false, "Nexus bridge patch finished, but the installed payload does not match the packaged bridge.", 0);
		}

		return new SetupStepResult(true, "Nexus bridge extension successfully synced and patched.", copied);
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
				!string.Equals(name, HudExtensionFolderName, StringComparison.OrdinalIgnoreCase) ||
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

	private static bool HasRequiredNexusBridgeFiles(string bridgePath)
	{
		if (string.IsNullOrWhiteSpace(bridgePath) || !Directory.Exists(bridgePath))
		{
			return false;
		}

		return NexusBridgeRequiredFiles.All(relativePath => File.Exists(Path.Combine(bridgePath, relativePath)));
	}

	private static bool IsNexusBridgePayloadCurrent(string sourceBridgeDir, string targetBridgeDir)
	{
		if (!HasRequiredNexusBridgeFiles(sourceBridgeDir) || !HasRequiredNexusBridgeFiles(targetBridgeDir))
		{
			return false;
		}

		try
		{
			foreach (string sourceFile in Directory.EnumerateFiles(sourceBridgeDir, "*", SearchOption.AllDirectories))
			{
				string relativePath = Path.GetRelativePath(sourceBridgeDir, sourceFile);
				string targetFile = Path.Combine(targetBridgeDir, relativePath);
				if (!AreFilesIdentical(sourceFile, targetFile))
				{
					return false;
				}
			}
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}

		return true;
	}

	private static bool AreFilesIdentical(string sourceFile, string targetFile)
	{
		var sourceInfo = new FileInfo(sourceFile);
		var targetInfo = new FileInfo(targetFile);
		if (!targetInfo.Exists || sourceInfo.Length != targetInfo.Length)
		{
			return false;
		}

		const int BufferSize = 32 * 1024;
		var sourceBuffer = new byte[BufferSize];
		var targetBuffer = new byte[BufferSize];
		using var source = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read);
		using var target = new FileStream(targetFile, FileMode.Open, FileAccess.Read, FileShare.Read);
		while (true)
		{
			int sourceRead = source.Read(sourceBuffer, 0, sourceBuffer.Length);
			int targetRead = target.Read(targetBuffer, 0, targetBuffer.Length);
			if (sourceRead != targetRead)
			{
				return false;
			}

			if (sourceRead == 0)
			{
				return true;
			}

			if (!sourceBuffer.AsSpan(0, sourceRead).SequenceEqual(targetBuffer.AsSpan(0, targetRead)))
			{
				return false;
			}
		}
	}

}
