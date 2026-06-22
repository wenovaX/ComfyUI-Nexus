namespace ComfyUI_Nexus.Diagnostics;

internal static class DebugExceptionDiagnostics
{
	private static int _isAttached;

	internal static void Attach()
	{
		if (Interlocked.Exchange(ref _isAttached, 1) == 1)
		{
			return;
		}

		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
		NexusLog.Info("[EXCEPTION] Global exception diagnostics attached.");
	}

	private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		string senderName = sender.GetType().FullName ?? "unknown";
		string terminating = e.IsTerminating ? "terminating" : "non-terminating";

		if (e.ExceptionObject is Exception exception)
		{
			NexusLog.Exception(exception, $"[UNHANDLED:{terminating}] sender={senderName}");
			NexusLog.FlushPersistentLog(TimeSpan.FromMilliseconds(300));
			return;
		}

		NexusLog.Error($"[UNHANDLED:{terminating}] sender={senderName}, object={e.ExceptionObject}");
		NexusLog.FlushPersistentLog(TimeSpan.FromMilliseconds(300));
	}

	private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		foreach (Exception exception in e.Exception.Flatten().InnerExceptions)
		{
			if (IsExpectedCancellation(exception))
			{
				continue;
			}

			NexusLog.Exception(exception, "[TASK:UNOBSERVED]");
		}
	}

	private static bool IsExpectedCancellation(Exception exception)
		=> exception is OperationCanceledException or TaskCanceledException;
}
