using System.Text.Json;

namespace ComfyUI_Nexus.Diagnostics;

internal enum NexusExitIntent
{
	Unknown,
	KeepServerRunningAndExit,
	KillServerAndExit,
}

/// <summary>
/// Owns the app session marker, heartbeat timer, and process-exit subscription.
/// This component must be owned by <see cref="NexusAppManager"/> because its state
/// describes one running Nexus process.
/// </summary>
internal sealed class NexusSessionDiagnosticsService : IDisposable
{
	private const string StateFileName = "nexus-session-state.json";
	private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);

	private readonly object _gate = new();
	private Timer? _timer;
	private string? _statePath;
	private SessionHeartbeatState? _state;
	private bool _isAttached;
	private NexusExitIntent _intent = NexusExitIntent.Unknown;
	private DateTimeOffset? _intentMarkedAt;
	private string _intentDetail = "";
	private bool _disposed;

	internal void Attach(string stateDirectory)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_isAttached)
		{
			return;
		}

		_isAttached = true;
		ReportPreviousSession(stateDirectory);
		StartHeartbeat(stateDirectory);
		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
		NexusLog.Info("[PROCESS] Process exit diagnostics attached.");
	}

	internal void MarkExitIntent(NexusExitIntent intent, string detail = "")
	{
		lock (_gate)
		{
			_intent = intent;
			_intentDetail = detail;
			_intentMarkedAt = DateTimeOffset.Now;
			UpdateStateNoThrow();
		}

		NexusLog.Info($"[PROCESS] Exit intent: {intent}{FormatDetail(detail)}");
		NexusLog.FlushPersistentLog(TimeSpan.FromMilliseconds(300));
	}

	internal void MarkCleanShutdown(string detail)
	{
		lock (_gate)
		{
			if (_state == null)
			{
				return;
			}

			StopHeartbeatNoThrow();
			_state.IsCleanShutdown = true;
			_state.ExitDetail = string.IsNullOrWhiteSpace(_state.ExitDetail) ? detail : $"{_state.ExitDetail}; {detail}";
			_state.LastHeartbeatAt = DateTimeOffset.Now;
			_state.XamlLifetime = XamlLifetimeDiagnostics.GetSnapshot();
			_state.Concurrency = NexusConcurrencyDiagnostics.GetSnapshot();
			_state.UiTrace = NexusUiActionTrace.GetSnapshot();
			WriteStateNoThrow();
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
		lock (_gate)
		{
			StopHeartbeatNoThrow();
		}
	}

	private void ReportPreviousSession(string stateDirectory)
	{
		string path = Path.Combine(stateDirectory, StateFileName);
		if (!File.Exists(path))
		{
			return;
		}

		try
		{
			SessionHeartbeatState? previous = JsonSerializer.Deserialize<SessionHeartbeatState>(File.ReadAllText(path));
			if (previous is not { IsCleanShutdown: false })
			{
				return;
			}

			NexusLog.Warning(
				$"[SESSION] Previous session ended without clean shutdown. pid={previous.ProcessId}, started={previous.StartedAt:O}, lastHeartbeat={previous.LastHeartbeatAt:O}, intent={previous.ExitIntent}, detail={previous.ExitDetail}, log={previous.LogPath}, uiTrace={previous.UiTrace}");
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"[SESSION] Previous session marker read failed: {ex.GetType().Name} - {ex.Message}");
		}
	}

	private void StartHeartbeat(string stateDirectory)
	{
		try
		{
			Directory.CreateDirectory(stateDirectory);
			string path = Path.Combine(stateDirectory, StateFileName);
			DateTimeOffset now = DateTimeOffset.Now;
			lock (_gate)
			{
				_statePath = path;
				_state = new SessionHeartbeatState
				{
					ProcessId = Environment.ProcessId,
					StartedAt = now,
					LastHeartbeatAt = now,
					IsCleanShutdown = false,
					ExitIntent = NexusExitIntent.Unknown.ToString(),
					ExitDetail = "",
					LogPath = NexusLog.CurrentLatestLogPath ?? "",
					XamlLifetime = XamlLifetimeDiagnostics.GetSnapshot(),
					Concurrency = NexusConcurrencyDiagnostics.GetSnapshot(),
					UiTrace = NexusUiActionTrace.GetSnapshot()
				};

				WriteStateNoThrow();
				StopHeartbeatNoThrow();
				_timer = new Timer(OnHeartbeat, null, HeartbeatInterval, HeartbeatInterval);
			}

			NexusLog.Info($"[SESSION] Heartbeat started: {path}");
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"[SESSION] Heartbeat start failed: {ex.GetType().Name} - {ex.Message}");
		}
	}

	private void OnHeartbeat(object? state)
	{
		lock (_gate)
		{
			if (_disposed || _state == null)
			{
				return;
			}

			UpdateStateNoThrow();
		}
	}

	private void UpdateStateNoThrow()
	{
		if (_state == null)
		{
			return;
		}

		_state.ExitIntent = _intent.ToString();
		_state.ExitDetail = _intentDetail;
		_state.LastHeartbeatAt = DateTimeOffset.Now;
		_state.XamlLifetime = XamlLifetimeDiagnostics.GetSnapshot();
		_state.Concurrency = NexusConcurrencyDiagnostics.GetSnapshot();
		_state.UiTrace = NexusUiActionTrace.GetSnapshot();
		WriteStateNoThrow();
	}

	private void StopHeartbeatNoThrow()
	{
		_timer?.Dispose();
		_timer = null;
	}

	private void OnProcessExit(object? sender, EventArgs e)
	{
		NexusExitIntent intent;
		DateTimeOffset? markedAt;
		string detail;
		lock (_gate)
		{
			intent = _intent;
			markedAt = _intentMarkedAt;
			detail = _intentDetail;
		}

		string markedAtText = markedAt.HasValue ? markedAt.Value.ToString("O") : "none";
		NexusLog.Info($"[PROCESS] ProcessExit fired. intent={intent}, intentMarkedAt={markedAtText}{FormatDetail(detail)}");
		NexusUiActionTrace.WriteSnapshot("process-exit");
		XamlLifetimeDiagnostics.WriteSnapshot("process-exit");
		NexusLog.FlushPersistentLog(TimeSpan.FromMilliseconds(500));
	}

	private void WriteStateNoThrow()
	{
		if (_statePath == null || _state == null)
		{
			return;
		}

		try
		{
			File.WriteAllText(_statePath, JsonSerializer.Serialize(_state, SessionHeartbeatJson.Options));
		}
		catch
		{
		}
	}

	private static string FormatDetail(string detail)
		=> string.IsNullOrWhiteSpace(detail) ? "" : $", detail={detail}";

	private sealed class SessionHeartbeatState
	{
		public int ProcessId { get; set; }
		public DateTimeOffset StartedAt { get; set; }
		public DateTimeOffset LastHeartbeatAt { get; set; }
		public bool IsCleanShutdown { get; set; }
		public string ExitIntent { get; set; } = "";
		public string ExitDetail { get; set; } = "";
		public string LogPath { get; set; } = "";
		public string XamlLifetime { get; set; } = "";
		public string Concurrency { get; set; } = "";
		public string UiTrace { get; set; } = "";
	}

	private static class SessionHeartbeatJson
	{
		internal static readonly JsonSerializerOptions Options = new()
		{
			WriteIndented = true
		};
	}
}
