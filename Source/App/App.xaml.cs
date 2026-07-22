using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Services;
using Microsoft.Extensions.DependencyInjection;
#if WINDOWS
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;
using WindowsColor = Windows.UI.Color;
#endif

namespace ComfyUI_Nexus;

#if WINDOWS
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763.0")]
#endif
public partial class App : Application
{
	internal NexusAppManager Runtime { get; }

#if WINDOWS
	private const int SafeShutdownMinimumVisibleMilliseconds = 1000;

	private bool _isExitConfirmed;
	private bool _isExitRequestPending;
	private readonly HashSet<IntPtr> _hookedWindowHandles = [];
	private readonly HashSet<IntPtr> _styledWindowHandles = [];
	private bool _isInitialWindowPlacementApplied;
#endif

	public App(IServiceProvider services)
	{
		ArgumentNullException.ThrowIfNull(services);
		Runtime = services.GetRequiredService<NexusAppManager>();
#if WINDOWS
		string webViewDataPath = ComfyInstallService.GetLocalRuntimePath("Cache/WebView2");
		Directory.CreateDirectory(webViewDataPath);
		Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", webViewDataPath, EnvironmentVariableTarget.Process);
#endif
		NexusLog.InitializePersistentLog(ComfyInstallService.GetLocalRuntimePath("Logs"));
		XamlLifetimeDiagnostics.WriteSnapshot("startup");
#if WINDOWS
		WindowsCrashReportDiagnostics.ReportRecentCrashArtifacts(TimeSpan.FromHours(12));
#endif
		try
		{
			PortablePackageBootstrapper.Materialize();
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[STARTUP] Portable package materialization failed");
		}

		Runtime.ExceptionDiagnostics.Attach();
#if DEBUG
		Runtime.BindingDiagnostics.Attach();
#endif
		Runtime.SessionDiagnostics.Attach(ComfyInstallService.GetLocalRuntimePath("State"));
#if WINDOWS
		Microsoft.UI.Xaml.Application.Current.UnhandledException += OnUnhandledException;
#endif
		NexusLog.Info("[STARTUP] App constructor starting.");
		try
		{
			SetupSettingsService settingsService = Runtime.Settings;
			string configuredLanguage = settingsService.Settings.LanguageCode;
			LocalizationManager.Initialize(configuredLanguage);
			if (!string.Equals(configuredLanguage, LocalizationManager.ActiveLanguage, StringComparison.Ordinal))
			{
				settingsService.Settings.LanguageCode = LocalizationManager.ActiveLanguage;
				settingsService.Save();
			}
			InitializeComponent();


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
		double savedWidth = Runtime.Preferences.Get(PreferenceKeys.WindowWidth, WindowOptions.DefaultWidth);
		double savedHeight = Runtime.Preferences.Get(PreferenceKeys.WindowHeight, WindowOptions.DefaultHeight);
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
			Title = SafeAppInfo.DisplayName,
			TitleBar = new Microsoft.Maui.Controls.TitleBar
			{
				Title = SafeAppInfo.DisplayName,
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
		appWindow.Title = SafeAppInfo.DisplayName;
		platformWindow.Title = SafeAppInfo.DisplayName;

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
		appWindow.Closing += async (_, args) =>
		{
			try
			{
				await OnAppWindowClosingAsync(window, platformWindow, args);
			}
			catch (Exception ex)
			{
				args.Cancel = true;
				NexusLog.Exception(ex, "[SHUTDOWN] Native window closing handler failed");
			}
		};
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
		await RequestExitWithConfirmationAsync(window, platformWindow);
	}

	internal async Task RequestExitWithConfirmationAsync(Window? window)
	{
		if (_isExitConfirmed || _isExitRequestPending)
		{
			return;
		}

		_isExitRequestPending = true;
		try
		{
			window ??= Windows.FirstOrDefault();
			if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window platformWindow)
			{
				await RequestExitWithConfirmationCoreAsync(window, platformWindow);
				return;
			}

			if (window != null)
			{
				if (IsConfiguredServerPortActive())
				{
					NexusLog.Warning("[SHUTDOWN] Exit request was blocked because the native confirmation surface is unavailable while the configured server port is active.");
					return;
				}

				Runtime.SessionDiagnostics.MarkExitIntent(
					NexusExitIntent.KeepServerRunningAndExit,
					"nativeConfirmationSurfaceUnavailable");
				await ExitWithoutNativeConfirmationAsync(window);
			}
		}
		finally
		{
			_isExitRequestPending = false;
		}
	}

	private async Task RequestExitWithConfirmationAsync(Window window, Microsoft.UI.Xaml.Window platformWindow)
	{
		if (_isExitConfirmed || _isExitRequestPending)
		{
			return;
		}

		_isExitRequestPending = true;
		try
		{
			await RequestExitWithConfirmationCoreAsync(window, platformWindow);
		}
		finally
		{
			_isExitRequestPending = false;
		}
	}

	private async Task RequestExitWithConfirmationCoreAsync(Window window, Microsoft.UI.Xaml.Window platformWindow)
	{
		if (_isExitConfirmed)
		{
			return;
		}

		Page? page = window.Page ?? Windows.FirstOrDefault(static candidate => candidate.Page != null)?.Page;
		if (page == null)
		{
			if (IsConfiguredServerPortActive())
			{
				NexusLog.Warning("[SHUTDOWN] Exit request was blocked because no confirmation page is available while the configured server port is active.");
				return;
			}

			await ExitSafelyAsync(platformWindow);
			return;
		}

		ComfyServerProcessInfo? serverProcess = Runtime.ServerProcesses.FindServerProcess();
		if (serverProcess == null)
		{
			if (IsConfiguredServerPortActive())
			{
				const string unresolvedServerMessage = "The configured ComfyUI port is still active, but Nexus could not identify its server process. Exit was cancelled to avoid leaving the server in an unknown state.";
				NexusLog.Warning($"[SHUTDOWN] {unresolvedServerMessage}");
				await page.DisplayAlertAsync(
					LocalizationManager.Text("app.shutdown_failed_title"),
					unresolvedServerMessage,
					LocalizationManager.Text("common.ok"));
				return;
			}

			await ExitSafelyAsync(platformWindow);
			return;
		}

		string action = await page.DisplayActionSheetAsync(
			$"ComfyUI server is still running on port {Runtime.Settings.Settings.ServerPort}.\n{serverProcess.ProcessName} ({serverProcess.ProcessId}) // {serverProcess.Source}",
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
			Runtime.SessionDiagnostics.MarkExitIntent(
				NexusExitIntent.KillServerAndExit,
				$"serverPid={serverProcess.ProcessId}, source={serverProcess.Source}");
			await ShutdownServerAndExitAsync(platformWindow, page, serverProcess);
			return;
		}

		Runtime.SessionDiagnostics.MarkExitIntent(
			NexusExitIntent.KeepServerRunningAndExit,
			$"serverPid={serverProcess.ProcessId}, source={serverProcess.Source}");
		await ExitSafelyAsync(platformWindow);
	}

	private async Task ExitWithoutNativeConfirmationAsync(Window window)
	{
		ServerLifecycleResult result = await Runtime.ServerLifecycle.RunAsync(
			new ServerLifecycleRequest(ServerLifecycleMode.KeepServerRunningAndExit));
		if (!result.IsSuccess)
		{
			NexusLog.Warning($"[SHUTDOWN] Exit was retained because shell service shutdown did not complete: {result.Message}");
			return;
		}

		MarkExitConfirmed();
		CloseWindow(window);
	}

	private async Task ShutdownServerAndExitAsync(
		Microsoft.UI.Xaml.Window platformWindow,
		Page page,
		ComfyServerProcessInfo serverProcess)
	{
		MainPage? mainPage = Shell.Current?.CurrentPage as MainPage;
		if (mainPage != null)
		{
			await mainPage.CloseAllPopupSurfacesAsync();
			await mainPage.SetShutdownBlockerVisibleAsync(
				true,
				LocalizationManager.Text("app.server_shutdown_title"),
				LocalizationManager.Text("app.server_shutdown_detail"));
		}

		try
		{
			ServerLifecycleResult result = await Runtime.ServerLifecycle.RunAsync(new ServerLifecycleRequest(ServerLifecycleMode.KillServerAndExit));
			if (!result.IsSuccess)
			{
				throw new InvalidOperationException(result.Message);
			}

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

		await CloseConfirmedAsync(platformWindow);
	}

	private async Task ExitSafelyAsync(Microsoft.UI.Xaml.Window platformWindow)
	{
		System.Diagnostics.Stopwatch? minimumVisibleTimer = null;
		MainPage? mainPage = Shell.Current?.CurrentPage as MainPage;
		if (mainPage != null)
		{
			await mainPage.CloseAllPopupSurfacesAsync();
			await mainPage.SetShutdownBlockerVisibleAsync(
				true,
				LocalizationManager.Text("app.safe_shutdown_title"),
				LocalizationManager.Text("app.safe_shutdown_detail"));
			minimumVisibleTimer = System.Diagnostics.Stopwatch.StartNew();
		}

		ServerLifecycleResult result = await Runtime.ServerLifecycle.RunAsync(new ServerLifecycleRequest(ServerLifecycleMode.KeepServerRunningAndExit));
		if (!result.IsSuccess)
		{
			NexusLog.Warning($"[SHUTDOWN] Exit was retained because shell service shutdown did not complete: {result.Message}");
			if (mainPage != null)
			{
				await mainPage.SetShutdownBlockerVisibleAsync(false);
			}

			return;
		}

		if (minimumVisibleTimer != null)
		{
			int remainingMilliseconds = SafeShutdownMinimumVisibleMilliseconds - (int)minimumVisibleTimer.ElapsedMilliseconds;
			if (remainingMilliseconds > 0)
			{
				await Task.Delay(remainingMilliseconds);
			}
		}

		await CloseConfirmedAsync(platformWindow);
	}

	private Task CloseConfirmedAsync(Microsoft.UI.Xaml.Window platformWindow)
	{
		if (_isExitConfirmed)
		{
			return Task.CompletedTask;
		}

		MarkExitConfirmed();
		platformWindow.Close();
		return Task.CompletedTask;
	}

	private void MarkExitConfirmed()
	{
		if (_isExitConfirmed)
		{
			return;
		}

		_isExitConfirmed = true;
		try
		{
			Runtime.ControlDeckWindow.Close();
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SHUTDOWN] Control Deck close failed");
		}
	}

	private bool IsConfiguredServerPortActive()
	{
		return LocalServerProbe.IsPortListening(Runtime.Settings.Settings.ServerPort);
	}

#endif

#if !WINDOWS
	internal Task RequestExitWithConfirmationAsync(Window? window)
	{
		window ??= Windows.FirstOrDefault();
		if (window != null)
		{
			CloseWindow(window);
		}

		return Task.CompletedTask;
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

		try
		{
			PersistWindowSize(window);
			Runtime.SessionDiagnostics.MarkCleanShutdown("Window.Destroying");
			// MAUI may raise control unload callbacks after Window.Destroying. Keep the
			// app-runtime owner alive until process exit so those callbacks cannot observe
			// disposed services or an unavailable NexusAppManager singleton.
			NexusLog.Info("[SHUTDOWN] Native window teardown started. App runtime disposal is deferred to process exit.");
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[SHUTDOWN] Window teardown cleanup failed");
		}
		finally
		{
			window.SizeChanged -= OnWindowSizeChanged;
			window.Destroying -= OnWindowDestroying;
#if WINDOWS
			window.HandlerChanged -= OnWindowHandlerChanged;
			window.Created -= OnWindowCreated;
#endif
		}
	}

	private void PersistWindowSize(Window window)
	{
		if (window.Width < WindowOptions.MinimumWidth || window.Height < WindowOptions.MinimumHeight)
		{
			return;
		}

		Runtime.Preferences.Set(PreferenceKeys.WindowWidth, window.Width);
		Runtime.Preferences.Set(PreferenceKeys.WindowHeight, window.Height);
	}
}
