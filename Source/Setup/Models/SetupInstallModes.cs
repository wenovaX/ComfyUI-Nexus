namespace ComfyUI_Nexus.Setup.Models;

internal static class SetupInstallModes
{
	internal const string LocalRuntime = "local_runtime";
	internal const string ExistingComfyPath = "existing_comfy_path";

	internal static bool IsKnown(string value)
		=> string.Equals(value, LocalRuntime, StringComparison.Ordinal)
			|| string.Equals(value, ExistingComfyPath, StringComparison.Ordinal);
}
