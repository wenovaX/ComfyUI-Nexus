using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace ComfyUI_Nexus.Diagnostics;

internal enum NexusLogLevel
{
	Trace,
	Info,
	Warning,
	Error,
}

internal static class NexusLog
{
	private static readonly object Gate = new();
	private static Action<string>? _sink;
	private static PersistentSessionLog? _persistentLog;
	private static bool _emitTraceToSink;

	internal static void InitializePersistentLog(string logDirectory)
	{
		PersistentSessionLog? initializedLog = null;
		lock (Gate)
		{
			if (_persistentLog != null)
			{
				return;
			}

			try
			{
				_persistentLog = new PersistentSessionLog(logDirectory);
				initializedLog = _persistentLog;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Persistent session log initialization failed: {ex.GetType().Name} - {ex.Message}");
			}
		}

		string version = typeof(NexusLog).Assembly.GetName().Version?.ToString() ?? "unknown";
		Info($"[SESSION] Started {DateTimeOffset.Now:O}. pid={Environment.ProcessId}, version={version}");
		if (initializedLog != null)
		{
			Info($"[SESSION] LogFile: {initializedLog.SessionLogPath}");
			Info($"[SESSION] LatestLog: {initializedLog.LatestLogPath}");
		}
	}

	internal static string? CurrentLatestLogPath
	{
		get
		{
			lock (Gate)
			{
				return _persistentLog?.LatestLogPath;
			}
		}
	}

	internal static void FlushPersistentLog(TimeSpan timeout)
	{
		PersistentSessionLog? persistentLog;
		lock (Gate)
		{
			persistentLog = _persistentLog;
		}
		persistentLog?.Flush(timeout);
	}

	internal static void ShutdownPersistentLog()
	{
		Info($"[SESSION] CLEAN_SHUTDOWN {DateTimeOffset.Now:O}");

		PersistentSessionLog? persistentLog;
		lock (Gate)
		{
			persistentLog = _persistentLog;
			_persistentLog = null;
		}
		persistentLog?.Dispose();
	}

	internal static void SetSink(Action<string>? sink)
	{
		lock (Gate)
		{
			_sink = sink;
		}
	}

	internal static void SetTraceSinkEnabled(bool enabled)
	{
		lock (Gate)
		{
			_emitTraceToSink = enabled;
		}
	}

	internal static void Trace(
		string message,
		[CallerMemberName] string memberName = "",
		[CallerFilePath] string filePath = "")
		=> Write(NexusLogLevel.Trace, message, memberName, filePath);

	internal static void Info(
		string message,
		[CallerMemberName] string memberName = "",
		[CallerFilePath] string filePath = "")
		=> Write(NexusLogLevel.Info, message, memberName, filePath);

	internal static void Warning(
		string message,
		[CallerMemberName] string memberName = "",
		[CallerFilePath] string filePath = "")
		=> Write(NexusLogLevel.Warning, message, memberName, filePath);

	internal static void Error(
		string message,
		[CallerMemberName] string memberName = "",
		[CallerFilePath] string filePath = "")
		=> Write(NexusLogLevel.Error, message, memberName, filePath);

	internal static void Exception(
		Exception exception,
		string message = "",
		[CallerMemberName] string memberName = "",
		[CallerFilePath] string filePath = "")
	{
		string prefix = string.IsNullOrWhiteSpace(message) ? "" : $"{message}: ";
		Write(NexusLogLevel.Error, $"{prefix}{exception.GetType().Name} - {exception.Message}", memberName, filePath);
		if (!string.IsNullOrWhiteSpace(exception.StackTrace))
		{
			Write(NexusLogLevel.Trace, exception.StackTrace, memberName, filePath);
		}
	}

	private static void Write(NexusLogLevel level, string message, string memberName, string filePath)
	{
		string timestamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
		string tag = BuildTag(memberName, filePath);
		string levelText = level == NexusLogLevel.Info ? "" : $" {level.ToString().ToUpperInvariant()}";
		string fullMessage = $"[{timestamp}]{levelText} [{tag}] {message}";

		Debug.WriteLine(fullMessage);

		Action<string>? sink;
		PersistentSessionLog? persistentLog;
		lock (Gate)
		{
			sink = _sink;
			persistentLog = _persistentLog;
		}
		persistentLog?.Enqueue(fullMessage);

		if (level != NexusLogLevel.Trace || _emitTraceToSink)
		{
			sink?.Invoke(fullMessage);
		}
	}

	private static string BuildTag(string memberName, string filePath)
	{
		string fileName = Path.GetFileName(filePath);
		string typeName = fileName.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase)
			? fileName[..^".xaml.cs".Length]
			: Path.GetFileNameWithoutExtension(fileName);

		int partialSeparatorIndex = typeName.IndexOf('.');
		if (partialSeparatorIndex > 0)
		{
			typeName = typeName[..partialSeparatorIndex];
		}

		if (string.IsNullOrWhiteSpace(typeName))
		{
			typeName = "Unknown";
		}

		if (string.IsNullOrWhiteSpace(memberName))
		{
			return typeName;
		}

		return $"{typeName}.{memberName}";
	}
}
