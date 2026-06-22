using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Platform.Windows
{
#if WINDOWS || NET6_0_WINDOWS
	public sealed class WindowsWebViewService : IPlatformWebViewService
	{
		public async Task ConfigureBridgeAsync(
			WebView webView,
			Func<string, Task> processMessageAsync,
			Action<string?> navigationStarting,
			Action? bridgeActivated = null,
			Func<NexusKey, bool, bool, bool, bool>? acceleratorKeyHandler = null)
		{
			if (webView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 nativeWebView)
			{
				return;
			}

			nativeWebView.AllowDrop = true;
			await nativeWebView.EnsureCoreWebView2Async();
			nativeWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
			TryEnableExternalDrop(nativeWebView);
			TryAttachAcceleratorKeyHandler(nativeWebView, acceleratorKeyHandler);

			nativeWebView.CoreWebView2.NavigationStarting += (sender, args) => {
				navigationStarting(args.Uri);
			};

			await nativeWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(NexusDocumentBootstrapScript);

			nativeWebView.WebMessageReceived += async (sender, args) => {
				await processMessageAsync(args.WebMessageAsJson);
			};

			bridgeActivated?.Invoke();
		}

		public async Task DisableBrowserReloadHandlingAsync(WebView webView, Func<Task> clearBeforeUnloadAsync)
		{
			if (webView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 nativeWebView)
			{
				return;
			}

			try
			{
				await nativeWebView.EnsureCoreWebView2Async();
				nativeWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
				await clearBeforeUnloadAsync();
			}
			catch
			{
			}
		}

		public void Reload(WebView webView)
		{
			if (webView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 nativeWebView)
			{
				nativeWebView.CoreWebView2.Reload();
				return;
			}

			webView.Reload();
		}

		public void Focus(WebView webView)
		{
			if (webView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 nativeWebView)
			{
				nativeWebView.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
				return;
			}

			webView.Focus();
		}

		public async void SetDevToolsEnabled(WebView webView, bool isEnabled)
		{
			if (webView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 nativeWebView) return;

			try
			{
				await nativeWebView.EnsureCoreWebView2Async();
				nativeWebView.CoreWebView2.Settings.AreDevToolsEnabled = isEnabled;
				nativeWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = isEnabled;
			}
			catch { }
		}

		private static void TryEnableExternalDrop(Microsoft.UI.Xaml.Controls.WebView2 nativeWebView)
		{
			try
			{
				var controllerProp = nativeWebView.GetType().GetProperty("CoreWebView2Controller");
				var controller = controllerProp?.GetValue(nativeWebView);
				var allowExternalDropProp = controller?.GetType().GetProperty("AllowExternalDrop");
				allowExternalDropProp?.SetValue(controller, true);
			}
			catch
			{
			}
		}

		private static void TryAttachAcceleratorKeyHandler(
			Microsoft.UI.Xaml.Controls.WebView2 nativeWebView,
			Func<NexusKey, bool, bool, bool, bool>? acceleratorKeyHandler)
		{
			if (acceleratorKeyHandler == null)
			{
				return;
			}

			try
			{
				var keyboard = new WindowsKeyboardState();
				var controllerProp = nativeWebView.GetType().GetProperty("CoreWebView2Controller");
				if (controllerProp?.GetValue(nativeWebView) is not Microsoft.Web.WebView2.Core.CoreWebView2Controller controller)
				{
					return;
				}

				controller.AcceleratorKeyPressed += (sender, args) => {
					if (!args.KeyEventKind.ToString().Contains("KeyDown", StringComparison.OrdinalIgnoreCase))
					{
						return;
					}

					var virtualKey = (global::Windows.System.VirtualKey)args.VirtualKey;
					NexusKey key = keyboard.ToNexusKey(virtualKey);
					NexusLog.Info($"[KEY:WEB_ACCEL_RAW] virtual={virtualKey} nexus={key} ctrl={keyboard.IsCtrlPressed()} shift={keyboard.IsShiftPressed()} alt={keyboard.IsAltPressed()}");
					if (key == NexusKey.Unknown)
					{
						NexusLog.Info($"[KEY:WEB_ACCEL_PASS] virtual={virtualKey} reason=unknown-key");
						return;
					}

					bool handled = acceleratorKeyHandler(
						key,
						keyboard.IsCtrlPressed(),
						keyboard.IsShiftPressed(),
						keyboard.IsAltPressed());
					NexusLog.Info($"[KEY:WEB_ACCEL_RESULT] key={key} handled={handled}");
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
