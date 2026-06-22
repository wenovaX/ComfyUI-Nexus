using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus.Views;

public partial class ControlDeckView : ContentView
{
	private const int ConsoleRenderBatchSize = 50;
	private const int ConsoleLogRowPrewarmCount = 50;
	private const uint PulseRunBlinkRiseLength = 420;
	private const uint PulseRunBlinkFallLength = 520;
	private static readonly Color TraceActiveColor = NexusColors.AccentText;
	private static readonly Color TraceInactiveTextColor = NexusColors.TextFaint;
	private static readonly Color TraceInactiveBorderColor = Color.FromArgb("#445566");
	private static readonly Color DevToolsActiveColor = Color.FromArgb("#88aa66");
	private static readonly Color DevToolsInactiveTextColor = Color.FromArgb("#668877");
	private static readonly Color DevToolsInactiveBorderColor = Color.FromArgb("#446655");
	private static readonly Color PulseLiveColor = NexusColors.Accent;
	private static readonly Color PulseIdleColor = Color.FromArgb("#2373ff");
	private static readonly Color PulseWarnColor = Color.FromArgb("#ffcc66");
	private static readonly Color PulseDangerColor = Color.FromArgb("#ff4d6d");
	private static readonly Color PulseMutedTextColor = NexusColors.TextMuted;
	private static readonly Color PulseLiveTextColor = Color.FromArgb("#baf8ff");
	private static readonly Color PulseWarnTextColor = Color.FromArgb("#ffe7a6");
	private static readonly Color PulseDangerTextColor = Color.FromArgb("#ffc4cf");

	internal event EventHandler? ManualRebootRequested;
	internal event EventHandler? ToggleBridgeDiagnosticsRequested;
	internal event EventHandler? ToggleWebLogsRequested;
	internal event EventHandler? ToggleDevToolsRequested;
	internal event EventHandler? ToggleUiIsolationRequested;
	internal event EventHandler? PatchLocalHudRequested;
	internal event EventHandler? PatchNexusBridgeRequested;
	internal event EventHandler? OpenFullLogRequested;
	internal event EventHandler? CopyAllRequested;
	internal event EventHandler? ClearLogRequested;
	internal event EventHandler<TextChangedEventArgs>? LogSearchChanged;

	private readonly List<string> _displayedLogLines = new();
	private readonly UiObjectPool<Label> _consoleLogRowPool = new(CreateConsoleLogRow, ResetConsoleLogRow);
	private int _consoleRenderVersion;
	private int _pulseRunBlinkVersion;
	private bool _isPulseRunBlinking;
	private bool _isConsoleRendered;

	public ControlDeckView()
	{
		InitializeComponent();
		_consoleLogRowPool.Prewarm(ConsoleLogRowPrewarmCount);
		SetPulseRun(isRunning: false, isInstantStop: false);
		SetPulseWeb(isLive: false, errorCount: 0, bridgeTraceEnabled: false, webLogsEnabled: false, devToolsEnabled: false);
		SetUiIsolationState(enabled: true);
	}

	internal string GetLogFilterText() => LogSearchEntry.Text ?? string.Empty;

	internal void AppendLogLine(string line)
	{
		_displayedLogLines.Add(line);

		if (_displayedLogLines.Count > LogOptions.MaxRenderedRows)
		{
			int excess = _displayedLogLines.Count - LogOptions.MaxRenderedRows;
			_displayedLogLines.RemoveRange(0, excess);

			if (_isConsoleRendered)
			{
				for (int i = 0; i < excess && ConsoleLogStack.Children.Count > 0; i++)
				{
					RemoveAndReturnConsoleLogRowAt(0);
				}
			}
		}

		if (!IsConsoleRenderActive)
		{
			_isConsoleRendered = false;
			return;
		}

		if (!_isConsoleRendered)
		{
			_ = RenderConsoleLogLinesAsync();
			return;
		}

		ConsoleLogStack.Children.Add(RentConsoleLogRow(line));
		_ = ConsoleLogScrollView.ScrollToAsync(ConsoleLogStack, ScrollToPosition.End, false);
	}

	internal void SetLogText(string text)
	{
		System.Threading.Interlocked.Increment(ref _consoleRenderVersion);
		_displayedLogLines.Clear();

		foreach (string line in text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
		{
			_displayedLogLines.Add(line);
		}

		TrimStoredLogLines();

		if (IsConsoleRenderActive)
		{
			_ = RenderConsoleLogLinesAsync();
		}
		else
		{
			ReturnAllConsoleLogRows();
			_isConsoleRendered = false;
		}
	}

	internal string GetLogText()
	{
		if (_displayedLogLines.Count == 0)
		{
			return string.Empty;
		}

		return string.Join(Environment.NewLine, _displayedLogLines) + Environment.NewLine;
	}

	internal void ClearLogText()
	{
		System.Threading.Interlocked.Increment(ref _consoleRenderVersion);
		_displayedLogLines.Clear();
		ReturnAllConsoleLogRows();
		_isConsoleRendered = IsConsoleRenderActive;
	}

	internal void SetDisplayState(bool isVisible, double opacity)
	{
		IsVisible = isVisible;
		Opacity = opacity;
		if (!isVisible)
		{
			System.Threading.Interlocked.Increment(ref _consoleRenderVersion);
			_isConsoleRendered = false;
			ReturnAllConsoleLogRows();
			return;
		}

		if (!_isConsoleRendered)
		{
			_ = RenderConsoleLogLinesAsync();
		}
	}

	internal void PrepareToShow()
	{
		Opacity = 0;
		IsVisible = true;
		_ = RenderConsoleLogLinesAsync();
	}

	internal Task AnimateShowAsync(uint length, Easing easing)
	{
		return this.FadeToAsync(1, length, easing);
	}

	internal Task AnimateHideAsync(uint length, Easing easing)
	{
		return this.FadeToAsync(0, length, easing);
	}

	internal void CompleteHide()
	{
		System.Threading.Interlocked.Increment(ref _consoleRenderVersion);
		IsVisible = false;
		_isConsoleRendered = false;
		ReturnAllConsoleLogRows();
	}

	internal void SetBridgeDiagnosticsState(bool enabled)
	{
		SetToggleButtonState(
			BridgeDiagnosticsButton,
			enabled,
			"BRIDGE TRACE: ON",
			"BRIDGE TRACE: OFF",
			TraceActiveColor,
			TraceInactiveTextColor,
			TraceInactiveBorderColor);
	}

	internal void SetWebLogsState(bool enabled)
	{
		SetToggleButtonState(
			WebLogsButton,
			enabled,
			"WEB LOGS: ON",
			"WEB LOGS: OFF",
			TraceActiveColor,
			TraceInactiveTextColor,
			TraceInactiveBorderColor);
	}

	internal void SetDevToolsState(bool enabled)
	{
		SetToggleButtonState(
			DevToolsButton,
			enabled,
			"DEVTOOLS: ON",
			"DEVTOOLS: OFF",
			DevToolsActiveColor,
			DevToolsInactiveTextColor,
			DevToolsInactiveBorderColor);
	}

	internal void SetUiIsolationState(bool enabled)
	{
		SetToggleButtonState(
			UiIsolationButton,
			enabled,
			"UI ISOLATION: ON",
			"UI ISOLATION: OFF",
			TraceActiveColor,
			Color.FromArgb("#ffcc66"),
			Color.FromArgb("#806622"));
	}

	internal void SetPulseRun(bool isRunning, bool isInstantStop)
	{
		PulseRunLabel.Text = isInstantStop ? "RUN STOP" : isRunning ? "RUN ACTIVE" : "RUN IDLE";
		PulseRunLabel.TextColor = isInstantStop ? PulseDangerTextColor : isRunning ? PulseLiveTextColor : PulseMutedTextColor;
		PulseRunBar.Color = isInstantStop ? PulseDangerColor : isRunning ? PulseLiveColor : PulseIdleColor;
		PulseRunBar.Opacity = isInstantStop || isRunning ? 0.9 : 0.34;
		SetPulseRunBlink(isRunning || isInstantStop);
	}

	internal void SetPulseWeb(
		bool isLive,
		int errorCount,
		bool bridgeTraceEnabled,
		bool webLogsEnabled,
		bool devToolsEnabled)
	{
		PulseBridgeStatusLabel.Text = isLive ? "BRIDGE LIVE" : "BRIDGE IDLE";
		PulseBridgeStatusLabel.TextColor = isLive ? PulseLiveTextColor : PulseMutedTextColor;

		if (errorCount > 0)
		{
			PulseWebLabel.Text = $"WEB ERR {errorCount}";
			PulseWebLabel.TextColor = PulseDangerTextColor;
			PulseWebBar.Color = PulseDangerColor;
			PulseWebBar.Opacity = 0.86;
			return;
		}

		if (bridgeTraceEnabled || webLogsEnabled || devToolsEnabled)
		{
			PulseWebLabel.Text = "WEB TRACE";
			PulseWebLabel.TextColor = PulseWarnTextColor;
			PulseWebBar.Color = PulseWarnColor;
			PulseWebBar.Opacity = 0.76;
			return;
		}

		PulseWebLabel.Text = isLive ? "WEB LIVE" : "WEB IDLE";
		PulseWebLabel.TextColor = isLive ? PulseLiveTextColor : PulseMutedTextColor;
		PulseWebBar.Color = isLive ? PulseLiveColor : PulseIdleColor;
		PulseWebBar.Opacity = isLive ? 0.78 : 0.3;
	}

	private async void SetPulseRunBlink(bool shouldBlink)
	{
		if (!shouldBlink)
		{
			_isPulseRunBlinking = false;
			_pulseRunBlinkVersion++;
			PulseRunBar.AbortAnimation("PulseRunBlink");
			PulseRunBar.Opacity = 0.34;
			return;
		}

		if (_isPulseRunBlinking)
		{
			return;
		}

		_isPulseRunBlinking = true;
		int version = ++_pulseRunBlinkVersion;
		while (version == _pulseRunBlinkVersion)
		{
			await PulseRunBar.FadeToAsync(1, PulseRunBlinkRiseLength, Easing.CubicOut);
			if (version != _pulseRunBlinkVersion) break;
			await PulseRunBar.FadeToAsync(0.38, PulseRunBlinkFallLength, Easing.CubicIn);
		}
	}

	private static void SetToggleButtonState(
		Button button,
		bool enabled,
		string enabledText,
		string disabledText,
		Color enabledColor,
		Color disabledTextColor,
		Color disabledBorderColor)
	{
		button.Text = enabled ? enabledText : disabledText;
		button.TextColor = enabled ? enabledColor : disabledTextColor;
		button.BorderColor = enabled ? enabledColor : disabledBorderColor;
	}

	private Label RentConsoleLogRow(string line)
	{
		var label = _consoleLogRowPool.Rent();
		label.FormattedText = CreateFormattedLogLine(line);
		return label;
	}

	private static Label CreateConsoleLogRow()
	{
		return new Label
		{
			FontSize = 10,
			FontFamily = "JetBrainsMono",
			LineBreakMode = LineBreakMode.WordWrap
		};
	}

	private static void ResetConsoleLogRow(Label label)
	{
		label.FormattedText = null;
		label.Text = string.Empty;
	}

	private bool IsConsoleRenderActive => IsVisible;

	private void TrimStoredLogLines()
	{
		if (_displayedLogLines.Count <= LogOptions.MaxRenderedRows)
		{
			return;
		}

		_displayedLogLines.RemoveRange(0, _displayedLogLines.Count - LogOptions.MaxRenderedRows);
	}

	private async Task RenderConsoleLogLinesAsync()
	{
		if (!IsConsoleRenderActive)
		{
			return;
		}

		int renderVersion = System.Threading.Interlocked.Increment(ref _consoleRenderVersion);
		var lines = _displayedLogLines.ToList();

		ReturnAllConsoleLogRows();
		_isConsoleRendered = false;

		int renderedInBatch = 0;
		foreach (string line in lines)
		{
			if (renderVersion != _consoleRenderVersion || !IsConsoleRenderActive)
			{
				return;
			}

			ConsoleLogStack.Children.Add(RentConsoleLogRow(line));

			renderedInBatch++;
			if (renderedInBatch >= ConsoleRenderBatchSize)
			{
				renderedInBatch = 0;
				await Task.Yield();
			}
		}

		_isConsoleRendered = true;
		if (ConsoleLogStack.Children.Count > 0)
		{
			_ = ConsoleLogScrollView.ScrollToAsync(ConsoleLogStack, ScrollToPosition.End, false);
		}
	}

	private void ReturnAllConsoleLogRows()
	{
		var labels = ConsoleLogStack.Children
			.OfType<Label>()
			.ToList();

		ConsoleLogStack.Children.Clear();

		foreach (var label in labels)
		{
			_consoleLogRowPool.Return(label);
		}
	}

	private void RemoveAndReturnConsoleLogRowAt(int index)
	{
		if (index < 0 || index >= ConsoleLogStack.Children.Count)
		{
			return;
		}

		var child = ConsoleLogStack.Children[index];
		ConsoleLogStack.Children.RemoveAt(index);
		if (child is Label label)
		{
			_consoleLogRowPool.Return(label);
		}
	}

	private static FormattedString CreateFormattedLogLine(string line)
	{
		var formatted = new FormattedString();
		var parts = ParseLogLine(line);

		if (!string.IsNullOrEmpty(parts.Timestamp))
		{
			formatted.Spans.Add(new Span
			{
				Text = parts.Timestamp,
				TextColor = Color.FromArgb("#49657f")
			});
		}

		if (!string.IsNullOrEmpty(parts.Level))
		{
			formatted.Spans.Add(new Span
			{
				Text = $" {parts.Level}",
				TextColor = GetLevelColor(parts.Level),
				FontAttributes = FontAttributes.Bold
			});
		}

		if (!string.IsNullOrEmpty(parts.Tag))
		{
			formatted.Spans.Add(new Span
			{
				Text = $" {parts.Tag}",
				TextColor = NexusColors.Accent,
				FontAttributes = FontAttributes.Bold
			});
		}

		if (!string.IsNullOrEmpty(parts.Message))
		{
			formatted.Spans.Add(new Span
			{
				Text = $" {parts.Message}",
				TextColor = GetMessageColor(parts.Level, parts.Message)
			});
		}

		if (formatted.Spans.Count == 0)
		{
			formatted.Spans.Add(new Span
			{
				Text = line,
				TextColor = TraceActiveColor
			});
		}

		return formatted;
	}

	private static ParsedLogLine ParseLogLine(string line)
	{
		string remaining = line;
		string timestamp = string.Empty;
		string level = string.Empty;
		string tag = string.Empty;

		if (remaining.StartsWith("[", StringComparison.Ordinal))
		{
			int timestampEnd = remaining.IndexOf(']');
			if (timestampEnd > 0)
			{
				timestamp = remaining[..(timestampEnd + 1)];
				remaining = remaining[(timestampEnd + 1)..].TrimStart();
			}
		}

		foreach (string knownLevel in new[] { "TRACE", "WARNING", "ERROR" })
		{
			if (remaining.StartsWith(knownLevel, StringComparison.Ordinal))
			{
				level = knownLevel;
				remaining = remaining[knownLevel.Length..].TrimStart();
				break;
			}
		}

		if (remaining.StartsWith("[", StringComparison.Ordinal))
		{
			int tagEnd = remaining.IndexOf(']');
			if (tagEnd > 0)
			{
				tag = remaining[..(tagEnd + 1)];
				remaining = remaining[(tagEnd + 1)..].TrimStart();
			}
		}

		return new ParsedLogLine(timestamp, level, tag, remaining);
	}

	private static Color GetLevelColor(string level)
	{
		return level switch
		{
			"ERROR" => Color.FromArgb("#ff6688"),
			"WARNING" => Color.FromArgb("#ffd166"),
			"TRACE" => Color.FromArgb("#8aa3ff"),
			_ => Color.FromArgb("#88e4ff"),
		};
	}

	private static Color GetMessageColor(string level, string message)
	{
		if (string.Equals(level, "ERROR", StringComparison.Ordinal))
		{
			return Color.FromArgb("#ffd6df");
		}

		if (message.Contains("[WEB:ERROR]", StringComparison.OrdinalIgnoreCase) ||
			message.Contains("failed", StringComparison.OrdinalIgnoreCase))
		{
			return Color.FromArgb("#ffb3c1");
		}

		if (message.Contains("enabled", StringComparison.OrdinalIgnoreCase) ||
			message.Contains("active", StringComparison.OrdinalIgnoreCase) ||
			message.Contains("ready", StringComparison.OrdinalIgnoreCase))
		{
			return Color.FromArgb("#baf8ff");
		}

		return Color.FromArgb("#9edff0");
	}

	private readonly record struct ParsedLogLine(
		string Timestamp,
		string Level,
		string Tag,
		string Message);

	private void OnManualRebootClicked(object? sender, EventArgs e) => ManualRebootRequested?.Invoke(this, e);
	private void OnToggleBridgeDiagnosticsClicked(object? sender, EventArgs e) => ToggleBridgeDiagnosticsRequested?.Invoke(this, e);
	private void OnToggleWebLogsClicked(object? sender, EventArgs e) => ToggleWebLogsRequested?.Invoke(this, e);
	private void OnToggleDevToolsClicked(object? sender, EventArgs e) => ToggleDevToolsRequested?.Invoke(this, e);
	private void OnToggleUiIsolationClicked(object? sender, EventArgs e) => ToggleUiIsolationRequested?.Invoke(this, e);
	private void OnPatchLocalHudClicked(object? sender, EventArgs e) => PatchLocalHudRequested?.Invoke(this, e);
	private void OnPatchNexusBridgeClicked(object? sender, EventArgs e) => PatchNexusBridgeRequested?.Invoke(this, e);
	private void OnOpenFullLogClicked(object? sender, EventArgs e) => OpenFullLogRequested?.Invoke(this, e);
	private void OnCopyAllClicked(object? sender, EventArgs e) => CopyAllRequested?.Invoke(this, e);
	private void OnClearLogClicked(object? sender, EventArgs e) => ClearLogRequested?.Invoke(this, e);
	private void OnLogSearchChanged(object? sender, TextChangedEventArgs e) => LogSearchChanged?.Invoke(this, e);
}
