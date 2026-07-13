namespace ComfyUI_Nexus.Platform;

public class UnsupportedPlatformManager : PlatformManager
{
	public override NexusPlatformKind Kind => NexusPlatformKind.Unsupported;

	public override IPlatformFilePicker FilePicker { get; } = new UnsupportedPlatformFilePicker();

	public override IPlatformKeyboardState Keyboard { get; } = new UnsupportedPlatformKeyboardState();

	public override IPlatformWebViewService WebView { get; } = new UnsupportedPlatformWebViewService();

	public override IPlatformDragDropService DragDrop { get; } = new UnsupportedPlatformDragDropService();

	public override IPlatformFileClipboardService FileClipboard { get; } = new UnsupportedPlatformFileClipboardService();

	public override IPlatformCursorService Cursor { get; } = new UnsupportedPlatformCursorService();

	public override IPlatformInteractionAddon Interactions { get; } = new UnsupportedPlatformInteractionAddon();

	public override IPlatformShellService Shell { get; } = new UnsupportedPlatformShellService();

	public override IPlatformDirectoryWatcherService DirectoryWatcher { get; } = new UnsupportedPlatformDirectoryWatcherService();
}
