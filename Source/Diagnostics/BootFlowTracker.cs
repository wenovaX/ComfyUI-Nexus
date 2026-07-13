using System.Diagnostics;

namespace ComfyUI_Nexus.Diagnostics;

internal enum BootFlowKind
{
	Startup,
	F5Refresh,
	CoreLinkSelection,
}

internal enum BootPhase
{
	CoreLinkCheck,
	ConfigRequired,
	CoreLinkSelected,
	CoreLinkValidationFailed,
	LoadingOverlayStart,
	LoadingOverlayStabilizing,
	LoadingOverlayHidden,
	WebViewSourceScheduled,
	WebViewSourceAssigned,
	NavigationStarting,
	NavigationSucceeded,
	NavigationFailed,
	ReloadRequested,
	ReloadFailed,
	BridgeIdentityInjected,
	HandshakeStarted,
	HandshakeAttempt,
	BrowserReloadHandlingDisabled,
	HandshakeTimeout,
	BootReadyReceived,
	WelcomeStarted,
	StandByStarted,
	WorkflowProbeStarted,
	WorkflowProbeCompleted,
	WorkflowProbeFailed,
	BookmarksLoadStarted,
	BookmarksLoadCompleted,
	SuccessSequenceStarted,
	RailPrepared,
	RailPrewarmStarted,
	RailPrewarmCompleted,
	NodeLibrarySyncStarted,
	NodeLibrarySyncCompleted,
	NodeLibrarySyncFailed,
	SuccessVisualsStarted,
	StableRequested,
	StableCompleted,
}

internal sealed class BootFlowTracker
{
	private readonly object _gate = new();
	private int _nextSessionId;
	private BootFlowSession? _current;

	internal BootFlowSession Begin(BootFlowKind kind, string reason)
	{
		lock (_gate)
		{
			int id = ++_nextSessionId;
			_current = new BootFlowSession(id, kind);
			_current.Write("START", reason);
			return _current;
		}
	}

	internal BootFlowSession Ensure(BootFlowKind kind, string reason)
	{
		lock (_gate)
		{
			if (_current != null && !_current.IsCompleted)
			{
				return _current;
			}
		}

		return Begin(kind, reason);
	}

	internal void Phase(BootPhase phase, string? detail = null)
	{
		BootFlowSession? session;
		lock (_gate)
		{
			session = _current;
		}

		session?.Phase(phase, detail);
	}

	internal void End(BootPhase phase, string? detail = null)
	{
		BootFlowSession? session;
		lock (_gate)
		{
			session = _current;
			_current = null;
		}

		session?.End(phase, detail);
	}
}

internal sealed class BootFlowSession
{
	private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
	private long _lastElapsedMs;

	internal BootFlowSession(int id, BootFlowKind kind)
	{
		Id = id;
		Kind = kind;
	}

	internal int Id { get; }
	internal BootFlowKind Kind { get; }
	internal bool IsCompleted { get; private set; }

	internal void Phase(BootPhase phase, string? detail = null)
		=> Write(phase.ToString(), detail);

	internal void End(BootPhase phase, string? detail = null)
	{
		IsCompleted = true;
		Write($"END:{phase}", detail);
	}

	internal void Write(string phase, string? detail = null)
	{
		long totalMs = _stopwatch.ElapsedMilliseconds;
		long deltaMs = totalMs - _lastElapsedMs;
		_lastElapsedMs = totalMs;

		string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" - {detail}";
		NexusLog.Info($"[BOOT #{Id:D2} {Kind}] +{deltaMs:D4}ms total={totalMs:D5}ms {phase}{suffix}");
	}
}
