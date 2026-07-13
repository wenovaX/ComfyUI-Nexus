namespace ComfyUI_Nexus.Platform;

using ComfyUI_Nexus.Configuration;
using Microsoft.Maui.ApplicationModel;

internal static class SafeAppInfo
{
	public static string DisplayName => NexusProductInfo.DisplayName;
	public static string VersionString => Read(static info => info.VersionString);
	public static string BuildString => Read(static info => info.BuildString);
	public static string PackageName => Read(static info => info.PackageName);
	public static string Name => Read(static info => info.Name);

	private static string Read(Func<IAppInfo, string?> selector)
	{
		try
		{
#if WINDOWS
			if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
			{
				return string.Empty;
			}
#endif
			return selector(AppInfo.Current) ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}
}
