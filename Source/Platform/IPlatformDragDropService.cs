namespace ComfyUI_Nexus.Platform;

public interface IPlatformDragDropService
{
	Task<bool> ContainsFolderAsync(DragEventArgs e);

	Task<IReadOnlyList<string>> GetDroppedPathsAsync(DragEventArgs e);

	Task<IReadOnlyList<string>> GetDroppedPathsAsync(DropEventArgs e);

	Task SetDragStartingPathsAsync(DragStartingEventArgs e, IReadOnlyList<string> paths);

	Task SetDragStartingTextAsync(DragStartingEventArgs e, string text);
}
