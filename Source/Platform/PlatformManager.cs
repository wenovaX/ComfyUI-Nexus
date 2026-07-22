namespace ComfyUI_Nexus.Platform;

public abstract class PlatformManager
{
	public abstract NexusPlatformKind Kind { get; }

	public abstract IPlatformFilePicker FilePicker { get; }

	public abstract IPlatformKeyboardState Keyboard { get; }

	public abstract IPlatformWebViewService WebView { get; }

	public abstract IPlatformDragDropService DragDrop { get; }

	public abstract IPlatformFileClipboardService FileClipboard { get; }

	public abstract IPlatformCursorService Cursor { get; }

	public abstract IPlatformInteractionAddon Interactions { get; }

	public abstract IPlatformShellService Shell { get; }

	public abstract IPlatformDirectoryWatcherService DirectoryWatcher { get; }

	internal static PlatformManager CreateForAppRuntime()
	{
#if WINDOWS
		return new Windows.WindowsPlatformManager();
#elif MACCATALYST
		return new MacCatalyst.MacCatalystPlatformManager();
#else
		return new UnsupportedPlatformManager();
#endif
	}
}
