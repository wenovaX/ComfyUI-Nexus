using Microsoft.Maui.Controls;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Platform;
#if WINDOWS
using ComfyUI_Nexus.Platform.Windows;
#endif

namespace ComfyUI_Nexus.Views;

public partial class WorkspaceView : ContentView
{
	private readonly MauiWebViewBrowserSurface _mauiBrowserSurface;

	internal event EventHandler<NexusBrowserNavigationEventArgs>? WebViewNavigated;
	internal event EventHandler? BrowserSurfaceChanged;

	public WorkspaceView()
	{
		InitializeComponent();
		_mauiBrowserSurface = new MauiWebViewBrowserSurface(ComfyWebView);
		_mauiBrowserSurface.Navigated += OnBrowserSurfaceNavigated;
		BrowserSurface = _mauiBrowserSurface;
		XamlLifetimeDiagnostics.RecordBrowser("workspace", "maui-selected");

#if WINDOWS
		if (string.Equals(Environment.GetEnvironmentVariable("NEXUS_WEBVIEW_HOST"), "composition", StringComparison.OrdinalIgnoreCase))
		{
			var compositionSurface = new NexusCompositionWebViewHost();
			compositionSurface.Navigated += OnBrowserSurfaceNavigated;
			compositionSurface.Failed += OnCompositionSurfaceFailed;
			ComfyWebView.IsVisible = false;
			BrowserSurfaceHost.Children.Add(compositionSurface);
			BrowserSurface = compositionSurface;
			XamlLifetimeDiagnostics.RecordBrowser("workspace", "composition-requested");
			NexusLog.Info("[WEBVIEW] Composition host requested.");
		}
#endif
	}

	internal INexusBrowserSurface BrowserSurface { get; private set; }

	internal void SetChromeOpacity(double opacity)
	{
		WorkspaceChromeGrid.Opacity = opacity;
	}

	internal void SetBrowserSurfaceState(bool isVisible, double opacity)
	{
		BrowserSurface.IsVisible = isVisible;
		BrowserSurface.Opacity = opacity;
	}

	internal void HideBrowserSurface()
	{
		BrowserSurface.IsVisible = false;
	}

	private void OnBrowserSurfaceNavigated(object? sender, NexusBrowserNavigationEventArgs e)
		=> WebViewNavigated?.Invoke(this, e);

#if WINDOWS
	private void OnCompositionSurfaceFailed(object? sender, Exception exception)
	{
		if (sender is not NexusCompositionWebViewHost compositionSurface || BrowserSurface != compositionSurface)
		{
			return;
		}

		compositionSurface.Navigated -= OnBrowserSurfaceNavigated;
		compositionSurface.Failed -= OnCompositionSurfaceFailed;
		BrowserSurfaceHost.Children.Remove(compositionSurface);
		compositionSurface.Dispose();
		ComfyWebView.IsVisible = true;
		BrowserSurface = _mauiBrowserSurface;
		XamlLifetimeDiagnostics.RecordBrowser("workspace", "maui-fallback");
		BrowserSurfaceChanged?.Invoke(this, EventArgs.Empty);
	}
#endif
}
