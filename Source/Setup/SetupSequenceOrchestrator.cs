using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Configuration;

namespace ComfyUI_Nexus.Setup;

internal sealed class SetupSequenceOrchestrator
{
	private readonly List<SetupStep> _steps = new();
	private readonly SetupStepContext _context;
	private readonly NexusToolingEnvironment _tooling;
	private readonly SetupSettingsService _settingsService;

	internal event Action<string>? OnMessage;
	internal event Action<double, string>? OnProgress;

	internal SetupSequenceOrchestrator(
		NexusToolingEnvironment tooling,
		ComfyInstallService installService,
		NexusServerProcessController serverProcesses)
	{
		_tooling = tooling ?? throw new ArgumentNullException(nameof(tooling));
		ArgumentNullException.ThrowIfNull(installService);
		_settingsService = installService.SettingsService;
		var detectorService = new Services.CoreLinkDetector(installService);
		var serverService = new Services.ComfyServerProcessService(serverProcesses, _settingsService, installService.Paths);
		var launchService = new Services.NexusAppEntryService(_settingsService);

		// Relay events from all services to the UI
		detectorService.OnMessage = msg => OnMessage?.Invoke(msg);
		installService.OnMessage = msg => OnMessage?.Invoke(msg);
		installService.OnProgress = (p, msg) => OnProgress?.Invoke(p, msg);
		serverService.OnMessage = msg => OnMessage?.Invoke(msg);
		launchService.OnMessage = msg => OnMessage?.Invoke(msg);

		_context = new SetupStepContext(
			detectorService,
			installService,
			serverService,
			launchService);
		SetSteps(SetupStepCatalog.CreateDefaultSteps());
	}

	internal IReadOnlyList<SetupStep> Steps => _steps;
	internal SetupStepContext Context => _context;
	internal SetupSettingsService SettingsService => _settingsService;

	internal void SetNexusAppEntry(INexusAppEntry appEntry)
		=> _context.NexusAppEntryService.SetEntry(appEntry);

	internal void SetSteps(IEnumerable<SetupStep> steps)
	{
		_steps.Clear();
		_steps.AddRange(steps);
	}

	internal async Task<SetupStepResult> RunStepAsync(string stepId, CancellationToken cancellationToken = default)
	{
		SetupStep? step = _steps.FirstOrDefault(candidate => string.Equals(candidate.Id, stepId, StringComparison.Ordinal));
		if (step == null)
		{
			return new SetupStepResult(false, $"Unknown setup step: {stepId}", 0);
		}

		return RequiresToolingAccess(stepId)
			? await _tooling.RunToolingAsync(_ => step.ExecuteAsync(_context, cancellationToken), cancellationToken)
			: await step.ExecuteAsync(_context, cancellationToken);
	}

	internal async Task<SetupStepResult> RunServerBootAsync(
		bool repairRuntimeBeforeBoot,
		Action<string>? log = null,
		CancellationToken cancellationToken = default)
	{
		await StaleRuntimeProcessCleaner.CleanupBeforeBootAsync(_context.ComfyInstallService.Paths, log, cancellationToken);

		var pendingTasksResult = await RunPendingBootTasksAsync(log, cancellationToken);
		if (!pendingTasksResult.IsSuccess || pendingTasksResult.RequiresSetupHandoff)
		{
			return pendingTasksResult;
		}

		bool pendingVenvDeleteWillRun = IsPendingDirectVenvDelete();
		var pendingVenvDeleteResult = await RunWithToolingAccessAsync(
			() => _context.ComfyInstallService.ApplyPendingComfyVenvDeleteAsync(cancellationToken),
			cancellationToken);
		if (!pendingVenvDeleteResult.IsSuccess)
		{
			return pendingVenvDeleteResult;
		}
		if (pendingVenvDeleteWillRun)
		{
			log?.Invoke("[TASKS] Pending .venv delete applied. Repairing DIRECT Python dependencies before boot.");
			var repairResult = await RunWithToolingAccessAsync(
				() => _context.ComfyInstallService.RepairRuntimeDependenciesAsync(cancellationToken),
				cancellationToken);
			if (!repairResult.IsSuccess) return repairResult;
			log?.Invoke("[TASKS] DIRECT Python dependencies repaired after .venv delete.");
		}

		if (repairRuntimeBeforeBoot)
		{
			log?.Invoke("[RECOVER] Runtime dependency repair requested before server boot.");
			var repairResult = await RunStepAsync(SetupStepIds.Dependencies, cancellationToken);
			if (!repairResult.IsSuccess) return repairResult;

			log?.Invoke("[RECOVER] Runtime dependencies repaired. Continuing server boot.");
			var extensionResult = await RepairManagedExtensionsIfNeededAsync(log, cancellationToken);
			if (!extensionResult.IsSuccess) return extensionResult;
		}

		var modelPathsResult = await SynchronizeExternalModelPathsIfNeededAsync(log, cancellationToken);
		if (!modelPathsResult.IsSuccess) return modelPathsResult;

		var bridgeResult = await RepairNexusBridgeIfNeededAsync(log, cancellationToken);
		if (!bridgeResult.IsSuccess) return bridgeResult;

		log?.Invoke("[BOOT] Initiating Nexus Kernel Process...");
		return await RunStepAsync(SetupStepIds.Server, cancellationToken);
	}

	private async Task<SetupStepResult> RunPendingBootTasksAsync(
		Action<string>? log,
		CancellationToken cancellationToken)
	{
		var tasks = _settingsService.GetRunnableBootTasks();
		if (tasks.Count == 0)
		{
			return new SetupStepResult(true, "No pending boot tasks.", 1);
		}

		log?.Invoke($"[TASKS] Running {tasks.Count} pending boot task(s).");
		foreach (var task in tasks)
		{
			cancellationToken.ThrowIfCancellationRequested();
			log?.Invoke($"[TASKS] Starting {task.Id}.");
			_settingsService.MarkBootTaskInProgress(task.Id);

			SetupStepResult result = RequiresToolingAccess(task.Id)
				? await _tooling.RunToolingAsync(
					_ => RunPendingBootTaskAsync(task, cancellationToken),
					cancellationToken)
				: await RunPendingBootTaskAsync(task, cancellationToken);
			if (!result.IsSuccess)
			{
				_settingsService.FailBootTask(task.Id, result.Message);
				log?.Invoke($"[TASKS] Failed {task.Id}: {result.Message}");
				return result;
			}

			_settingsService.CompleteBootTask(task.Id);
			log?.Invoke($"[TASKS] Completed {task.Id}: {result.Message}");
			if (result.RequiresSetupHandoff)
			{
				return result;
			}
		}

		return new SetupStepResult(true, "Pending boot tasks completed.", 1);
	}

	private async Task<SetupStepResult> SynchronizeExternalModelPathsIfNeededAsync(
		Action<string>? log,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		SetupSettings settings = _settingsService.Settings;
		string comfyPath = _context.ComfyInstallService.Paths.ActiveComfyPath;
		if (settings.ModelLibraryRoots.Count == 0)
		{
			log?.Invoke("[MODEL PATHS] No external model libraries configured.");
			return new SetupStepResult(true, "No external model libraries configured.", 1);
		}

		var syncResult = await Task.Run(() =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!Services.ExtraModelPathsService.NeedsSynchronization(settings, comfyPath))
			{
				return (NeedsSync: false, Result: new ExtraModelPathsResult(true, "External model paths already synchronized."), Transaction: (ExtraModelPathsTransaction?)null);
			}

			ExtraModelPathsResult result = Services.ExtraModelPathsService.TryApply(
				settings,
				comfyPath,
				out ExtraModelPathsTransaction? transaction);
			return (NeedsSync: true, Result: result, Transaction: transaction);
		}, cancellationToken);

		if (!syncResult.NeedsSync)
		{
			log?.Invoke("[MODEL PATHS] External model paths verified before server boot.");
			return new SetupStepResult(true, syncResult.Result.Message, 1);
		}

		if (!syncResult.Result.IsSuccess)
		{
			syncResult.Transaction?.Rollback();
			log?.Invoke($"[MODEL PATHS] extra_model_paths.yaml synchronization failed before server boot: {syncResult.Result.Message}");
			return new SetupStepResult(false, syncResult.Result.Message, 0);
		}

		syncResult.Transaction?.Commit();
		log?.Invoke("[MODEL PATHS] extra_model_paths.yaml synchronized before server boot.");
		return new SetupStepResult(true, syncResult.Result.Message, 1);
	}

	private async Task<SetupStepResult> RunPendingBootTaskAsync(PendingBootTask task, CancellationToken cancellationToken)
		=> task.Id switch
		{
			PendingBootTaskIds.RuntimePurge => (await _context.ComfyInstallService.PurgeRuntimeAsync(cancellationToken)) with { RequiresSetupHandoff = true },
			PendingBootTaskIds.ResetSetup => RunResetSetupTask(),
			PendingBootTaskIds.ResetAll => RunResetAllTask(),
			PendingBootTaskIds.ComfyUpdate => await RunComfyUpdateTaskAsync(cancellationToken),
			PendingBootTaskIds.ExtensionRepair => await RunExtensionRepairTaskAsync(task.TargetFolders, task.Action, cancellationToken),
			PendingBootTaskIds.VenvCreate => await _context.ComfyInstallService.EnsureComfyVenvOnlyAsync(cancellationToken),
			PendingBootTaskIds.VenvRebuild => await _context.ComfyInstallService.RebuildComfyVenvOnlyAsync(cancellationToken),
			PendingBootTaskIds.VenvDelete => await RunVenvDeleteTaskAsync(cancellationToken),
			PendingBootTaskIds.RuntimeRepair => await _context.ComfyInstallService.RepairRuntimeDependenciesAsync(cancellationToken),
			_ => new SetupStepResult(true, $"Unknown pending boot task skipped: {task.Id}", 1)
		};

	private async Task<SetupStepResult> RunVenvDeleteTaskAsync(CancellationToken cancellationToken)
	{
		SetupStepResult deleteResult = await _context.ComfyInstallService.DeleteComfyVenvOnlyAsync(cancellationToken);
		if (!deleteResult.IsSuccess)
		{
			return deleteResult;
		}

		OnMessage?.Invoke("[TASKS] .venv delete switches to DIRECT Python. Repairing dependencies before server boot.");
		SetupStepResult repairResult = await _context.ComfyInstallService.RepairRuntimeDependenciesAsync(cancellationToken);
		if (!repairResult.IsSuccess)
		{
			return repairResult;
		}

		return new SetupStepResult(true, ".venv deleted and DIRECT Python dependencies are ready.", 1);
	}

	private bool IsPendingDirectVenvDelete()
	{
		SetupSettings settings = _settingsService.Settings;
		return settings.PendingVenvDelete
			&& string.Equals(settings.ServerPythonMode, PythonExecutionModes.ConfiguredPython, StringComparison.Ordinal);
	}

	private SetupStepResult RunResetSetupTask()
	{
		_settingsService.ResetSetupPath();
		return new SetupStepResult(true, "Setup settings reset. Returning to setup.", 1, RequiresSetupHandoff: true);
	}

	private SetupStepResult RunResetAllTask()
	{
		_settingsService.ResetAll();
		return new SetupStepResult(true, "All settings reset. Returning to setup.", 1, RequiresSetupHandoff: true);
	}

	private async Task<SetupStepResult> RunComfyUpdateTaskAsync(CancellationToken cancellationToken)
	{
		var updateResult = await _context.ComfyInstallService.UpdateComfyRepositoryAsync(cancellationToken);
		if (!updateResult.IsSuccess) return updateResult;

		return await _context.ComfyInstallService.RepairRuntimeDependenciesAsync(cancellationToken);
	}

	private async Task<SetupStepResult> RepairManagedExtensionsIfNeededAsync(
		Action<string>? log,
		CancellationToken cancellationToken)
	{
		var missingTargets = GetMissingManagedExtensionTargets();
		if (missingTargets.Count == 0)
		{
			log?.Invoke("[RECOVER] Managed extensions verified.");
			return new SetupStepResult(true, "Managed extensions verified.", 1);
		}

		log?.Invoke($"[RECOVER] Repairing managed extension(s): {string.Join(", ", missingTargets)}");
		var extensionResult = await RunWithToolingAccessAsync(
			() => _context.ComfyInstallService.InstallManagedExtensionsAsync(
				missingTargets,
				forceSyncExisting: false,
				reinstallExisting: false,
				cancellationToken),
			cancellationToken);
		if (!extensionResult.IsSuccess) return extensionResult;

		log?.Invoke("[RECOVER] Managed extensions repaired.");
		return extensionResult;
	}

	private async Task<SetupStepResult> RepairNexusBridgeIfNeededAsync(
		Action<string>? log,
		CancellationToken cancellationToken)
	{
		if (_context.ComfyInstallService.IsNexusBridgeExtensionHealthy())
		{
			log?.Invoke("[Bridge] Nexus bridge extension verified before server boot.");
			return new SetupStepResult(true, "Nexus bridge extension verified.", 1);
		}

		log?.Invoke("[Bridge] Nexus bridge extension is missing, incomplete, or outdated. Syncing before server boot...");
		SetupStepResult bridgeResult = await RunWithToolingAccessAsync(
			() => _context.ComfyInstallService.PatchNexusBridgeAsync(cancellationToken),
			cancellationToken);
		if (!bridgeResult.IsSuccess)
		{
			log?.Invoke($"[Bridge] Nexus bridge repair failed before server boot: {bridgeResult.Message}");
			return bridgeResult;
		}

		log?.Invoke("[Bridge] Nexus bridge extension repaired before server boot.");
		return bridgeResult;
	}

	private List<string> GetMissingManagedExtensionTargets()
	{
		string customNodesPath = _context.ComfyInstallService.Paths.ActiveCustomNodesPath;
		var missingTargets = new List<string>();

		AddIfMissing(
			missingTargets,
			HudBridgeInstaller.ManagerExtensionFolderName,
			Path.Combine(customNodesPath, HudBridgeInstaller.ManagerExtensionFolderName, "__init__.py"));
		AddIfMissing(
			missingTargets,
			HudBridgeInstaller.HudExtensionFolderName,
			Path.Combine(customNodesPath, HudBridgeInstaller.HudExtensionFolderName, "__init__.py"));
		AddIfMissing(
			missingTargets,
			HudBridgeInstaller.NexusBridgeExtensionFolderName,
			Path.Combine(customNodesPath, HudBridgeInstaller.NexusBridgeExtensionFolderName, "__init__.py"));

		foreach (var node in _settingsService.Settings.EssentialNodes)
		{
			if (IsBuiltInManagedExtension(node.Folder)) continue;

			string nodePath = Path.Combine(customNodesPath, node.Folder);
			if (!Directory.Exists(nodePath))
			{
				missingTargets.Add(node.Folder);
			}
		}

		return missingTargets;
	}

	private static void AddIfMissing(List<string> targets, string folder, string requiredFilePath)
	{
		if (!File.Exists(requiredFilePath) &&
			!targets.Contains(folder, StringComparer.OrdinalIgnoreCase))
		{
			targets.Add(folder);
		}
	}

	private static bool IsBuiltInManagedExtension(string folder)
		=> string.Equals(folder, HudBridgeInstaller.ManagerExtensionFolderName, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(folder, HudBridgeInstaller.HudExtensionFolderName, StringComparison.OrdinalIgnoreCase);

	private static bool RequiresToolingAccess(string stepOrTaskId)
		=> stepOrTaskId is
			SetupStepIds.CoreLink or
			SetupStepIds.ComfyCore or
			SetupStepIds.BaseResources or
			SetupStepIds.Manager or
			SetupStepIds.HudBridge or
			SetupStepIds.Dependencies or
			PendingBootTaskIds.ComfyUpdate or
			PendingBootTaskIds.ExtensionRepair or
			PendingBootTaskIds.VenvCreate or
			PendingBootTaskIds.VenvRebuild or
			PendingBootTaskIds.VenvDelete or
			PendingBootTaskIds.RuntimeRepair;

	private Task<SetupStepResult> RunWithToolingAccessAsync(
		Func<Task<SetupStepResult>> operation,
		CancellationToken cancellationToken)
		=> _tooling.RunToolingAsync(_ => operation(), cancellationToken);

	private async Task<SetupStepResult> RunExtensionRepairTaskAsync(
		IReadOnlyList<string> targetFolders,
		string action,
		CancellationToken cancellationToken)
	{
		bool reinstallExisting = string.Equals(action, PendingBootTaskActions.ExtensionReinstall, StringComparison.Ordinal);
		var managerResult = await _context.ComfyInstallService.InstallManagedExtensionsAsync(
			targetFolders,
			forceSyncExisting: true,
			reinstallExisting,
			cancellationToken);
		if (!managerResult.IsSuccess) return managerResult;

		return await _context.ComfyInstallService.InstallDependenciesAsync(cancellationToken);
	}
}
