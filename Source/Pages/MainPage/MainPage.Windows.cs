#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Input;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Platform.Windows;
using ComfyUI_Nexus.Views.Controls.Buttons;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	// Keep disabled by default: keyboard events are a hot path and diagnostic strings add measurable noise.
	private const bool KeyboardIssueDiagnosticsEnabled = false;

	partial void InitializePlatformHooks()
	{
		WorkspaceControl.BrowserSurface.Ready += OnBrowserSurfaceReady;
		WorkspaceControl.BrowserSurfaceChanged += OnBrowserSurfaceChanged;
		if (WorkspaceControl.BrowserSurface.IsReady)
		{
			_ = ConfigureBrowserSurfaceAsync();
		}
		HandlerChanged += OnMainPageHandlerChanged;
		RailResizeHandleControl.HandleElement.HandlerChanged += OnRailResizeHandleHandlerChanged;
	}

	private void OnBrowserSurfaceChanged(object? sender, EventArgs e)
	{
		WorkspaceControl.BrowserSurface.Ready += OnBrowserSurfaceReady;
		_ = ConfigureBrowserSurfaceAsync();
	}

	private void OnBrowserSurfaceReady(object? sender, EventArgs e)
		=> _ = ConfigureBrowserSurfaceAsync();

	private async Task ConfigureBrowserSurfaceAsync()
	{
		try
		{
			await WorkspaceControl.BrowserSurface.ConfigureBridgeAsync(
				raw =>
				{
					ProcessMessage(raw);
					return Task.CompletedTask;
				},
				uri => _ = MainThread.InvokeOnMainThreadAsync(() => OnWebViewNavigationStarting(uri)),
				() =>
				{
					Log("WebView2 bridge activated");
					_webViewPlatformReady.TrySetResult(true);
				},
				(key, ctrl, shift, alt) => TryHandleWebViewAcceleratorKey(key, ctrl, shift, alt));

			_devToolsController.Apply(WorkspaceControl.BrowserSurface);
		}
		catch (Exception ex)
		{
			Log($"[WEBVIEW] Bridge configuration failed: {ex.GetType().Name} - {ex.Message}");
		}
	}

	private void OnMainPageHandlerChanged(object? sender, EventArgs e)
	{
		if (Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement)
		{
			var window = Microsoft.Maui.Controls.Application.Current?.Windows.Count > 0
				? Microsoft.Maui.Controls.Application.Current.Windows[0].Handler?.PlatformView as Microsoft.UI.Xaml.Window
				: null;

			if (window?.Content is Microsoft.UI.Xaml.FrameworkElement windowContent)
			{
				// Route top-level keyboard shortcuts through the shell before the embedded page consumes them.
				var inputContext = CreateNexusInputContext();

				long lastHandledEscapeTicks = 0;
				async void RouteNativeKeyDown(object? sender, KeyRoutedEventArgs args)
				{
					bool ctrl = PlatformManager.Current.Keyboard.IsCtrlPressed();
					bool shift = PlatformManager.Current.Keyboard.IsShiftPressed();
					bool alt = PlatformManager.Current.Keyboard.IsAltPressed();
					NexusKey key = PlatformManager.Current.Keyboard.ToNexusKey(args.Key);
					var modifiers = new NexusKeyModifiers(ctrl, shift, alt);
					bool nativeInputFocused = IsNativeInputFocused() || _webInputMode;
					bool mediaViewerOpen = MediaViewerOverlayControl?.IsOpen == true;
					bool railShortcutAvailable = !alt && RailControl.CanHandleKeyboardShortcut(key, ctrl, shift);
					bool modalKeyboardOwnerIsOpen = NexusDialogService.IsOpen || mediaViewerOpen;
					if (CanLogKeyboardIssueRoute(key))
					{
						LogKeyboardIssueRoute(
							"NATIVE_IN",
							key,
							modifiers,
							$"argsHandled={args.Handled} wasKeyDown={args.KeyStatus.WasKeyDown} modal={modalKeyboardOwnerIsOpen} nativeInput={nativeInputFocused} railCan={railShortcutAvailable}");
					}

					if (key == NexusKey.Escape && DateTime.UtcNow.Ticks - lastHandledEscapeTicks < TimeSpan.FromMilliseconds(100).Ticks)
					{
						LogKeyboardIssueRoute("NATIVE_DEBOUNCE", key, modifiers);
						args.Handled = true;
						return;
					}

					if (modalKeyboardOwnerIsOpen &&
						key == NexusKey.Enter &&
						args.KeyStatus.WasKeyDown)
					{
						LogKeyboardIssueRoute("NATIVE_REPEAT_SUPPRESS", key, modifiers);
						args.Handled = true;
						return;
					}

					if (args.Handled && key != NexusKey.Escape && !modalKeyboardOwnerIsOpen)
					{
						LogKeyboardIssueRoute("NATIVE_SKIP", key, modifiers, "reason=already-handled");
						return;
					}

					if (key == NexusKey.F12 &&
						_controlDeckWindow.IsOpen &&
						_devToolsController.TryOpen(WorkspaceControl.BrowserSurface))
					{
						args.Handled = true;
						return;
					}

					bool shouldPreHandleNative =
						!nativeInputFocused &&
						(modalKeyboardOwnerIsOpen ||
							railShortcutAvailable ||
							_uiSurfaceManager.BlocksWebViewKeyboard ||
							NexusInputManager.IsGlobalAppShortcut(key, ctrl, shift, alt));
					if (shouldPreHandleNative)
					{
						args.Handled = true;
						LogKeyboardIssueRoute("NATIVE_PREHANDLED", key, modifiers);
					}

					var route = await _inputRouter.RouteAsync(
						NexusKeyboardInputSource.NativeWindow,
						inputContext,
						key,
						modifiers,
						nativeInputFocused,
						mediaViewerOpen,
						TryHandleMediaViewerShortcutAsync);
					if (CanLogKeyboardIssueRoute(key))
					{
						LogKeyboardIssueRoute("NATIVE_ROUTE", key, modifiers, $"stage={route.Stage} result={route.Kind}");
					}

					if (route.Kind is NexusKeyRouteDecisionKind.Consume or NexusKeyRouteDecisionKind.ConsumeAndRun)
					{
						if (CanLogKeyboardIssueRoute(key))
						{
							LogKeyboardIssueRoute("NATIVE_HANDLED", key, modifiers, $"stage={route.Stage}");
						}
						args.Handled = true;
						if (key == NexusKey.Escape)
						{
							lastHandledEscapeTicks = DateTime.UtcNow.Ticks;
						}
					}
					else if (route.Kind == NexusKeyRouteDecisionKind.RelayToWeb &&
						await TryRelayNativeKeyToWebAsync(key, args.Key, modifiers))
					{
						LogKeyboardIssueRoute("NATIVE_RELAY_WEB", key, modifiers);
						args.Handled = true;
					}
					else if (route.Kind == NexusKeyRouteDecisionKind.RelayToWeb)
					{
						if (CanLogKeyboardIssueRoute(key))
						{
							LogKeyboardIssueRoute("NATIVE_RELAY_PASS", key, modifiers, $"argsHandled={args.Handled} preHandled={shouldPreHandleNative}");
						}
					}
				}

				windowContent.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(RouteNativeKeyDown), handledEventsToo: true);

				windowContent.AddHandler(
					UIElement.PointerReleasedEvent,
					new PointerEventHandler(OnNativeWindowPointerReleased),
					handledEventsToo: true);
				windowContent.AddHandler(
					UIElement.PointerExitedEvent,
					new PointerEventHandler(OnNativeWindowPointerExited),
					handledEventsToo: true);
			}
		}
	}

	private void OnNativeWindowPointerReleased(object? sender, PointerRoutedEventArgs args)
	{
		_ = MainThread.InvokeOnMainThreadAsync(HandleGlobalPointerReleasedAsync);
	}

	private static void OnNativeWindowPointerExited(object? sender, PointerRoutedEventArgs args)
	{
		if (sender is not Microsoft.UI.Xaml.FrameworkElement windowContent)
		{
			return;
		}

		var position = args.GetCurrentPoint(windowContent).Position;
		if (position.X >= 0
			&& position.Y >= 0
			&& position.X <= windowContent.ActualWidth
			&& position.Y <= windowContent.ActualHeight)
		{
			return;
		}

		RailHoverRegistry.ResetAll();
	}

	private void OnRailResizeHandleHandlerChanged(object? sender, EventArgs e)
		=> ConfigureRailResizeCursor();

	private async Task HandleGlobalPointerReleasedAsync()
	{
		HeaderControl.DismissRunModeOptionsFromGlobalPointerRelease();
		if (CommandMenuControl.IsMenuVisible && !CommandMenuControl.IsPointerOverMenuBody())
		{
			await SetCommandMenuVisible(false);
		}
		if (HeaderControl.IsViewQueueActive())
		{
			await SyncViewQueueButtonVisualAsync();
		}
		await CompleteActiveAssetDragFromPointerReleaseAsync();
	}

	private bool TryHandleWebViewAcceleratorKey(NexusKey key, bool ctrl, bool shift, bool alt)
	{
		var modifiers = new NexusKeyModifiers(ctrl, shift, alt);
		bool railShortcutAvailable = !alt && RailControl.CanHandleKeyboardShortcut(key, ctrl, shift);
		bool shouldCapture = _inputRouter.ShouldCaptureWebViewAccelerator(
			key,
			modifiers,
			MediaViewerOverlayControl?.IsOpen == true) ||
			railShortcutAvailable;
		if (CanLogKeyboardIssueRoute(key))
		{
			LogKeyboardIssueRoute("WEB_ACCEL_IN", key, modifiers, $"capture={shouldCapture} railCan={railShortcutAvailable}");
		}
		if (!shouldCapture)
		{
			return false;
		}

		_ = MainThread.InvokeOnMainThreadAsync(async () =>
		{
			var inputContext = CreateNexusInputContext();
			var route = await _inputRouter.RouteAsync(
				NexusKeyboardInputSource.WebViewAccelerator,
				inputContext,
				key,
				modifiers,
				isNativeTextInputFocused: false,
				MediaViewerOverlayControl?.IsOpen == true,
				TryHandleMediaViewerShortcutAsync);
			if (CanLogKeyboardIssueRoute(key))
			{
				LogKeyboardIssueRoute("WEB_ACCEL_ROUTE", key, modifiers, $"stage={route.Stage} result={route.Kind}");
			}
		});
		return true;
	}

	private async Task<bool> TryRelayNativeKeyToWebAsync(NexusKey key, object? platformKey, NexusKeyModifiers modifiers)
	{
		if (IsNativeInputFocused() || !TryGetWebRelayKey(key, platformKey, out string relayKey))
		{
			return false;
		}

		await _webViewBridge.RelayShortcutAsync(relayKey, modifiers.Ctrl, modifiers.Shift, modifiers.Alt);
		return true;
	}

	private NexusInputContext CreateNexusInputContext()
		=> new(
			PerformSystemRebootAsync: PerformSystemReboot,
			OpenNexusCommandConsoleAsync: OpenNexusCommandConsoleAsync,
			CloseActiveWorkflowTab: CloseActiveWorkflowTab,
			ToggleRailAsync: ToggleRailAsync,
			TryHandleRailShortcut: (key, ctrl, shift) => RailControl.TryHandleKeyboardShortcut(key, ctrl, shift));

	private void LogKeyboardIssueRoute(string stage, NexusKey key, NexusKeyModifiers modifiers, string? detail = null)
	{
		if (!CanLogKeyboardIssueRoute(key))
		{
			return;
		}

		string suffix = string.IsNullOrWhiteSpace(detail) ? "" : $" {detail}";
		Log($"[KEYFIX:{stage}] key={key} ctrl={modifiers.Ctrl} shift={modifiers.Shift} alt={modifiers.Alt}{suffix}");
	}

	private static bool CanLogKeyboardIssueRoute(NexusKey key)
		=> KeyboardIssueDiagnosticsEnabled && ShouldLogKeyboardIssueKey(key);

	private static bool ShouldLogKeyboardIssueKey(NexusKey key)
		=> key is NexusKey.Delete or NexusKey.Enter or NexusKey.Escape;

	private static bool TryGetWebRelayKey(NexusKey key, object? platformKey, out string relayKey)
	{
		relayKey = key switch
		{
			NexusKey.A => "A",
			NexusKey.B => "B",
			NexusKey.C => "C",
			NexusKey.D => "D",
			NexusKey.H => "H",
			NexusKey.L => "L",
			NexusKey.M => "M",
			NexusKey.O => "O",
			NexusKey.S => "S",
			NexusKey.V => "V",
			NexusKey.W => "W",
			NexusKey.X => "X",
			NexusKey.Period => "PERIOD",
			NexusKey.Enter => "ENTER",
			NexusKey.Space => "SPACE",
			NexusKey.Escape => "ESCAPE",
			NexusKey.Backspace => "BACKSPACE",
			NexusKey.Tab => "TAB",
			NexusKey.Delete => "DELETE",
			NexusKey.Left => "LEFT",
			NexusKey.Up => "UP",
			NexusKey.Down => "DOWN",
			NexusKey.Right => "RIGHT",
			_ => string.Empty,
		};

		if (!string.IsNullOrWhiteSpace(relayKey))
		{
			return true;
		}

		if (platformKey is global::Windows.System.VirtualKey virtualKey)
		{
			int rawValue = (int)virtualKey;
			if (virtualKey == global::Windows.System.VirtualKey.Decimal || rawValue == 190)
			{
				relayKey = "PERIOD";
				return true;
			}
		}

		return false;
	}
}
#endif
