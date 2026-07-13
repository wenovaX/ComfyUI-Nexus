namespace ComfyUI_Nexus.Setup.Models;

internal static class ComfyCoreSources
{
	internal const string RemoteLatest = "remote_latest";
	internal const string BuiltIn = "built_in";

	internal static bool IsKnown(string? value)
		=> string.Equals(value, RemoteLatest, StringComparison.Ordinal)
			|| string.Equals(value, BuiltIn, StringComparison.Ordinal);
}
