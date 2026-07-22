using System.Text.Json;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

public partial class AssetsBrowserView
{
	private AssetOpenRequest CreateOpenRequest(string fullPath)
		=> CreateInteractionRequest(fullPath, AssetInteractionAction.Open);

	private AssetOpenRequest CreateDragRequest(string fullPath)
		=> CreateInteractionRequest(fullPath, AssetInteractionAction.DragStart);

	private bool ShouldPublishNativeFileDragPayload(AssetOpenRequest request)
		=> (_currentProfile?.AllowFileDrag ?? true) &&
			request.Mode is AssetInteractionMode.File or AssetInteractionMode.Image or AssetInteractionMode.Workflow;

	private bool ShouldPublishPathDragProperties(AssetOpenRequest request)
		=> ShouldPublishNativeFileDragPayload(request);

	private bool ShouldUsePseudoIntentDrag(AssetOpenRequest request)
		=> !ShouldPublishNativeFileDragPayload(request) &&
			request.Mode is AssetInteractionMode.Model or AssetInteractionMode.Node;

	private bool ShouldAllowAssetDrag(AssetOpenRequest request, bool isDirectory)
		=> isDirectory
			? _currentProfile?.AllowFolderDrag ?? false
			: _currentProfile?.AllowFileDrag ?? true;

	private void BeginPseudoIntentDrag(AssetOpenRequest request)
	{
		_activeDragRequest = request;
		AssetInteractionRequested?.Invoke(this, request);
	}

	private void CancelPseudoIntentDrag()
	{
		_activeDragRequest = null;
	}

	private bool ShouldAllowSelectedAssetDrag(AssetOpenRequest request, IReadOnlyList<string> selectedPaths)
	{
		if (!ShouldAllowAssetDrag(request, Directory.Exists(request.FullPath)))
		{
			return false;
		}

		return (_currentProfile?.AllowFolderDrag ?? false) || selectedPaths.All(path => !Directory.Exists(path));
	}

	private bool CanAcceptFileDrop(object? dataPackage)
	{
		if (IsAssetIntentOnlyDrag(dataPackage))
		{
			return false;
		}

		try
		{
			if (IsCurrentRootDrag(dataPackage))
			{
				return IsDuplicateDragRequested()
					? _currentProfile?.AllowDuplicate == true
					: CurrentAllowsInternalMove;
			}
		}
		catch
		{
		}

		return IsActiveNativeDragFromCurrentRoot()
			? (IsDuplicateDragRequested() ? _currentProfile?.AllowDuplicate == true : CurrentAllowsInternalMove)
			: CurrentAllowsDropImport;
	}

	private Microsoft.Maui.Controls.DataPackageOperation GetAcceptedFileDropOperation(object? dataPackage)
		=> Microsoft.Maui.Controls.DataPackageOperation.Copy;

	private bool IsDuplicateDragRequested()
		=> NexusAppManager.Instance.Platform.Keyboard.IsAltPressed();

	private void SetActiveNativeDrag(IReadOnlyList<string> selectedPaths)
	{
		_activeDragRootPath = _rootPath;
		_activeDragPaths = selectedPaths.ToArray();
	}

	private void ClearActiveNativeDrag()
	{
		_activeDragRootPath = null;
		_activeDragPaths = null;
	}

	private bool IsCurrentRootDrag(object? dataPackage)
	{
		try
		{
			var properties = dataPackage?.GetType().GetProperty("Properties")?.GetValue(dataPackage);
			if (properties is IDictionary<string, object> values &&
				values.TryGetValue("root", out object? rootObj) &&
				rootObj is string sourceRoot &&
				string.Equals(sourceRoot, _rootPath, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		catch
		{
		}

		return IsActiveNativeDragFromCurrentRoot();
	}

	private bool IsActiveNativeDragFromCurrentRoot()
		=> !string.IsNullOrWhiteSpace(_activeDragRootPath) &&
		   string.Equals(_activeDragRootPath, _rootPath, StringComparison.OrdinalIgnoreCase);

	private async Task<IReadOnlyList<string>> TryGetDroppedPathsWithActiveFallbackAsync(DropEventArgs e)
	{
		var droppedPaths = await TryGetDroppedPathsAsync(e);
		if (droppedPaths.Count > 0)
		{
			return droppedPaths;
		}

		return IsActiveNativeDragFromCurrentRoot() && _activeDragPaths != null
			? _activeDragPaths
			: [];
	}

	private async Task<IReadOnlyList<string>> TryGetDraggedPathsWithActiveFallbackAsync(DragEventArgs e)
	{
		var draggedPaths = await TryGetDroppedPathsAsync(e);
		if (draggedPaths.Count > 0)
		{
			return draggedPaths;
		}

		return IsActiveNativeDragFromCurrentRoot() && _activeDragPaths != null
			? _activeDragPaths
			: [];
	}

	private static string CreateAssetDragIntentText(AssetOpenRequest request)
	{
		string payload = JsonSerializer.Serialize(new
		{
			path = request.FullPath,
			name = request.Name,
			displayName = request.DisplayName,
			extension = request.Extension,
			kind = request.Kind.ToString(),
			mode = request.Mode.ToString(),
			action = request.Action.ToString(),
			sourceRoot = request.SourceRoot,
			modelDirectory = request.ModelDirectory,
			modelPathIndex = request.ModelPathIndex,
			nodeType = request.NodeType,
			dragId = request.DragId,
		});

		return $"nexus-asset-intent:{payload}";
	}

	private static bool IsAssetIntentOnlyDrag(object? dataPackage)
	{
		try
		{
			var properties = dataPackage?.GetType().GetProperty("Properties")?.GetValue(dataPackage);
			if (properties is not IDictionary<string, object> values)
			{
				return false;
			}

			if (!values.TryGetValue("assetMode", out object? modeObj) || modeObj is not string mode)
			{
				return false;
			}

			return mode is nameof(AssetInteractionMode.Model) or nameof(AssetInteractionMode.Node);
		}
		catch
		{
			return false;
		}
	}

	private AssetOpenRequest CreateInteractionRequest(string fullPath, AssetInteractionAction action)
	{
		string extension = Path.GetExtension(fullPath);
		string normalizedExtension = extension.ToLowerInvariant();
		string sourceRoot = ResolveSourceRoot(fullPath);
		AssetOpenKind kind = ResolveAssetOpenKind(fullPath, normalizedExtension, sourceRoot);
		AssetInteractionMode mode = ResolveInteractionMode(fullPath, kind);

		return new AssetOpenRequest(
			fullPath,
			kind,
			kind == AssetOpenKind.ModelFile ? GetModelAssetName(fullPath) : Path.GetFileName(fullPath),
			normalizedExtension,
			sourceRoot,
			DisplayName: GetDisplayName(kind, fullPath),
			ModelDirectory: kind == AssetOpenKind.ModelFile ? GetModelDirectory(fullPath) : null,
			ModelPathIndex: kind == AssetOpenKind.ModelFile ? GetModelPathIndex(fullPath) : null,
			Mode: mode,
			Action: action,
			DragId: action == AssetInteractionAction.DragStart
				? Guid.NewGuid().ToString("N")
				: null);
	}

	private AssetOpenKind ResolveAssetOpenKind(string fullPath, string normalizedExtension, string sourceRoot)
	{
		if (string.Equals(sourceRoot, "models", StringComparison.OrdinalIgnoreCase) && _assetHubService.IsModelFile(fullPath))
		{
			return AssetOpenKind.ModelFile;
		}

		if (string.Equals(normalizedExtension, ".json", StringComparison.OrdinalIgnoreCase))
		{
			if (string.Equals(sourceRoot, "workflows", StringComparison.OrdinalIgnoreCase))
			{
				return AssetOpenKind.WorkflowJson;
			}

			if (LooksLikeWorkflowPath(fullPath))
			{
				return AssetOpenKind.WorkflowJson;
			}
		}

		return AssetOpenKind.GenericFile;
	}

	private AssetInteractionMode ResolveInteractionMode(string fullPath, AssetOpenKind kind)
	{
		if (Directory.Exists(fullPath))
		{
			return AssetInteractionMode.Folder;
		}

		return kind switch
		{
			AssetOpenKind.WorkflowJson => AssetInteractionMode.Workflow,
			AssetOpenKind.ModelFile => AssetInteractionMode.Model,
			_ when IsImageExtension(Path.GetExtension(fullPath)) => AssetInteractionMode.Image,
			_ when IsViewerVideoExtension(Path.GetExtension(fullPath)) => AssetInteractionMode.Video,
			_ => AssetInteractionMode.File,
		};
	}

	private static bool IsImageExtension(string extension)
		=> extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);

	private static bool IsViewerVideoExtension(string extension)
		=> extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase);

	private string ResolveSourceRoot(string fullPath)
	{
		if (_currentProfile != null && IsPathWithinRoot(fullPath, _currentProfile.Path))
		{
			return _currentProfile.Id;
		}

		string comfyRoot = _appManager.Paths.ConfiguredComfyPath;
		if (string.IsNullOrWhiteSpace(comfyRoot))
		{
			return "assets";
		}

		string outputPath = Path.Combine(comfyRoot, "output");
		if (IsPathWithinRoot(fullPath, outputPath))
		{
			return "output";
		}

		string inputPath = Path.Combine(comfyRoot, "input");
		if (IsPathWithinRoot(fullPath, inputPath))
		{
			return "input";
		}

		string modelsPath = Path.Combine(comfyRoot, "models");
		if (IsPathWithinRoot(fullPath, modelsPath))
		{
			return "models";
		}

		if (!string.IsNullOrWhiteSpace(_fixedWorkflowsPath) && IsPathWithinRoot(fullPath, _fixedWorkflowsPath))
		{
			return "workflows";
		}

		return "assets";
	}

	private bool LooksLikeWorkflowPath(string fullPath)
	{
		string? parent = Path.GetDirectoryName(fullPath);
		if (string.IsNullOrWhiteSpace(parent))
		{
			return false;
		}

		if (!string.IsNullOrWhiteSpace(_fixedWorkflowsPath) && IsPathWithinRoot(fullPath, _fixedWorkflowsPath))
		{
			return true;
		}

		return parent.Contains("workflow", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsPathWithinRoot(string path, string rootPath)
	{
		if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(rootPath))
		{
			return false;
		}

		if (string.Equals(path, rootPath, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return path.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith(rootPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}

	private static string GetDisplayName(AssetOpenKind kind, string fullPath)
	{
		string fileName = Path.GetFileName(fullPath);
		if (kind != AssetOpenKind.ModelFile)
		{
			return fileName;
		}

		return Path.GetFileNameWithoutExtension(fileName);
	}

	private string? GetModelDirectory(string fullPath)
	{
		string modelsRoot = GetModelsRootPath();
		if (string.IsNullOrWhiteSpace(modelsRoot) || !IsPathWithinRoot(fullPath, modelsRoot))
		{
			return null;
		}

		try
		{
			string relativePath = Path.GetRelativePath(modelsRoot, fullPath);
			string? firstSegment = relativePath
				.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
				.FirstOrDefault();

			return string.IsNullOrWhiteSpace(firstSegment) ? null : firstSegment;
		}
		catch
		{
			return null;
		}
	}

	private string GetModelAssetName(string fullPath)
	{
		string modelsRoot = GetModelsRootPath();
		if (string.IsNullOrWhiteSpace(modelsRoot) || !IsPathWithinRoot(fullPath, modelsRoot))
		{
			return Path.GetFileName(fullPath);
		}

		try
		{
			string relativePath = Path.GetRelativePath(modelsRoot, fullPath);
			string[] parts = relativePath
				.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
			return parts.Length > 1
				? string.Join("/", parts.Skip(1))
				: Path.GetFileName(fullPath);
		}
		catch
		{
			return Path.GetFileName(fullPath);
		}
	}

	private int GetModelPathIndex(string fullPath)
	{
		return ResolveModelAssetPathMatches(fullPath).FirstOrDefault()?.RootIndex ?? 0;
	}
}
