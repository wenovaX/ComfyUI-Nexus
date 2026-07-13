using Microsoft.Maui.Controls;

namespace ComfyUI_Nexus.Dialogs;

internal sealed class NexusDialogRequest
{
	internal NexusDialogKind Kind { get; init; }
	internal string Title { get; init; } = string.Empty;
	internal string Message { get; init; } = string.Empty;
	internal string OkText { get; init; } = "OK";
	internal string CancelText { get; init; } = "Cancel";
	internal bool OkIsDanger { get; init; }
	internal string Placeholder { get; init; } = string.Empty;
	internal string InitialValue { get; init; } = string.Empty;
	internal int MaxLength { get; init; } = -1;
	internal Keyboard Keyboard { get; init; } = Keyboard.Text;
	internal NexusDialogReturnFocusTarget ReturnFocusTarget { get; init; } = NexusDialogReturnFocusTarget.None;
	internal Func<Task<NexusDialogActionResult>>? OkCallback { get; init; }
	internal Func<Task>? CancelCallback { get; init; }
	internal Func<string, Task<NexusDialogActionResult>>? PromptOkCallback { get; init; }
	internal IReadOnlyList<NexusDialogChoice> Choices { get; init; } = [];
	internal IReadOnlyList<NexusDialogThumbnailChoice> ThumbnailChoices { get; init; } = [];
}
