namespace ComfyUI_Nexus.Diagnostics;

internal enum NexusExitIntent
{
	Unknown,
	KeepServerRunningAndExit,
	KillServerAndExit,
}

internal static class ProcessExitDiagnostics
{
	private static readonly object Gate = new();
	private static int _isAttached;
	private static NexusExitIntent _intent = NexusExitIntent.Unknown;
	private static DateTimeOffset? _intentMarkedAt;
	private static string _intentDetail = "";

	internal static void Attach(string stateDirectory)
	{
		if (Interlocked.Exchange(ref _isAttached, 1) == 1)
		{
			return;
		}

		SessionHeartbeatDiagnostics.ReportPreviousSession(stateDirectory);
		SessionHeartbeatDiagnostics.Start(stateDirectory);
		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
		NexusLog.Info("[PROCESS] Process exit diagnostics attached.");
	}

	internal static void MarkExitIntent(NexusExitIntent intent, string detail = "")
	{
		lock (Gate)
		{
			_intent = intent;
			_intentDetail = detail;
			_intentMarkedAt = DateTimeOffset.Now;
		}

		NexusLog.Info($"[PROCESS] Exit intent: {intent}{FormatDetail(detail)}");
		SessionHeartbeatDiagnostics.MarkExitIntent(intent, detail);
		NexusLog.FlushPersistentLog(TimeSpan.FromMilliseconds(300));
	}

	internal static void MarkCleanShutdown(string detail)
		=> SessionHeartbeatDiagnostics.MarkCleanShutdown(detail);

	private static void OnProcessExit(object? sender, EventArgs e)
	{
		NexusExitIntent intent;
		DateTimeOffset? markedAt;
		string detail;
		lock (Gate)
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

	private static string FormatDetail(string detail)
		=> string.IsNullOrWhiteSpace(detail) ? "" : $", detail={detail}";
}
