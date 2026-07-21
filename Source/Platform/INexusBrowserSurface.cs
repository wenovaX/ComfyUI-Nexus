using Microsoft.Maui.Controls;

namespace ComfyUI_Nexus.Platform;

public sealed class NexusBrowserNavigationEventArgs(string? url, bool isSuccess, string? detail = null) : EventArgs
{
	internal string? Url { get; } = url;
	internal bool IsSuccess { get; } = isSuccess;
	internal string? Detail { get; } = detail;
}

/// <summary>
/// Browser rendering surface used by the Nexus shell. Implementations may use a MAUI WebView
/// or a WebView2 composition controller, while the bridge remains host-agnostic.
/// </summary>
public interface INexusBrowserSurface : IDisposable
{
	VisualElement InputElement { get; }
	bool IsReady { get; }
	bool IsVisible { get; set; }
	double Opacity { get; set; }

	event EventHandler? Ready;
	event EventHandler<NexusBrowserNavigationEventArgs>? Navigated;

	Task NavigateAsync(string source);
	Task<string?> ExecuteScriptAsync(string script);
	Task EnsureReadyAsync();
	Task ConfigureBridgeAsync(
		Func<string, Task> processMessageAsync,
		Action<string?> navigationStarting,
		Action? bridgeActivated,
		Func<NexusKey, bool, bool, bool, bool>? acceleratorKeyHandler);
	Task DisableReloadHandlingAsync(Func<Task> clearBeforeUnloadAsync);
	Task SimulateFileDropAsync(string filePath, string? workflowRelativePath = null);
	void Reload();
	void FocusBrowserInput();
	void SetDevToolsEnabled(bool isEnabled);
	void OpenDevTools();
}
