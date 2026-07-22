namespace ComfyUI_Nexus.Views.Overlays;

using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;

public partial class SettingsOverlayView
{	private async void OnMaintenanceClearServerLogClicked(object? sender, EventArgs e)
	{
		await RunMaintenanceOperationAsync(
			"clear-server-log",
			LocalizationManager.Text("settings.logs.clear_title"),
			LocalizationManager.Text("settings.logs.clear_message"),
			ClearServerLogAsync,
			LocalizationManager.Text("settings.logs.clear_complete"),
			reloadSettings: false);
	}

	private async void OnMaintenanceResetSettingsClicked(object? sender, EventArgs e)
	{
		await ScheduleMaintenanceBootTaskAsync(
			"reset-settings",
			PendingBootTaskIds.ResetAll,
			LocalizationManager.Text("settings.maintenance.reset_settings_title"),
			LocalizationManager.Text("settings.maintenance.reset_settings_message"),
			LocalizationManager.Text("settings.maintenance.reset_settings_scheduled"));
	}

	private async void OnMaintenanceBackupRuntimeClicked(object? sender, EventArgs e)
	{
		const string operationId = "backup-runtime-data";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		var backupTargets = GetSelectedRuntimeBackupTargets();
		if (backupTargets.Count == 0)
		{
			EndOperation(operationId);
			return;
		}

		SetMaintenanceBusy(true);
		int generation = ++_runtimeBackupAnalysisGeneration;
		RuntimeBackupActivity.IsVisible = true;
		RuntimeBackupActivity.IsRunning = true;
		RuntimeBackupAnalysisLabel.Text = LocalizationManager.Text("settings.runtime_backup.calculating");
		RuntimeBackupAnalysisLabel.TextColor = SettingsInfoTextColor;
		MaintenanceBackupProgressBar.Progress = 0;
		try
		{
			RuntimeBackupAnalysis analysis = await ComfyInstall.AnalyzeRuntimeBackupAsync(
				backupTargets,
				CancellationToken.None);
			if (generation != _runtimeBackupAnalysisGeneration)
			{
				return;
			}

			_runtimeBackupAnalysis = analysis;
			RuntimeBackupActivity.IsRunning = false;
			RuntimeBackupActivity.IsVisible = false;
			RefreshRuntimeBackupCard();
			if (!analysis.IsSuccess)
			{
				RuntimeBackupAnalysisLabel.Text = analysis.AvailableBytes >= 0
					? LocalizationManager.Format(
						"settings.runtime_backup.insufficient_space",
						RuntimeBackupService.FormatBytes(analysis.RequiredBytes),
						RuntimeBackupService.FormatBytes(analysis.AvailableBytes))
					: LocalizationManager.Format(
						"settings.runtime_backup.analysis_failed",
						analysis.Message);
				RuntimeBackupAnalysisLabel.TextColor = SettingsFailureTextColor;
				return;
			}

			string formatLabel = IsRuntimeBackupFormat(RuntimeBackupFormats.Zip)
				? LocalizationManager.Text("settings.runtime_backup.zip")
				: LocalizationManager.Text("settings.runtime_backup.folder");
			RuntimeBackupAnalysisLabel.Text = LocalizationManager.Format(
				"settings.runtime_backup.analysis_ready",
				RuntimeBackupService.FormatBytes(analysis.SourceBytes),
				analysis.FileCount,
				RuntimeBackupService.FormatBytes(analysis.RequiredBytes),
				RuntimeBackupService.FormatBytes(analysis.AvailableBytes));
			RuntimeBackupAnalysisLabel.TextColor = SettingsSuccessTextColor;

			string selectedTargets = string.Join(", ", analysis.Targets.Select(GetRuntimeBackupLabel));
			bool confirmed = await _appManager.Dialogs.ConfirmAsync(
				LocalizationManager.Text("settings.runtime_backup.confirm_title"),
				LocalizationManager.Format(
					"settings.runtime_backup.confirm_message",
					selectedTargets,
					formatLabel,
					RuntimeBackupService.FormatBytes(analysis.SourceBytes),
					RuntimeBackupService.FormatBytes(analysis.RequiredBytes),
					RuntimeBackupService.FormatBytes(analysis.AvailableBytes),
					analysis.BackupRoot),
				LocalizationManager.Text("settings.runtime_backup.start"),
				LocalizationManager.Text("common.cancel"));
			if (!confirmed)
			{
				return;
			}

			RuntimeBackupAnalysis finalCheck = ComfyInstall.RefreshRuntimeBackupSpace(analysis);
			if (!finalCheck.IsSuccess)
			{
				RuntimeBackupAnalysisLabel.Text = finalCheck.AvailableBytes >= 0
					? LocalizationManager.Format(
						"settings.runtime_backup.insufficient_space",
						RuntimeBackupService.FormatBytes(finalCheck.RequiredBytes),
						RuntimeBackupService.FormatBytes(finalCheck.AvailableBytes))
					: LocalizationManager.Format("settings.runtime_backup.analysis_failed", finalCheck.Message);
				RuntimeBackupAnalysisLabel.TextColor = SettingsFailureTextColor;
				return;
			}

			BeginComfyOperationLog("RUNTIME BACKUP");
			var service = ComfyInstall;
			Action<string>? previousLogHandler = service.OnMessage;
			Action<double, string>? previousProgressHandler = service.OnProgress;
			SetupStepResult result;
			try
			{
				service.OnMessage = AppendComfyOperationLog;
				service.OnProgress = UpdateMaintenanceBackupProgress;
				result = await service.BackupRuntimeDataAsync(
					finalCheck,
					_editor.Draft.RuntimeBackupFormat,
					CancellationToken.None);
			}
			finally
			{
				service.OnMessage = previousLogHandler;
				service.OnProgress = previousProgressHandler;
			}

			string resultMessage = result.IsSuccess
				? LocalizationManager.Format(
					"settings.runtime_backup.backup_complete",
					result.Message.StartsWith("Runtime backup completed: ", StringComparison.Ordinal)
						? result.Message["Runtime backup completed: ".Length..]
						: result.Message)
				: LocalizationManager.Format("settings.runtime_backup.operation_failed", result.Message);
			RuntimeBackupAnalysisLabel.Text = resultMessage;
			RuntimeBackupAnalysisLabel.TextColor = result.IsSuccess
				? SettingsSuccessTextColor
				: SettingsFailureTextColor;
			CompleteComfyOperationLog(result.IsSuccess, resultMessage);
			if (result.IsSuccess)
			{
				MaintenanceBackupProgressBar.Progress = 1;
			}
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETTINGS] Runtime backup failed");
			RuntimeBackupAnalysisLabel.Text = LocalizationManager.Format(
				"settings.runtime_backup.operation_failed",
				ex.Message);
			RuntimeBackupAnalysisLabel.TextColor = SettingsFailureTextColor;
			CompleteComfyOperationLog(false, ex.Message);
		}
		finally
		{
			RuntimeBackupActivity.IsRunning = false;
			RuntimeBackupActivity.IsVisible = false;
			SetMaintenanceBusy(false);
			EndOperation(operationId);
		}
	}

	private async void OnMaintenanceRestoreRuntimeClicked(object? sender, EventArgs e)
	{
		const string operationId = "analyze-runtime-restore";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		string? backupPath = await PickRuntimeRestoreFolderAsync();
		if (string.IsNullOrWhiteSpace(backupPath))
		{
			EndOperation(operationId);
			return;
		}

		SetMaintenanceBusy(true);
		await SetSettingsOperationBlockerVisibleAsync(
			true,
			LocalizationManager.Text("settings.runtime_backup.restore_analyzing_title"),
			LocalizationManager.Text("settings.runtime_backup.restore_analyzing_detail"));
		try
		{
			RuntimeRestoreAnalysis analysis = await ComfyInstall.AnalyzeRuntimeRestoreAsync(
				backupPath,
				CancellationToken.None);
			if (!analysis.IsSuccess)
			{
				RuntimeBackupAnalysisLabel.Text = analysis.AvailableBytes >= 0
					? LocalizationManager.Format(
						"settings.runtime_backup.insufficient_space",
						RuntimeBackupService.FormatBytes(analysis.RequiredBytes),
						RuntimeBackupService.FormatBytes(analysis.AvailableBytes))
					: LocalizationManager.Format("settings.runtime_backup.analysis_failed", analysis.Message);
				RuntimeBackupAnalysisLabel.TextColor = SettingsFailureTextColor;
				return;
			}

			RuntimeBackupAnalysisLabel.Text = LocalizationManager.Format(
				"settings.runtime_backup.restore_analysis_ready",
				analysis.AddCount,
				analysis.ReplaceCount,
				analysis.UnchangedCount,
				analysis.RetainedCount);
			RuntimeBackupAnalysisLabel.TextColor = SettingsSuccessTextColor;
			if (analysis.AddCount + analysis.ReplaceCount == 0)
			{
				await _appManager.Dialogs.AlertAsync(
					LocalizationManager.Text("settings.runtime_backup.restore_no_changes_title"),
					LocalizationManager.Format(
						"settings.runtime_backup.restore_no_changes_message",
						analysis.UnchangedCount,
						analysis.RetainedCount));
				return;
			}

			bool startRestore = false;
			string preview = string.Join(
				Environment.NewLine,
				analysis.Items
					.Where(item => item.Action is RuntimeRestoreAction.Add or RuntimeRestoreAction.Replace)
					.Take(6)
					.Select(item => $"- {item.RelativePath}"));
			if (analysis.AddCount + analysis.ReplaceCount > 6)
			{
				preview += $"{Environment.NewLine}- ...";
			}
			string mappings = string.Join(
				Environment.NewLine,
				analysis.Targets.Select(target =>
					$"{target}: {(analysis.BackupFormat == RuntimeBackupFormats.Zip ? $"{analysis.BackupPath}!/{target}" : System.IO.Path.Combine(analysis.BackupPath, target))} -> {System.IO.Path.Combine(analysis.ComfyPath, target)}"));

			await _appManager.Dialogs.ChoiceAsync(
				LocalizationManager.Text("settings.runtime_backup.restore_confirm_title"),
				LocalizationManager.Format(
					"settings.runtime_backup.restore_analysis_message",
					analysis.BackupPath,
					analysis.ComfyPath,
					analysis.AddCount,
					analysis.ReplaceCount,
					analysis.UnchangedCount,
					analysis.RetainedCount,
					RuntimeBackupService.FormatBytes(analysis.CopyBytes),
					RuntimeBackupService.FormatBytes(analysis.RequiredBytes),
					RuntimeBackupService.FormatBytes(analysis.AvailableBytes),
					preview,
					mappings),
				[
					new NexusDialogChoice(
						LocalizationManager.Text("settings.runtime_backup.open_change_list"),
						async () =>
						{
							var openResult = await NexusAppManager.Instance.Platform.Shell.OpenPathAsync(analysis.PreviewReportPath);
							if (!openResult.IsSuccess)
							{
								throw new InvalidOperationException(openResult.Message
									?? LocalizationManager.Text("settings.runtime_backup.open_report_failed"));
							}
							return NexusDialogActionResult.KeepOpen;
						}),
					new NexusDialogChoice(
						LocalizationManager.Text("settings.runtime_backup.start_restore"),
						() =>
						{
							startRestore = true;
							return Task.FromResult(NexusDialogActionResult.Close);
						},
						IsDanger: true)
				],
				LocalizationManager.Text("common.cancel"));
			if (!startRestore)
			{
				return;
			}

			var args = new RuntimeRestoreRequestedEventArgs(
				new RuntimeRestoreRequest(
					analysis,
					ServerProcesses.FindServerProcess() != null));
			if (RuntimeRestoreRequested is null)
			{
				RuntimeBackupAnalysisLabel.Text = LocalizationManager.Text("settings.runtime_backup.restore_handler_missing");
				RuntimeBackupAnalysisLabel.TextColor = SettingsFailureTextColor;
				return;
			}

			RuntimeRestoreRequested.Invoke(this, args);
			RuntimeRestoreResult result = await args.Completion.Task;
			RuntimeBackupAnalysisLabel.Text = result.IsSuccess
				? LocalizationManager.Format(
					args.Request.ServerWasRunning
						? "settings.runtime_backup.restore_merge_complete_restarting"
						: "settings.runtime_backup.restore_merge_complete",
					result.CompletedFiles)
				: LocalizationManager.Format("settings.runtime_backup.restore_merge_failed", result.Message);
			RuntimeBackupAnalysisLabel.TextColor = result.IsSuccess ? SettingsSuccessTextColor : SettingsFailureTextColor;
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETTINGS] Runtime restore analysis failed");
			RuntimeBackupAnalysisLabel.Text = LocalizationManager.Format(
				"settings.runtime_backup.analysis_failed",
				ex.Message);
			RuntimeBackupAnalysisLabel.TextColor = SettingsFailureTextColor;
		}
		finally
		{
			await SetSettingsOperationBlockerVisibleAsync(false);
			SetMaintenanceBusy(false);
			EndOperation(operationId);
		}
	}

	private async void OnMaintenanceDeleteBackupClicked(object? sender, EventArgs e)
	{
		string? backupPath = await PickRuntimeBackupFolderToDeleteAsync();
		if (string.IsNullOrWhiteSpace(backupPath))
		{
			SetMaintenanceStatus(LocalizationManager.Text("settings.runtime_backup.none_selected"));
			return;
		}

		string backupName = System.IO.Path.GetFileName(backupPath);
		ResetMaintenanceBackupProgress(LocalizationManager.Format(
			"settings.runtime_backup.ready_to_delete",
			backupName));
		await RunMaintenanceOperationAsync(
			"delete-runtime-backup",
			LocalizationManager.Text("settings.runtime_backup.delete_confirm_title"),
			LocalizationManager.Format("settings.runtime_backup.delete_confirm_message", backupPath),
			ct => ComfyInstall.DeleteRuntimeBackupAsync(backupPath, ct),
			LocalizationManager.Text("settings.runtime_backup.delete_complete"),
			reloadSettings: false,
			preferSuccessMessage: true);
	}

	private async void OnMaintenanceOpenBackupFolderClicked(object? sender, EventArgs e)
	{
		try
		{
			Directory.CreateDirectory(RuntimeBackupsPath);
			var result = await NexusAppManager.Instance.Platform.Shell.OpenPathAsync(RuntimeBackupsPath);
			if (!result.IsSuccess)
			{
				SetMaintenanceStatus(string.IsNullOrWhiteSpace(result.Message)
					? LocalizationManager.Text("settings.runtime_backup.open_failed")
					: result.Message);
			}
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SETTINGS] Unable to open runtime backup folder");
			SetMaintenanceStatus(ex.Message);
		}
	}

	private async void OnMaintenancePurgeRuntimeClicked(object? sender, EventArgs e)
	{
		const string operationId = "purge-local-runtime";
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		try
		{
			string deletePath = ComfyInstallService.InstalledPath;
			string backupPath = ComfyInstall.RuntimeBackupsPath;
			bool confirmed = await ShowConfirmationAsync(
				LocalizationManager.Text("settings.maintenance.purge_runtime_title"),
				LocalizationManager.Format(
					"settings.maintenance.purge_runtime_message",
					deletePath,
					backupPath),
				LocalizationManager.Text("settings.maintenance.prepare_reset"),
				LocalizationManager.Text("common.cancel"));
			if (!confirmed)
			{
				return;
			}

			await SetSettingsOperationBlockerVisibleAsync(
				true,
				LocalizationManager.Text("settings.maintenance.purge_runtime_queue_title"),
				LocalizationManager.Text("settings.maintenance.purge_runtime_queue_detail"));
			try
			{
				await Task.Yield();
				SettingsService.ScheduleRuntimePurge();
				SettingsService.Reload();
				_editor.Reload();
				Refresh(startProbes: false);
				SetMaintenanceStatus(LocalizationManager.Text("settings.maintenance.purge_runtime_scheduled"));
			}
			finally
			{
				await SetSettingsOperationBlockerVisibleAsync(false);
			}

			RuntimePurgeRequested?.Invoke(this, EventArgs.Empty);
		}
		finally
		{
			EndOperation(operationId);
		}
	}

	private async Task ScheduleMaintenanceBootTaskAsync(
		string operationId,
		string taskId,
		string title,
		string message,
		string scheduledMessage)
	{
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		try
		{
			bool confirmed = await ShowConfirmationAsync(
				title,
				message,
				LocalizationManager.Text("settings.maintenance.prepare"),
				LocalizationManager.Text("common.cancel"));
			if (!confirmed)
			{
				return;
			}

			bool queued = await QueueBootTaskAsync(
				taskId,
				LocalizationManager.Text("settings.maintenance.queue_title"),
				LocalizationManager.Text("settings.maintenance.queue_detail"),
				saveDraft: false,
				afterRefresh: () => SetMaintenanceStatus(scheduledMessage));
			if (!queued)
			{
				return;
			}

			RuntimePurgeRequested?.Invoke(this, EventArgs.Empty);
		}
		finally
		{
			EndOperation(operationId);
		}
	}

	private async Task RunMaintenanceOperationAsync(
		string operationId,
		string title,
		string message,
		Func<CancellationToken, Task<SetupStepResult>> operation,
		string successMessage,
		bool reloadSettings,
		bool preferSuccessMessage = false)
	{
		if (!TryBeginOperation(operationId))
		{
			return;
		}

		SetMaintenanceBusy(true);
		try
		{
			bool confirmed = await ShowConfirmationAsync(
				title,
				message,
				LocalizationManager.Text("settings.maintenance.continue"),
				LocalizationManager.Text("common.cancel"));
			if (!confirmed)
			{
				return;
			}

			SetMaintenanceStatus($"{title.TrimEnd('?')} running...");
			BeginComfyOperationLog("MAINTENANCE LOG");
			var service = ComfyInstall;
			Action<string>? previousLogHandler = service.OnMessage;
			Action<double, string>? previousProgressHandler = service.OnProgress;
			SetupStepResult result;
			try
			{
				service.OnMessage = AppendComfyOperationLog;
				service.OnProgress = UpdateMaintenanceBackupProgress;
				result = await NexusSoftTimeout.AwaitAsync(
					operation(_lifetimeCts.Token),
					TimeSpan.FromMinutes(60),
					() =>
					{
						if (!_isUnloaded)
						{
							SetMaintenanceStatus("Maintenance is still running. Waiting for the current operation to finish...");
							AppendComfyOperationLog("[MAINTENANCE] Soft timeout reached. Waiting for the active operation to finish.");
						}
					});
			}
			catch (OperationCanceledException)
			{
				if (!_isUnloaded)
				{
					SetMaintenanceStatus("Maintenance operation stopped because Settings was closed.");
					CompleteComfyOperationLog(false, "Maintenance operation stopped because Settings was closed.");
				}
				return;
			}
			catch (Exception ex)
			{
				SetMaintenanceStatus($"Maintenance failed: {ex.Message}");
				CompleteComfyOperationLog(false, ex.Message);
				return;
			}
			finally
			{
				service.OnMessage = previousLogHandler;
				service.OnProgress = previousProgressHandler;
			}

			if (!result.IsSuccess)
			{
				SetMaintenanceStatus(result.Message);
				CompleteComfyOperationLog(false, result.Message);
				return;
			}

			if (reloadSettings)
			{
				SettingsService.Reload();
				_editor.Reload();
				Refresh();
			}

			string finalMessage = preferSuccessMessage || string.IsNullOrWhiteSpace(result.Message)
				? successMessage
				: result.Message;
			SetMaintenanceStatus(finalMessage);
			if (operationId is "backup-runtime-data" or "restore-runtime-data")
			{
				UpdateMaintenanceBackupProgress(1, finalMessage);
			}

			CompleteComfyOperationLog(true, finalMessage);
			TryUpdateUi(UpdateStateChrome);
		}
		finally
		{
			SetMaintenanceBusy(false);
			EndOperation(operationId);
		}
	}

	private Task<SetupStepResult> ClearServerLogAsync(CancellationToken cancellationToken)
	{
		return Task.Run(() =>
		{
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				string logsDirectory = ComfyInstallService.GetLocalRuntimePath("Logs");
				string latestLogPath = SessionLogPaths.GetLatestLogPath(logsDirectory, SessionLogPaths.ComfyServerLatestFileName);
				string? directory = System.IO.Path.GetDirectoryName(latestLogPath);
				if (!string.IsNullOrWhiteSpace(directory))
				{
					Directory.CreateDirectory(directory);
				}

				var targets = new List<string> { latestLogPath };
				string? activeSessionLogPath = ServerProcesses.FindServerProcess()?.LogPath;
				if (!string.IsNullOrWhiteSpace(activeSessionLogPath)
					&& !targets.Any(path => string.Equals(path, activeSessionLogPath, StringComparison.OrdinalIgnoreCase)))
				{
					targets.Add(activeSessionLogPath);
				}

				int clearedCount = 0;
				int missingCount = 0;
				var lockedFiles = new List<string>();
				foreach (string target in targets)
				{
					ClearLogFileResult clearResult = TryClearLogFile(target);
					switch (clearResult)
					{
						case ClearLogFileResult.Cleared:
							clearedCount++;
							break;
						case ClearLogFileResult.Missing:
							missingCount++;
							break;
						case ClearLogFileResult.Locked:
							lockedFiles.Add(target);
							break;
					}
				}

				if (lockedFiles.Count > 0)
				{
					return new SetupStepResult(
						true,
						LocalizationManager.Format("settings.logs.clear_locked", clearedCount, lockedFiles.Count),
						1);
				}

				if (clearedCount == 0 && missingCount == targets.Count)
				{
					return new SetupStepResult(true, LocalizationManager.Text("settings.logs.clear_already_empty"), 1);
				}

				return new SetupStepResult(
					true,
					LocalizationManager.Format("settings.logs.clear_complete_count", clearedCount),
					1);
			}
			catch (Exception ex)
			{
				return new SetupStepResult(false, LocalizationManager.Format("settings.logs.clear_failed", ex.Message), 0);
			}
		}, cancellationToken);
	}

	private static ClearLogFileResult TryClearLogFile(string logPath)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
			{
				return ClearLogFileResult.Missing;
			}

			using var stream = new FileStream(
				logPath,
				FileMode.Open,
				FileAccess.Write,
				FileShare.ReadWrite | FileShare.Delete);
			stream.SetLength(0);
			return ClearLogFileResult.Cleared;
		}
		catch (IOException)
		{
			return ClearLogFileResult.Locked;
		}
		catch (UnauthorizedAccessException)
		{
			return ClearLogFileResult.Locked;
		}
	}

	private enum ClearLogFileResult
	{
		Cleared,
		Missing,
		Locked
	}

	private async Task RunVenvMaintenanceAsync(VenvMaintenanceAction action)
	{
		if (!TryBeginOperation("venv-maintenance"))
		{
			return;
		}

		VenvActionGroup.IsVisible = false;
		try
		{
			string title = action switch
			{
				VenvMaintenanceAction.Reset => LocalizationManager.Text("settings.venv.reset_title"),
				VenvMaintenanceAction.Delete => LocalizationManager.Text("settings.venv.delete_title"),
				_ => LocalizationManager.Text("settings.venv.create_title")
			};
			bool draftUsesVenvBeforeAction = IsDraftUsingVenv();
			bool activeServerUsesVenv = ActiveServerUsesVenv();
			string message = action switch
			{
				VenvMaintenanceAction.Reset => draftUsesVenvBeforeAction || activeServerUsesVenv
					? LocalizationManager.Text("settings.venv.reset_message_active")
					: LocalizationManager.Text("settings.venv.reset_message_direct"),
				VenvMaintenanceAction.Delete => draftUsesVenvBeforeAction || activeServerUsesVenv
					? LocalizationManager.Text("settings.venv.delete_message_active")
					: LocalizationManager.Text("settings.venv.delete_message_direct"),
				_ => draftUsesVenvBeforeAction
					? LocalizationManager.Text("settings.venv.create_message_venv")
					: LocalizationManager.Text("settings.venv.create_message_direct")
			};

			if (!await ShowConfirmationAsync(
				title,
				message,
				LocalizationManager.Text("settings.maintenance.continue"),
				LocalizationManager.Text("common.cancel")))
			{
				UpdateVenvCard();
				return;
			}

			if (action == VenvMaintenanceAction.Delete && draftUsesVenvBeforeAction)
			{
				_editor.Draft.ServerPythonMode = PythonExecutionModes.ConfiguredPython;
			}

			bool queued = await QueueBootTaskAsync(
				action switch
				{
					VenvMaintenanceAction.Reset => PendingBootTaskIds.VenvRebuild,
					VenvMaintenanceAction.Delete => PendingBootTaskIds.VenvDelete,
					_ => PendingBootTaskIds.VenvCreate
				},
				action switch
				{
					VenvMaintenanceAction.Reset => LocalizationManager.Text("settings.venv.queue_rebuild_title"),
					VenvMaintenanceAction.Delete => LocalizationManager.Text("settings.venv.queue_delete_title"),
					_ => LocalizationManager.Text("settings.venv.queue_create_title")
				},
				LocalizationManager.Text("settings.venv.queue_detail"),
				saveDraft: true,
				afterRefresh: () =>
				{
					_venvRestartRequired = false;
					VenvDetailValueLabel.Text = action switch
					{
						VenvMaintenanceAction.Reset => LocalizationManager.Text("settings.venv.rebuild_scheduled"),
						VenvMaintenanceAction.Delete => LocalizationManager.Text("settings.venv.delete_scheduled"),
						_ => LocalizationManager.Text("settings.venv.create_scheduled")
					};
				});
			if (!queued)
			{
				UpdateVenvCard();
			}
		}
		finally
		{
			EndOperation("venv-maintenance");
		}
	}

	private bool IsDraftUsingVenv()
		=> string.Equals(_editor.Draft.ServerPythonMode, PythonExecutionModes.Venv, StringComparison.Ordinal);

	private bool HasDraftBootTask(string taskId)
		=> _editor.Draft.PendingBootTasks.Any(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));

	private bool IsRequiredVenvCreateTask(string taskId)
		=> string.Equals(taskId, PendingBootTaskIds.VenvCreate, StringComparison.Ordinal)
			&& string.Equals(_editor.Draft.ServerPythonMode, PythonExecutionModes.Venv, StringComparison.Ordinal)
			&& !File.Exists(_appManager.Paths.ActiveVenvPythonExe);

	private void AddDraftBootTask(string taskId, string origin = "")
	{
		if (HasDraftBootTask(taskId)) return;

		if (taskId is PendingBootTaskIds.VenvCreate or PendingBootTaskIds.VenvRebuild or PendingBootTaskIds.VenvDelete)
		{
			_editor.Draft.PendingBootTasks.RemoveAll(task =>
				(task.Id is PendingBootTaskIds.VenvCreate or PendingBootTaskIds.VenvRebuild or PendingBootTaskIds.VenvDelete)
				&& !string.Equals(task.Id, taskId, StringComparison.Ordinal));
		}

		_editor.Draft.PendingBootTasks.Add(new PendingBootTask { Id = taskId, Origin = origin });
	}

	private void RemoveDraftBootTask(string taskId)
		=> _editor.Draft.PendingBootTasks.RemoveAll(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));

	private void RemoveDraftBootTask(string taskId, string origin)
		=> _editor.Draft.PendingBootTasks.RemoveAll(task =>
			string.Equals(task.Id, taskId, StringComparison.Ordinal)
			&& string.Equals(task.Origin, origin, StringComparison.Ordinal));

	private void RemoveDraftAutoVenvCreateTask()
		=> _editor.Draft.PendingBootTasks.RemoveAll(task =>
			string.Equals(task.Id, PendingBootTaskIds.VenvCreate, StringComparison.Ordinal)
			&& string.Equals(task.Origin, PendingBootTaskOrigins.VenvModeSelection, StringComparison.Ordinal));

	private bool ActiveServerUsesVenv()
		=> RuntimePythonModePresenter.ShouldDisplayVenvMode(SettingsService.Settings);

}
