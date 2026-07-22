namespace ComfyUI_Nexus.Diagnostics;

#if DEBUG
using System.Runtime.ExceptionServices;

/// <summary>
/// Owns debug-only binding diagnostics for the current app process.
/// </summary>
internal sealed class NexusBindingDiagnosticsService : IDisposable
{
	private readonly HashSet<string> _reportedBindings = new(StringComparer.Ordinal);
	private bool _isAttached;

	internal void Attach()
	{
		if (_isAttached)
		{
			return;
		}

		_isAttached = true;
		AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
		NexusLog.Info("[BINDING] Debug conversion diagnostics attached.");
	}

	public void Dispose()
	{
		if (!_isAttached)
		{
			return;
		}

		_isAttached = false;
		AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
		_reportedBindings.Clear();
	}

	private void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
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
		if (!_reportedBindings.Add(key))
		{
			return;
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
internal sealed class NexusBindingDiagnosticsService : IDisposable
{
	internal void Attach()
	{
	}

	public void Dispose()
	{
	}
}
#endif
