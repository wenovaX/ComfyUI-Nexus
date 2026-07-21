using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Platform.Windows
{
#if WINDOWS || NET6_0_WINDOWS
	public sealed class WindowsWebViewService : IPlatformWebViewService
	{
		public Task EnsureReadyAsync(INexusBrowserSurface surface)
			=> surface is MauiWebViewBrowserSurface mauiSurface
				? EnsureNativeReadyAsync(mauiSurface.NativeWebView)
				: Task.CompletedTask;

		public async Task ConfigureBridgeAsync(
			INexusBrowserSurface surface,
			Func<string, Task> processMessageAsync,
			Action<string?> navigationStarting,
			Action? bridgeActivated = null,
			Func<NexusKey, bool, bool, bool, bool>? acceleratorKeyHandler = null)
		{
			if (surface is not MauiWebViewBrowserSurface mauiSurface ||
				mauiSurface.NativeWebView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 nativeWebView)
			{
				return;
			}

			nativeWebView.AllowDrop = true;
			await nativeWebView.EnsureCoreWebView2Async();
			var controller = TryGetController(nativeWebView);
			await ConfigureCoreAsync(nativeWebView.CoreWebView2, controller, processMessageAsync, navigationStarting, bridgeActivated, acceleratorKeyHandler);
		}

		public async Task DisableBrowserReloadHandlingAsync(INexusBrowserSurface surface, Func<Task> clearBeforeUnloadAsync)
		{
			if (surface is not MauiWebViewBrowserSurface mauiSurface ||
				mauiSurface.NativeWebView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 nativeWebView)
			{
				return;
			}

			try
			{
				await nativeWebView.EnsureCoreWebView2Async();
				await DisableReloadHandlingAsync(nativeWebView.CoreWebView2, clearBeforeUnloadAsync);
			}
			catch
			{
			}
		}

		public void Reload(INexusBrowserSurface surface)
		{
			if (surface is not MauiWebViewBrowserSurface mauiSurface)
			{
				return;
			}

			if (mauiSurface.NativeWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 nativeWebView)
			{
				nativeWebView.CoreWebView2.Reload();
				return;
			}

			mauiSurface.NativeWebView.Reload();
		}

		public void Focus(INexusBrowserSurface surface)
		{
			if (surface is not MauiWebViewBrowserSurface mauiSurface)
			{
				return;
			}

			if (mauiSurface.NativeWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 nativeWebView)
			{
				nativeWebView.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
				return;
			}

			mauiSurface.NativeWebView.Focus();
		}

		public async void SetDevToolsEnabled(INexusBrowserSurface surface, bool isEnabled)
		{
			if (surface is not MauiWebViewBrowserSurface mauiSurface ||
				mauiSurface.NativeWebView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 nativeWebView) return;

			try
			{
				await nativeWebView.EnsureCoreWebView2Async();
				nativeWebView.CoreWebView2.Settings.AreDevToolsEnabled = isEnabled;
				nativeWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = isEnabled;
			}
			catch { }
		}

		public async void OpenDevTools(INexusBrowserSurface surface)
		{
			if (surface is not MauiWebViewBrowserSurface mauiSurface ||
				mauiSurface.NativeWebView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 nativeWebView) return;

			try
			{
				await nativeWebView.EnsureCoreWebView2Async();
				nativeWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
				nativeWebView.CoreWebView2.OpenDevToolsWindow();
			}
			catch { }
		}

		internal static async Task ConfigureCompositionBridgeAsync(
			Microsoft.Web.WebView2.Core.CoreWebView2 coreWebView2,
			Microsoft.Web.WebView2.Core.CoreWebView2CompositionController controller,
			Func<string, Task> processMessageAsync,
			Action<string?> navigationStarting,
			Action? bridgeActivated,
			Func<NexusKey, bool, bool, bool, bool>? acceleratorKeyHandler)
			=> await ConfigureCoreAsync(coreWebView2, controller, processMessageAsync, navigationStarting, bridgeActivated, acceleratorKeyHandler);

		internal static async Task DisableCompositionReloadHandlingAsync(
			Microsoft.Web.WebView2.Core.CoreWebView2 coreWebView2,
			Func<Task> clearBeforeUnloadAsync)
			=> await DisableReloadHandlingAsync(coreWebView2, clearBeforeUnloadAsync);

		private static Task EnsureNativeReadyAsync(WebView webView)
			=> webView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 nativeWebView
				? nativeWebView.EnsureCoreWebView2Async().AsTask()
				: Task.CompletedTask;

		private static async Task ConfigureCoreAsync(
			Microsoft.Web.WebView2.Core.CoreWebView2 coreWebView2,
			Microsoft.Web.WebView2.Core.CoreWebView2Controller? controller,
			Func<string, Task> processMessageAsync,
			Action<string?> navigationStarting,
			Action? bridgeActivated,
			Func<NexusKey, bool, bool, bool, bool>? acceleratorKeyHandler)
		{
			coreWebView2.Settings.IsWebMessageEnabled = true;
			if (controller != null)
			{
				controller.AllowExternalDrop = true;
				TryAttachAcceleratorKeyHandler(controller, acceleratorKeyHandler);
			}

			coreWebView2.NavigationStarting += (sender, args) => navigationStarting(args.Uri);
			await coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(NexusDocumentBootstrapScript);
			coreWebView2.WebMessageReceived += async (sender, args) => await processMessageAsync(args.WebMessageAsJson);
			bridgeActivated?.Invoke();
		}

		private static async Task DisableReloadHandlingAsync(Microsoft.Web.WebView2.Core.CoreWebView2 coreWebView2, Func<Task> clearBeforeUnloadAsync)
		{
			coreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
			await clearBeforeUnloadAsync();
		}

		private static Microsoft.Web.WebView2.Core.CoreWebView2Controller? TryGetController(Microsoft.UI.Xaml.Controls.WebView2 nativeWebView)
			=> nativeWebView.GetType().GetProperty("CoreWebView2Controller")?.GetValue(nativeWebView)
				as Microsoft.Web.WebView2.Core.CoreWebView2Controller;

		private static void TryAttachAcceleratorKeyHandler(
			Microsoft.Web.WebView2.Core.CoreWebView2Controller controller,
			Func<NexusKey, bool, bool, bool, bool>? acceleratorKeyHandler)
		{
			if (acceleratorKeyHandler == null)
			{
				return;
			}

			try
			{
				var keyboard = new WindowsKeyboardState();
				controller.AcceleratorKeyPressed += (sender, args) =>
				{
					if (!args.KeyEventKind.ToString().Contains("KeyDown", StringComparison.OrdinalIgnoreCase))
					{
						return;
					}

					var virtualKey = (global::Windows.System.VirtualKey)args.VirtualKey;
					NexusKey key = keyboard.ToNexusKey(virtualKey);
					NexusLog.Trace($"[KEY:WEB_ACCEL_RAW] virtual={virtualKey} nexus={key} ctrl={keyboard.IsCtrlPressed()} shift={keyboard.IsShiftPressed()} alt={keyboard.IsAltPressed()}");
					if (key == NexusKey.Unknown)
					{
						NexusLog.Trace($"[KEY:WEB_ACCEL_PASS] virtual={virtualKey} reason=unknown-key");
						return;
					}

					bool handled = acceleratorKeyHandler(
						key,
						keyboard.IsCtrlPressed(),
						keyboard.IsShiftPressed(),
						keyboard.IsAltPressed());
					NexusLog.Trace($"[KEY:WEB_ACCEL_RESULT] key={key} handled={handled}");
					if (handled)
					{
						args.Handled = true;
					}
				};
			}
			catch
			{
			}
		}

		private static readonly string NexusDocumentBootstrapScript = $$"""
						(function() {
							window._nexusWebLogsEnabled = false;
							window.isNexusShell = true;
							window.__nexusNative = window.__nexusNative || {
								post: function(type, data) {
									window.chrome?.webview?.postMessage({ type: type, data: data, timestamp: Date.now() });
								}
							};

							const nexusSafeStringify = (value) => {
								try {
									if (value instanceof Error) {
										return value.stack || value.message || String(value);
									}
									if (typeof value === 'object' && value !== null) {
										const seen = new WeakSet();
										return JSON.stringify(value, (key, innerValue) => {
											if (typeof innerValue === 'object' && innerValue !== null) {
												if (seen.has(innerValue)) return '[Circular]';
												seen.add(innerValue);
											}
											return innerValue;
										});
									}
									return String(value);
								} catch (error) {
									return String(value);
								}
							};

							const postNexusWebError = (data) => {
								try {
									window.__nexusNative.post('{{BridgeMessageTypes.WebError}}', data);
								} catch {
								}
							};

							// Mirror browser console output into the native diagnostics panel when enabled.
							const methods = ['log', 'warn', 'error', 'info'];
							methods.forEach(method => {
								const original = console[method];
								console[method] = function(...args) {
									original.apply(console, args);
									if (!window._nexusWebLogsEnabled && method !== 'error') return;
									const msg = args.map(nexusSafeStringify).join(' ');
									window.__nexusNative.post('{{BridgeMessageTypes.WebConsole}}', method.toUpperCase() + '|' + msg);
								};
							});

							window.addEventListener('error', event => {
								postNexusWebError({
									kind: 'error',
									message: event.message || String(event.error || 'Unknown script error'),
									source: event.filename || '',
									line: event.lineno || 0,
									column: event.colno || 0,
									stack: event.error?.stack || ''
								});
							}, true);

							window.addEventListener('unhandledrejection', event => {
								const reason = event.reason;
								postNexusWebError({
									kind: 'unhandledrejection',
									message: reason?.message || nexusSafeStringify(reason),
									stack: reason?.stack || ''
								});
							}, true);

							// Block unload hooks that would fight the shell-controlled reload flow.
							const originalAddEventListener = window.addEventListener;
							window.addEventListener = function(type, listener, options) {
								if (type === 'beforeunload' || type === 'unload') {
									console.log('Nexus blocked ' + type + ' listener.');
									return;
								}
								return originalAddEventListener.call(window, type, listener, options);
							};

							Object.defineProperty(window, 'onbeforeunload', {
								get: function() { return null; },
								set: function() { console.log('Nexus blocked onbeforeunload assignment.'); },
								configurable: false
							});
						})();
					""";
	}
#endif
}
