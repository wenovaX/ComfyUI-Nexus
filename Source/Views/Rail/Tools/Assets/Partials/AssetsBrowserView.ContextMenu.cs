using ComfyUI_Nexus.AssetHub;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

public partial class AssetsBrowserView
{
	private enum AssetContextMenuSource
	{
		Background,
		Tree,
		Search,
	}

	private enum AssetContextActionKind
	{
		Open,
		InsertWorkflow,
		AddWorkflowBookmark,
		Reveal,
		OpenModelFolder,
		AddModelThumbnail,
		SetModelPrimaryThumbnail,
		Copy,
		Cut,
		Paste,
		Duplicate,
		AddFolder,
		CopyPath,
		Rename,
		Delete,
		Refresh,
	}

	private sealed record AssetContextActionDefinition(
		AssetContextActionKind Kind,
		string LabelKey,
		bool StartsNewSection = false);

	private readonly record struct AssetContextMenuContext(
		AssetContextMenuSource Source,
		string FullPath,
		bool IsDirectory,
		AssetOperationPolicy PastePolicy)
	{
		internal bool CanPaste => IsDirectory && PastePolicy != AssetOperationPolicy.None;
	}

	private static readonly AssetContextActionDefinition[] AssetContextActions =
	[
		new(AssetContextActionKind.Open, "common.open"),
		new(AssetContextActionKind.InsertWorkflow, "context_menu.insert_workflow_into_graph"),
		new(AssetContextActionKind.AddWorkflowBookmark, "context_menu.add_workflow_bookmark"),
		new(AssetContextActionKind.Reveal, "context_menu.reveal_in_explorer"),
		new(AssetContextActionKind.OpenModelFolder, "context_menu.open_model_folder"),
		new(AssetContextActionKind.AddModelThumbnail, "context_menu.add_model_thumbnail"),
		new(AssetContextActionKind.SetModelPrimaryThumbnail, "context_menu.set_model_primary_thumbnail"),
		new(AssetContextActionKind.Copy, "common.copy", StartsNewSection: true),
		new(AssetContextActionKind.Cut, "common.cut"),
		new(AssetContextActionKind.Paste, "common.paste"),
		new(AssetContextActionKind.Duplicate, "common.duplicate"),
		new(AssetContextActionKind.AddFolder, "views.rail.tools.assets.file_operations.add_folder_title"),
		new(AssetContextActionKind.CopyPath, "context_menu.copy_path"),
		new(AssetContextActionKind.Rename, "common.rename", StartsNewSection: true),
		new(AssetContextActionKind.Delete, "common.delete"),
		new(AssetContextActionKind.Refresh, "common.refresh", StartsNewSection: true),
	];

	private void AttachContextMenu(Grid row, RailTreeNode node)
		=> FlyoutBase.SetContextFlyout(row, BuildAssetContextMenu(CreateTreeContext(node)));

	private void AttachSearchResultContextMenu(Grid row, AssetHubItem item)
	{
		FlyoutBase.SetContextFlyout(row, BuildAssetContextMenu(CreateSearchContext(item)));
	}

	private void RefreshBackgroundContextMenu()
		=> FlyoutBase.SetContextFlyout(RailAssetListSurface, BuildAssetContextMenu(CreateBackgroundContext()));

	private MenuFlyout BuildAssetContextMenu(AssetContextMenuContext context)
	{
		var flyout = new MenuFlyout();
		bool hasVisibleItemInSection = false;

		foreach (var action in AssetContextActions)
		{
			if (!CanExecuteContextAction(action.Kind, context))
			{
				continue;
			}

			if (action.StartsNewSection && hasVisibleItemInSection)
			{
				flyout.Add(new MenuFlyoutSeparator());
			}

			var item = new MenuFlyoutItem { Text = GetContextActionLabel(action, context) };
			item.Clicked += async (s, e) =>
			{
				await ExecuteContextActionAsync(action.Kind, context);
			};
			flyout.Add(item);
			hasVisibleItemInSection = true;
		}

		return flyout;
	}

	private AssetContextMenuContext CreateTreeContext(RailTreeNode node)
		=> CreateContext(AssetContextMenuSource.Tree, node.FullPath, node.IsDirectory);

	private AssetContextMenuContext CreateSearchContext(AssetHubItem item)
		=> CreateContext(AssetContextMenuSource.Search, item.FullPath, item.Type == AssetHubItemType.Directory);

	private AssetContextMenuContext CreateBackgroundContext()
		=> CreateContext(AssetContextMenuSource.Background, _rootPath, isDirectory: true);

	private AssetContextMenuContext CreateContext(AssetContextMenuSource source, string fullPath, bool isDirectory)
	{
		return new AssetContextMenuContext(
			source,
			fullPath,
			isDirectory,
			_currentProfile?.PastePolicy ?? AssetOperationPolicy.All);
	}

	private bool CanExecuteContextAction(AssetContextActionKind actionKind, AssetContextMenuContext context)
	{
		return actionKind switch
		{
			AssetContextActionKind.Open => context.Source != AssetContextMenuSource.Background,
			AssetContextActionKind.InsertWorkflow => CanInsertWorkflowAtContext(context),
			AssetContextActionKind.AddWorkflowBookmark => CanAddWorkflowBookmarkAtContext(context),
			AssetContextActionKind.Reveal => !IsModelApiFolderContext(context) &&
				!string.IsNullOrWhiteSpace(context.FullPath) &&
				(File.Exists(context.FullPath) || Directory.Exists(context.FullPath)),
			AssetContextActionKind.OpenModelFolder => CanOpenResolvedModelFolder(context),
			AssetContextActionKind.AddModelThumbnail => CanAddModelThumbnailAtContext(context),
			AssetContextActionKind.SetModelPrimaryThumbnail => CanSetModelPrimaryThumbnailAtContext(context),
			AssetContextActionKind.Copy => context.Source != AssetContextMenuSource.Background && CanCopySelection(GetEffectiveContextPaths(context)),
			AssetContextActionKind.Cut => context.Source != AssetContextMenuSource.Background && CanCutSelection(GetEffectiveContextPaths(context)),
			AssetContextActionKind.Paste => context.CanPaste,
			AssetContextActionKind.Duplicate => context.Source != AssetContextMenuSource.Background && CanDuplicateSelection(GetEffectiveContextPaths(context)),
			AssetContextActionKind.AddFolder => CanAddFolderAtContext(context),
			AssetContextActionKind.CopyPath => CanCopyPathAtContext(context),
			AssetContextActionKind.Rename => context.Source != AssetContextMenuSource.Background && CanRenameSelection(GetEffectiveContextPaths(context)),
			AssetContextActionKind.Delete => context.Source != AssetContextMenuSource.Background && CanDeleteSelection(GetEffectiveContextPaths(context)),
			AssetContextActionKind.Refresh => true,
			_ => false,
		};
	}

	private IReadOnlyList<string> GetEffectiveContextPaths(AssetContextMenuContext context)
	{
		if (context.Source == AssetContextMenuSource.Background)
		{
			return [];
		}

		if (_selection.Contains(context.FullPath))
		{
			return GetSelectedExistingPaths();
		}

		return File.Exists(context.FullPath) || Directory.Exists(context.FullPath)
			? [context.FullPath]
			: [];
	}

	private static string GetContextActionLabel(AssetContextActionDefinition action, AssetContextMenuContext context)
	{
		if (action.Kind == AssetContextActionKind.Refresh && context.Source == AssetContextMenuSource.Search)
		{
			return LocalizationManager.Text("context_menu.refresh_search");
		}

		return LocalizationManager.Text(action.LabelKey);
	}

	private async Task ExecuteContextActionAsync(AssetContextActionKind actionKind, AssetContextMenuContext context)
	{
		bool preserveMultiSelection = actionKind != AssetContextActionKind.Open;
		if (context.Source != AssetContextMenuSource.Background)
		{
			PrepareContextSelection(context.FullPath, preserveMultiSelection);
		}

		if (!CanExecuteSelectionAction(actionKind, context, GetSelectedExistingPaths()))
		{
			return;
		}

		switch (actionKind)
		{
			case AssetContextActionKind.Open:
				await OpenContextTargetAsync(context);
				break;
			case AssetContextActionKind.InsertWorkflow:
				await InsertWorkflowContextTargetAsync(context);
				break;
			case AssetContextActionKind.AddWorkflowBookmark:
				await AddWorkflowBookmarkAsync(context.FullPath);
				break;
			case AssetContextActionKind.Reveal:
				await _fileOperations.RevealInExplorerAsync(context.FullPath);
				break;
			case AssetContextActionKind.OpenModelFolder:
				await OpenResolvedModelFolderAsync(context.FullPath);
				break;
			case AssetContextActionKind.AddModelThumbnail:
				await AddModelThumbnailAsync(context.FullPath);
				break;
			case AssetContextActionKind.SetModelPrimaryThumbnail:
				await SetModelPrimaryThumbnailAsync(context.FullPath);
				break;
			case AssetContextActionKind.Copy:
				BeginCopySelected(GetSelectedExistingPaths());
				break;
			case AssetContextActionKind.Cut:
				BeginCutSelected(GetSelectedExistingPaths());
				break;
			case AssetContextActionKind.Paste:
				await PasteIntoSelectionAsync(context.FullPath);
				break;
			case AssetContextActionKind.Duplicate:
				await DuplicatePathsAsync(GetSelectedExistingPaths(), ResolveDuplicateDestination(context));
				break;
			case AssetContextActionKind.AddFolder:
				await _fileOperations.AddFolderAsync(ResolveAddFolderDestination(context));
				break;
			case AssetContextActionKind.CopyPath:
				await AssetFileOperationService.CopyPathAsync(ResolveCopyPath(context));
				break;
			case AssetContextActionKind.Rename:
				await RenameSelectionAsync(GetSelectedExistingPaths());
				break;
			case AssetContextActionKind.Delete:
				await DeleteSelectionAsync(GetSelectedExistingPaths());
				break;
			case AssetContextActionKind.Refresh:
				await RefreshContextAsync(context.Source);
				break;
		}
	}

	private bool TryHandleAssetContextShortcut(NexusKey key, bool ctrl, bool shift)
	{
		var actionKind = ResolveShortcutAction(key, ctrl, shift);
		if (actionKind == null)
		{
			return false;
		}

		var paths = GetSelectedExistingPaths();
		var context = CreateShortcutContext(actionKind.Value);

		if (!CanExecuteSelectionAction(actionKind.Value, context, paths))
		{
			return false;
		}

		_ = ExecuteSelectionActionAsync(actionKind.Value, context, paths);
		return true;
	}

	private bool CanHandleAssetContextShortcut(NexusKey key, bool ctrl, bool shift)
	{
		var actionKind = ResolveShortcutAction(key, ctrl, shift);
		if (actionKind == null)
		{
			return false;
		}

		var paths = GetSelectedExistingPaths();
		var context = CreateShortcutContext(actionKind.Value);
		return CanExecuteSelectionAction(actionKind.Value, context, paths);
	}

	private AssetContextMenuContext CreateShortcutContext(AssetContextActionKind actionKind)
	{
		var primaryPath = GetPrimarySelectedPath();
		if (string.IsNullOrWhiteSpace(primaryPath) && actionKind == AssetContextActionKind.Paste)
		{
			return CreateContext(AssetContextMenuSource.Background, _rootPath, isDirectory: true);
		}

		bool primaryIsDirectory = !string.IsNullOrWhiteSpace(primaryPath) && Directory.Exists(primaryPath);
		return CreateContext(AssetContextMenuSource.Tree, primaryPath ?? _rootPath, primaryIsDirectory);
	}

	private static AssetContextActionKind? ResolveShortcutAction(NexusKey key, bool ctrl, bool shift)
	{
		if (shift)
		{
			return null;
		}

		if (ctrl && key == NexusKey.C) return AssetContextActionKind.Copy;
		if (ctrl && key == NexusKey.X) return AssetContextActionKind.Cut;
		if (ctrl && key == NexusKey.V) return AssetContextActionKind.Paste;
		if (ctrl && key == NexusKey.D) return AssetContextActionKind.Duplicate;
		if (!ctrl && key == NexusKey.Delete) return AssetContextActionKind.Delete;
		if (!ctrl && key == NexusKey.F2) return AssetContextActionKind.Rename;
		return null;
	}

	private bool CanExecuteSelectionAction(
		AssetContextActionKind actionKind,
		AssetContextMenuContext context,
		IReadOnlyList<string> paths)
	{
		return actionKind switch
		{
			AssetContextActionKind.Copy => CanCopySelection(paths),
			AssetContextActionKind.Cut => CanCutSelection(paths),
			AssetContextActionKind.Paste => context.Source == AssetContextMenuSource.Background
				? context.CanPaste
				: CanPasteIntoSelection(paths),
			AssetContextActionKind.Duplicate => CanDuplicateSelection(paths),
			AssetContextActionKind.AddFolder => CanAddFolderAtContext(context),
			AssetContextActionKind.Rename => CanRenameSelection(paths),
			AssetContextActionKind.Delete => CanDeleteSelection(paths),
			_ => true,
		};
	}

	private async Task ExecuteSelectionActionAsync(
		AssetContextActionKind actionKind,
		AssetContextMenuContext context,
		IReadOnlyList<string> paths)
	{
		switch (actionKind)
		{
			case AssetContextActionKind.Copy:
				BeginCopySelected(paths);
				break;
			case AssetContextActionKind.Cut:
				BeginCutSelected(paths);
				break;
			case AssetContextActionKind.Paste:
				await PasteIntoSelectionAsync(context.FullPath);
				break;
			case AssetContextActionKind.Duplicate:
				await DuplicatePathsAsync(paths, ResolveDuplicateDestination(context));
				break;
			case AssetContextActionKind.AddFolder:
				await _fileOperations.AddFolderAsync(ResolveAddFolderDestination(context));
				break;
			case AssetContextActionKind.Rename:
				await RenameSelectionAsync(paths);
				break;
			case AssetContextActionKind.Delete:
				await DeleteSelectionAsync(paths);
				break;
		}
	}

	private async Task OpenContextTargetAsync(AssetContextMenuContext context)
	{
		if (context.Source == AssetContextMenuSource.Search)
		{
			var item = _searchResults.FirstOrDefault(result =>
				string.Equals(result.FullPath, context.FullPath, StringComparison.OrdinalIgnoreCase));

			if (item != null)
			{
				await OpenSearchResultAsync(item);
				return;
			}
		}

		if (context.IsDirectory)
		{
			if (context.Source == AssetContextMenuSource.Search)
			{
				RailSearchEntry.Text = string.Empty;
				SetRootPath(context.FullPath);
				return;
			}

			if (FindNodeByPath(context.FullPath) is { } node)
			{
				await ToggleNodeExpansionAsync(node);
			}

			return;
		}

		var request = CreateOpenRequest(context.FullPath);
		if (request.Kind is AssetOpenKind.WorkflowJson or AssetOpenKind.ModelFile)
		{
			FileOpenRequested?.Invoke(this, request);
			return;
		}

		await OpenInOsAsync(context.FullPath);
	}

	private Task InsertWorkflowContextTargetAsync(AssetContextMenuContext context)
	{
		string relativePath = ResolveWorkflowUserDataPath(context.FullPath);
		var decision = AssetInsertPolicy.Evaluate(context.FullPath, relativePath);
		if (!decision.IsAllowed)
		{
			return Task.CompletedTask;
		}

		AssetInteractionRequested?.Invoke(this, CreateOpenRequest(context.FullPath) with
		{
			Action = AssetInteractionAction.Insert,
			Mode = AssetInteractionMode.Workflow,
			DisplayName = decision.UserDataPath ?? Path.GetFileName(context.FullPath)
		});
		return Task.CompletedTask;
	}

	private bool CanInsertWorkflowAtContext(AssetContextMenuContext context)
	{
		if (context.Source == AssetContextMenuSource.Background || context.IsDirectory)
		{
			return false;
		}

		return AssetInsertPolicy.Evaluate(
			context.FullPath,
			ResolveWorkflowUserDataPath(context.FullPath)).IsAllowed;
	}

	private bool CanAddWorkflowBookmarkAtContext(AssetContextMenuContext context)
	{
		if (_currentProfile?.AllowWorkflowBookmarks != true ||
			context.Source == AssetContextMenuSource.Background ||
			context.IsDirectory ||
			!File.Exists(context.FullPath) ||
			string.Equals(Path.GetFileName(context.FullPath), ".index.json", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string relativePath = ResolveWorkflowUserDataPath(context.FullPath);
		return string.Equals(Path.GetExtension(context.FullPath), ".json", StringComparison.OrdinalIgnoreCase) &&
			!string.IsNullOrWhiteSpace(relativePath) &&
			!_workflowBookmarkPaths.Contains(WorkflowTabController.NormalizeWorkflowRelativePath(relativePath));
	}

	private bool CanCopyPathAtContext(AssetContextMenuContext context)
	{
		if (string.IsNullOrWhiteSpace(context.FullPath) || IsModelApiFolderContext(context))
		{
			return false;
		}

		if (IsModelApiFileContext(context))
		{
			return File.Exists(context.FullPath) || ResolveModelAssetPathMatches(context.FullPath).Count > 0;
		}

		return true;
	}

	private string ResolveCopyPath(AssetContextMenuContext context)
	{
		if (IsModelApiFileContext(context) && !File.Exists(context.FullPath))
		{
			ModelAssetPathMatch? match = ResolveModelAssetPathMatches(context.FullPath).FirstOrDefault();
			if (match is not null)
			{
				return match.FullPath;
			}
		}

		return context.FullPath;
	}

	private bool CanOpenResolvedModelFolder(AssetContextMenuContext context)
	{
		if (!IsModelApiFileContext(context) || File.Exists(context.FullPath) || Directory.Exists(context.FullPath))
		{
			return false;
		}

		return ResolveModelAssetPathMatches(context.FullPath).Count > 0;
	}

	private async Task OpenResolvedModelFolderAsync(string syntheticPath)
	{
		ModelAssetPathMatch? match = ResolveModelAssetPathMatches(syntheticPath).FirstOrDefault();
		if (match is null)
		{
			return;
		}

		bool isDirectory = await Task.Run(() => Directory.Exists(match.FullPath));
		string folderPath = isDirectory ? match.FullPath : Path.GetDirectoryName(match.FullPath) ?? match.RootPath;
		var result = await NexusAppManager.Instance.Platform.Shell.OpenPathAsync(folderPath);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			NexusLog.Warning($"Model folder open failed: {result.Message}");
		}
	}

	private bool CanAddModelThumbnailAtContext(AssetContextMenuContext context)
		=> IsModelApiFileContext(context) && ResolveActualModelPath(context.FullPath) is not null;

	private bool CanSetModelPrimaryThumbnailAtContext(AssetContextMenuContext context)
	{
		string? modelPath = ResolveActualModelPath(context.FullPath);
		return modelPath is not null && ModelAssetThumbnailResolver.GetGalleryImages(modelPath).Count >= 2;
	}

	private async Task AddModelThumbnailAsync(string syntheticPath)
	{
		string? modelPath = ResolveActualModelPath(syntheticPath);
		if (modelPath is null)
		{
			await _appManager.Dialogs.AlertAsync(
				LocalizationManager.Text("model_thumbnail.model_not_found_title"),
				LocalizationManager.Text("model_thumbnail.model_not_found_message"),
				LocalizationManager.Text("common.ok"));
			return;
		}

		var picked = await NexusAppManager.Instance.Platform.FilePicker.PickFileAsync(
			LocalizationManager.Text("model_thumbnail.pick_image_title"),
			[".png", ".jpg", ".jpeg", ".webp"]);
		if (!picked.IsSuccess || string.IsNullOrWhiteSpace(picked.Value))
		{
			if (!string.IsNullOrWhiteSpace(picked.Message))
			{
				await _appManager.Dialogs.AlertAsync(
					LocalizationManager.Text("model_thumbnail.pick_failed_title"),
					picked.Message,
					LocalizationManager.Text("common.ok"));
			}
			return;
		}

		if (!ModelAssetThumbnailResolver.IsSupportedImageFile(picked.Value))
		{
			await _appManager.Dialogs.AlertAsync(
				LocalizationManager.Text("model_thumbnail.unsupported_title"),
				LocalizationManager.Text("model_thumbnail.unsupported_message"),
				LocalizationManager.Text("common.ok"));
			return;
		}

		try
		{
			var result = ModelAssetThumbnailResolver.AddThumbnail(modelPath, picked.Value);
			if (!result.Success)
			{
				await _appManager.Dialogs.AlertAsync(
					LocalizationManager.Text("model_thumbnail.add_failed_title"),
					result.Error,
					LocalizationManager.Text("common.ok"));
				return;
			}

			InvalidateModelThumbnailPreview(syntheticPath);
			await _appManager.Dialogs.AlertAsync(
				LocalizationManager.Text("model_thumbnail.add_complete_title"),
				LocalizationManager.Format("model_thumbnail.add_complete_message", result.FileName),
				LocalizationManager.Text("common.ok"));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			await _appManager.Dialogs.AlertAsync(
				LocalizationManager.Text("model_thumbnail.add_failed_title"),
				ex.Message,
				LocalizationManager.Text("common.ok"));
		}
	}

	private async Task SetModelPrimaryThumbnailAsync(string syntheticPath)
	{
		string? modelPath = ResolveActualModelPath(syntheticPath);
		if (modelPath is null)
		{
			return;
		}

		var images = ModelAssetThumbnailResolver.GetGalleryImages(modelPath);
		if (images.Count < 2)
		{
			return;
		}

		var result = await _appManager.Dialogs.ThumbnailChoiceAsync(
			LocalizationManager.Text("model_thumbnail.set_primary_title"),
			LocalizationManager.Text("model_thumbnail.set_primary_message"),
			images.Select(image => new NexusDialogThumbnailChoice(image.FileName, image.Path, image.IsPrimary)).ToList(),
			LocalizationManager.Text("model_thumbnail.set_primary_action"),
			LocalizationManager.Text("common.cancel"));
		if (!result.Accepted || string.IsNullOrWhiteSpace(result.Choice))
		{
			return;
		}

		try
		{
			var selectedImage = images.FirstOrDefault(image =>
				string.Equals(image.FileName, result.Choice, StringComparison.OrdinalIgnoreCase));
			if (selectedImage is null || !File.Exists(selectedImage.Path))
			{
				await _appManager.Dialogs.AlertAsync(
					LocalizationManager.Text("model_thumbnail.image_missing_title"),
					LocalizationManager.Text("model_thumbnail.image_missing_message"),
					LocalizationManager.Text("common.ok"));
				InvalidateModelThumbnailPreview(syntheticPath);
				return;
			}

			if (!ModelAssetThumbnailResolver.SetPrimaryThumbnail(modelPath, result.Choice))
			{
				await _appManager.Dialogs.AlertAsync(
					LocalizationManager.Text("model_thumbnail.set_primary_failed_title"),
					LocalizationManager.Text("model_thumbnail.set_primary_failed_message"),
					LocalizationManager.Text("common.ok"));
				return;
			}

			InvalidateModelThumbnailPreview(syntheticPath);
			await _appManager.Dialogs.AlertAsync(
				LocalizationManager.Text("model_thumbnail.set_primary_complete_title"),
				LocalizationManager.Format("model_thumbnail.set_primary_complete_message", result.Choice),
				LocalizationManager.Text("common.ok"));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			await _appManager.Dialogs.AlertAsync(
				LocalizationManager.Text("model_thumbnail.set_primary_failed_title"),
				ex.Message,
				LocalizationManager.Text("common.ok"));
		}
	}

	private string? ResolveActualModelPath(string syntheticPath)
	{
		if (File.Exists(syntheticPath))
		{
			return syntheticPath;
		}

		return ResolveModelAssetPathMatches(syntheticPath).FirstOrDefault()?.FullPath;
	}

	private void InvalidateModelThumbnailPreview(string syntheticPath)
	{
		_modelThumbnailPathCache.Remove(syntheticPath);
		HideModelThumbnailPreview();
		RefreshVisibleContextMenu(syntheticPath);
	}

	private void RefreshVisibleContextMenu(string path)
	{
		if (_rowMap.TryGetValue(path, out var row) &&
			TryGetNode(path, out var node))
		{
			AttachContextMenu(row, node);
		}

		if (_searchRowMap.TryGetValue(path, out var searchRow) &&
			searchRow.BindingContext is AssetHubItem item)
		{
			AttachSearchResultContextMenu(searchRow, item);
		}
	}

	private IReadOnlyList<ModelAssetPathMatch> ResolveModelAssetPathMatches(string syntheticPath)
	{
		string modelsRoot = GetModelsRootPath();
		if (string.IsNullOrWhiteSpace(modelsRoot))
		{
			return [];
		}

		return ModelAssetPathResolver.ResolveMatches(
			syntheticPath,
			modelsRoot,
			SettingsService.Settings.ModelLibraryRoots);
	}

	private bool IsModelApiFileContext(AssetContextMenuContext context)
	{
		return _currentProfile?.TreeSource == AssetTreeSource.ModelApi &&
			!context.IsDirectory &&
			string.Equals(_currentProfile.Id, "models", StringComparison.OrdinalIgnoreCase) &&
			IsPathWithinRoot(context.FullPath, GetModelsRootPath());
	}

	private bool IsModelApiFolderContext(AssetContextMenuContext context)
	{
		return _currentProfile?.TreeSource == AssetTreeSource.ModelApi &&
			context.IsDirectory &&
			string.Equals(_currentProfile.Id, "models", StringComparison.OrdinalIgnoreCase) &&
			IsPathWithinRoot(context.FullPath, GetModelsRootPath());
	}

	private async Task AddWorkflowBookmarkAsync(string fullPath)
	{
		string relativePath = WorkflowTabController.NormalizeWorkflowRelativePath(ResolveWorkflowUserDataPath(fullPath));
		if (string.IsNullOrWhiteSpace(relativePath))
		{
			return;
		}

		HashSet<string> bookmarks = await WorkflowBookmarkService.SyncAndLoadAsync(_fixedWorkflowsPath);
		if (!bookmarks.Add(relativePath))
		{
			return;
		}

		await WorkflowBookmarkService.SaveAsync(_fixedWorkflowsPath, bookmarks);
		_workflowBookmarkPaths.Add(relativePath);
		await RefreshWorkflowBookmarksSectionAsync();
		WorkflowBookmarksChanged?.Invoke(this, EventArgs.Empty);
	}

	private string ResolveWorkflowUserDataPath(string fullPath)
	{
		if (string.IsNullOrWhiteSpace(FixedWorkflowsPath) ||
			string.IsNullOrWhiteSpace(fullPath) ||
			!IsPathWithinRoot(fullPath, FixedWorkflowsPath))
		{
			return string.Empty;
		}

		try
		{
			string relative = Path.GetRelativePath(FixedWorkflowsPath, fullPath)
				.Replace(Path.DirectorySeparatorChar, '/')
				.Replace(Path.AltDirectorySeparatorChar, '/')
				.TrimStart('/');
			return string.IsNullOrWhiteSpace(relative) || relative.StartsWith("../", StringComparison.Ordinal)
				? string.Empty
				: $"workflows/{relative}";
		}
		catch
		{
			return string.Empty;
		}
	}

	private bool CanAddFolderAtContext(AssetContextMenuContext context)
	{
		if (_currentProfile?.AllowAddFolder != true)
		{
			return false;
		}

		string destination = ResolveAddFolderDestination(context);
		return !string.IsNullOrWhiteSpace(destination) && Directory.Exists(destination);
	}

	private string ResolveAddFolderDestination(AssetContextMenuContext context)
	{
		if (context.IsDirectory)
		{
			return context.FullPath;
		}

		string? parent = Path.GetDirectoryName(context.FullPath);
		if (string.IsNullOrWhiteSpace(parent))
		{
			return _rootPath;
		}

		if (string.Equals(parent, _rootPath, StringComparison.OrdinalIgnoreCase))
		{
			return _rootPath;
		}

		return parent;
	}

	private string ResolveDuplicateDestination(AssetContextMenuContext context)
	{
		if (context.IsDirectory)
		{
			return context.FullPath;
		}

		string? parent = Path.GetDirectoryName(context.FullPath);
		return string.IsNullOrWhiteSpace(parent) ? _rootPath : parent;
	}

	private Task RefreshContextAsync(AssetContextMenuSource source)
	{
		if (source == AssetContextMenuSource.Search)
		{
			return RefreshSearchResultsAsync(immediate: true);
		}

		RefreshTree();
		return Task.CompletedTask;
	}

	private RailTreeNode? FindNodeByPath(string fullPath)
	{
		return TryGetNode(fullPath, out var node) ? node : null;
	}
}
