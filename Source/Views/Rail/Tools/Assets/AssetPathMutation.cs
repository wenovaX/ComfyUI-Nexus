namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

internal enum AssetPathMutationKind
{
	Rename,
	Move,
	Delete
}

internal enum AssetMutationPreparationResult
{
	Proceed,
	Handled,
	Cancel
}

internal sealed record AssetPathMutation(
	AssetPathMutationKind Kind,
	string SourcePath,
	string? DestinationPath,
	bool IsDirectory,
	bool IsBatch = false);
