namespace ComfyUI_Nexus.Setup.Services;

using System.Diagnostics;
using ComfyUI_Nexus.Setup.Models;

internal sealed class RuntimePurgeService
{
	private const string RuntimeTag = "[Runtime]";

	private readonly Action<string> _log;

	internal RuntimePurgeService(Action<string> log)
	{
		_log = log;
	}

	internal async Task<SetupStepResult> PurgeRuntimeAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		try
		{
			SetupSettingsService.Instance.MarkRuntimePurgeInProgress();

			bool hasInstalledRuntime = Directory.Exists(ComfyInstallService.InstalledPath);
			bool hasPipCache = Directory.Exists(PipCacheService.GetDefaultCachePath());
			if (!hasInstalledRuntime && !hasPipCache)
			{
				_log($"{RuntimeTag} Installed runtime and Nexus pip cache do not exist. Nothing to purge.");
				SetupSettingsService.Instance.CompleteRuntimePurgeAndResetSetup();
				return new SetupStepResult(true, "Runtime is already clean.", 1);
			}

			_log($"{RuntimeTag} Terminating active processes in runtime...");
			await KillProcessesInRuntimeAsync();

			if (hasInstalledRuntime)
			{
				_log($"{RuntimeTag} Purging local runtime (Installed folder)...");

				bool success = await RobustDeleteDirectoryAsync(
					ComfyInstallService.InstalledPath,
					Math.Max(1, SetupSettingsService.Instance.Settings.PurgeRetryCount),
					cancellationToken);

				if (!success)
				{
					throw new InvalidOperationException("Failed to delete the directory after multiple retries. Some files may be locked by another process.");
				}

				_log($"{RuntimeTag} Cleanly deleted all installed binaries.");
			}

			if (hasPipCache)
			{
				_log($"{RuntimeTag} Clearing Nexus pip cache...");
				await Task.Run(PipCacheService.ClearDefaultCache, cancellationToken);
				_log($"{RuntimeTag} Nexus pip cache cleared.");
			}

			SetupSettingsService.Instance.CompleteRuntimePurgeAndResetSetup();
			return new SetupStepResult(true, "Local runtime and Nexus pip cache cleared successfully.", 1);
		}
		catch (Exception ex)
		{
			_log($"{RuntimeTag} [ERROR] Failed to purge runtime: {ex.Message}");
			return new SetupStepResult(false, $"Purge failed: {ex.Message}. Ensure ComfyUI and Python processes are closed.", 0);
		}
	}

	private async Task KillProcessesInRuntimeAsync()
	{
#if !WINDOWS
		_log($"{RuntimeTag} Process termination is only available on Windows. Skipping process cleanup.");
		await Task.CompletedTask;
#else
		await Task.Run(() =>
		{
			string targetPrefix = ComfyInstallService.InstalledPath.ToLowerInvariant();
			foreach (var process in Process.GetProcesses())
			{
				try
				{
					string name = process.ProcessName.ToLowerInvariant();
					if (name is not ("python" or "git" or "cmd" or "conhost")) continue;

					string? fileName = process.MainModule?.FileName?.ToLowerInvariant();
					if (fileName != null && fileName.StartsWith(targetPrefix, StringComparison.Ordinal))
					{
						_log($"{RuntimeTag} Terminating process: {process.ProcessName} (PID: {process.Id})");
						process.Kill(entireProcessTree: true);
						process.WaitForExit(2000);
					}
				}
				catch
				{
				}
				finally
				{
					process.Dispose();
				}
			}
		});
#endif
	}

	private static async Task<bool> RobustDeleteDirectoryAsync(string path, int maxRetries, CancellationToken cancellationToken)
	{
		for (int i = 0; i < maxRetries; i++)
		{
			try
			{
				return await Task.Run(() =>
				{
					ClearReadOnlyAttributes(new DirectoryInfo(path));
					Directory.Delete(path, recursive: true);
					return true;
				}, cancellationToken);
			}
			catch (IOException) when (i < maxRetries - 1)
			{
				await Task.Delay(GetPurgeRetryDelay(), cancellationToken);
			}
			catch (UnauthorizedAccessException) when (i < maxRetries - 1)
			{
				await Task.Delay(GetPurgeRetryDelay(), cancellationToken);
			}
		}

		return false;
	}

	private static TimeSpan GetPurgeRetryDelay()
		=> TimeSpan.FromMilliseconds(Math.Max(50, SetupSettingsService.Instance.Settings.PurgeRetryDelayMilliseconds));

	private static void ClearReadOnlyAttributes(DirectoryInfo directory)
	{
		if (!directory.Exists) return;

		foreach (var file in directory.GetFiles())
		{
			file.Attributes = FileAttributes.Normal;
		}

		foreach (var subDir in directory.GetDirectories())
		{
			ClearReadOnlyAttributes(subDir);
		}
	}

}
