namespace ComfyUI_Nexus.Platform;

public sealed class UnsupportedPlatformWebViewService : IPlatformWebViewService
{
	public Task EnsureReadyAsync(INexusBrowserSurface surface)
		=> Task.CompletedTask;

	public Task ConfigureBridgeAsync(
		INexusBrowserSurface surface,
		Func<string, Task> processMessageAsync,
		Action<string?> navigationStarting,
		Action? bridgeActivated = null,
		Func<NexusKey, bool, bool, bool, bool>? acceleratorKeyHandler = null)
	{
		bridgeActivated?.Invoke();
		return Task.CompletedTask;
	}

	public async Task DisableBrowserReloadHandlingAsync(INexusBrowserSurface surface, Func<Task> clearBeforeUnloadAsync)
	{
		await clearBeforeUnloadAsync();
	}

	public void Reload(INexusBrowserSurface surface)
	{
		surface.Reload();
	}

	public void Focus(INexusBrowserSurface surface)
	{
		surface.FocusBrowserInput();
	}

	public void SetDevToolsEnabled(INexusBrowserSurface surface, bool isEnabled)
	{
	}

	public void OpenDevTools(INexusBrowserSurface surface)
	{
	}
}
