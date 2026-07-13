namespace ComfyUI_Nexus.Diagnostics;

#if DEBUG
using System.Runtime.ExceptionServices;

internal static class NexusBindingDiagnostics
{
	private static readonly HashSet<string> ReportedBindings = new(StringComparer.Ordinal);
	private static readonly object ReportedBindingsLock = new();
	private static int _isAttached;

	internal static void Attach()
	{
		if (Interlocked.Exchange(ref _isAttached, 1) == 1)
		{
			return;
		}

		AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
		NexusLog.Info("[BINDING] Debug conversion diagnostics attached.");
	}

	private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
	{
		if (e.Exception is not (InvalidCastException or ArgumentException))
		{
			return;
		}

		string? stackTrace = e.Exception.StackTrace;
		if (string.IsNullOrWhiteSpace(stackTrace)
			|| (!stackTrace.Contains("Microsoft.Maui.Controls.BindingExpression", StringComparison.Ordinal)
				&& !stackTrace.Contains("Microsoft.Maui.Controls.BindingExpressionHelper", StringComparison.Ordinal)))
		{
			return;
		}

		string origin = GetBindingOrigin(stackTrace);
		string key = $"{e.Exception.GetType().FullName}:{origin}";
		lock (ReportedBindingsLock)
		{
			if (!ReportedBindings.Add(key))
			{
				return;
			}
		}

		NexusLog.Warning($"[BINDING:CONVERSION] {e.Exception.GetType().Name}: {e.Exception.Message} Origin={origin}");
	}

	private static string GetBindingOrigin(string stackTrace)
	{
		foreach (string line in stackTrace.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (!line.Contains("Microsoft.Maui", StringComparison.Ordinal))
			{
				return line;
			}
		}

		return "Microsoft.Maui binding pipeline";
	}
}
#else
internal static class NexusBindingDiagnostics
{
	internal static void Attach()
	{
	}
}
#endif
