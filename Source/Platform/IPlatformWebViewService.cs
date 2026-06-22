namespace ComfyUI_Nexus.Platform;

public interface IPlatformWebViewService
{
	Task ConfigureBridgeAsync(
		WebView webView,
		Func<string, Task> processMessageAsync,
		Action<string?> navigationStarting,
		Action? bridgeActivated = null,
		Func<NexusKey, bool, bool, bool, bool>? acceleratorKeyHandler = null);

	Task DisableBrowserReloadHandlingAsync(WebView webView, Func<Task> clearBeforeUnloadAsync);

	void Reload(WebView webView);
	void Focus(WebView webView);
	void SetDevToolsEnabled(WebView webView, bool isEnabled);
}
