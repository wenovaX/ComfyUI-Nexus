using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

/// <summary>
/// Builds fixed asset-browser profiles from the active ComfyUI root.
/// </summary>
internal static class AssetRootProfileProvider
{
	/// <summary>
	/// Returns the fixed Output/Input/Models/Workflows profiles that exist on disk.
	/// </summary>
	/// <param name="comfyRoot">ComfyUI root directory.</param>
	/// <param name="fixedWorkflowsPath">Configured workflows directory, if available.</param>
	internal static IReadOnlyList<AssetRootProfile> GetFixedProfiles(string comfyRoot, string fixedWorkflowsPath)
	{
		var profiles = new List<AssetRootProfile>();
		if (string.IsNullOrWhiteSpace(comfyRoot) || !Directory.Exists(comfyRoot))
		{
			return profiles;
		}

		string outputPath = Path.Combine(comfyRoot, ComfyPathOptions.OutputDirectoryName);
		if (Directory.Exists(outputPath))
		{
			profiles.Add(new AssetRootProfile(
				"output",
				"Output",
				outputPath,
				"assets_output.png",
				"#8de7ff",
				"ComfyUI output files",
				AssetInteractionMode.File,
				WatcherOptions: AssetWatcherProfiles.Output));
		}

		string inputPath = Path.Combine(comfyRoot, ComfyPathOptions.InputDirectoryName);
		if (Directory.Exists(inputPath))
		{
			profiles.Add(new AssetRootProfile(
				"input",
				"Input",
				inputPath,
				"assets_input.png",
				"#7be495",
				"ComfyUI input/import files",
				AssetInteractionMode.File,
				WatcherOptions: AssetWatcherProfiles.Input));
		}

		string modelsPath = Path.Combine(comfyRoot, ComfyPathOptions.ModelsDirectoryName);
		if (Directory.Exists(modelsPath))
		{
			profiles.Add(new AssetRootProfile(
				"models",
				"Models",
				modelsPath,
				"assets_model.png",
				"#ffcc33",
				"Model files",
				AssetInteractionMode.Model,
				TreeSource: AssetTreeSource.ModelApi,
				AllowFileDrag: true,
				AllowFolderDrag: false,
				AllowDropImport: false,
				AllowInternalMove: false,
				CopyPolicy: AssetOperationPolicy.None,
				CutPolicy: AssetOperationPolicy.None,
				PastePolicy: AssetOperationPolicy.None,
				AllowAddFolder: false,
				AllowDuplicate: false,
				RenamePolicy: AssetOperationPolicy.None,
				DeletePolicy: AssetOperationPolicy.None,
				AllowBookmarkDrop: false,
				FilterModelFilesOnly: true,
				SearchIncludesDirectories: false,
				WatcherOptions: AssetWatcherProfiles.Models));
		}

		if (!string.IsNullOrWhiteSpace(fixedWorkflowsPath) && Directory.Exists(fixedWorkflowsPath))
		{
			profiles.Add(new AssetRootProfile(
				"workflows",
				"Workflows",
				fixedWorkflowsPath,
				"assets_workflows.png",
				"#d39dff",
				"Workflow files",
				AssetInteractionMode.Workflow,
				AllowFileDrag: true,
				AllowFolderDrag: false,
				AllowDropImport: false,
				AllowInternalMove: true,
				CutPolicy: AssetOperationPolicy.None,
				PastePolicy: AssetOperationPolicy.All,
				AllowDuplicate: true,
				AllowWorkflowBookmarks: true,
				RenamePolicy: AssetOperationPolicy.All,
				DeletePolicy: AssetOperationPolicy.All,
				WatcherOptions: AssetWatcherProfiles.Workflows));
		}

		return profiles;
	}

	/// <summary>
	/// Resolves a root path to a fixed profile or falls back to a custom folder profile.
	/// </summary>
	/// <param name="path">Root path requested by the rail.</param>
	/// <param name="comfyRoot">ComfyUI root directory used to rebuild fixed profiles.</param>
	/// <param name="fixedWorkflowsPath">Configured workflows directory used to rebuild fixed profiles.</param>
	internal static AssetRootProfile ResolveForPath(string? path, string comfyRoot, string fixedWorkflowsPath)
	{
		string normalizedPath = path ?? string.Empty;
		return GetFixedProfiles(comfyRoot, fixedWorkflowsPath).FirstOrDefault(profile =>
			string.Equals(profile.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
			?? AssetRootProfile.ForCustomPath(normalizedPath);
	}

	/// <summary>
	/// Returns fixed-root paths that should not be treated as removable user bookmarks.
	/// </summary>
	/// <param name="comfyRoot">ComfyUI root directory.</param>
	/// <param name="fixedWorkflowsPath">Configured workflows directory, if available.</param>
	internal static IEnumerable<string> GetProtectedBookmarkPaths(string comfyRoot, string fixedWorkflowsPath)
	{
		foreach (AssetRootProfile profile in GetFixedProfiles(comfyRoot, fixedWorkflowsPath))
		{
			yield return profile.Path;
		}
	}
}
