namespace ComfyUI_Nexus.Setup.Models;

internal static class PipCacheModes
{
	internal const string PipDefault = "pip_default";
	internal const string NexusDefault = "nexus_default";
	internal const string Custom = "custom";

	internal static bool IsKnown(string? mode)
		=> string.Equals(mode, PipDefault, StringComparison.Ordinal)
			|| string.Equals(mode, NexusDefault, StringComparison.Ordinal)
			|| string.Equals(mode, Custom, StringComparison.Ordinal);
}
