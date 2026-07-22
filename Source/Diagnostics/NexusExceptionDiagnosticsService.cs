namespace ComfyUI_Nexus.Diagnostics;

/// <summary>
/// Owns global exception event subscriptions for the current app process.
/// </summary>
internal sealed class NexusExceptionDiagnosticsService : IDisposable
{
	private bool _isAttached;

	internal void Attach()
	{
		if (_isAttached)
		{
			return;
		}

		_isAttached = true;
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
		NexusLog.Info("[EXCEPTION] Global exception diagnostics attached.");
	}

	public void Dispose()
	{
		if (!_isAttached)
		{
			return;
		}

		_isAttached = false;
		AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
		TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
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
		bool wroteException = false;
		foreach (Exception exception in e.Exception.Flatten().InnerExceptions)
		{
			if (exception is OperationCanceledException or TaskCanceledException)
			{
				continue;
			}

			NexusLog.Exception(exception, "[TASK:UNOBSERVED]");
			wroteException = true;
		}

		if (wroteException)
		{
			NexusLog.FlushPersistentLog(TimeSpan.FromMilliseconds(300));
		}
	}
}
