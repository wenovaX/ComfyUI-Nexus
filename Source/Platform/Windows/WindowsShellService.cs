#if WINDOWS
using System.Diagnostics;

namespace ComfyUI_Nexus.Platform.Windows;

public sealed class WindowsShellService : IPlatformShellService
{
	private int _shellLaunchInProgress;

	public Task<PlatformFeatureResult<bool>> OpenPathAsync(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return Task.FromResult(PlatformFeatureResult<bool>.Failed("Path is required."));
		}

		if (Interlocked.Exchange(ref _shellLaunchInProgress, 1) == 1)
		{
			return Task.FromResult(PlatformFeatureResult<bool>.Success(false));
		}

		return Task.Run(() =>
		{
			try
			{
				string cleanPath = path.Replace("\"", "");
				if (Directory.Exists(cleanPath))
				{
					Process.Start("explorer.exe", $"\"{cleanPath}\"");
				}
				else
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = cleanPath,
						UseShellExecute = true,
					});
				}

				return PlatformFeatureResult<bool>.Success(true);
			}
			catch (Exception ex)
			{
				return PlatformFeatureResult<bool>.Failed(ex.Message);
			}
			finally
			{
				Interlocked.Exchange(ref _shellLaunchInProgress, 0);
			}
		});
	}

	public Task<PlatformFeatureResult<bool>> RevealInFileManagerAsync(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return Task.FromResult(PlatformFeatureResult<bool>.Failed("Path is required."));
		}

		if (Interlocked.Exchange(ref _shellLaunchInProgress, 1) == 1)
		{
			return Task.FromResult(PlatformFeatureResult<bool>.Success(false));
		}

		return Task.Run(() =>
		{
			try
			{
				string cleanPath = path.Replace("\"", "");
				if (File.Exists(cleanPath))
				{
					Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{cleanPath}\"") { UseShellExecute = true });
				}
				else if (Directory.Exists(cleanPath))
				{
					Process.Start("explorer.exe", $"\"{cleanPath}\"");
				}
				else
				{
					return PlatformFeatureResult<bool>.Failed($"Path was not found: {cleanPath}");
				}

				return PlatformFeatureResult<bool>.Success(true);
			}
			catch (Exception ex)
			{
				return PlatformFeatureResult<bool>.Failed(ex.Message);
			}
			finally
			{
				Interlocked.Exchange(ref _shellLaunchInProgress, 0);
			}
		});
	}
}
#endif
