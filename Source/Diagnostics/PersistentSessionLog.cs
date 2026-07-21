using System.Text;
using System.Threading.Channels;

namespace ComfyUI_Nexus.Diagnostics;

internal sealed class PersistentSessionLog : IDisposable
{
	private const int MaximumQueuedLines = 4096;
	private readonly Channel<string> _lines = Channel.CreateBounded<string>(new BoundedChannelOptions(MaximumQueuedLines)
	{
		FullMode = BoundedChannelFullMode.DropWrite,
		SingleReader = true,
		SingleWriter = false,
	});
	private readonly string _sessionLogPath;
	private readonly string _latestLogPath;
	private readonly Thread _writerThread;
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
		_writerThread = new Thread(WriteLoop)
		{
			IsBackground = true,
			Name = "Nexus.PersistentSessionLog",
		};
		_writerThread.Start();
	}

	internal string SessionLogPath => _sessionLogPath;

	internal string LatestLogPath => _latestLogPath;

	internal void Enqueue(string line)
	{
		if (Volatile.Read(ref _isDisposed) != 0)
		{
			return;
		}

		try
		{
			_ = _lines.Writer.TryWrite(line);
		}
		catch (ChannelClosedException)
		{
		}
	}

	internal void Flush(TimeSpan timeout)
	{
		_ = timeout;
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
		{
			return;
		}

		_lines.Writer.TryComplete();
	}

	private void WriteLoop()
	{
		StreamWriter? sessionWriter = null;
		StreamWriter? latestWriter = null;
		try
		{
			sessionWriter = OpenWriter(_sessionLogPath, append: true);
			latestWriter = OpenWriter(_latestLogPath, append: true);
			while (_lines.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
			{
				while (_lines.Reader.TryRead(out string? line))
				{
					sessionWriter.WriteLine(line);
					latestWriter.WriteLine(line);
				}
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
