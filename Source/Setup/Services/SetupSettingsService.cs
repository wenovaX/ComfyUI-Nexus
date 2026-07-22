namespace ComfyUI_Nexus.Setup.Services;

using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Setup.Models;

internal sealed class SetupSettingsService
{
	private readonly string _settingsPath;

	internal SetupSettings Settings { get; private set; }

	internal SetupSettingsService()
	{
		_settingsPath = NexusStorageLayout.SettingsPath;
		Settings = SetupSettings.Load(_settingsPath);
		NormalizeSettings();

		// Ensure file exists with current defaults if missing
		if (!File.Exists(_settingsPath))
		{
			Settings.Save(_settingsPath);
		}
	}

	internal void Reload()
	{
		Settings = SetupSettings.Load(_settingsPath);
		NormalizeSettings();
	}

	internal void Save()
	{
		Settings.Save(_settingsPath);
	}

	internal bool TrySave()
		=> Settings.TrySave(_settingsPath);

	internal void ResetLaunchOptions()
	{
		var defaults = new SetupSettings();

		Settings.GpuId = defaults.GpuId;
		Settings.ListenAddress = defaults.ListenAddress;
		Settings.ServerPort = defaults.ServerPort;
		Settings.ServerStartupTimeoutSeconds = defaults.ServerStartupTimeoutSeconds;
		Settings.PortProbeIntervalMilliseconds = defaults.PortProbeIntervalMilliseconds;
		Settings.ServerLogFile = defaults.ServerLogFile;
		Settings.ServerLogTailIntervalMilliseconds = defaults.ServerLogTailIntervalMilliseconds;
		Settings.LastActivePort = defaults.LastActivePort;
		Settings.LastLaunchSuccessful = defaults.LastLaunchSuccessful;
		Settings.PendingVenvDelete = defaults.PendingVenvDelete;
		Settings.PendingRuntimePurge = defaults.PendingRuntimePurge;
		Settings.RuntimePurgeInProgress = defaults.RuntimePurgeInProgress;
		RemoveBootTask(PendingBootTaskIds.RuntimePurge, save: false);

		Save();
	}

	internal void ResetSetupPath()
	{
		var defaults = new SetupSettings();

		Settings.InstallMode = defaults.InstallMode;
		Settings.ComfyPath = defaults.ComfyPath;
		Settings.ComfyCoreSource = defaults.ComfyCoreSource;
		Settings.GitSource = defaults.GitSource;
		Settings.GitPath = defaults.GitPath;
		Settings.PythonSource = defaults.PythonSource;
		Settings.PythonPath = defaults.PythonPath;
		Settings.ServerPythonMode = defaults.ServerPythonMode;
		Settings.PendingVenvDelete = defaults.PendingVenvDelete;
		Settings.PortableOnly = defaults.PortableOnly;
		Settings.LastActivePort = defaults.LastActivePort;
		Settings.LastLaunchSuccessful = defaults.LastLaunchSuccessful;
		Settings.PendingRuntimePurge = defaults.PendingRuntimePurge;
		Settings.RuntimePurgeInProgress = defaults.RuntimePurgeInProgress;
		RemoveBootTask(PendingBootTaskIds.RuntimePurge, save: false);
		RemoveBootTask(PendingBootTaskIds.ResetSetup, save: false);

		NormalizeSettings();
		Save();
	}

	internal void ResetAll()
	{
		Settings = new SetupSettings();
		NormalizeSettings();
		Save();
	}

	internal void UseLocalRuntime()
	{
		Settings.InstallMode = SetupInstallModes.LocalRuntime;
		Settings.ComfyPath = string.Empty;
		Settings.ServerPythonMode = PythonExecutionModes.Venv;
		Settings.PendingVenvDelete = false;
		Settings.PendingRuntimePurge = false;
		Settings.RuntimePurgeInProgress = false;
		Save();
	}

	internal void UseExistingComfyPath(string comfyPath)
	{
		Settings.InstallMode = SetupInstallModes.ExistingComfyPath;
		Settings.ComfyPath = comfyPath;
		Settings.ServerPythonMode = PythonExecutionModes.Venv;
		Settings.PendingVenvDelete = false;
		Settings.PendingRuntimePurge = false;
		Settings.RuntimePurgeInProgress = false;
		Save();
	}

	internal void ScheduleRuntimePurge()
	{
		Settings.PendingRuntimePurge = true;
		Settings.RuntimePurgeInProgress = false;
		EnqueueBootTask(PendingBootTaskIds.RuntimePurge, save: false);
		Save();
	}

	internal void MarkRuntimePurgeInProgress()
	{
		Settings.PendingRuntimePurge = false;
		Settings.RuntimePurgeInProgress = true;
		Save();
	}

	internal void CompleteRuntimePurgeAndResetSetup()
	{
		ResetAll();
		Settings.PendingRuntimePurge = false;
		Settings.RuntimePurgeInProgress = false;
		Settings.LastActivePort = null;
		Settings.LastLaunchSuccessful = false;
		Settings.ActiveServerLaunchSettings = null;
		RemoveBootTask(PendingBootTaskIds.RuntimePurge, save: false);
		Save();
	}

	internal void ClearRuntimePurgeFlags()
	{
		Settings.PendingRuntimePurge = false;
		Settings.RuntimePurgeInProgress = false;
		RemoveBootTask(PendingBootTaskIds.RuntimePurge, save: false);
		Save();
	}

	internal IReadOnlyList<PendingBootTask> GetRunnableBootTasks()
		=> Settings.PendingBootTasks
			.Where(task => !string.IsNullOrWhiteSpace(task.Id))
			.OrderBy(task => GetTaskOrder(task.Id))
			.ThenBy(task => task.CreatedAtUtc)
			.ToList();

	internal bool HasRunnableBootTasks()
		=> GetRunnableBootTasks().Count > 0;

	internal bool HasBootTask(string taskId)
		=> Settings.PendingBootTasks.Any(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));

	internal void EnqueueBootTask(
		string taskId,
		bool save = true,
		string origin = "",
		IEnumerable<string>? targetFolders = null,
		string action = "")
	{
		RemoveMutuallyExclusiveBootTasks(taskId);
		var targets = targetFolders?
			.Where(target => !string.IsNullOrWhiteSpace(target))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList() ?? new List<string>();

		if (FindBootTask(taskId) is { } existingTask)
		{
			existingTask.TargetFolders = targets;
			existingTask.Origin = origin;
			existingTask.Action = action;
			if (save) Save();
			return;
		}

		Settings.PendingBootTasks.Add(new PendingBootTask
		{
			Id = taskId,
			Origin = origin,
			TargetFolders = targets,
			Action = action
		});
		if (save) Save();
	}

	internal void MarkBootTaskInProgress(string taskId)
	{
		if (FindBootTask(taskId) is not { } task) return;

		task.State = PendingBootTaskStates.InProgress;
		task.StartedAtUtc = DateTime.UtcNow;
		task.LastError = string.Empty;
		Save();
	}

	internal void CompleteBootTask(string taskId)
		=> RemoveBootTask(taskId, save: true);

	internal void FailBootTask(string taskId, string error)
	{
		if (FindBootTask(taskId) is not { } task) return;

		task.State = PendingBootTaskStates.Pending;
		task.LastError = error;
		Save();
	}

	internal void CancelBootTask(string taskId, bool save = true)
		=> RemoveBootTask(taskId, save);

	internal void CancelPendingVenvDelete()
	{
		Settings.PendingVenvDelete = false;
		RemoveBootTask(PendingBootTaskIds.VenvDelete, save: false);
		Save();
	}

	private PendingBootTask? FindBootTask(string taskId)
		=> Settings.PendingBootTasks.FirstOrDefault(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));

	private void RemoveBootTask(string taskId, bool save)
	{
		Settings.PendingBootTasks.RemoveAll(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));
		if (save) Save();
	}

	private void RemoveMutuallyExclusiveBootTasks(string taskId)
	{
		if (taskId is not (PendingBootTaskIds.VenvCreate or PendingBootTaskIds.VenvRebuild or PendingBootTaskIds.VenvDelete))
		{
			return;
		}

		Settings.PendingBootTasks.RemoveAll(task =>
			(task.Id is PendingBootTaskIds.VenvCreate or PendingBootTaskIds.VenvRebuild or PendingBootTaskIds.VenvDelete)
			&& !string.Equals(task.Id, taskId, StringComparison.Ordinal));
	}

	private static int GetTaskOrder(string taskId)
		=> taskId switch
		{
			PendingBootTaskIds.RuntimePurge => 0,
			PendingBootTaskIds.ResetAll => 1,
			PendingBootTaskIds.ResetSetup => 2,
			PendingBootTaskIds.ComfyUpdate => 10,
			PendingBootTaskIds.VenvDelete => 30,
			PendingBootTaskIds.VenvRebuild => 31,
			PendingBootTaskIds.VenvCreate => 32,
			PendingBootTaskIds.RuntimeRepair => 40,
			PendingBootTaskIds.ExtensionRepair => 50,
			_ => 100
		};

	private void NormalizeSettings()
	{
		if (!SetupInstallModes.IsKnown(Settings.InstallMode))
		{
			Settings.InstallMode = SetupInstallModes.LocalRuntime;
		}

		if (!PythonExecutionModes.IsKnown(Settings.ServerPythonMode))
		{
			Settings.ServerPythonMode = PythonExecutionModes.Venv;
		}

		if (!ComfyCoreSources.IsKnown(Settings.ComfyCoreSource))
		{
			Settings.ComfyCoreSource = ComfyCoreSources.RemoteLatest;
		}

		if (string.Equals(Settings.GitSource, "builtin", StringComparison.Ordinal))
		{
			Settings.GitPath = Path.Combine(NexusStorageLayout.LocalRuntimeRoot, "Installed", "Git", "cmd", "git.exe");
		}

		if (string.Equals(Settings.PythonSource, "builtin", StringComparison.Ordinal))
		{
			Settings.PythonPath = Path.Combine(NexusStorageLayout.LocalRuntimeRoot, "Installed", "Python", "python.exe");
		}

		Settings.PendingBootTasks ??= new List<PendingBootTask>();
		Settings.ModelLibraryRoots ??= new List<string>();
		if (!PipCacheModes.IsKnown(Settings.PipCacheMode))
		{
			Settings.PipCacheMode = string.IsNullOrWhiteSpace(Settings.PipCachePath)
				? PipCacheModes.NexusDefault
				: PipCacheModes.Custom;
		}

		Settings.PipCachePath = NormalizeOptionalPath(Settings.PipCachePath);
		Settings.RuntimeBackupPath = NormalizeOptionalPath(Settings.RuntimeBackupPath);
		if (!RuntimeBackupFormats.IsKnown(Settings.RuntimeBackupFormat))
		{
			Settings.RuntimeBackupFormat = RuntimeBackupFormats.Folder;
		}
		NormalizeModelRoots();
	}

	private static string NormalizeOptionalPath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return string.Empty;
		}

		try
		{
			return Path.GetFullPath(path.Trim())
				.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}
		catch
		{
			return string.Empty;
		}
	}

	private void NormalizeModelRoots()
	{
		if (!string.IsNullOrWhiteSpace(Settings.LegacyPrimaryModelRoot))
		{
			Settings.ModelLibraryRoots.Insert(0, Settings.LegacyPrimaryModelRoot);
		}

		if (Settings.LegacyAdditionalModelRoots is { Count: > 0 })
		{
			Settings.ModelLibraryRoots.AddRange(Settings.LegacyAdditionalModelRoots);
		}

		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		Settings.ModelLibraryRoots = Settings.ModelLibraryRoots
			.Select(ExtraModelPathsService.NormalizeFileSystemPath)
			.Where(path => path.Length > 0 && seen.Add(path))
			.ToList();
		Settings.LegacyPrimaryModelRoot = null;
		Settings.LegacyAdditionalModelRoots = null;
	}
}
