namespace ComfyUI_Nexus.Dialogs;

internal sealed record NexusDialogResult(
	bool Accepted,
	string? Value = null,
	string? Choice = null)
{
	internal static NexusDialogResult Cancelled { get; } = new(false);
}
