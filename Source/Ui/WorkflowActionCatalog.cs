namespace ComfyUI_Nexus.Ui;

internal enum WorkflowActionKind
{
	Rename,
	Duplicate,
	Bookmark,
	Save,
	SaveAs,
	Export,
	ExportApi,
	Clear,
	Delete,
}

internal sealed record WorkflowActionState(bool HasFile, bool IsModified, bool IsBookmarked, string BookmarkLabel);

internal sealed record WorkflowActionMenuItem(
	WorkflowActionKind Kind,
	string Label,
	bool IsEnabled,
	bool StartsNewSection = false);

internal static class WorkflowActionCatalog
{
	internal static WorkflowActionState CreateState(bool hasFile, bool isModified, bool isBookmarked)
		=> new(hasFile, isModified, isBookmarked, GetBookmarkMenuText(isBookmarked));

	internal static string GetBookmarkMenuText(bool isBookmarked)
		=> isBookmarked ? "Remove Bookmark" : "Add to Bookmarks";

	internal static IReadOnlyList<WorkflowActionMenuItem> BuildMenuItems(WorkflowActionState state)
	{
		return
		[
			new(WorkflowActionKind.Rename, "Rename", state.HasFile),
			new(WorkflowActionKind.Duplicate, "Duplicate", true),
			new(WorkflowActionKind.Bookmark, state.BookmarkLabel, state.HasFile),
			new(WorkflowActionKind.Save, "Save", true, StartsNewSection: true),
			new(WorkflowActionKind.SaveAs, "Save As", true),
			new(WorkflowActionKind.Export, "Export", true, StartsNewSection: true),
			new(WorkflowActionKind.ExportApi, "Export (API)", true),
			new(WorkflowActionKind.Clear, "Clear Workflow", true, StartsNewSection: true),
			new(WorkflowActionKind.Delete, "Delete Workflow", true, StartsNewSection: true),
		];
	}

	internal static IReadOnlyList<WorkflowActionMenuItem> BuildTabContextMenuItems(WorkflowActionState state)
		=> BuildMenuItems(state)
			.Where(item => item.Kind != WorkflowActionKind.Delete)
			.ToArray();
}
