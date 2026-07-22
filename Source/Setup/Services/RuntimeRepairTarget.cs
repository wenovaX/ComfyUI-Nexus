namespace ComfyUI_Nexus.Setup.Services;

using ComfyUI_Nexus.Setup.Models;

internal static class RuntimeRepairTarget
{
	internal static bool IsUsingVenv(SetupSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		return string.Equals(settings.ServerPythonMode, PythonExecutionModes.Venv, StringComparison.Ordinal);
	}

	internal static string GetPythonExecutable(SetupSettings settings, NexusComfyRuntimePaths paths)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(paths);
		if (IsUsingVenv(settings)) return paths.ActiveVenvPythonExe;

		return string.IsNullOrWhiteSpace(settings.PythonPath) ? "python" : settings.PythonPath;
	}

	internal static string GetLabel(SetupSettings settings)
		=> IsUsingVenv(settings) ? "ComfyUI .venv" : "configured Python";

	internal static string GetDisplay(SetupSettings settings, NexusComfyRuntimePaths paths)
		=> $"{GetLabel(settings)} ({GetPythonExecutable(settings, paths)})";
}
