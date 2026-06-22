using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

/// <summary>
/// Fixed or custom asset-browser root configuration.
/// </summary>
/// <param name="Id">Stable profile id used for cache keys and tab identity.</param>
/// <param name="Label">Display label shown in the rail UI.</param>
/// <param name="Path">Absolute root directory represented by this profile.</param>
/// <param name="IconSource">Image resource used by fixed cards and tab UI.</param>
/// <param name="AccentColor">Hex color used to theme the profile.</param>
/// <param name="Subtitle">Short descriptive text shown under the profile label.</param>
/// <param name="Mode">Interaction mode sent to the web bridge for files from this root.</param>
/// <param name="TreeSource">Where tree entries come from: filesystem or ComfyUI model API.</param>
/// <param name="AllowFileDrag">Whether file rows can start native-to-web drag/open gestures.</param>
/// <param name="AllowFolderDrag">Whether folder rows can start drag gestures.</param>
/// <param name="AllowDropImport">Whether external OS file drops may import into this root.</param>
/// <param name="AllowInternalMove">Whether in-tree drag/drop may move files inside this root.</param>
/// <param name="CopyPolicy">Selection policy for copy commands.</param>
/// <param name="CutPolicy">Selection policy for cut commands.</param>
/// <param name="PastePolicy">Selection policy for paste commands.</param>
/// <param name="AllowAddFolder">Whether folder creation is allowed in this root.</param>
/// <param name="AllowDuplicate">Whether file duplication is enabled in this root.</param>
/// <param name="AllowWorkflowBookmarks">Whether workflow files may be added to the workflow bookmark index.</param>
/// <param name="RenamePolicy">Selection policy for rename commands.</param>
/// <param name="DeletePolicy">Selection policy for delete commands.</param>
/// <param name="AllowBookmarkDrop">Whether bookmark drop targets are enabled for this root.</param>
/// <param name="FilterModelFilesOnly">Whether search/tree file results should be limited to model-like files.</param>
/// <param name="SearchRecursive">Whether searches should recurse below the current directory.</param>
/// <param name="SearchIncludesDirectories">Whether directory rows are allowed in search results.</param>
/// <param name="WatcherOptions">Optional watcher tuning for this root.</param>
internal sealed record AssetRootProfile(
	string Id,
	string Label,
	string Path,
	string IconSource,
	string AccentColor,
	string Subtitle,
	AssetInteractionMode Mode,
	AssetTreeSource TreeSource = AssetTreeSource.FileSystem,
	bool AllowFileDrag = true,
	bool AllowFolderDrag = false,
	bool AllowDropImport = true,
	bool AllowInternalMove = true,
	AssetOperationPolicy CopyPolicy = AssetOperationPolicy.All,
	AssetOperationPolicy CutPolicy = AssetOperationPolicy.All,
	AssetOperationPolicy PastePolicy = AssetOperationPolicy.All,
	bool AllowAddFolder = true,
	bool AllowDuplicate = false,
	bool AllowWorkflowBookmarks = false,
	AssetOperationPolicy RenamePolicy = AssetOperationPolicy.All,
	AssetOperationPolicy DeletePolicy = AssetOperationPolicy.All,
	bool AllowBookmarkDrop = true,
	bool FilterModelFilesOnly = false,
	bool SearchRecursive = true,
	bool SearchIncludesDirectories = true,
	DirectoryWatcherOptions? WatcherOptions = null)
{
	/// <summary>
	/// Creates a permissive profile for a user-selected folder that is not one of the fixed ComfyUI roots.
	/// </summary>
	/// <param name="path">Absolute folder path selected by the user.</param>
	internal static AssetRootProfile ForCustomPath(string path)
	{
		string label = System.IO.Path.GetFileName(path.TrimEnd(
			System.IO.Path.DirectorySeparatorChar,
			System.IO.Path.AltDirectorySeparatorChar));

		if (string.IsNullOrWhiteSpace(label))
		{
			label = path;
		}

		return new AssetRootProfile(
			"custom",
			label,
			path,
			"assets_input.png",
			"#8de7ff",
			"Custom asset folder",
			AssetInteractionMode.File);
	}
}
