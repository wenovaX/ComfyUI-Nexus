namespace ComfyUI_Nexus.Platform;

public abstract class PlatformManager
{
	private static readonly Lazy<PlatformManager> CurrentValue = new(CreateCurrent);

	public static PlatformManager Current => CurrentValue.Value;

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

	private static PlatformManager CreateCurrent()
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
