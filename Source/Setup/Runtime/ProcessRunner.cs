namespace ComfyUI_Nexus.Setup.Runtime;

using System.Diagnostics;
using System.Text;
using System.Threading;

internal sealed class ProcessRunner
{
	internal static Process StartWithFileLog(
		string fileName,
		string arguments,
		string logFilePath,
		Action<string> onLog,
		TimeSpan tailPollingDelay,
		out IDisposable logTail,
		string? workingDirectory = null,
		IReadOnlyDictionary<string, string>? environmentVariables = null,
		string? latestLogFilePath = null)
	{
#if !WINDOWS
		logTail = EmptyDisposable.Instance;
		throw new PlatformNotSupportedException("Starting external processes is only supported on Windows.");
#else
		string? logDirectory = Path.GetDirectoryName(logFilePath);
		if (!string.IsNullOrWhiteSpace(logDirectory))
		{
			Directory.CreateDirectory(logDirectory);
		}

		AppendShared(
			logFilePath,
			$"{Environment.NewLine}[Nexus] Server log attached at {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}{Environment.NewLine}");
		if (!string.IsNullOrWhiteSpace(latestLogFilePath))
		{
			string? latestDirectory = Path.GetDirectoryName(latestLogFilePath);
			if (!string.IsNullOrWhiteSpace(latestDirectory))
			{
				Directory.CreateDirectory(latestDirectory);
			}

			File.WriteAllText(latestLogFilePath, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			AppendShared(
				latestLogFilePath,
				$"{Environment.NewLine}[Nexus] Server log attached at {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}{Environment.NewLine}");
		}

		long tailStartPosition = new FileInfo(logFilePath).Length;
		var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = "cmd.exe",
				Arguments = BuildRedirectedCommand(fileName, arguments, logFilePath),
				WorkingDirectory = workingDirectory ?? string.Empty,
				RedirectStandardOutput = false,
				RedirectStandardError = false,
				UseShellExecute = false,
				CreateNoWindow = true,
			},
			EnableRaisingEvents = true
		};

		if (environmentVariables != null)
		{
			foreach (var (key, value) in environmentVariables)
			{
				process.StartInfo.Environment[key] = value;
			}
		}

		process.Start();
		logTail = StartLogTail(logFilePath, tailStartPosition, process, onLog, tailPollingDelay, latestLogFilePath);

		return process;
#endif
	}

	internal static IDisposable AttachLogTail(
		string logFilePath,
		Process process,
		Action<string> onLog,
		TimeSpan tailPollingDelay,
		string? latestLogFilePath = null)
	{
		string? logDirectory = Path.GetDirectoryName(logFilePath);
		if (!string.IsNullOrWhiteSpace(logDirectory))
		{
			Directory.CreateDirectory(logDirectory);
		}

		long tailStartPosition = File.Exists(logFilePath)
			? new FileInfo(logFilePath).Length
			: 0;
		return StartLogTail(logFilePath, tailStartPosition, process, onLog, tailPollingDelay, latestLogFilePath);
	}

	internal static async Task WaitForServerLogAppendAccessAsync(
		string filePath,
		TimeSpan timeout,
		TimeSpan retryDelay,
		CancellationToken cancellationToken = default)
	{
		DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
		Exception? lastError = null;

		while (DateTimeOffset.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				string? directory = Path.GetDirectoryName(filePath);
				if (!string.IsNullOrWhiteSpace(directory))
				{
					Directory.CreateDirectory(directory);
				}

				using var stream = new FileStream(
					filePath,
					FileMode.OpenOrCreate,
					FileAccess.Write,
					FileShare.ReadWrite | FileShare.Delete);
				stream.Seek(0, SeekOrigin.End);
				return;
			}
			catch (IOException ex)
			{
				lastError = ex;
			}
			catch (UnauthorizedAccessException ex)
			{
				lastError = ex;
			}

			await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
		}

		throw new IOException(
			$"Server log is still locked by a previous process or log tail: {filePath}",
			lastError);
	}

	internal static async Task<(int ExitCode, string Output, string Error)> RunAsync(
		string fileName,
		string arguments,
		string? workingDirectory = null,
		Action<string>? onLog = null,
		CancellationToken cancellationToken = default,
		TimeSpan? idleTimeout = null,
		IReadOnlyDictionary<string, string>? environmentVariables = null)
	{
#if !WINDOWS
		await Task.CompletedTask;
		string message = "External process execution is only supported on Windows.";
		onLog?.Invoke(message);
		return (-1, string.Empty, message);
#else
		var output = new StringBuilder();
		var error = new StringBuilder();
		DateTimeOffset lastOutputAt = DateTimeOffset.UtcNow;
		string? idleTimeoutMessage = null;

		using var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = fileName,
				Arguments = arguments,
				WorkingDirectory = workingDirectory ?? string.Empty,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			}
		};

		if (environmentVariables is not null)
		{
			foreach (var (key, value) in environmentVariables)
			{
				if (string.IsNullOrWhiteSpace(key))
				{
					continue;
				}

				process.StartInfo.Environment[key] = value;
			}
		}

		process.OutputDataReceived += (s, e) =>
		{
			if (e.Data == null) return;
			lastOutputAt = DateTimeOffset.UtcNow;
			output.AppendLine(e.Data);
			onLog?.Invoke(e.Data);
		};
		process.ErrorDataReceived += (s, e) =>
		{
			if (e.Data == null) return;
			lastOutputAt = DateTimeOffset.UtcNow;
			error.AppendLine(e.Data);
			onLog?.Invoke(e.Data);
		};

		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		Task? idleMonitor = idleTimeout.HasValue
			? MonitorIdleTimeoutAsync(process, idleTimeout.Value, () => lastOutputAt, message => idleTimeoutMessage = message, cancellationToken)
			: null;

		try
		{
			await process.WaitForExitAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			TryKillProcessTree(process);
			throw;
		}

		if (idleMonitor != null)
		{
			await idleMonitor;
		}

		if (!string.IsNullOrWhiteSpace(idleTimeoutMessage))
		{
			error.AppendLine(idleTimeoutMessage);
			onLog?.Invoke(idleTimeoutMessage);
		}

		return (TryGetExitCode(process), output.ToString(), error.ToString());
#endif
	}

	private static string BuildRedirectedCommand(string fileName, string arguments, string logFilePath)
	{
		string command = $"{QuoteForCmd(fileName)} {arguments} >> {QuoteForCmd(logFilePath)} 2>&1";
		return $"/d /s /c \"{command}\"";
	}

	private static void AppendShared(string filePath, string text)
	{
		using var stream = new FileStream(
			filePath,
			FileMode.OpenOrCreate,
			FileAccess.Write,
			FileShare.ReadWrite | FileShare.Delete);
		stream.Seek(0, SeekOrigin.End);
		using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		writer.Write(text);
	}

	private static string QuoteForCmd(string value)
		=> $"\"{value.Replace("\"", "\\\"")}\"";

	private static IDisposable StartLogTail(
		string logFilePath,
		long startPosition,
		Process process,
		Action<string> onLog,
		TimeSpan pollingDelay,
		string? latestLogFilePath)
	{
		var cancellation = new CancellationTokenSource();
		Task tailTask = TailLogFileAsync(logFilePath, startPosition, process, onLog, pollingDelay, latestLogFilePath, cancellation.Token);
		return new CancellationDisposable(cancellation, tailTask);
	}

	private static async Task TailLogFileAsync(
		string logFilePath,
		long startPosition,
		Process process,
		Action<string> onLog,
		TimeSpan pollingDelay,
		string? latestLogFilePath,
		CancellationToken cancellationToken)
	{
		StreamWriter? latestWriter = null;
		try
		{
			if (!string.IsNullOrWhiteSpace(latestLogFilePath))
			{
				latestWriter = OpenSharedAppendWriter(latestLogFilePath);
			}

			using var stream = new FileStream(
				logFilePath,
				FileMode.OpenOrCreate,
				FileAccess.Read,
				FileShare.ReadWrite | FileShare.Delete);
			stream.Seek(startPosition, SeekOrigin.Begin);
			using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

			while (IsProcessRunning(process) && !cancellationToken.IsCancellationRequested)
			{
				string? line = await reader.ReadLineAsync(cancellationToken);
				if (line != null)
				{
					WriteLatestLine(latestWriter, line);
					onLog(line);
					continue;
				}

				await Task.Delay(pollingDelay, cancellationToken);
			}

			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}

			while (await reader.ReadLineAsync(cancellationToken) is { } line)
			{
				WriteLatestLine(latestWriter, line);
				onLog(line);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			if (ComfyServerProcessRegistry.IsShuttingDown(process))
			{
				return;
			}

			onLog($"[ProcessRunner] Log tail stopped: {ex.Message}");
		}
		finally
		{
			latestWriter?.Dispose();
		}
	}

	private static StreamWriter OpenSharedAppendWriter(string filePath)
	{
		string? directory = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}

		var stream = new FileStream(
			filePath,
			FileMode.Append,
			FileAccess.Write,
			FileShare.ReadWrite | FileShare.Delete);
		return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
	}

	private static void WriteLatestLine(StreamWriter? latestWriter, string line)
	{
		try
		{
			latestWriter?.WriteLine(line);
		}
		catch
		{
		}
	}

	private static async Task MonitorIdleTimeoutAsync(
		Process process,
		TimeSpan idleTimeout,
		Func<DateTimeOffset> getLastOutputAt,
		Action<string> onTimeout,
		CancellationToken cancellationToken)
	{
		try
		{
			while (IsProcessRunning(process) && !cancellationToken.IsCancellationRequested)
			{
				if (DateTimeOffset.UtcNow - getLastOutputAt() > idleTimeout)
				{
					string message = $"Process produced no output for {idleTimeout.TotalMinutes:0.#} minute(s).";
					onTimeout(message);
					TryKillProcessTree(process);
					return;
				}

				await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private static bool IsProcessRunning(Process process)
	{
		try
		{
			return !process.HasExited;
		}
		catch
		{
			return false;
		}
	}

	private static int TryGetExitCode(Process process)
	{
		try
		{
			return process.ExitCode;
		}
		catch
		{
			return -1;
		}
	}

	private static void TryKillProcessTree(Process process)
	{
#if WINDOWS
		try
		{
			if (!IsProcessRunning(process)) return;

			process.Kill(entireProcessTree: true);
		}
		catch
		{
		}
#endif
	}

	private sealed class CancellationDisposable : IDisposable
	{
		private readonly CancellationTokenSource _cancellation;
		private readonly Task _task;
		private bool _isDisposed;

		internal CancellationDisposable(CancellationTokenSource cancellation, Task task)
		{
			_cancellation = cancellation;
			_task = task;
		}

		public void Dispose()
		{
			if (_isDisposed) return;

			_isDisposed = true;
			_cancellation.Cancel();
			try
			{
				_task.Wait(TimeSpan.FromSeconds(1));
			}
			catch
			{
			}
			_cancellation.Dispose();
		}
	}

	private sealed class EmptyDisposable : IDisposable
	{
		internal static readonly EmptyDisposable Instance = new();

		public void Dispose()
		{
		}
	}
}
