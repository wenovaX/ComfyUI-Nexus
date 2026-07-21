using System.Windows.Input;
using Microsoft.Maui.Controls;

namespace ComfyUI_Nexus.Ui;

public enum NexusControlDeckServerStatus
{
	Unknown,
	Ready,
	Transitioning,
	Offline,
}

/// <summary>
/// Optional diagnostics deck contract. The main shell only depends on this contract;
/// the visual tool can be omitted from a build without affecting shell startup.
/// </summary>
internal interface INexusControlDeck
{
	ICommand? ManualRebootCommand { get; set; }
	ICommand? BootServerCommand { get; set; }
	ICommand? ShutdownServerCommand { get; set; }
	ICommand? ToggleBridgeDiagnosticsCommand { get; set; }
	ICommand? ToggleWebLogsCommand { get; set; }
	ICommand? ToggleDevToolsCommand { get; set; }
	ICommand? ToggleUiIsolationCommand { get; set; }
	ICommand? PatchLocalHudCommand { get; set; }
	ICommand? PatchNexusBridgeCommand { get; set; }
	ICommand? OpenFullLogCommand { get; set; }
	ICommand? ClearLogCommand { get; set; }
	event EventHandler<TextChangedEventArgs>? LogSearchChanged;
	string GetLogFilterText();
	void AppendLogLine(string line);
	void SetLogText(string text);
	void ClearLogText();
	void SetLogFileRelativePath(string relativePath);
	void SetBridgeDiagnosticsState(bool enabled);
	void SetWebLogsState(bool enabled);
	void SetDevToolsState(bool enabled);
	void SetUiIsolationState(bool enabled);
	void SetPulseRun(bool isRunning, bool isInstantStop);
	void SetPulseWeb(
		bool isBridgeLive,
		NexusControlDeckServerStatus serverStatus,
		int errorCount,
		bool bridgeTraceEnabled,
		bool webLogsEnabled,
		bool devToolsEnabled);
}
