namespace ComfyUI_Nexus.Setup.Services;

using ComfyUI_Nexus.Configuration;

/// <summary>
/// Resolves the live ComfyUI runtime paths for the current app runtime.
/// The resolver deliberately does not cache results because setup settings can
/// switch between the managed runtime and an external ComfyUI installation.
/// </summary>
internal sealed class NexusComfyRuntimePaths
{
	private readonly SetupSettingsService _settingsService;
	private readonly NexusPreferenceStore _preferences;

	internal NexusComfyRuntimePaths(SetupSettingsService settingsService, NexusPreferenceStore preferences)
	{
		_settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
		_preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
	}

	internal string ManagedComfyPath => Path.Combine(NexusStorageLayout.LocalRuntimeRoot, "Installed", "ComfyUI");

	internal string ConfiguredComfyPath
	{
		get
		{
			string configuredPath = _settingsService.Settings.ComfyPath;
			return string.IsNullOrWhiteSpace(configuredPath) ? ManagedComfyPath : configuredPath;
		}
	}

	internal string ActiveComfyPath
	{
		get
		{
			string preferredPath = _preferences.Get(PreferenceKeys.ComfyUIPath, string.Empty);
			if (!string.IsNullOrWhiteSpace(preferredPath) && Directory.Exists(preferredPath))
			{
				return preferredPath;
			}

			string configuredPath = ConfiguredComfyPath;
			return Directory.Exists(configuredPath) ? configuredPath : string.Empty;
		}
	}

	internal string ActiveModelsRootPath => CombineWithActivePath("models");
	internal string ActiveCustomNodesPath => CombineWithActivePath("custom_nodes");
	internal string ActiveVenvPath => CombineWithActivePath(".venv");
	internal string ActiveVenvPythonExe => string.IsNullOrWhiteSpace(ActiveVenvPath)
		? string.Empty
		: Path.Combine(ActiveVenvPath, "Scripts", "python.exe");

	internal string ActiveWorkflowsPath
	{
		get
		{
			string comfyPath = ActiveComfyPath;
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
	}

	private string CombineWithActivePath(string relativePath)
	{
		string comfyPath = ActiveComfyPath;
		return string.IsNullOrWhiteSpace(comfyPath) ? string.Empty : Path.Combine(comfyPath, relativePath);
	}
}
