#if WINDOWS
namespace ComfyUI_Nexus.Platform.Windows;

public sealed class WindowsPlatformManager : PlatformManager
{
	public override NexusPlatformKind Kind => NexusPlatformKind.Windows;

	public override IPlatformFilePicker FilePicker { get; } = new WindowsFilePicker();

	public override IPlatformKeyboardState Keyboard { get; } = new WindowsKeyboardState();

	public override IPlatformWebViewService WebView { get; } = new WindowsWebViewService();

	public override IPlatformDragDropService DragDrop { get; } = new WindowsDragDropService();

	public override IPlatformFileClipboardService FileClipboard { get; } = new WindowsFileClipboardService();

	public override IPlatformCursorService Cursor { get; } = new WindowsCursorService();

	public override IPlatformInteractionAddon Interactions { get; } = new WindowsInteractionAddon();

	public override IPlatformShellService Shell { get; } = new WindowsShellService();

	public override IPlatformDirectoryWatcherService DirectoryWatcher { get; } = new WindowsDirectoryWatcherService();
}
#endif
