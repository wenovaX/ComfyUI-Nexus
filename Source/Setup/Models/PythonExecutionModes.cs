namespace ComfyUI_Nexus.Setup.Models;

internal static class PythonExecutionModes
{
	internal const string Venv = "venv";
	internal const string ConfiguredPython = "configured_python";

	internal static bool IsKnown(string value)
		=> string.Equals(value, Venv, StringComparison.Ordinal)
			|| string.Equals(value, ConfiguredPython, StringComparison.Ordinal);
}
