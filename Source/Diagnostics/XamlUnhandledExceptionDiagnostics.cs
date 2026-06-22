#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace ComfyUI_Nexus.Diagnostics;

internal static class XamlUnhandledExceptionDiagnostics
{
	private static readonly AsyncLocal<string?> CurrentUiOperation = new();

	internal static IDisposable EnterUiOperation(string operation)
	{
		string? previousOperation = CurrentUiOperation.Value;
		CurrentUiOperation.Value = operation;
		return new UiOperationScope(previousOperation);
	}

#if WINDOWS
	internal static void Handle(string source, object? sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
	{
		string senderName = sender?.GetType().FullName ?? "null";
		Exception? exception = e.Exception;
		string operation = CurrentUiOperation.Value ?? "unknown";
		NexusLog.Error($"[{source}:UNHANDLED] sender={senderName}, operation={operation}, handled={e.Handled}, message={e.Message}");

		if (exception == null)
		{
			e.Handled = true;
			NexusLog.Warning($"[{source}:UNHANDLED] Missing exception object was handled to keep the shell alive.");
			return;
		}

		LogExceptionTree(source, exception);

		if (ShouldKeepShellAlive(exception))
		{
			e.Handled = true;
			NexusLog.Warning($"[{source}:UNHANDLED] UI exception was handled to keep the shell alive.");
		}
	}

	private static void LogExceptionTree(string source, Exception exception)
	{
		for (Exception? current = exception; current != null; current = current.InnerException)
		{
			NexusLog.Exception(current, $"[{source}:UNHANDLED] {current.GetType().Name}");
		}
	}

	private static bool ShouldKeepShellAlive(Exception exception)
	{
		if (exception is OutOfMemoryException)
		{
			return false;
		}

		return exception is OperationCanceledException
			or TaskCanceledException
			or COMException
			or ArgumentException
			or InvalidOperationException
			or ObjectDisposedException
			or IOException;
	}
#endif

	private sealed class UiOperationScope : IDisposable
	{
		private readonly string? _previousOperation;
		private bool _disposed;

		internal UiOperationScope(string? previousOperation)
		{
			_previousOperation = previousOperation;
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			CurrentUiOperation.Value = _previousOperation;
			_disposed = true;
		}
	}
}
