namespace ComfyUI_Nexus.Setup.Runtime;

using System.Diagnostics;
using ComfyUI_Nexus.Setup.Services;

internal static class StaleRuntimeProcessCleaner
{
	private static readonly HashSet<string> TargetProcessNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"python",
		"pythonw",
		"git"
	};

	internal static Task<int> CleanupBeforeBootAsync(
		NexusComfyRuntimePaths paths,
		Action<string>? log,
		CancellationToken cancellationToken)
		=> Task.Run(() => CleanupBeforeBoot(paths, log, cancellationToken), cancellationToken);

	private static int CleanupBeforeBoot(NexusComfyRuntimePaths paths, Action<string>? log, CancellationToken cancellationToken)
	{
#if !WINDOWS
		log?.Invoke("[BOOT] Runtime process cleanup is only available on Windows.");
		return 0;
#else
		var roots = GetRuntimeRoots(paths);
		if (roots.Count == 0)
		{
			return 0;
		}

		int killed = 0;
		int currentProcessId = Environment.ProcessId;
		foreach (Process process in Process.GetProcesses())
		{
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				if (process.Id == currentProcessId || !TargetProcessNames.Contains(process.ProcessName))
				{
					continue;
				}

				string? executablePath = process.MainModule?.FileName;
				if (string.IsNullOrWhiteSpace(executablePath) || !IsUnderAnyRoot(executablePath, roots))
				{
					continue;
				}

				log?.Invoke($"[BOOT] Terminating stale runtime process before boot: {process.ProcessName} (PID: {process.Id})");
				process.Kill(entireProcessTree: true);
				process.WaitForExit(2000);
				killed++;
			}
			catch
			{
			}
			finally
			{
				process.Dispose();
			}
		}

		if (killed > 0)
		{
			log?.Invoke($"[BOOT] Runtime process cleanup completed. Terminated {killed} stale process(es).");
		}

		return killed;
#endif
	}

	private static List<string> GetRuntimeRoots(NexusComfyRuntimePaths paths)
	{
		var roots = new List<string>();
		AddRoot(roots, ComfyInstallService.LocalRuntimePath);
		AddRoot(roots, ComfyInstallService.InstalledPath);
		AddRoot(roots, ComfyInstallService.PythonPath);
		AddRoot(roots, paths.ActiveVenvPath);
		return roots;
	}

	private static void AddRoot(List<string> roots, string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		try
		{
			string fullPath = Path.GetFullPath(path)
				.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			if (Directory.Exists(fullPath)
				&& !roots.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
			{
				roots.Add(fullPath);
			}
		}
		catch
		{
		}
	}

	private static bool IsUnderAnyRoot(string executablePath, IReadOnlyList<string> roots)
	{
		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(executablePath);
		}
		catch
		{
			return false;
		}

		foreach (string root in roots)
		{
			if (fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}
