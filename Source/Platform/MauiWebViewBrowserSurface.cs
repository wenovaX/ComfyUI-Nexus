using ComfyUI_Nexus.Ui;
using Microsoft.Maui.Controls;

namespace ComfyUI_Nexus.Platform;

internal sealed class MauiWebViewBrowserSurface : INexusBrowserSurface
{
	private readonly WebView _webView;
	private bool _disposed;

	internal MauiWebViewBrowserSurface(WebView webView)
	{
		_webView = webView;
		_webView.HandlerChanged += OnHandlerChanged;
		_webView.Navigated += OnNavigated;
	}

	public VisualElement InputElement => _webView;
	public bool IsReady => !_disposed && _webView.Handler != null;
	public bool IsVisible { get => _webView.IsVisible; set => _webView.IsVisible = value; }
	public double Opacity { get => _webView.Opacity; set => _webView.Opacity = value; }
	public event EventHandler? Ready;
	public event EventHandler<NexusBrowserNavigationEventArgs>? Navigated;

	public Task NavigateAsync(string source)
	{
		if (!_disposed)
		{
			_webView.Source = source;
		}

		return Task.CompletedTask;
	}

	public Task<string?> ExecuteScriptAsync(string script)
		=> _disposed ? Task.FromResult<string?>(null) : _webView.EvaluateJavaScriptAsync(script);

	public Task EnsureReadyAsync()
		=> PlatformManager.Current.WebView.EnsureReadyAsync(this);

	public Task ConfigureBridgeAsync(Func<string, Task> processMessageAsync, Action<string?> navigationStarting, Action? bridgeActivated, Func<NexusKey, bool, bool, bool, bool>? acceleratorKeyHandler)
		=> PlatformManager.Current.WebView.ConfigureBridgeAsync(this, processMessageAsync, navigationStarting, bridgeActivated, acceleratorKeyHandler);

	public Task DisableReloadHandlingAsync(Func<Task> clearBeforeUnloadAsync)
		=> PlatformManager.Current.WebView.DisableBrowserReloadHandlingAsync(this, clearBeforeUnloadAsync);

	public Task SimulateFileDropAsync(string filePath, string? workflowRelativePath = null)
		=> WebViewUtility.SimulateFileDropAsync(this, filePath, workflowRelativePath);

	public void Reload()
	{
		if (!_disposed)
		{
			PlatformManager.Current.WebView.Reload(this);
		}
	}

	public void FocusBrowserInput()
	{
		if (!_disposed)
		{
			PlatformManager.Current.WebView.Focus(this);
		}
	}

	public void SetDevToolsEnabled(bool isEnabled)
	{
		if (!_disposed)
		{
			PlatformManager.Current.WebView.SetDevToolsEnabled(this, isEnabled);
		}
	}

	public void OpenDevTools()
	{
		if (!_disposed)
		{
			PlatformManager.Current.WebView.OpenDevTools(this);
		}
	}

	internal WebView NativeWebView => _webView;

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_webView.HandlerChanged -= OnHandlerChanged;
		_webView.Navigated -= OnNavigated;
	}

	private void OnHandlerChanged(object? sender, EventArgs e)
		=> Ready?.Invoke(this, EventArgs.Empty);

	private void OnNavigated(object? sender, WebNavigatedEventArgs e)
		=> Navigated?.Invoke(this, new NexusBrowserNavigationEventArgs(e.Url, e.Result == WebNavigationResult.Success, e.Result.ToString()));
}
