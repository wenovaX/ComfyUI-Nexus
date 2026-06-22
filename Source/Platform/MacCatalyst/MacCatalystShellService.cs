#if MACCATALYST
using System.Diagnostics;

namespace ComfyUI_Nexus.Platform.MacCatalyst;

public sealed class MacCatalystShellService : IPlatformShellService
{
	public Task<PlatformFeatureResult<bool>> OpenPathAsync(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return Task.FromResult(PlatformFeatureResult<bool>.Failed("Path is required."));
		}

		try
		{
			Process.Start("open", path);
			return Task.FromResult(PlatformFeatureResult<bool>.Success(true));
		}
		catch (Exception ex)
		{
			return Task.FromResult(PlatformFeatureResult<bool>.Failed(ex.Message));
		}
	}

	public Task<PlatformFeatureResult<bool>> RevealInFileManagerAsync(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return Task.FromResult(PlatformFeatureResult<bool>.Failed("Path is required."));
		}

		try
		{
			Process.Start("open", $"-R \"{path.Replace("\"", "\\\"")}\"");
			return Task.FromResult(PlatformFeatureResult<bool>.Success(true));
		}
		catch (Exception ex)
		{
			return Task.FromResult(PlatformFeatureResult<bool>.Failed(ex.Message));
		}
	}
}
#endif
