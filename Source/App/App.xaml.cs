using Microsoft.Extensions.DependencyInjection;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Services;
#if WINDOWS
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;
using WindowsColor = Windows.UI.Color;
#endif

namespace ComfyUI_Nexus;

public partial class App : Application
{
#if WINDOWS
	private bool _isExitConfirmed;
	private readonly HashSet<IntPtr> _hookedWindowHandles = [];
	private readonly HashSet<IntPtr> _styledWindowHandles = [];
	private bool _isInitialWindowPlacementApplied;
#endif

	public App()
	{
#if WINDOWS
		string webViewDataPath = ComfyInstallService.GetLocalRuntimePath("Cache/WebView2");
		Directory.CreateDirectory(webViewDataPath);
		Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", webViewDataPath, EnvironmentVariableTarget.Process);
#endif
		NexusLog.InitializePersistentLog(ComfyInstallService.GetLocalRuntimePath("Logs"));
		try
		{
			PortablePackageBootstrapper.Materialize();
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[STARTUP] Portable package materialization failed");
		}

		DebugExceptionDiagnostics.Attach();
#if WINDOWS
		Microsoft.UI.Xaml.Application.Current.UnhandledException += OnUnhandledException;
#endif
		NexusLog.Info("[STARTUP] App constructor starting.");
		try
		{
			SetupSettingsService settingsService = SetupSettingsService.Instance;
			string configuredLanguage = settingsService.Settings.LanguageCode;
			LocalizationManager.Initialize(configuredLanguage);
			if (!string.Equals(configuredLanguage, LocalizationManager.ActiveLanguage, StringComparison.Ordinal))
			{
				settingsService.Settings.LanguageCode = LocalizationManager.ActiveLanguage;
				settingsService.Save();
			}
			InitializeComponent();
			GpuDiscoveryService.Prewarm();
			NexusLog.Info("[STARTUP] App InitializeComponent completed.");
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[STARTUP] App InitializeComponent failed");
			throw;
		}
	}

#if WINDOWS
	private static void OnUnhandledException(object? sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
		=> XamlUnhandledExceptionDiagnostics.Handle("XAML", sender, e);
#endif

	protected override Window CreateWindow(IActivationState? activationState)
	{
		NexusLog.Info("[STARTUP] CreateWindow starting.");
		double savedWidth = PortablePreferences.Get(PreferenceKeys.WindowWidth, WindowOptions.DefaultWidth);
		double savedHeight = PortablePreferences.Get(PreferenceKeys.WindowHeight, WindowOptions.DefaultHeight);
		double launchWidth = Math.Max(WindowOptions.MinimumWidth, savedWidth);
		double launchHeight = Math.Max(WindowOptions.MinimumHeight, savedHeight);

		AppShell shell;
		try
		{
			shell = new AppShell();
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[STARTUP] AppShell creation failed");
			throw;
		}

		var window = new Window(shell)
		{
			Title = "ComfyUI Nexus",
			TitleBar = new Microsoft.Maui.Controls.TitleBar
			{
				Title = "ComfyUI Nexus",
				HeightRequest = 32,
				BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#070C14"),
				ForegroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#DAF5FF"),
				Padding = new Thickness(12, 0)
			},
			Width = launchWidth,
			Height = launchHeight,
			MinimumWidth = WindowOptions.MinimumWidth,
			MinimumHeight = WindowOptions.MinimumHeight
		};

		window.SizeChanged += OnWindowSizeChanged;
		window.Destroying += OnWindowDestroying;
#if WINDOWS
		window.HandlerChanged += OnWindowHandlerChanged;
		window.Created += OnWindowCreated;
#endif
		NexusLog.Info($"[STARTUP] Window created. size={launchWidth:0}x{launchHeight:0}");
		return window;
	}

#if WINDOWS
	private void OnWindowHandlerChanged(object? sender, EventArgs e)
	{
		if (sender is Window window)
		{
			TryAttachWindowClosing(window);
			TryApplyWindowTitleBar(window);
		}
	}

	private void OnWindowCreated(object? sender, EventArgs e)
	{
		if (sender is not Window window)
		{
			return;
		}

		TryAttachWindowClosing(window);
		TryApplyInitialWindowPlacement(window);
		TryApplyWindowTitleBar(window);
		window.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), () => TryAttachWindowClosing(window));
		window.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), () => TryApplyInitialWindowPlacement(window));
		window.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), () => TryApplyWindowTitleBar(window));
	}

	private void TryApplyWindowTitleBar(Window window)
	{
		if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window platformWindow)
		{
			return;
		}

		IntPtr hwnd = WindowNative.GetWindowHandle(platformWindow);
		if (hwnd == IntPtr.Zero || _styledWindowHandles.Contains(hwnd))
		{
			return;
		}

		var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
		AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
		appWindow.Title = "ComfyUI Nexus";
		platformWindow.Title = "ComfyUI Nexus";

		if (!AppWindowTitleBar.IsCustomizationSupported())
		{
			return;
		}

		AppWindowTitleBar titleBar = appWindow.TitleBar;
		WindowsColor background = WindowsColor.FromArgb(255, 7, 12, 20);
		WindowsColor inactiveBackground = WindowsColor.FromArgb(255, 10, 16, 26);
		WindowsColor foreground = WindowsColor.FromArgb(255, 218, 245, 255);
		WindowsColor inactiveForeground = WindowsColor.FromArgb(255, 105, 132, 148);
		WindowsColor hoverBackground = WindowsColor.FromArgb(255, 15, 52, 70);
		WindowsColor pressedBackground = WindowsColor.FromArgb(255, 19, 73, 94);

		titleBar.BackgroundColor = background;
		titleBar.InactiveBackgroundColor = inactiveBackground;
		titleBar.ForegroundColor = foreground;
		titleBar.InactiveForegroundColor = inactiveForeground;
		titleBar.ButtonBackgroundColor = background;
		titleBar.ButtonInactiveBackgroundColor = inactiveBackground;
		titleBar.ButtonForegroundColor = foreground;
		titleBar.ButtonInactiveForegroundColor = inactiveForeground;
		titleBar.ButtonHoverBackgroundColor = hoverBackground;
		titleBar.ButtonHoverForegroundColor = WindowsColor.FromArgb(255, 49, 216, 255);
		titleBar.ButtonPressedBackgroundColor = pressedBackground;
		titleBar.ButtonPressedForegroundColor = WindowsColor.FromArgb(255, 255, 255, 255);

		_styledWindowHandles.Add(hwnd);
	}

	private void TryAttachWindowClosing(Window window)
	{
		if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window platformWindow)
		{
			return;
		}

		IntPtr hwnd = WindowNative.GetWindowHandle(platformWindow);
		if (hwnd == IntPtr.Zero || !_hookedWindowHandles.Add(hwnd))
		{
			return;
		}

		var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
		AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
		appWindow.Closing += async (_, args) => await OnAppWindowClosingAsync(window, platformWindow, args);
	}

	private void TryApplyInitialWindowPlacement(Window window)
	{
		if (_isInitialWindowPlacementApplied ||
			window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window platformWindow)
		{
			return;
		}

		IntPtr hwnd = WindowNative.GetWindowHandle(platformWindow);
		if (hwnd == IntPtr.Zero)
		{
			return;
		}

		var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
		AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
		DisplayArea displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
		RectInt32 workArea = displayArea.WorkArea;

		(int targetWidth, int targetHeight) = ResolveInitialWindowSize(workArea);

		int targetX = workArea.X + (workArea.Width - targetWidth) / 2;
		int targetY = workArea.Y + (workArea.Height - targetHeight) / 2;

		appWindow.MoveAndResize(new RectInt32(targetX, targetY, targetWidth, targetHeight));
		_isInitialWindowPlacementApplied = true;
	}

	private static (int Width, int Height) ResolveInitialWindowSize(RectInt32 workArea)
	{
		double aspect = WindowOptions.InitialAspectWidth / WindowOptions.InitialAspectHeight;
		int targetWidth = Math.Max((int)Math.Round(workArea.Width * WindowOptions.InitialScreenWidthRatio), (int)WindowOptions.MinimumWidth);
		int targetHeight = Math.Max((int)Math.Round(targetWidth / aspect), (int)WindowOptions.MinimumHeight);

		if (targetHeight > workArea.Height)
		{
			targetHeight = workArea.Height;
			targetWidth = Math.Max((int)Math.Round(targetHeight * aspect), (int)WindowOptions.MinimumWidth);
		}

		return (
			Math.Min(targetWidth, workArea.Width),
			Math.Min(targetHeight, workArea.Height));
	}

	private async Task OnAppWindowClosingAsync(Window window, Microsoft.UI.Xaml.Window platformWindow, AppWindowClosingEventArgs args)
	{
		if (_isExitConfirmed)
		{
			return;
		}

		args.Cancel = true;

		Page? page = window.Page ?? Windows.FirstOrDefault(static candidate => candidate.Page != null)?.Page;
		if (page == null)
		{
			await ConfirmExitAsync(platformWindow);
			return;
		}

		ComfyServerProcessInfo? serverProcess = ComfyServerProcessRegistry.FindServerProcess();
		if (serverProcess == null)
		{
			await ConfirmExitAsync(platformWindow);
			return;
		}

		string action = await page.DisplayActionSheetAsync(
			$"ComfyUI server is still running on port {SetupSettingsService.Instance.Settings.ServerPort}.\n{serverProcess.ProcessName} ({serverProcess.ProcessId}) // {serverProcess.Source}",
			"Cancel",
			null,
			"Kill server and exit",
			"Keep server running and exit");

		if (string.Equals(action, "Cancel", StringComparison.Ordinal) ||
			string.IsNullOrWhiteSpace(action))
		{
			return;
		}

		if (string.Equals(action, "Kill server and exit", StringComparison.Ordinal))
		{
			MainPage? mainPage = Shell.Current?.CurrentPage as MainPage;
			if (mainPage != null)
			{
				await mainPage.SetShutdownBlockerVisibleAsync(
					true,
					"SHUTTING DOWN",
					"Stopping ComfyUI server before Nexus exits...");
			}

			try
			{
				await GpuDiscoveryService.ShutdownAsync();
				await ComfyServerProcessRegistry.ShutdownAsync(serverProcess, TimeSpan.FromSeconds(5));
			}
			catch (Exception ex)
			{
				if (mainPage != null)
				{
					await mainPage.SetShutdownBlockerVisibleAsync(false);
				}

				await page.DisplayAlertAsync(
					LocalizationManager.Text("app.shutdown_failed_title"),
					ex.Message,
					LocalizationManager.Text("common.ok"));
				return;
			}
		}
		else
		{
			await GpuDiscoveryService.ShutdownAsync();
		}

		await ConfirmExitAsync(platformWindow, shutdownGpu: false);
	}

	private async Task ConfirmExitAsync(Microsoft.UI.Xaml.Window platformWindow, bool shutdownGpu = true)
	{
		if (shutdownGpu)
		{
			await GpuDiscoveryService.ShutdownAsync();
		}

		_isExitConfirmed = true;
		platformWindow.Close();
	}
#endif

	private void OnWindowSizeChanged(object? sender, EventArgs e)
	{
		if (sender is not Window window)
		{
			return;
		}

		PersistWindowSize(window);
	}

	private void OnWindowDestroying(object? sender, EventArgs e)
	{
		if (sender is not Window window)
		{
			return;
		}

		PersistWindowSize(window);
		NexusLog.ShutdownPersistentLog();
		window.SizeChanged -= OnWindowSizeChanged;
		window.Destroying -= OnWindowDestroying;
#if WINDOWS
		window.HandlerChanged -= OnWindowHandlerChanged;
		window.Created -= OnWindowCreated;
#endif
	}

	private static void PersistWindowSize(Window window)
	{
		if (window.Width < WindowOptions.MinimumWidth || window.Height < WindowOptions.MinimumHeight)
		{
			return;
		}

		PortablePreferences.Set(PreferenceKeys.WindowWidth, window.Width);
		PortablePreferences.Set(PreferenceKeys.WindowHeight, window.Height);
	}
}
