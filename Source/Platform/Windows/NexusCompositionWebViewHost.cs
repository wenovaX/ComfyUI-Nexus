#if WINDOWS
using ComfyUI_Nexus.Diagnostics;
using Microsoft.Maui.Controls;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;

namespace ComfyUI_Nexus.Platform.Windows;

/// <summary>
/// Hosts WebView2 as a composition visual so the shell retains the native focus owner.
/// </summary>
internal sealed class NexusCompositionWebViewHost : ContentView, INexusBrowserSurface
{
	private readonly TaskCompletionSource _readyCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private CoreWebView2CompositionController? _controller;
	private ContainerVisual? _rootVisual;
	private FrameworkElement? _platformElement;
	private bool _initializing;
	private bool _disposed;
	private bool _devToolsEnabled;
	private long _lifecycleGeneration;
	private string? _pendingSource;

	internal event EventHandler<Exception>? Failed;

	internal NexusCompositionWebViewHost()
	{
		BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#070A13");
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
		HandlerChanged += OnHandlerChanged;
	}

	public VisualElement InputElement => this;
	public bool IsReady => !_disposed && _controller?.CoreWebView2 != null;
	public event EventHandler? Ready;
	public event EventHandler<NexusBrowserNavigationEventArgs>? Navigated;

	public async Task NavigateAsync(string source)
	{
		_pendingSource = source;
		await EnsureReadyAsync();
		if (!_disposed && _controller?.CoreWebView2 != null)
		{
			_controller.CoreWebView2.Navigate(source);
		}
	}

	public async Task<string?> ExecuteScriptAsync(string script)
	{
		await EnsureReadyAsync();
		return _disposed || _controller?.CoreWebView2 == null
			? null
			: await _controller.CoreWebView2.ExecuteScriptAsync(script);
	}

	public Task EnsureReadyAsync()
	{
		if (_disposed)
		{
			return Task.CompletedTask;
		}

		if (!_initializing && _controller == null && Handler != null)
		{
			_ = MainThread.InvokeOnMainThreadAsync(InitializeAsync);
		}

		return _readyCompletion.Task;
	}

	public async Task ConfigureBridgeAsync(Func<string, Task> processMessageAsync, Action<string?> navigationStarting, Action? bridgeActivated, Func<NexusKey, bool, bool, bool, bool>? acceleratorKeyHandler)
	{
		await EnsureReadyAsync();
		if (_controller?.CoreWebView2 == null || _disposed)
		{
			return;
		}

		await WindowsWebViewService.ConfigureCompositionBridgeAsync(
			_controller.CoreWebView2,
			_controller,
			processMessageAsync,
			navigationStarting,
			bridgeActivated,
			acceleratorKeyHandler);
	}

	public async Task DisableReloadHandlingAsync(Func<Task> clearBeforeUnloadAsync)
	{
		await EnsureReadyAsync();
		if (_controller?.CoreWebView2 != null && !_disposed)
		{
			await WindowsWebViewService.DisableCompositionReloadHandlingAsync(_controller.CoreWebView2, clearBeforeUnloadAsync);
		}
	}

	public Task SimulateFileDropAsync(string filePath, string? workflowRelativePath = null)
		=> Ui.WebViewUtility.SimulateFileDropAsync(this, filePath, workflowRelativePath);

	public void Reload()
	{
		if (!_disposed)
		{
			_controller?.CoreWebView2?.Reload();
		}
	}

	public void FocusBrowserInput()
	{
		// The composition host intentionally leaves focus with the Nexus shell.
	}

	public void SetDevToolsEnabled(bool isEnabled)
	{
		_devToolsEnabled = isEnabled;
		ApplyDevToolsState(openWindow: false);
	}

	public void OpenDevTools()
	{
		if (_devToolsEnabled)
		{
			ApplyDevToolsState(openWindow: true);
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_lifecycleGeneration++;
		_readyCompletion.TrySetResult();
		Loaded -= OnLoaded;
		Unloaded -= OnUnloaded;
		HandlerChanged -= OnHandlerChanged;
		DetachPlatformInput();
		if (_controller != null)
		{
			_controller.CursorChanged -= OnCursorChanged;
			_controller.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
			_controller.Close();
			_controller = null;
		}

		if (_platformElement != null)
		{
			ElementCompositionPreview.SetElementChildVisual(_platformElement, null);
		}

		_rootVisual = null;
		_platformElement = null;
		XamlLifetimeDiagnostics.RecordBrowser("workspace", "composition-disposed");
	}

	private void OnLoaded(object? sender, EventArgs e)
		=> _ = InitializeAsync();

	private void OnHandlerChanged(object? sender, EventArgs e)
	{
		if (Handler != null)
		{
			_ = InitializeAsync();
		}
	}

	private void OnUnloaded(object? sender, EventArgs e)
		=> Dispose();

	private async Task InitializeAsync()
	{
		if (_disposed || _initializing || _controller != null)
		{
			return;
		}

		_initializing = true;
		long lifecycleGeneration = _lifecycleGeneration;
		CoreWebView2CompositionController? createdController = null;
		try
		{
			FrameworkElement platformElement = Handler?.PlatformView as FrameworkElement
				?? throw new InvalidOperationException("Composition browser platform view is unavailable.");
			var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window
				?? throw new InvalidOperationException("Composition browser window is unavailable.");
			IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
			if (hwnd == IntPtr.Zero)
			{
				throw new InvalidOperationException("Composition browser window handle is unavailable.");
			}

			var environment = await CoreWebView2Environment.CreateAsync();
			if (!CanAttach(lifecycleGeneration, platformElement))
			{
				return;
			}

			var parentWindow = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)hwnd.ToInt64());
			createdController = await environment.CreateCoreWebView2CompositionControllerAsync(parentWindow);
			if (!CanAttach(lifecycleGeneration, platformElement))
			{
				createdController.Close();
				return;
			}

			var compositor = ElementCompositionPreview.GetElementVisual(platformElement).Compositor;
			var rootVisual = compositor.CreateContainerVisual();
			ElementCompositionPreview.SetElementChildVisual(platformElement, rootVisual);
			createdController.RootVisualTarget = rootVisual;
			createdController.AllowExternalDrop = true;
			createdController.CursorChanged += OnCursorChanged;
			createdController.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

			_platformElement = platformElement;
			_rootVisual = rootVisual;
			_controller = createdController;
			createdController = null;
			ApplyDevToolsState(openWindow: false);
			AttachPlatformInput(platformElement);
			UpdateBounds();
			Ready?.Invoke(this, EventArgs.Empty);
			_readyCompletion.TrySetResult();
			XamlLifetimeDiagnostics.RecordBrowser("workspace", "composition-ready");
			NexusLog.Info("[WEBVIEW] Composition host ready.");

			if (!string.IsNullOrWhiteSpace(_pendingSource))
			{
				_controller.CoreWebView2.Navigate(_pendingSource);
			}
		}
		catch (Exception) when (_disposed || lifecycleGeneration != _lifecycleGeneration)
		{
			createdController?.Close();
		}
		catch (Exception ex)
		{
			createdController?.Close();
			_readyCompletion.TrySetException(ex);
			XamlLifetimeDiagnostics.RecordBrowser("workspace", $"composition-failed:{ex.GetType().Name}");
			NexusLog.Warning($"[WEBVIEW] Composition host failed: {ex.GetType().Name} - {ex.Message}");
			Failed?.Invoke(this, ex);
		}
		finally
		{
			_initializing = false;
		}
	}

	private bool CanAttach(long lifecycleGeneration, FrameworkElement platformElement)
		=> !_disposed
			&& lifecycleGeneration == _lifecycleGeneration
			&& ReferenceEquals(Handler?.PlatformView, platformElement);

	private void ApplyDevToolsState(bool openWindow)
	{
		CoreWebView2? coreWebView2 = _controller?.CoreWebView2;
		if (_disposed || coreWebView2 == null)
		{
			return;
		}

		try
		{
			coreWebView2.Settings.AreDevToolsEnabled = _devToolsEnabled;
			coreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = _devToolsEnabled;
			if (_devToolsEnabled && openWindow)
			{
				coreWebView2.OpenDevToolsWindow();
			}
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"[WEBVIEW] Composition DevTools update failed: {ex.GetType().Name} - {ex.Message}");
		}
	}

	private void AttachPlatformInput(FrameworkElement platformElement)
	{
		platformElement.SizeChanged += OnPlatformSizeChanged;
		platformElement.PointerMoved += OnPointerMoved;
		platformElement.PointerPressed += OnPointerPressed;
		platformElement.PointerReleased += OnPointerReleased;
		platformElement.PointerWheelChanged += OnPointerWheelChanged;
	}

	private void DetachPlatformInput()
	{
		if (_platformElement == null)
		{
			return;
		}

		_platformElement.SizeChanged -= OnPlatformSizeChanged;
		_platformElement.PointerMoved -= OnPointerMoved;
		_platformElement.PointerPressed -= OnPointerPressed;
		_platformElement.PointerReleased -= OnPointerReleased;
		_platformElement.PointerWheelChanged -= OnPointerWheelChanged;
	}

	private void OnPlatformSizeChanged(object sender, SizeChangedEventArgs args)
		=> UpdateBounds();

	private void UpdateBounds()
	{
		if (_controller == null || _platformElement == null || _disposed)
		{
			return;
		}

		double scale = _platformElement.XamlRoot?.RasterizationScale ?? 1d;
		_controller.Bounds = new global::Windows.Foundation.Rect(0, 0,
			Math.Max(1, _platformElement.ActualWidth * scale),
			Math.Max(1, _platformElement.ActualHeight * scale));
	}

	private void OnPointerMoved(object sender, PointerRoutedEventArgs args)
		=> ForwardMouse(CoreWebView2MouseEventKind.Move, args);

	private void OnPointerPressed(object sender, PointerRoutedEventArgs args)
		=> ForwardMouse(args.GetCurrentPoint(_platformElement).Properties.IsRightButtonPressed
			? CoreWebView2MouseEventKind.RightButtonDown
			: CoreWebView2MouseEventKind.LeftButtonDown, args);

	private void OnPointerReleased(object sender, PointerRoutedEventArgs args)
		=> ForwardMouse(args.GetCurrentPoint(_platformElement).Properties.PointerUpdateKind.ToString().Contains("Right", StringComparison.Ordinal)
			? CoreWebView2MouseEventKind.RightButtonUp
			: CoreWebView2MouseEventKind.LeftButtonUp, args);

	private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs args)
		=> ForwardMouse(CoreWebView2MouseEventKind.Wheel, args, args.GetCurrentPoint(_platformElement).Properties.MouseWheelDelta);

	private void ForwardMouse(CoreWebView2MouseEventKind kind, PointerRoutedEventArgs args, int mouseData = 0)
	{
		if (_controller == null || _platformElement == null || _disposed)
		{
			return;
		}

		var point = args.GetCurrentPoint(_platformElement);
		var keys = CoreWebView2MouseEventVirtualKeys.None;
		if (point.Properties.IsLeftButtonPressed) keys |= CoreWebView2MouseEventVirtualKeys.LeftButton;
		if (point.Properties.IsRightButtonPressed) keys |= CoreWebView2MouseEventVirtualKeys.RightButton;
		if (point.Properties.IsMiddleButtonPressed) keys |= CoreWebView2MouseEventVirtualKeys.MiddleButton;
		_controller.SendMouseInput(kind, keys, unchecked((uint)mouseData), new global::Windows.Foundation.Point(point.Position.X, point.Position.Y));
	}

	private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
		=> Navigated?.Invoke(this, new NexusBrowserNavigationEventArgs(_controller?.CoreWebView2.Source, args.IsSuccess, args.WebErrorStatus.ToString()));

	private void OnCursorChanged(object? sender, object args)
	{
		NexusAppManager.Instance.Platform.Cursor.SetCssCursor(this, "default");
	}
}
#endif
