namespace ComfyUI_Nexus.Setup.Services;

using ComfyUI_Nexus.Setup.Models;

internal static class RuntimeRepairTarget
{
	internal static bool IsUsingVenv(SetupSettings? settings = null)
	{
		settings ??= SetupSettingsService.Instance.Settings;
		return string.Equals(settings.ServerPythonMode, PythonExecutionModes.Venv, StringComparison.Ordinal);
	}

	internal static string GetPythonExecutable(SetupSettings? settings = null)
	{
		settings ??= SetupSettingsService.Instance.Settings;
		if (IsUsingVenv(settings)) return ComfyInstallService.ComfyVenvPythonExe;

		return string.IsNullOrWhiteSpace(settings.PythonPath) ? "python" : settings.PythonPath;
	}

	internal static string GetLabel(SetupSettings? settings = null)
		=> IsUsingVenv(settings) ? "ComfyUI .venv" : "configured Python";

	internal static string GetDisplay(SetupSettings? settings = null)
		=> $"{GetLabel(settings)} ({GetPythonExecutable(settings)})";
}
