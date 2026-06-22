namespace ComfyUI_Nexus.Settings;

using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Models;

internal static class SettingsStatusPresenter
{
	internal static string FormatQueuedTasks(
		IReadOnlyList<PendingBootTask> pendingTasks,
		Func<PendingBootTask, bool> predicate)
	{
		var taskTitles = pendingTasks
			.Where(predicate)
			.Select(GetPendingBootTaskTitle)
			.ToList();
		return taskTitles.Count == 0
			? string.Empty
			: LocalizationManager.Format("settings.pending_tasks.queued_for_next_boot", string.Join(", ", taskTitles));
	}

	internal static string GetOperationLabel(string operationId)
		=> operationId switch
		{
			"select-comfy-path" => LocalizationManager.Text("settings.operations.select_comfy_path"),
			"open-comfy-folder" => LocalizationManager.Text("settings.operations.open_folder"),
			"check-comfy-updates" => LocalizationManager.Text("settings.operations.check_comfy_updates"),
			"apply-comfy-update" => LocalizationManager.Text("settings.operations.apply_comfy_update"),
			"repair-comfy-dependencies" => LocalizationManager.Text("settings.operations.repair_runtime"),
			"select-custom-git" => LocalizationManager.Text("settings.operations.select_custom_git"),
			"select-custom-python" => LocalizationManager.Text("settings.operations.select_custom_python"),
			"select-pip-cache" => LocalizationManager.Text("settings.operations.select_pip_cache"),
			"open-pip-cache" => LocalizationManager.Text("settings.operations.open_pip_cache"),
			"clear-pip-cache" => LocalizationManager.Text("settings.operations.clear_pip_cache"),
			"venv-maintenance" => LocalizationManager.Text("settings.operations.venv_maintenance"),
			"open-extensions-folder" => LocalizationManager.Text("settings.operations.open_extensions_folder"),
			"scan-extensions" => LocalizationManager.Text("settings.operations.scan_extensions"),
			"repair-extensions" => LocalizationManager.Text("settings.operations.repair_extensions"),
			"clear-server-log" => LocalizationManager.Text("settings.operations.clear_server_log"),
			"reset-settings" => LocalizationManager.Text("settings.operations.reset_settings"),
			"backup-runtime-data" => LocalizationManager.Text("settings.operations.backup_runtime_data"),
			"restore-runtime-data" => LocalizationManager.Text("settings.operations.restore_runtime_data"),
			"delete-runtime-backup" => LocalizationManager.Text("settings.operations.delete_runtime_backup"),
			"purge-local-runtime" => LocalizationManager.Text("settings.operations.purge_local_runtime"),
			_ => operationId.Replace('-', ' ')
		};

	internal static string GetPendingBootTaskTitle(PendingBootTask task)
		=> task.Id switch
		{
			PendingBootTaskIds.RuntimePurge => LocalizationManager.Text("settings.pending_tasks.runtime_purge_title"),
			PendingBootTaskIds.ResetSetup => LocalizationManager.Text("settings.pending_tasks.reset_setup_title"),
			PendingBootTaskIds.ResetAll => LocalizationManager.Text("settings.pending_tasks.reset_all_title"),
			PendingBootTaskIds.ComfyUpdate => LocalizationManager.Text("settings.pending_tasks.comfy_update_title"),
			PendingBootTaskIds.ExtensionRepair => task.TargetFolders.Count == 0
				? LocalizationManager.Text("settings.extensions.pending_title_all")
				: LocalizationManager.Format("settings.extensions.pending_title_count", task.TargetFolders.Count),
			PendingBootTaskIds.VenvCreate => LocalizationManager.Text("settings.pending_tasks.venv_create_title"),
			PendingBootTaskIds.VenvRebuild => LocalizationManager.Text("settings.pending_tasks.venv_rebuild_title"),
			PendingBootTaskIds.VenvDelete => LocalizationManager.Text("settings.pending_tasks.venv_delete_title"),
			PendingBootTaskIds.RuntimeRepair => LocalizationManager.Text("settings.pending_tasks.runtime_repair_title"),
			_ => task.Id.Replace('-', ' ')
		};

	internal static string GetPendingBootTaskDetail(PendingBootTask task)
		=> task.Id switch
		{
			PendingBootTaskIds.RuntimePurge => LocalizationManager.Text("settings.pending_tasks.runtime_purge_detail"),
			PendingBootTaskIds.ResetSetup => LocalizationManager.Text("settings.pending_tasks.reset_setup_detail"),
			PendingBootTaskIds.ResetAll => LocalizationManager.Text("settings.pending_tasks.reset_all_detail"),
			PendingBootTaskIds.ComfyUpdate => LocalizationManager.Text("settings.pending_tasks.comfy_update_detail"),
			PendingBootTaskIds.ExtensionRepair => task.TargetFolders.Count == 0
				? LocalizationManager.Text("settings.extensions.pending_detail_all")
				: LocalizationManager.Format(
					"settings.extensions.pending_detail_targets",
					GetPendingExtensionActionLabel(task),
					$"{string.Join(", ", task.TargetFolders.Take(4))}{(task.TargetFolders.Count > 4 ? "..." : string.Empty)}"),
			PendingBootTaskIds.VenvCreate => LocalizationManager.Text("settings.pending_tasks.venv_create_detail"),
			PendingBootTaskIds.VenvRebuild => LocalizationManager.Text("settings.pending_tasks.venv_rebuild_detail"),
			PendingBootTaskIds.VenvDelete => LocalizationManager.Text("settings.pending_tasks.venv_delete_detail"),
			PendingBootTaskIds.RuntimeRepair => LocalizationManager.Text("settings.pending_tasks.runtime_repair_detail"),
			_ => string.IsNullOrWhiteSpace(task.LastError)
				? LocalizationManager.Text("settings.pending_tasks.generic_detail")
				: task.LastError
		};

	private static string GetPendingExtensionActionLabel(PendingBootTask task)
		=> string.Equals(task.Action, PendingBootTaskActions.ExtensionReinstall, StringComparison.Ordinal)
			? LocalizationManager.Text("settings.extensions.action_reinstall")
			: LocalizationManager.Text("settings.extensions.action_sync_update");
}
