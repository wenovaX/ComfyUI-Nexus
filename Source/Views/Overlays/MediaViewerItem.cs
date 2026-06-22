namespace ComfyUI_Nexus.Views.Overlays;

public sealed record MediaViewerItem(
	string Name,
	string FullPath,
	bool IsImage = true,
	bool IsVideo = false,
	string? JobId = null,
	string Type = "output",
	string Subfolder = "",
	bool IsBatchInferred = false);
