namespace ComfyUI_Nexus.Platform;

public interface IPlatformWebViewService
{
	Task EnsureReadyAsync(INexusBrowserSurface surface);

	Task ConfigureBridgeAsync(
		INexusBrowserSurface surface,
		Func<string, Task> processMessageAsync,
		Action<string?> navigationStarting,
		Action? bridgeActivated = null,
		Func<NexusKey, bool, bool, bool, bool>? acceleratorKeyHandler = null);

	Task DisableBrowserReloadHandlingAsync(INexusBrowserSurface surface, Func<Task> clearBeforeUnloadAsync);

	void Reload(INexusBrowserSurface surface);
	void Focus(INexusBrowserSurface surface);
	void SetDevToolsEnabled(INexusBrowserSurface surface, bool isEnabled);
	void OpenDevTools(INexusBrowserSurface surface);
}
