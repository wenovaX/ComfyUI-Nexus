using Microsoft.Maui.Controls;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;
using System.Runtime.CompilerServices;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private readonly List<string> _allLogs = new();
	private readonly Dictionary<string, DateTime> _throttleMap = new();
	private bool _webLogsEnabled = false;
	private bool _devToolsEnabled = false;

	internal void Log(
		string message,
		[CallerMemberName] string memberName = "",
		[CallerFilePath] string filePath = "")
	{
		NexusLog.Info(message, memberName, filePath);
	}

	private void AppendLogEntry(string fullMsg)
	{
		UiThread.TryBeginInvoke(() =>
		{
			_allLogs.Add(fullMsg);
			TrimStoredLogEntries();

			string filter = ControlDeckControl?.GetLogFilterText() ?? string.Empty;
			if (LogMatchesFilter(fullMsg, filter))
			{
				ControlDeckControl?.AppendLogLine(fullMsg);
			}
		}, "MAIN_LOG:UI");
	}

	private void TrimStoredLogEntries()
	{
		if (_allLogs.Count > LogOptions.MaxEntries)
		{
			_allLogs.RemoveRange(0, _allLogs.Count - LogOptions.MaxEntries);
		}
	}

	private static bool LogMatchesFilter(string line, string filter)
		=> string.IsNullOrWhiteSpace(filter) || line.Contains(filter, StringComparison.OrdinalIgnoreCase);

	private void LogThrottled(string key, string message, int intervalMs = 2000)
	{
		var now = DateTime.UtcNow;
		if (_throttleMap.TryGetValue(key, out var lastLoggedUtc) &&
			(now - lastLoggedUtc).TotalMilliseconds < intervalMs)
		{
			return;
		}

		_throttleMap[key] = now;
		Log(message);
	}

	private void OnLogSearchChanged(object? sender, TextChangedEventArgs e)
	{
		string filter = e.NewTextValue ?? string.Empty;
		var lines = _allLogs.Where(line => LogMatchesFilter(line, filter));
		ControlDeckControl.SetLogText(CreateLogText(lines));
	}

	private static string CreateLogText(IEnumerable<string> lines)
		=> string.Join(Environment.NewLine, lines) + Environment.NewLine;

	private void OnCopyAllClicked(object? sender, EventArgs e)
	{
		if (ControlDeckControl != null)
		{
			Clipboard.Default.SetTextAsync(ControlDeckControl.GetLogText());
		}
	}

	private void OnClearLogClicked(object? sender, EventArgs e)
	{
		_allLogs.Clear();
		ControlDeckControl?.ClearLogText();
	}

	private async void OnToggleWebLogsClicked(object? sender, EventArgs e)
	{
		_webLogsEnabled = !_webLogsEnabled;
		ControlDeckControl.SetWebLogsState(_webLogsEnabled);
		RefreshControlDeckWebPulse(force: true);

		Log(_webLogsEnabled ? "WEB LOGS: ENABLED" : "WEB LOGS: DISABLED");

		await _webViewBridge.SetWebLogsEnabledAsync(_webLogsEnabled);
	}

	private void OnToggleDevToolsClicked(object? sender, EventArgs e)
	{
		_devToolsEnabled = !_devToolsEnabled;
		ControlDeckControl.SetDevToolsState(_devToolsEnabled);
		RefreshControlDeckWebPulse(force: true);

		Log(_devToolsEnabled ? "DEVTOOLS: ENABLED (F12 AVAILABLE)" : "DEVTOOLS: DISABLED");

		PlatformManager.Current.WebView.SetDevToolsEnabled(WorkspaceControl.BrowserView, _devToolsEnabled);
	}

	private async void OnOpenFullLogClicked(object? sender, EventArgs e)
	{
		string logPath = NexusLog.CurrentLatestLogPath
			?? ComfyInstallService.GetLocalRuntimePath($"Logs/{SessionLogPaths.NexusLatestFileName}");
		var result = await PlatformManager.Current.Shell.OpenPathAsync(logPath);
		if (!result.IsSuccess)
		{
			NexusLog.Warning($"Failed to open persistent session log: {result.Message}");
		}
	}

	private async void OnToggleUiIsolationClicked(object? sender, EventArgs e)
	{
		_uiIsolationEnabled = !_uiIsolationEnabled;
		ControlDeckControl.SetUiIsolationState(_uiIsolationEnabled);

		Log(_uiIsolationEnabled
			? "UI ISOLATION: ENABLED"
			: "UI ISOLATION: DISABLED FOR DEBUGGING");

		await _webViewBridge.InvokeActionAsync(
			BridgeActions.SetUiIsolation,
			System.Text.Json.JsonSerializer.Serialize(new { enabled = _uiIsolationEnabled }));
	}
}
