using ComfyUI_Nexus.Setup.Models;

namespace ComfyUI_Nexus.Setup;

internal sealed class SetupSequenceOrchestrator
{
	private readonly List<SetupStep> _steps = new();
	private readonly SetupStepContext _context;

	internal event Action<string>? OnMessage;
	internal event Action<double, string>? OnProgress;

	internal SetupSequenceOrchestrator()
	{
		var detectorService = new Services.CoreLinkDetector();
		var installService = new Services.ComfyInstallService();
		var serverService = new Services.ComfyServerProcessService();
		var launchService = new Services.NexusAppEntryService();

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

		return await step.ExecuteAsync(_context, cancellationToken);
	}

	internal async Task<SetupStepResult> RunServerBootAsync(
		bool repairRuntimeBeforeBoot,
		Action<string>? log = null,
		CancellationToken cancellationToken = default)
	{
		var pendingTasksResult = await RunPendingBootTasksAsync(log, cancellationToken);
		if (!pendingTasksResult.IsSuccess || pendingTasksResult.RequiresSetupHandoff)
		{
			return pendingTasksResult;
		}

		bool pendingVenvDeleteWillRun = IsPendingDirectVenvDelete();
		var pendingVenvDeleteResult = await _context.ComfyInstallService.ApplyPendingComfyVenvDeleteAsync(cancellationToken);
		if (!pendingVenvDeleteResult.IsSuccess)
		{
			return pendingVenvDeleteResult;
		}
		if (pendingVenvDeleteWillRun)
		{
			log?.Invoke("[TASKS] Pending .venv delete applied. Repairing DIRECT Python dependencies before boot.");
			var repairResult = await _context.ComfyInstallService.RepairRuntimeDependenciesAsync(cancellationToken);
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

		log?.Invoke("[BOOT] Initiating Nexus Kernel Process...");
		return await RunStepAsync(SetupStepIds.Server, cancellationToken);
	}

	private async Task<SetupStepResult> RunPendingBootTasksAsync(
		Action<string>? log,
		CancellationToken cancellationToken)
	{
		var settingsService = Services.SetupSettingsService.Instance;
		var tasks = settingsService.GetRunnableBootTasks();
		if (tasks.Count == 0)
		{
			return new SetupStepResult(true, "No pending boot tasks.", 1);
		}

		log?.Invoke($"[TASKS] Running {tasks.Count} pending boot task(s).");
		foreach (var task in tasks)
		{
			cancellationToken.ThrowIfCancellationRequested();
			log?.Invoke($"[TASKS] Starting {task.Id}.");
			settingsService.MarkBootTaskInProgress(task.Id);

			SetupStepResult result = await RunPendingBootTaskAsync(task, cancellationToken);
			if (!result.IsSuccess)
			{
				settingsService.FailBootTask(task.Id, result.Message);
				log?.Invoke($"[TASKS] Failed {task.Id}: {result.Message}");
				return result;
			}

			settingsService.CompleteBootTask(task.Id);
			log?.Invoke($"[TASKS] Completed {task.Id}: {result.Message}");
			if (result.RequiresSetupHandoff)
			{
				return result;
			}
		}

		return new SetupStepResult(true, "Pending boot tasks completed.", 1);
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

	private static bool IsPendingDirectVenvDelete()
	{
		SetupSettings settings = Services.SetupSettingsService.Instance.Settings;
		return settings.PendingVenvDelete
			&& string.Equals(settings.ServerPythonMode, PythonExecutionModes.ConfiguredPython, StringComparison.Ordinal);
	}

	private static SetupStepResult RunResetSetupTask()
	{
		Services.SetupSettingsService.Instance.ResetSetupPath();
		return new SetupStepResult(true, "Setup settings reset. Returning to setup.", 1, RequiresSetupHandoff: true);
	}

	private static SetupStepResult RunResetAllTask()
	{
		Services.SetupSettingsService.Instance.ResetAll();
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
		var extensionResult = await _context.ComfyInstallService.InstallManagedExtensionsAsync(
			missingTargets,
			forceSyncExisting: false,
			reinstallExisting: false,
			cancellationToken);
		if (!extensionResult.IsSuccess) return extensionResult;

		log?.Invoke("[RECOVER] Managed extensions repaired.");
		return extensionResult;
	}

	private static List<string> GetMissingManagedExtensionTargets()
	{
		string customNodesPath = Services.ComfyPathResolver.ResolveActiveCustomNodesPath();
		var missingTargets = new List<string>();

		AddIfMissing(
			missingTargets,
			"ComfyUI-Manager",
			Path.Combine(customNodesPath, "ComfyUI-Manager", "__init__.py"));
		AddIfMissing(
			missingTargets,
			"ComfyUI-HUD",
			Path.Combine(customNodesPath, "ComfyUI-HUD", "__init__.py"));

		foreach (var node in Services.SetupSettingsService.Instance.Settings.EssentialNodes)
		{
			if (node.Folder is "ComfyUI-Manager" or "ComfyUI-HUD") continue;

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
		if (!File.Exists(requiredFilePath))
		{
			targets.Add(folder);
		}
	}

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
