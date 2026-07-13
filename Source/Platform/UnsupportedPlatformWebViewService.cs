namespace ComfyUI_Nexus.Platform;

public sealed class UnsupportedPlatformWebViewService : IPlatformWebViewService
{
	public Task ConfigureBridgeAsync(
		WebView webView,
		Func<string, Task> processMessageAsync,
		Action<string?> navigationStarting,
		Action? bridgeActivated = null,
		Func<NexusKey, bool, bool, bool, bool>? acceleratorKeyHandler = null)
	{
		bridgeActivated?.Invoke();
		return Task.CompletedTask;
	}

	public async Task DisableBrowserReloadHandlingAsync(WebView webView, Func<Task> clearBeforeUnloadAsync)
	{
		await clearBeforeUnloadAsync();
	}

	public void Reload(WebView webView)
	{
		webView.Reload();
	}

	public void Focus(WebView webView)
	{
		webView.Focus();
	}

	public void SetDevToolsEnabled(WebView webView, bool isEnabled)
	{
	}
}
