namespace ComfyUI_Nexus.Setup.Models;

internal static class RuntimeBackupFormats
{
	internal const string Folder = "folder";
	internal const string Zip = "zip";

	internal static bool IsKnown(string? value)
		=> value is Folder or Zip;
}
