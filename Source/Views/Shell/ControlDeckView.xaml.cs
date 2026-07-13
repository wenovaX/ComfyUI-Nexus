using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Ui;
using System.Windows.Input;

namespace ComfyUI_Nexus.Views;

public partial class ControlDeckView : ContentView
{
	private const uint PulseRunBlinkRiseLength = 420;
	private const uint PulseRunBlinkFallLength = 520;
	private static readonly Color TraceActiveColor = NexusColors.AccentText;
	private static readonly Color TraceInactiveTextColor = NexusColors.TextFaint;
	private static Color TraceInactiveBorderColor => ResourceColor("DeckToggleInactiveBorderColor", "#445566");
	private static Color DevToolsActiveColor => ResourceColor("DeckDevToolsActiveColor", "#88aa66");
	private static Color DevToolsInactiveTextColor => ResourceColor("DeckDevToolsInactiveTextColor", "#668877");
	private static Color DevToolsInactiveBorderColor => ResourceColor("DeckDevToolsInactiveBorderColor", "#446655");
	private static readonly Color PulseLiveColor = NexusColors.Accent;
	private static Color PulseIdleColor => ResourceColor("DeckPulseIdleColor", "#2373ff");
	private static Color PulseWarnColor => ResourceColor("DeckPulseWarningColor", "#ffcc66");
	private static Color PulseDangerColor => ResourceColor("DeckPulseDangerColor", "#ff4d6d");
	private static readonly Color PulseMutedTextColor = NexusColors.TextMuted;
	private static Color PulseLiveTextColor => ResourceColor("DeckPulseLiveTextColor", "#baf8ff");
	private static Color PulseWarnTextColor => ResourceColor("DeckPulseWarningTextColor", "#ffe7a6");
	private static Color PulseDangerTextColor => ResourceColor("DeckPulseDangerTextColor", "#ffc4cf");

	public static readonly BindableProperty ManualRebootCommandProperty = BindableProperty.Create(nameof(ManualRebootCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty ToggleBridgeDiagnosticsCommandProperty = BindableProperty.Create(nameof(ToggleBridgeDiagnosticsCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty ToggleWebLogsCommandProperty = BindableProperty.Create(nameof(ToggleWebLogsCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty ToggleDevToolsCommandProperty = BindableProperty.Create(nameof(ToggleDevToolsCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty ToggleUiIsolationCommandProperty = BindableProperty.Create(nameof(ToggleUiIsolationCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty PatchLocalHudCommandProperty = BindableProperty.Create(nameof(PatchLocalHudCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty PatchNexusBridgeCommandProperty = BindableProperty.Create(nameof(PatchNexusBridgeCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty OpenFullLogCommandProperty = BindableProperty.Create(nameof(OpenFullLogCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty ClearLogCommandProperty = BindableProperty.Create(nameof(ClearLogCommand), typeof(ICommand), typeof(ControlDeckView));

	internal event EventHandler<TextChangedEventArgs>? LogSearchChanged;

	private readonly List<string> _displayedLogLines = new();
	private int _pulseRunBlinkVersion;
	private bool _isPulseRunBlinking;
	private bool _isUnloaded;

	public ICommand? ManualRebootCommand
	{
		get => (ICommand?)GetValue(ManualRebootCommandProperty);
		set => SetValue(ManualRebootCommandProperty, value);
	}

	public ICommand? ToggleBridgeDiagnosticsCommand
	{
		get => (ICommand?)GetValue(ToggleBridgeDiagnosticsCommandProperty);
		set => SetValue(ToggleBridgeDiagnosticsCommandProperty, value);
	}

	public ICommand? ToggleWebLogsCommand
	{
		get => (ICommand?)GetValue(ToggleWebLogsCommandProperty);
		set => SetValue(ToggleWebLogsCommandProperty, value);
	}

	public ICommand? ToggleDevToolsCommand
	{
		get => (ICommand?)GetValue(ToggleDevToolsCommandProperty);
		set => SetValue(ToggleDevToolsCommandProperty, value);
	}

	public ICommand? ToggleUiIsolationCommand
	{
		get => (ICommand?)GetValue(ToggleUiIsolationCommandProperty);
		set => SetValue(ToggleUiIsolationCommandProperty, value);
	}

	public ICommand? PatchLocalHudCommand
	{
		get => (ICommand?)GetValue(PatchLocalHudCommandProperty);
		set => SetValue(PatchLocalHudCommandProperty, value);
	}

	public ICommand? PatchNexusBridgeCommand
	{
		get => (ICommand?)GetValue(PatchNexusBridgeCommandProperty);
		set => SetValue(PatchNexusBridgeCommandProperty, value);
	}

	public ICommand? OpenFullLogCommand
	{
		get => (ICommand?)GetValue(OpenFullLogCommandProperty);
		set => SetValue(OpenFullLogCommandProperty, value);
	}

	public ICommand? ClearLogCommand
	{
		get => (ICommand?)GetValue(ClearLogCommandProperty);
		set => SetValue(ClearLogCommandProperty, value);
	}

	public ControlDeckView()
	{
		InitializeComponent();
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
		ConsoleLogTail.RowColorResolver = ResolveConsoleRowColor;
		SetPulseRun(isRunning: false, isInstantStop: false);
		SetPulseWeb(isLive: false, errorCount: 0, bridgeTraceEnabled: false, webLogsEnabled: false, devToolsEnabled: false);
		SetUiIsolationState(enabled: true);
	}

	internal void SetLogFileRelativePath(string relativePath)
	{
		LogFilePathLabel.Text = string.IsNullOrWhiteSpace(relativePath)
			? "Logs/nexus-latest.log"
			: relativePath;
	}

	private void OnLoaded(object? sender, EventArgs e)
		=> _isUnloaded = false;

	private void OnUnloaded(object? sender, EventArgs e)
	{
		_isUnloaded = true;
		_isPulseRunBlinking = false;
		_pulseRunBlinkVersion++;
		SafeAnimation.AbortAnimation(PulseRunBar, "PulseRunBlink", "ControlDeck.PulseRun");
	}

	internal string GetLogFilterText() => LogSearchEntry.Text ?? string.Empty;

	internal void AppendLogLine(string line)
	{
		_displayedLogLines.Add(line);

		if (_displayedLogLines.Count > LogOptions.MaxRenderedRows)
		{
			int excess = _displayedLogLines.Count - LogOptions.MaxRenderedRows;
			_displayedLogLines.RemoveRange(0, excess);
		}

		if (IsConsoleRenderActive)
		{
			ConsoleLogTail.AppendLine(line);
		}
	}

	internal void SetLogText(string text)
	{
		_displayedLogLines.Clear();

		foreach (string line in text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
		{
			_displayedLogLines.Add(line);
		}

		TrimStoredLogLines();

		if (IsConsoleRenderActive)
		{
			ConsoleLogTail.SetLines(_displayedLogLines);
		}
		else
		{
			ConsoleLogTail.Clear();
		}
	}

	internal void ClearLogText()
	{
		_displayedLogLines.Clear();
		ConsoleLogTail.Clear();
	}

	internal void SetDisplayState(bool isVisible, double opacity)
	{
		IsVisible = isVisible;
		Opacity = opacity;
		if (!isVisible)
		{
			ConsoleLogTail.ReleaseRows();
			return;
		}

		ConsoleLogTail.SetLines(_displayedLogLines);
	}

	internal void PrepareToShow()
	{
		Opacity = 0;
		IsVisible = true;
		ConsoleLogTail.SetLines(_displayedLogLines);
	}

	internal Task AnimateShowAsync(uint length, Easing easing)
	{
		return SafeAnimation.FadeToAsync(this, 1, length, easing, "ControlDeck.Show");
	}

	internal Task AnimateHideAsync(uint length, Easing easing)
	{
		return SafeAnimation.FadeToAsync(this, 0, length, easing, "ControlDeck.Hide");
	}

	internal void CompleteHide()
	{
		IsVisible = false;
		ConsoleLogTail.ReleaseRows();
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
			ResourceColor("DeckPulseWarningColor", "#ffcc66"),
			ResourceColor("DeckPulseWarningTrackColor", "#806622"));
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
			SafeAnimation.AbortAnimation(PulseRunBar, "PulseRunBlink", "ControlDeck.PulseRun");
			if (!_isUnloaded)
			{
				PulseRunBar.Opacity = 0.34;
			}
			return;
		}

		if (_isUnloaded || _isPulseRunBlinking)
		{
			return;
		}

		_isPulseRunBlinking = true;
		int version = ++_pulseRunBlinkVersion;
		while (version == _pulseRunBlinkVersion)
		{
			if (_isUnloaded) break;

			bool rose = await SafeAnimation.TryFadeToAsync(PulseRunBar, 1, PulseRunBlinkRiseLength, Easing.CubicOut, "ControlDeck.PulseRun");
			if (!rose || _isUnloaded || version != _pulseRunBlinkVersion) break;

			bool fell = await SafeAnimation.TryFadeToAsync(PulseRunBar, 0.38, PulseRunBlinkFallLength, Easing.CubicIn, "ControlDeck.PulseRun");
			if (!fell || _isUnloaded) break;
		}

		if (_isUnloaded)
		{
			_isPulseRunBlinking = false;
			return;
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

	private bool IsConsoleRenderActive => IsVisible;

	private void TrimStoredLogLines()
	{
		if (_displayedLogLines.Count <= LogOptions.MaxRenderedRows)
		{
			return;
		}

		_displayedLogLines.RemoveRange(0, _displayedLogLines.Count - LogOptions.MaxRenderedRows);
	}

	private static Color ResourceColor(string key, string fallback)
	{
		if (Application.Current?.Resources.TryGetValue(key, out object? value) == true)
		{
			return value switch
			{
				Color color => color,
				SolidColorBrush brush => brush.Color,
				_ => Color.FromArgb(fallback),
			};
		}

		return Color.FromArgb(fallback);
	}

	private static Color? ResolveConsoleRowColor(string line)
	{
		if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
		{
			return ResourceColor("DeckConsoleErrorMessageColor", "#ffd6df");
		}

		if (line.Contains("WARNING", StringComparison.OrdinalIgnoreCase))
		{
			return ResourceColor("DeckConsoleWarningLevelColor", "#ffd166");
		}

		if (line.Contains("TRACE", StringComparison.OrdinalIgnoreCase))
		{
			return ResourceColor("DeckConsoleTraceLevelColor", "#8aa3ff");
		}

		if (line.Contains("[WEB:ERROR]", StringComparison.OrdinalIgnoreCase) ||
			line.Contains("failed", StringComparison.OrdinalIgnoreCase))
		{
			return ResourceColor("DeckConsoleFailureMessageColor", "#ffb3c1");
		}

		if (line.Contains("enabled", StringComparison.OrdinalIgnoreCase) ||
			line.Contains("active", StringComparison.OrdinalIgnoreCase) ||
			line.Contains("ready", StringComparison.OrdinalIgnoreCase))
		{
			return ResourceColor("DeckConsoleReadyMessageColor", "#baf8ff");
		}

		return ResourceColor("DeckConsoleDefaultMessageColor", "#9edff0");
	}

	private void OnLogSearchChanged(object? sender, TextChangedEventArgs e) => LogSearchChanged?.Invoke(this, e);
}
