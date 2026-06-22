using Microsoft.Maui.Controls;

namespace ComfyUI_Nexus.Views;

public partial class WorkspaceView : ContentView
{
	internal event EventHandler<WebNavigatedEventArgs>? WebViewNavigated;

	public WorkspaceView()
	{
		InitializeComponent();
	}

	internal WebView BrowserView => ComfyWebView;

	internal void SetFocusState(bool isFocused)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			CanvasFrameBorder.Stroke = isFocused ? Color.FromArgb("#4400e5ff") : Colors.Transparent;
			CanvasFrameShadow.Opacity = isFocused ? 0.6f : 0;
			CanvasFrameShadow.Radius = isFocused ? 15 : 0;
		});
	}

	internal void SetChromeOpacity(double opacity)
	{
		WorkspaceChromeGrid.Opacity = opacity;
	}

	internal void SetBrowserSurfaceState(bool isVisible, double opacity)
	{
		ComfyWebView.IsVisible = isVisible;
		ComfyWebView.Opacity = opacity;
	}

	internal void HideBrowserSurface()
	{
		ComfyWebView.IsVisible = false;
	}

	private void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
	{
		WebViewNavigated?.Invoke(this, e);
	}
}
