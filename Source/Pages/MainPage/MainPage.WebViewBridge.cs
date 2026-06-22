using Microsoft.Maui.Controls;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
	{
		Log($"Navigation event: {e.Result} -> {e.Url}");

		if (e.Result == WebNavigationResult.Success && IsExpectedComfyNavigationUrl(e.Url))
		{
			_loginSequence.NotifyNavigationSucceeded(e.Url);
		}
		else if (e.Result != WebNavigationResult.Success)
		{
			_loginSequence.NotifyNavigationFailed($"{e.Result} -> {e.Url}");
		}
	}

	private static bool IsExpectedComfyNavigationUrl(string? url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return false;
		}

		if (!Uri.TryCreate(url, UriKind.Absolute, out var navigatedUri)
			|| !Uri.TryCreate(ComfyApiOptions.LocalBaseUrl, UriKind.Absolute, out var configuredUri))
		{
			return false;
		}

		return string.Equals(navigatedUri.Scheme, configuredUri.Scheme, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(navigatedUri.Host, configuredUri.Host, StringComparison.OrdinalIgnoreCase)
			&& navigatedUri.Port == configuredUri.Port;
	}

	private Task PrepareWebViewNavigationSuccessAsync()
	{
		Log("Neural Bridge Active. Injecting Identity & Waking Agent...");

		_ = ProbeComfyUIWorkflowPath();
		_isBooted = false;
		_bootReadyHandled = false;
		_stabilizedVisualStateApplied = false;

		return Task.CompletedTask;
	}

	private void ApplyWebViewNavigationFailureUi(string detail)
	{
		Log("CONNECTION FAILED. RETRYING...");
		_loadingOverlayController.Error(
			LocalizationManager.Text("loading.comfy_unreachable_title"),
			detail,
			LocalizationManager.Text("loading.comfy_unreachable_status"),
			LoadingWarningColor);
	}

	private async Task<BridgeBootProbeResult> InvokeBridgeBootAsync()
	{
		if (_isShuttingDown)
		{
			return BridgeBootProbeResult.NativeUnavailable;
		}

		return await MainThread.InvokeOnMainThreadAsync(_webViewBridge.BootProtosAsync);
	}

	private void ApplyHandshakeTimeoutUi()
	{
		if (_isShuttingDown)
		{
			return;
		}

		Log("BOOT ERROR: Agent PROTOS failed to respond. Check JS logs.");
		UiThread.TryBeginInvoke(() =>
		{
			_loadingOverlayController.Error(
				LocalizationManager.Text("loading.bridge_timeout_title"),
				LocalizationManager.Text("loading.bridge_timeout_detail"),
				LocalizationManager.Text("loading.bridge_timeout_status"),
				LoadingWarningColor);
		}, "WEBVIEW_BRIDGE:HANDSHAKE_TIMEOUT");
	}

	private async Task DisableBrowserReloadHandlingAsync()
	{
		if (_isShuttingDown)
		{
			return;
		}

		await MainThread.InvokeOnMainThreadAsync(async () =>
		{
			if (_isShuttingDown)
			{
				return;
			}

			await PlatformManager.Current.WebView.DisableBrowserReloadHandlingAsync(
				WorkspaceControl.BrowserView,
				_webViewBridge.ClearBeforeUnloadAsync);
		});
	}
}
