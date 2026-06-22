using System.Collections.Concurrent;
using System.Text;

namespace ComfyUI_Nexus.Diagnostics;

internal sealed class PersistentSessionLog : IDisposable
{
	private const int MaximumQueuedLines = 4096;
	private readonly BlockingCollection<string> _lines = new(
		new ConcurrentQueue<string>(),
		MaximumQueuedLines);
	private readonly string _sessionLogPath;
	private readonly string _latestLogPath;
	private readonly Task _writerTask;
	private int _isDisposed;

	internal PersistentSessionLog(string logDirectory)
	{
		Directory.CreateDirectory(logDirectory);
		_sessionLogPath = SessionLogPaths.CreateSessionLogPath(
			logDirectory,
			SessionLogPaths.NexusRuntimePrefix,
			Environment.ProcessId);
		_latestLogPath = SessionLogPaths.GetLatestLogPath(logDirectory, SessionLogPaths.NexusLatestFileName);
		File.WriteAllText(_latestLogPath, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		SessionLogPaths.PruneOldSessionLogs(logDirectory, SessionLogPaths.NexusRuntimePrefix);
		_writerTask = Task.Factory.StartNew(
			WriteLoop,
			CancellationToken.None,
			TaskCreationOptions.LongRunning,
			TaskScheduler.Default);
	}

	internal string SessionLogPath => _sessionLogPath;

	internal string LatestLogPath => _latestLogPath;

	internal void Enqueue(string line)
	{
		if (Volatile.Read(ref _isDisposed) != 0 || _lines.IsAddingCompleted)
		{
			return;
		}

		try
		{
			_lines.TryAdd(line);
		}
		catch (InvalidOperationException)
		{
		}
	}

	internal void Flush(TimeSpan timeout)
	{
		DateTime deadline = DateTime.UtcNow + timeout;
		while (_lines.Count > 0 && DateTime.UtcNow < deadline)
		{
			Thread.Sleep(10);
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
		{
			return;
		}

		_lines.CompleteAdding();
		try
		{
			_writerTask.Wait(TimeSpan.FromSeconds(1));
		}
		catch
		{
		}
		_lines.Dispose();
	}

	private void WriteLoop()
	{
		StreamWriter? sessionWriter = null;
		StreamWriter? latestWriter = null;
		try
		{
			sessionWriter = OpenWriter(_sessionLogPath, append: true);
			latestWriter = OpenWriter(_latestLogPath, append: true);
			foreach (string line in _lines.GetConsumingEnumerable())
			{
				sessionWriter.WriteLine(line);
				latestWriter.WriteLine(line);
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Persistent session log stopped: {ex.GetType().Name} - {ex.Message}");
		}
		finally
		{
			sessionWriter?.Dispose();
			latestWriter?.Dispose();
		}
	}

	private static StreamWriter OpenWriter(string path, bool append)
	{
		var stream = new FileStream(
			path,
			append ? FileMode.Append : FileMode.Create,
			FileAccess.Write,
			FileShare.ReadWrite | FileShare.Delete);
		return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
		{
			AutoFlush = true
		};
	}
}
