namespace ComfyUI_Nexus.Setup.Services;

using ComfyUI_Nexus.Setup.Models;

internal static class RuntimePythonModePresenter
{
	internal static bool ShouldDisplayVenvMode(
		SetupSettings settings,
		bool includeActiveLaunchSnapshot = true)
	{
		if (HasPendingVenvDelete(settings))
		{
			return false;
		}

		if (string.Equals(settings.ServerPythonMode, PythonExecutionModes.Venv, StringComparison.Ordinal))
		{
			return true;
		}

		if (HasPendingVenvActivation(settings))
		{
			return true;
		}

		return includeActiveLaunchSnapshot
			&& settings.ActiveServerLaunchSettings is { } active
			&& string.Equals(active.ServerPythonMode, PythonExecutionModes.Venv, StringComparison.Ordinal);
	}

	internal static bool HasPendingVenvActivation(SetupSettings settings)
		=> settings.PendingBootTasks.Any(task => task.Id is PendingBootTaskIds.VenvCreate or PendingBootTaskIds.VenvRebuild);

	internal static bool HasPendingVenvDelete(SetupSettings settings)
		=> settings.PendingVenvDelete
			|| settings.PendingBootTasks.Any(task => string.Equals(task.Id, PendingBootTaskIds.VenvDelete, StringComparison.Ordinal));
}
