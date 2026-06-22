namespace ComfyUI_Nexus.Setup.Services;

using ComfyUI_Nexus.Configuration;

internal static class ComfyPathResolver
{
	internal static string ResolveConfiguredComfyPath()
	{
		string comfyPath = PortablePreferences.Get(PreferenceKeys.ComfyUIPath, string.Empty);
		if (!string.IsNullOrWhiteSpace(comfyPath) && Directory.Exists(comfyPath))
		{
			return comfyPath;
		}

		comfyPath = ComfyInstallService.ComfyPath;
		return !string.IsNullOrWhiteSpace(comfyPath) && Directory.Exists(comfyPath)
			? comfyPath
			: string.Empty;
	}

	internal static string ResolveModelsRootPath()
	{
		string comfyPath = ResolveConfiguredComfyPath();
		return string.IsNullOrWhiteSpace(comfyPath)
			? string.Empty
			: Path.Combine(comfyPath, "models");
	}
}
