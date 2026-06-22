namespace ComfyUI_Nexus.Dialogs;

internal sealed record NexusDialogThumbnailChoice(
	string Text,
	string ImagePath,
	bool IsPrimary = false);
