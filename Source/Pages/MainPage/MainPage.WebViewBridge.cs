using Microsoft.Maui.Controls;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private void OnWebViewNavigationStarting(string? uri)
	{
		if (_bridgeSession.BeginNavigation())
		{
			RefreshControlDeckWebPulse(force: true);
		}

		if (IsExpectedComfyNavigationUrl(uri))
		{
			UpdateRebootUI(true);
		}
	}

	private void OnWebViewNavigated(object? sender, NexusBrowserNavigationEventArgs e)
	{
		Log($"Navigation event: {(e.IsSuccess ? "Success" : e.Detail)} -> {e.Url}");

		if (e.IsSuccess && IsExpectedComfyNavigationUrl(e.Url))
		{
			_loginSequence.NotifyNavigationSucceeded(e.Url!);
		}
		else if (!e.IsSuccess)
		{
			if (_bridgeSession.MarkDisconnected())
			{
				RefreshControlDeckWebPulse(force: true);
			}

			_loginSequence.NotifyNavigationFailed($"{e.Detail} -> {e.Url}");
		}
	}

	private bool IsExpectedComfyNavigationUrl(string? url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return false;
		}

		if (!Uri.TryCreate(url, UriKind.Absolute, out var navigatedUri)
			|| !Uri.TryCreate(ComfyApiOptions.GetLocalBaseUrl(_appManager.Settings.Settings), UriKind.Absolute, out var configuredUri))
		{
			return false;
		}

		return string.Equals(navigatedUri.Scheme, configuredUri.Scheme, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(navigatedUri.Host, configuredUri.Host, StringComparison.OrdinalIgnoreCase)
			&& navigatedUri.Port == configuredUri.Port;
	}

	private Task PrepareWebViewNavigationSuccessAsync()
	{
		Log("[Bridge] ComfyUI navigation ready. Preparing Nexus connection.");

		_ = ProbeComfyUIWorkflowPath();
		_isBooted = false;
		_bootReadyHandled = false;
		_stabilizedVisualStateApplied = false;
		_webInputMode = false;

		return Task.CompletedTask;
	}

	private void ApplyWebViewNavigationFailureUi(string detail)
	{
		Log("CONNECTION FAILED. Waiting for retry.");
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

		return await MainThread.InvokeOnMainThreadAsync(_webViewBridge.BootNexusAsync);
	}

	private void ApplyHandshakeTimeoutUi()
	{
		if (_isShuttingDown)
		{
			return;
		}

		Log("[Bridge] Nexus connection did not respond before the readiness deadline.");
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

			await WorkspaceControl.BrowserSurface.DisableReloadHandlingAsync(_webViewBridge.ClearBeforeUnloadAsync);
		});
	}
}
