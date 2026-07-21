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
	private readonly object _logThrottleGate = new();
	private bool _webLogsEnabled = false;
	private readonly NexusDevToolsController _devToolsController = new();

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

			INexusControlDeck? deck = CurrentControlDeck;
			string filter = deck?.GetLogFilterText() ?? string.Empty;
			if (LogMatchesFilter(fullMsg, filter))
			{
				deck?.AppendLogLine(fullMsg);
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
		lock (_logThrottleGate)
		{
			if (_throttleMap.TryGetValue(key, out var lastLoggedUtc) &&
				(now - lastLoggedUtc).TotalMilliseconds < intervalMs)
			{
				return;
			}

			_throttleMap[key] = now;
		}

		Log(message);
	}

	private void OnLogSearchChanged(object? sender, TextChangedEventArgs e)
	{
		string filter = e.NewTextValue ?? string.Empty;
		var lines = _allLogs.Where(line => LogMatchesFilter(line, filter));
		CurrentControlDeck?.SetLogText(CreateLogText(lines));
	}

	private static string CreateLogText(IEnumerable<string> lines)
		=> string.Join(Environment.NewLine, lines) + Environment.NewLine;

	private void ExecuteControlDeckClearLog()
	{
		_allLogs.Clear();
		CurrentControlDeck?.ClearLogText();
	}

	private async Task ExecuteControlDeckToggleWebLogsAsync()
	{
		_webLogsEnabled = !_webLogsEnabled;
		CurrentControlDeck?.SetWebLogsState(_webLogsEnabled);
		RefreshControlDeckWebPulse(force: true);

		Log(_webLogsEnabled ? "WEB LOGS: ENABLED" : "WEB LOGS: DISABLED");

		await _webViewBridge.SetWebLogsEnabledAsync(_webLogsEnabled);
	}

	private void ExecuteControlDeckToggleDevTools()
	{
		bool isEnabled = _devToolsController.Toggle();
		CurrentControlDeck?.SetDevToolsState(isEnabled);
		RefreshControlDeckWebPulse(force: true);

		Log(isEnabled ? "DEVTOOLS: ENABLED (F12 AVAILABLE)" : "DEVTOOLS: DISABLED");

		_devToolsController.Apply(WorkspaceControl.BrowserSurface);
	}

	private async Task ExecuteControlDeckOpenFullLogAsync()
	{
		string logPath = NexusLog.CurrentLatestLogPath
			?? ComfyInstallService.GetLocalRuntimePath($"Logs/{SessionLogPaths.NexusLatestFileName}");
		CurrentControlDeck?.SetLogFileRelativePath(GetControlDeckLogRelativePath(logPath));
		var result = await PlatformManager.Current.Shell.OpenPathAsync(logPath);
		if (!result.IsSuccess)
		{
			NexusLog.Warning($"Failed to open persistent session log: {result.Message}");
		}
	}

	private static string GetControlDeckLogRelativePath(string? logPath = null)
	{
		logPath ??= NexusLog.CurrentLatestLogPath
			?? ComfyInstallService.GetLocalRuntimePath($"Logs/{SessionLogPaths.NexusLatestFileName}");

		try
		{
			string relativePath = Path.GetRelativePath(ComfyInstallService.LocalRuntimePath, logPath);
			if (!relativePath.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativePath))
			{
				return relativePath.Replace(Path.DirectorySeparatorChar, '/');
			}
		}
		catch
		{
		}

		return Path.GetFileName(logPath);
	}

	private async Task ExecuteControlDeckToggleUiIsolationAsync()
	{
		_uiIsolationEnabled = !_uiIsolationEnabled;
		CurrentControlDeck?.SetUiIsolationState(_uiIsolationEnabled);

		Log(_uiIsolationEnabled
			? "UI ISOLATION: ENABLED"
			: "UI ISOLATION: DISABLED FOR DEBUGGING");

		await _webViewBridge.InvokeActionAsync(
			BridgeActions.SetUiIsolation,
			System.Text.Json.JsonSerializer.Serialize(new { enabled = _uiIsolationEnabled }));
	}
}
