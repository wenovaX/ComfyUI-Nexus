using System.Globalization;

namespace ComfyUI_Nexus.Diagnostics;

internal static class SessionLogPaths
{
	internal const int RetainedSessionLogs = 5;
	internal const string NexusRuntimePrefix = "nexus-runtime";
	internal const string ComfyServerPrefix = "comfy-server";
	internal const string NexusLatestFileName = "nexus-latest.log";
	internal const string ComfyServerLatestFileName = "comfy-server-latest.log";

	internal static string CreateSessionLogPath(string logDirectory, string prefix, int? processId = null)
	{
		Directory.CreateDirectory(logDirectory);

		string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
		string suffix = processId.HasValue ? $"p{processId.Value}" : "pending";
		string candidate = Path.Combine(logDirectory, $"{prefix}-{timestamp}-{suffix}.log");
		if (!File.Exists(candidate))
		{
			return candidate;
		}

		for (int index = 2; index < 1000; index++)
		{
			candidate = Path.Combine(logDirectory, $"{prefix}-{timestamp}-{suffix}-{index}.log");
			if (!File.Exists(candidate))
			{
				return candidate;
			}
		}

		return Path.Combine(logDirectory, $"{prefix}-{timestamp}-{suffix}-{Guid.NewGuid():N}.log");
	}

	internal static string GetLatestLogPath(string logDirectory, string latestFileName)
	{
		Directory.CreateDirectory(logDirectory);
		return Path.Combine(logDirectory, latestFileName);
	}

	internal static void PruneOldSessionLogs(string logDirectory, string prefix, int retainedCount = RetainedSessionLogs)
	{
		try
		{
			if (!Directory.Exists(logDirectory))
			{
				return;
			}

			var files = Directory.EnumerateFiles(logDirectory, $"{prefix}-*.log")
				.Select(path => new FileInfo(path))
				.OrderByDescending(file => file.CreationTimeUtc)
				.ThenByDescending(file => file.LastWriteTimeUtc)
				.Skip(Math.Max(0, retainedCount))
				.ToList();

			foreach (FileInfo file in files)
			{
				try
				{
					file.Delete();
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

}
