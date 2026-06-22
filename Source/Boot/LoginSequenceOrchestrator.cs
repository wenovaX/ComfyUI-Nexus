using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus.Boot;

internal enum LoginSequenceType
{
	Startup,
	F5Refresh,
	CoreLinkSelection,
}

/// <summary>
/// Coordinates the app login lifecycle around WebView navigation, JS bridge handshaking, initial state sync, and shell reveal.
/// </summary>
/// <remarks>
/// Feature-specific work is supplied through <see cref="LoginSequenceSteps"/> so the sequence can stay readable and testable.
/// </remarks>
internal sealed class LoginSequenceOrchestrator
{
	private const int StartupNavigationDelayMs = 0;
	private const int ImmediateNavigationDelayMs = 0;
	private const int HandshakeMaxAttempts = 30;
	private static readonly TimeSpan HandshakeRetryDelay = TimeSpan.FromMilliseconds(200);
	private static readonly TimeSpan HandshakeFinalGraceDelay = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan NavigationWaitTimeout = TimeSpan.FromSeconds(8);

	private readonly object _gate = new();
	private readonly BootFlowTracker _bootFlow = new();
	private LoginSequenceType? _currentType;
	private TaskCompletionSource<LoginNavigationResult>? _navigationSignal;
	private TaskCompletionSource<string>? _bootReadySignal;
	private bool _isCanceled;

	internal LoginSequenceType? CurrentType
	{
		get
		{
			lock (_gate)
			{
				return _currentType;
			}
		}
	}

	internal void Cancel()
	{
		lock (_gate)
		{
			_isCanceled = true;
			_navigationSignal?.TrySetResult(new LoginNavigationResult(false, "login sequence canceled"));
			_bootReadySignal?.TrySetResult(string.Empty);
		}
	}

	/// <summary>
	/// Runs the cold-start login sequence from saved Core Link resolution through shell reveal.
	/// </summary>
	/// <param name="reason">Human-readable boot reason written to boot flow diagnostics.</param>
	/// <param name="steps">Host-provided callbacks that perform UI, WebView, bridge, and state-sync work.</param>
	internal async Task RunStartupAsync(string reason, LoginSequenceSteps steps)
	{
		Begin(LoginSequenceType.Startup, BootFlowKind.Startup, reason);

		// 01. Resolve Core Link.
		Phase(BootPhase.CoreLinkCheck);
		string path = steps.GetCoreLinkPath();
		if (string.IsNullOrEmpty(path))
		{
			CompleteConfigRequired(steps.ShowConfigRequired);
			return;
		}

		// 02. Prepare shell for startup login.
		await steps.PrepareStartupCoreLinkAsync(path);

		// 03. Navigate WebView to ComfyUI.
		if (!await NavigateWebViewAsync(StartupNavigationDelayMs, steps))
		{
			return;
		}

		// 04. Initialize bridge identity.
		await InitializeBridgeAsync(steps);

		// 05. Wake JS agent and wait for BootReady.
		string? agentId = await WakeAgentAndWaitForBootReadyAsync(steps);
		if (string.IsNullOrEmpty(agentId))
		{
			return;
		}

		// 06. Sync first app state payloads.
		await SyncInitialAppStateAsync(agentId, steps);

		// 07. Show final welcome/stand-by and reveal the shell.
		await ShowWelcomeAndRevealAsync(steps);
	}

	/// <summary>
	/// Runs login after the user selects a new ComfyUI root path.
	/// </summary>
	/// <param name="path">Selected ComfyUI root directory to persist and load.</param>
	/// <param name="steps">Host-provided callbacks for navigation, bridge setup, and shell reveal.</param>
	internal async Task RunCoreLinkSelectionAsync(string path, LoginSequenceSteps steps)
	{
		Begin(LoginSequenceType.CoreLinkSelection, BootFlowKind.CoreLinkSelection, "User selected ComfyUI root");

		// 01. Persist selected Core Link.
		Phase(BootPhase.CoreLinkSelected, path);
		await steps.PrepareSelectedCoreLinkAsync(path);

		// 02. Navigate WebView to ComfyUI.
		if (!await NavigateWebViewAsync(ImmediateNavigationDelayMs, steps))
		{
			return;
		}

		// 03. Initialize bridge identity.
		await InitializeBridgeAsync(steps);

		// 04. Wake JS agent and wait for BootReady.
		string? agentId = await WakeAgentAndWaitForBootReadyAsync(steps);
		if (string.IsNullOrEmpty(agentId))
		{
			return;
		}

		// 05. Sync first app state payloads.
		await SyncInitialAppStateAsync(agentId, steps);

		// 06. Show final welcome/stand-by and reveal the shell.
		await ShowWelcomeAndRevealAsync(steps);
	}

	/// <summary>
	/// Runs the F5/WebView refresh sequence without redoing Core Link selection.
	/// </summary>
	/// <param name="reason">Refresh reason used in boot flow diagnostics.</param>
	/// <param name="steps">Host-provided callbacks for reload, bridge setup, and state re-sync.</param>
	internal async Task RunRefreshAsync(string reason, LoginSequenceSteps steps)
	{
		Begin(LoginSequenceType.F5Refresh, BootFlowKind.F5Refresh, reason);

		// 01. Validate Core Link before refresh.
		if (!steps.ValidateCoreLink())
		{
			return;
		}

		ResetSignals();

		// 02. Reload WebView.
		await steps.ReloadWebViewAsync();

		// 03. Wait for navigation result.
		if (!await WaitForNavigationAsync(steps))
		{
			return;
		}

		// 04. Initialize bridge identity.
		await InitializeBridgeAsync(steps);

		// 05. Wake JS agent and wait for BootReady.
		string? agentId = await WakeAgentAndWaitForBootReadyAsync(steps);
		if (string.IsNullOrEmpty(agentId))
		{
			return;
		}

		// 06. Sync first app state payloads.
		await SyncInitialAppStateAsync(agentId, steps);

		// 07. Show final welcome/stand-by and reveal the shell.
		await ShowWelcomeAndRevealAsync(steps);
	}

	/// <summary>
	/// Opens a diagnostic F5 boot session if one is not already active.
	/// </summary>
	/// <param name="reason">Reason attached to the generated boot flow session.</param>
	internal void EnsureF5Refresh(string reason)
	{
		lock (_gate)
		{
			if (_currentType is not null)
			{
				return;
			}
		}

		Begin(LoginSequenceType.F5Refresh, BootFlowKind.F5Refresh, reason);
	}

	/// <summary>
	/// Completes the pending navigation wait with success.
	/// </summary>
	/// <param name="url">Final URL reported by the WebView navigation event.</param>
	internal void NotifyNavigationSucceeded(string url)
		=> CompleteNavigation(new LoginNavigationResult(true, url));

	/// <summary>
	/// Completes the pending navigation wait with failure.
	/// </summary>
	/// <param name="detail">Failure text to show in diagnostics and loading UI.</param>
	internal void NotifyNavigationFailed(string detail)
		=> CompleteNavigation(new LoginNavigationResult(false, detail));

	/// <summary>
	/// Signals that the injected JS agent finished booting and identified itself.
	/// </summary>
	/// <param name="agentId">Agent identifier sent from the WebView bridge.</param>
	internal void NotifyBootReady(string agentId)
	{
		lock (_gate)
		{
			_bootReadySignal?.TrySetResult(agentId);
		}
	}

	/// <summary>
	/// Writes an intermediate boot phase to the active boot flow session.
	/// </summary>
	/// <param name="phase">Boot phase enum value used for stable diagnostics.</param>
	/// <param name="detail">Optional detail string, such as URL, attempt count, or failure reason.</param>
	internal void Phase(BootPhase phase, string? detail = null)
		=> _bootFlow.Phase(phase, detail);

	/// <summary>
	/// Ends the active boot flow session and clears all pending sequence signals.
	/// </summary>
	/// <param name="phase">Terminal phase used in the END log line.</param>
	/// <param name="detail">Optional terminal detail for diagnostics.</param>
	internal void End(BootPhase phase, string? detail = null)
	{
		_bootFlow.End(phase, detail);
		lock (_gate)
		{
			_currentType = null;
			_navigationSignal = null;
			_bootReadySignal = null;
		}
	}

	private async Task<bool> NavigateWebViewAsync(int delayMs, LoginSequenceSteps steps)
	{
		ResetSignals();
		string detail = delayMs > 0 ? $"delay={delayMs}ms" : "immediate";
		Phase(BootPhase.WebViewSourceScheduled, detail);
		await steps.StartWebViewNavigationAsync(delayMs);
		Phase(BootPhase.WebViewSourceAssigned, detail);
		return await WaitForNavigationAsync(steps);
	}

	private async Task<bool> WaitForNavigationAsync(LoginSequenceSteps steps)
	{
		Task<LoginNavigationResult> navigationTask;
		lock (_gate)
		{
			navigationTask = _navigationSignal?.Task ?? Task.FromResult(new LoginNavigationResult(false, "navigation signal unavailable"));
		}

		Task completed = await Task.WhenAny(navigationTask, Task.Delay(NavigationWaitTimeout));
		LoginNavigationResult result = completed == navigationTask
			? await navigationTask
			: new LoginNavigationResult(false, $"navigation timed out after {NavigationWaitTimeout.TotalSeconds:0}s");

		if (result.IsSuccess)
		{
			Phase(BootPhase.NavigationSucceeded, result.Detail);
			return true;
		}

		Phase(BootPhase.NavigationFailed, result.Detail);
		steps.ApplyNavigationFailureUi(result.Detail);
		End(BootPhase.NavigationFailed);
		return false;
	}

	private async Task InitializeBridgeAsync(LoginSequenceSteps steps)
	{
		await steps.PrepareBridgeAsync();
		await steps.InjectShellIdentityAsync();
		Phase(BootPhase.BridgeIdentityInjected);
	}

	private async Task<string?> WakeAgentAndWaitForBootReadyAsync(LoginSequenceSteps steps)
	{
		Task<string> bootReadyTask;
		lock (_gate)
		{
			bootReadyTask = _bootReadySignal?.Task ?? Task.FromResult(string.Empty);
		}

		Phase(BootPhase.HandshakeStarted);
		_ = DisableBrowserReloadHandlingInBackgroundAsync(steps);

		for (int attempt = 1; attempt <= HandshakeMaxAttempts; attempt++)
		{
			if (IsCanceled())
			{
				return null;
			}

			BridgeBootProbeResult probe = await steps.InvokeBridgeBootAsync();
			Phase(BootPhase.HandshakeAttempt, $"attempt={attempt}/{HandshakeMaxAttempts}, {probe.ToLogDetail()}");

			Task completed = await Task.WhenAny(bootReadyTask, Task.Delay(HandshakeRetryDelay));
			if (completed == bootReadyTask)
			{
				return await bootReadyTask;
			}
		}

		if (IsCanceled())
		{
			return null;
		}

		BridgeBootProbeResult finalProbe = await steps.InvokeBridgeBootAsync();
		Phase(BootPhase.HandshakeAttempt, $"final-grace={HandshakeFinalGraceDelay.TotalSeconds:0}s, {finalProbe.ToLogDetail()}");

		Task finalCompleted = await Task.WhenAny(bootReadyTask, Task.Delay(HandshakeFinalGraceDelay));
		if (finalCompleted == bootReadyTask)
		{
			return await bootReadyTask;
		}

		Phase(BootPhase.HandshakeTimeout, $"attempts={HandshakeMaxAttempts}");
		steps.ApplyHandshakeTimeoutUi();
		End(BootPhase.HandshakeTimeout);
		return null;
	}

	private async Task DisableBrowserReloadHandlingInBackgroundAsync(LoginSequenceSteps steps)
	{
		try
		{
			await steps.DisableBrowserReloadHandlingAsync();
			Phase(BootPhase.BrowserReloadHandlingDisabled);
		}
		catch
		{
			// Reload handling is a safety guard; handshake should continue if the script cannot be applied.
		}
	}

	private async Task SyncInitialAppStateAsync(string agentId, LoginSequenceSteps steps)
	{
		Phase(BootPhase.BootReadyReceived, agentId);
		await Task.WhenAll(
			steps.FetchBlueprintsAsync(),
			steps.SyncRunModeAsync(),
			steps.SyncViewQueueAsync());
	}

	private async Task ShowWelcomeAndRevealAsync(LoginSequenceSteps steps)
	{
		Phase(BootPhase.WelcomeStarted);
		await steps.ShowWelcomeAsync();
		Phase(BootPhase.StandByStarted);
		await steps.ShowStandByAsync();
		await steps.RunSuccessSequenceAsync();
	}

	private void CompleteConfigRequired(Action<string> showConfigRequired)
	{
		const string detail = "Core Link preference is empty";
		Phase(BootPhase.ConfigRequired, detail);
		End(BootPhase.ConfigRequired);
		showConfigRequired(detail);
	}

	private void ResetSignals()
	{
		lock (_gate)
		{
			_navigationSignal = new TaskCompletionSource<LoginNavigationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
			_bootReadySignal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		}
	}

	private void CompleteNavigation(LoginNavigationResult result)
	{
		lock (_gate)
		{
			_navigationSignal?.TrySetResult(result);
		}
	}

	private void Begin(LoginSequenceType type, BootFlowKind bootKind, string reason)
	{
		lock (_gate)
		{
			_currentType = type;
			_isCanceled = false;
			_navigationSignal = null;
			_bootReadySignal = null;
			_bootFlow.Begin(bootKind, $"{type}: {reason}");
		}
	}

	private bool IsCanceled()
	{
		lock (_gate)
		{
			return _isCanceled;
		}
	}
}

/// <summary>
/// Host callbacks used by <see cref="LoginSequenceOrchestrator"/> to keep sequencing separate from UI and WebView implementation details.
/// </summary>
/// <param name="GetCoreLinkPath">Returns the saved ComfyUI root path, or an empty value when first-run configuration is required.</param>
/// <param name="ShowConfigRequired">Shows the Core Link setup UI with the supplied reason.</param>
/// <param name="PrepareStartupCoreLinkAsync">Prepares app state for the saved Core Link path during cold startup.</param>
/// <param name="PrepareSelectedCoreLinkAsync">Persists and prepares a newly selected Core Link path.</param>
/// <param name="StartWebViewNavigationAsync">Assigns the WebView source after the supplied delay in milliseconds.</param>
/// <param name="ValidateCoreLink">Returns whether the current Core Link can be used for refresh.</param>
/// <param name="ReloadWebViewAsync">Requests a WebView reload for F5 refresh.</param>
/// <param name="ApplyNavigationFailureUi">Updates loading UI when WebView navigation fails.</param>
/// <param name="PrepareBridgeAsync">Prepares platform bridge hooks before JS identity injection.</param>
/// <param name="InjectShellIdentityAsync">Injects native shell identity into the WebView.</param>
/// <param name="InvokeBridgeBootAsync">Pings the JS agent during handshake attempts and returns the current bridge readiness stage.</param>
/// <param name="DisableBrowserReloadHandlingAsync">Disables browser-level reload/unload hooks after navigation.</param>
/// <param name="ApplyHandshakeTimeoutUi">Updates loading UI when the JS agent never reports ready.</param>
/// <param name="FetchBlueprintsAsync">Starts blueprint/subgraph synchronization.</param>
/// <param name="SyncRunModeAsync">Reads the current web run mode and mirrors it into native UI.</param>
/// <param name="SyncViewQueueAsync">Reads the web queue panel state and mirrors it into native UI.</param>
/// <param name="ShowWelcomeAsync">Shows the welcome loading-overlay phase.</param>
/// <param name="ShowStandByAsync">Shows the stand-by loading-overlay phase before reveal.</param>
/// <param name="RunSuccessSequenceAsync">Runs final shell reveal and post-boot preparation.</param>
internal sealed record LoginSequenceSteps(
	Func<string> GetCoreLinkPath,
	Action<string> ShowConfigRequired,
	Func<string, Task> PrepareStartupCoreLinkAsync,
	Func<string, Task> PrepareSelectedCoreLinkAsync,
	Func<int, Task> StartWebViewNavigationAsync,
	Func<bool> ValidateCoreLink,
	Func<Task> ReloadWebViewAsync,
	Action<string> ApplyNavigationFailureUi,
	Func<Task> PrepareBridgeAsync,
	Func<Task> InjectShellIdentityAsync,
	Func<Task<BridgeBootProbeResult>> InvokeBridgeBootAsync,
	Func<Task> DisableBrowserReloadHandlingAsync,
	Action ApplyHandshakeTimeoutUi,
	Func<Task> FetchBlueprintsAsync,
	Func<Task> SyncRunModeAsync,
	Func<Task> SyncViewQueueAsync,
	Func<Task> ShowWelcomeAsync,
	Func<Task> ShowStandByAsync,
	Func<Task> RunSuccessSequenceAsync);

/// <summary>
/// Result object used to bridge asynchronous WebView navigation events back into the login sequence.
/// </summary>
/// <param name="IsSuccess">True when WebView navigation completed successfully.</param>
/// <param name="Detail">Success URL, timeout text, or failure reason.</param>
internal sealed record LoginNavigationResult(bool IsSuccess, string Detail);
