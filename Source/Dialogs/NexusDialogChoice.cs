namespace ComfyUI_Nexus.Dialogs;

internal sealed record NexusDialogChoice(
	string Text,
	Func<Task<NexusDialogActionResult>>? Callback = null,
	bool IsDanger = false);
