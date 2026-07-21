using System.Text.Json;

namespace ComfyUI_Nexus.Diagnostics;

internal static class SessionHeartbeatDiagnostics
{
	private const string StateFileName = "nexus-session-state.json";
	private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
	private static readonly object Gate = new();

	private static Timer? _timer;
	private static string? _statePath;
	private static SessionHeartbeatState? _state;

	internal static void ReportPreviousSession(string stateDirectory)
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

	internal static void Start(string stateDirectory)
	{
		try
		{
			Directory.CreateDirectory(stateDirectory);
			string path = Path.Combine(stateDirectory, StateFileName);
			var now = DateTimeOffset.Now;
			lock (Gate)
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
				_timer?.Dispose();
				_timer = new Timer(OnHeartbeat, null, HeartbeatInterval, HeartbeatInterval);
			}

			NexusLog.Info($"[SESSION] Heartbeat started: {path}");
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"[SESSION] Heartbeat start failed: {ex.GetType().Name} - {ex.Message}");
		}
	}

	internal static void MarkExitIntent(NexusExitIntent intent, string detail)
	{
		lock (Gate)
		{
			if (_state == null)
			{
				return;
			}

			_state.ExitIntent = intent.ToString();
			_state.ExitDetail = detail;
			_state.LastHeartbeatAt = DateTimeOffset.Now;
			_state.XamlLifetime = XamlLifetimeDiagnostics.GetSnapshot();
			_state.Concurrency = NexusConcurrencyDiagnostics.GetSnapshot();
			_state.UiTrace = NexusUiActionTrace.GetSnapshot();
			WriteStateNoThrow();
		}
	}

	internal static void MarkCleanShutdown(string detail)
	{
		lock (Gate)
		{
			if (_state == null)
			{
				return;
			}

			_timer?.Dispose();
			_timer = null;
			_state.IsCleanShutdown = true;
			_state.ExitDetail = string.IsNullOrWhiteSpace(_state.ExitDetail) ? detail : $"{_state.ExitDetail}; {detail}";
			_state.LastHeartbeatAt = DateTimeOffset.Now;
			_state.XamlLifetime = XamlLifetimeDiagnostics.GetSnapshot();
			_state.Concurrency = NexusConcurrencyDiagnostics.GetSnapshot();
			_state.UiTrace = NexusUiActionTrace.GetSnapshot();
			WriteStateNoThrow();
		}
	}

	private static void OnHeartbeat(object? state)
	{
		lock (Gate)
		{
			if (_state == null)
			{
				return;
			}

			_state.LastHeartbeatAt = DateTimeOffset.Now;
			_state.XamlLifetime = XamlLifetimeDiagnostics.GetSnapshot();
			_state.Concurrency = NexusConcurrencyDiagnostics.GetSnapshot();
			_state.UiTrace = NexusUiActionTrace.GetSnapshot();
			WriteStateNoThrow();
		}
	}

	private static void WriteStateNoThrow()
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
