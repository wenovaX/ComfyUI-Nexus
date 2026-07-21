using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Ui;
using System.Windows.Input;

namespace ComfyUI_Nexus.Views;

public partial class ControlDeckView : ContentView, INexusControlDeck
{
	private static readonly Color PulseMutedTextColor = NexusColors.TextMuted;
	private static Color PulseLiveTextColor => ResourceColor("DeckPulseLiveTextColor", "#baf8ff");
	private static Color PulseWarnTextColor => ResourceColor("DeckPulseWarningTextColor", "#ffe7a6");
	private static Color PulseDangerTextColor => ResourceColor("DeckPulseDangerTextColor", "#ffc4cf");

	public static readonly BindableProperty ManualRebootCommandProperty = BindableProperty.Create(nameof(ManualRebootCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty BootServerCommandProperty = BindableProperty.Create(nameof(BootServerCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty ShutdownServerCommandProperty = BindableProperty.Create(nameof(ShutdownServerCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty ToggleBridgeDiagnosticsCommandProperty = BindableProperty.Create(nameof(ToggleBridgeDiagnosticsCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty ToggleWebLogsCommandProperty = BindableProperty.Create(nameof(ToggleWebLogsCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty ToggleDevToolsCommandProperty = BindableProperty.Create(nameof(ToggleDevToolsCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty ToggleUiIsolationCommandProperty = BindableProperty.Create(nameof(ToggleUiIsolationCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty PatchLocalHudCommandProperty = BindableProperty.Create(nameof(PatchLocalHudCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty PatchNexusBridgeCommandProperty = BindableProperty.Create(nameof(PatchNexusBridgeCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty OpenFullLogCommandProperty = BindableProperty.Create(nameof(OpenFullLogCommand), typeof(ICommand), typeof(ControlDeckView));
	public static readonly BindableProperty ClearLogCommandProperty = BindableProperty.Create(nameof(ClearLogCommand), typeof(ICommand), typeof(ControlDeckView));

	public event EventHandler<TextChangedEventArgs>? LogSearchChanged;

	private readonly List<string> _displayedLogLines = new();
	private readonly NexusMotionController _motion;
	private readonly NexusAnimatedWebpClip _pulseRunIdleClip;
	private readonly NexusAnimatedWebpClip _pulseRunActiveClip;
	private readonly NexusAnimatedWebpClip _pulseWebIdleClip;
	private readonly NexusAnimatedWebpClip _pulseWebLiveClip;
	private NexusAnimatedWebpCacheLease? _animationCacheLease;
	private Task<NexusAnimatedWebpCacheLease>? _animationCacheAcquireTask;
	private bool _isUnloaded;
	private bool _isPulseAnimationActive;
	private bool _isPulseRunActive;
	private bool _isPulseWebLive;
	private bool? _lastPulseRunAnimationState;
	private bool? _lastPulseWebAnimationState;
	private bool _isApplyingToggleState;

	public ICommand? ManualRebootCommand
	{
		get => (ICommand?)GetValue(ManualRebootCommandProperty);
		set => SetValue(ManualRebootCommandProperty, value);
	}

	public ICommand? BootServerCommand
	{
		get => (ICommand?)GetValue(BootServerCommandProperty);
		set => SetValue(BootServerCommandProperty, value);
	}

	public ICommand? ShutdownServerCommand
	{
		get => (ICommand?)GetValue(ShutdownServerCommandProperty);
		set => SetValue(ShutdownServerCommandProperty, value);
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
		_motion = new NexusMotionController("control-deck", "CONTROL_DECK", Dispatcher);
		_pulseRunIdleClip = new NexusAnimatedWebpClip(_motion, PulseRunIdleSurface, "ControlDeck.PulseRunIdle", NexusAnimatedWebpCacheCatalog.HeaderGpuIdle);
		_pulseRunActiveClip = new NexusAnimatedWebpClip(_motion, PulseRunActiveSurface, "ControlDeck.PulseRunActive", NexusAnimatedWebpCacheCatalog.HeaderGpuRunning);
		_pulseWebIdleClip = new NexusAnimatedWebpClip(_motion, PulseWebIdleSurface, "ControlDeck.PulseWebIdle", NexusAnimatedWebpCacheCatalog.HeaderGpuIdle);
		_pulseWebLiveClip = new NexusAnimatedWebpClip(_motion, PulseWebLiveSurface, "ControlDeck.PulseWebLive", NexusAnimatedWebpCacheCatalog.HeaderGpuRunning);
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
		ConsoleLogTail.RowColorResolver = ResolveConsoleRowColor;
		SetPulseRun(isRunning: false, isInstantStop: false);
		SetPulseWeb(
			isBridgeLive: false,
			serverStatus: NexusControlDeckServerStatus.Unknown,
			errorCount: 0,
			bridgeTraceEnabled: false,
			webLogsEnabled: false,
			devToolsEnabled: false);
		SetUiIsolationState(enabled: true);
	}

	public void SetLogFileRelativePath(string relativePath)
	{
		LogFilePathLabel.Text = string.IsNullOrWhiteSpace(relativePath)
			? "Logs/nexus-latest.log"
			: relativePath;
	}

	private void OnLoaded(object? sender, EventArgs e)
	{
		_isUnloaded = false;
		_ = EnsureAnimationCacheAsync();
		StartPulseAnimations();
	}

	private void OnUnloaded(object? sender, EventArgs e)
	{
		_isUnloaded = true;
		_isPulseAnimationActive = false;
		StopPulseAnimations();
		_motion.StopAll();
		ReleaseAnimationCache();
	}

	public string GetLogFilterText() => LogSearchEntry.Text ?? string.Empty;

	public void AppendLogLine(string line)
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

	public void SetLogText(string text)
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

	public void ClearLogText()
	{
		_displayedLogLines.Clear();
		ConsoleLogTail.Clear();
	}

	public void SetBridgeDiagnosticsState(bool enabled)
		=> SetToggleState(BridgeDiagnosticsToggle, enabled);

	public void SetWebLogsState(bool enabled)
		=> SetToggleState(WebLogsToggle, enabled);

	public void SetDevToolsState(bool enabled)
		=> SetToggleState(DevToolsToggle, enabled);

	public void SetUiIsolationState(bool enabled)
		=> SetToggleState(UiIsolationToggle, enabled);

	public void SetPulseRun(bool isRunning, bool isInstantStop)
	{
		_isPulseRunActive = isRunning || isInstantStop;
		PulseRunLabel.Text = isInstantStop ? "RUN STOP" : isRunning ? "RUN ACTIVE" : "RUN IDLE";
		PulseRunLabel.TextColor = isInstantStop ? PulseDangerTextColor : isRunning ? PulseLiveTextColor : PulseMutedTextColor;
		UpdatePulseClipState();
	}

	public void SetPulseWeb(
		bool isBridgeLive,
		NexusControlDeckServerStatus serverStatus,
		int errorCount,
		bool bridgeTraceEnabled,
		bool webLogsEnabled,
		bool devToolsEnabled)
	{
		_isPulseWebLive = isBridgeLive;
		SetServerStatus(serverStatus);

		if (errorCount > 0)
		{
			PulseWebLabel.Text = $"WEB ERR {errorCount}";
			PulseWebLabel.TextColor = PulseDangerTextColor;
			UpdatePulseClipState();
			return;
		}

		if (bridgeTraceEnabled || webLogsEnabled || devToolsEnabled)
		{
			PulseWebLabel.Text = "WEB TRACE";
			PulseWebLabel.TextColor = PulseWarnTextColor;
			UpdatePulseClipState();
			return;
		}

		PulseWebLabel.Text = isBridgeLive ? "BRIDGE LIVE" : "BRIDGE IDLE";
		PulseWebLabel.TextColor = isBridgeLive ? PulseLiveTextColor : PulseMutedTextColor;
		UpdatePulseClipState();
	}

	private void SetServerStatus(NexusControlDeckServerStatus serverStatus)
	{
		switch (serverStatus)
		{
			case NexusControlDeckServerStatus.Ready:
				PulseBridgeStatusLabel.Text = "API READY";
				PulseBridgeStatusLabel.TextColor = PulseLiveTextColor;
				break;
			case NexusControlDeckServerStatus.Transitioning:
				PulseBridgeStatusLabel.Text = "API TRANSITION";
				PulseBridgeStatusLabel.TextColor = PulseWarnTextColor;
				break;
			case NexusControlDeckServerStatus.Offline:
				PulseBridgeStatusLabel.Text = "API OFFLINE";
				PulseBridgeStatusLabel.TextColor = PulseDangerTextColor;
				break;
			default:
				PulseBridgeStatusLabel.Text = "API UNKNOWN";
				PulseBridgeStatusLabel.TextColor = PulseMutedTextColor;
				break;
		}
	}

	private void StartPulseAnimations()
	{
		if (_isUnloaded || _isPulseAnimationActive
			|| PulseRunIdleSurface.Handler is null
			|| PulseRunActiveSurface.Handler is null
			|| PulseWebIdleSurface.Handler is null
			|| PulseWebLiveSurface.Handler is null)
		{
			return;
		}

		_isPulseAnimationActive = true;
		UpdatePulseClipState();
	}

	private void StopPulseAnimations()
	{
		_lastPulseRunAnimationState = null;
		_lastPulseWebAnimationState = null;
		_pulseRunIdleClip.Stop();
		_pulseRunActiveClip.Stop();
		_pulseWebIdleClip.Stop();
		_pulseWebLiveClip.Stop();
	}

	private async Task EnsureAnimationCacheAsync()
	{
		if (_animationCacheLease is not null)
		{
			return;
		}

		_animationCacheAcquireTask ??= NexusAnimatedWebpFrameCache.AcquireAsync(NexusAnimatedWebpCacheGroup.ControlDeck);
		_animationCacheLease = await _animationCacheAcquireTask;
	}

	private void ReleaseAnimationCache()
	{
		_animationCacheLease?.Dispose();
		_animationCacheLease = null;
		_animationCacheAcquireTask = null;
	}

	private void UpdatePulseClipState()
	{
		if (_isUnloaded || !_isPulseAnimationActive)
		{
			return;
		}

		if (_lastPulseRunAnimationState != _isPulseRunActive)
		{
			ApplyPulseClipState(_pulseRunIdleClip, _pulseRunActiveClip, PulseRunIdleSurface, PulseRunActiveSurface, _isPulseRunActive);
			_lastPulseRunAnimationState = _isPulseRunActive;
		}

		if (_lastPulseWebAnimationState != _isPulseWebLive)
		{
			ApplyPulseClipState(_pulseWebIdleClip, _pulseWebLiveClip, PulseWebIdleSurface, PulseWebLiveSurface, _isPulseWebLive);
			_lastPulseWebAnimationState = _isPulseWebLive;
		}
	}

	private void ApplyPulseClipState(
		NexusAnimatedWebpClip idleClip,
		NexusAnimatedWebpClip activeClip,
		Image idleSurface,
		Image activeSurface,
		bool isActive)
	{
		idleSurface.Opacity = isActive ? 0 : 0.34;
		activeSurface.Opacity = isActive ? 1 : 0;

		if (isActive)
		{
			idleClip.Stop();
			activeClip.PlayLoop(() => CanRunPulseSurface(activeSurface));
			return;
		}

		activeClip.Stop();
		idleClip.PlayLoop(() => CanRunPulseSurface(idleSurface));
	}

	private bool CanRunPulseSurface(Image surface)
		=> !_isUnloaded && IsVisible && Handler is not null && surface.Handler is not null;

	private void SetToggleState(Switch toggle, bool enabled)
	{
		_isApplyingToggleState = true;
		toggle.IsToggled = enabled;
		_isApplyingToggleState = false;
	}

	private void OnBridgeDiagnosticsToggled(object? sender, ToggledEventArgs e)
		=> ExecuteToggleCommand(ToggleBridgeDiagnosticsCommand);

	private void OnWebLogsToggled(object? sender, ToggledEventArgs e)
		=> ExecuteToggleCommand(ToggleWebLogsCommand);

	private void OnDevToolsToggled(object? sender, ToggledEventArgs e)
		=> ExecuteToggleCommand(ToggleDevToolsCommand);

	private void OnUiIsolationToggled(object? sender, ToggledEventArgs e)
		=> ExecuteToggleCommand(ToggleUiIsolationCommand);

	private void ExecuteToggleCommand(ICommand? command)
	{
		if (_isApplyingToggleState || command?.CanExecute(null) != true)
		{
			return;
		}

		command.Execute(null);
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
