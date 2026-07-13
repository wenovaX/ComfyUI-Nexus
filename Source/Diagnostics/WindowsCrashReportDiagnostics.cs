#if WINDOWS
namespace ComfyUI_Nexus.Diagnostics;

internal static class WindowsCrashReportDiagnostics
{
	private const int MaximumCrashDumpsToReport = 3;
	private const int MaximumWerReportsToReport = 3;
	private const string ProcessName = "ComfyUI-Nexus.exe";
	private const string WerReportPrefix = "AppCrash_ComfyUI-Nexus";

	internal static void ReportRecentCrashArtifacts(TimeSpan lookback)
	{
		try
		{
			DateTime since = DateTime.Now.Subtract(lookback);
			ReportRecentCrashDumps(since);
			ReportRecentWerReports(since);
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"[CRASH] Crash artifact scan failed: {ex.GetType().Name} - {ex.Message}");
		}
	}

	private static void ReportRecentCrashDumps(DateTime since)
	{
		string dumpDirectory = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"CrashDumps");

		if (!Directory.Exists(dumpDirectory))
		{
			return;
		}

		var dumpFiles = Directory.EnumerateFiles(dumpDirectory, $"{ProcessName}.*.dmp")
			.Select(path => new FileInfo(path))
			.Where(file => file.LastWriteTime >= since)
			.OrderByDescending(file => file.LastWriteTimeUtc)
			.Take(MaximumCrashDumpsToReport)
			.ToArray();

		foreach (FileInfo dumpFile in dumpFiles)
		{
			NexusLog.Warning(
				$"[CRASH] Recent crash dump: path={dumpFile.FullName}, time={dumpFile.LastWriteTime:O}, size={dumpFile.Length}");
		}
	}

	private static void ReportRecentWerReports(DateTime since)
	{
		var reportRoots = new[]
		{
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportArchive"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportQueue")
		};

		var reportDirectories = reportRoots
			.Where(Directory.Exists)
			.SelectMany(root => SafeEnumerateDirectories(root, $"{WerReportPrefix}*"))
			.Select(path => new DirectoryInfo(path))
			.Where(directory => directory.LastWriteTime >= since)
			.OrderByDescending(directory => directory.LastWriteTimeUtc)
			.Take(MaximumWerReportsToReport)
			.ToArray();

		foreach (DirectoryInfo reportDirectory in reportDirectories)
		{
			string reportPath = Path.Combine(reportDirectory.FullName, "Report.wer");
			var report = File.Exists(reportPath)
				? ReadWerReport(reportPath)
				: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			NexusLog.Warning(
				$"[CRASH] Recent WER report: path={reportDirectory.FullName}, time={reportDirectory.LastWriteTime:O}, event={GetWerValue(report, "EventType") ?? "unknown"}, code={GetWerSignatureValue(report, "P8") ?? GetWerValue(report, "ExceptionCode") ?? "unknown"}, module={GetWerSignatureValue(report, "P4") ?? "unknown"}");
		}
	}

	private static IEnumerable<string> SafeEnumerateDirectories(string root, string pattern)
	{
		try
		{
			return Directory.EnumerateDirectories(root, pattern).ToArray();
		}
		catch
		{
			return [];
		}
	}

	private static Dictionary<string, string> ReadWerReport(string reportPath)
	{
		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			foreach (string line in File.ReadLines(reportPath))
			{
				int separator = line.IndexOf('=');
				if (separator <= 0)
				{
					continue;
				}

				string key = line[..separator].Trim();
				string value = line[(separator + 1)..].Trim();
				if (key.Length > 0)
				{
					values[key] = value;
				}
			}
		}
		catch
		{
		}

		return values;
	}

	private static string? GetWerValue(IReadOnlyDictionary<string, string> report, string key)
		=> report.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
			? value
			: null;

	private static string? GetWerSignatureValue(IReadOnlyDictionary<string, string> report, string signatureName)
	{
		foreach (var pair in report)
		{
			if (!pair.Key.StartsWith("Sig[", StringComparison.OrdinalIgnoreCase) ||
				!pair.Key.EndsWith("].Name", StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(pair.Value, signatureName, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string valueKey = pair.Key[..^".Name".Length] + ".Value";
			return GetWerValue(report, valueKey);
		}

		return null;
	}
}
#endif
