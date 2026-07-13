namespace ComfyUI_Nexus.Setup.Services;

using ComfyUI_Nexus.Configuration;

internal static class ComfyPathResolver
{
	internal static string ResolveActiveComfyPath()
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

	internal static string ResolveManagedComfyPath()
		=> ComfyInstallService.DefaultComfyPath;

	internal static string ResolveConfiguredComfyPath()
		=> ResolveActiveComfyPath();

	internal static string ResolveActiveModelsRootPath()
	{
		string comfyPath = ResolveActiveComfyPath();
		return string.IsNullOrWhiteSpace(comfyPath)
			? string.Empty
			: Path.Combine(comfyPath, "models");
	}

	internal static string ResolveActiveCustomNodesPath()
	{
		string comfyPath = ResolveActiveComfyPath();
		return string.IsNullOrWhiteSpace(comfyPath)
			? string.Empty
			: Path.Combine(comfyPath, "custom_nodes");
	}

	internal static string ResolveActiveWorkflowsPath()
	{
		string comfyPath = ResolveActiveComfyPath();
		if (string.IsNullOrWhiteSpace(comfyPath))
		{
			return string.Empty;
		}

		string userRoot = Path.Combine(comfyPath, "user");
		string defaultWorkflowPath = Path.Combine(userRoot, "default", "workflows");
		if (Directory.Exists(defaultWorkflowPath) || !Directory.Exists(userRoot))
		{
			return defaultWorkflowPath;
		}

		string? userFolder = Directory
			.EnumerateDirectories(userRoot)
			.Select(Path.GetFileName)
			.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
		return string.IsNullOrWhiteSpace(userFolder)
			? defaultWorkflowPath
			: Path.Combine(userRoot, userFolder, "workflows");
	}

	internal static string ResolveActiveVenvPath()
	{
		string comfyPath = ResolveActiveComfyPath();
		return string.IsNullOrWhiteSpace(comfyPath)
			? string.Empty
			: Path.Combine(comfyPath, ".venv");
	}

	internal static string ResolveActiveVenvPythonExe()
	{
		string venvPath = ResolveActiveVenvPath();
		return string.IsNullOrWhiteSpace(venvPath)
			? string.Empty
			: Path.Combine(venvPath, "Scripts", "python.exe");
	}

	internal static string ResolveModelsRootPath()
		=> ResolveActiveModelsRootPath();
}
