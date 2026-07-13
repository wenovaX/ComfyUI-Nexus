namespace ComfyUI_Nexus.Ui;

/// <summary>
/// High-level asset kind used by bridge consumers to choose workflow/model/file behavior.
/// </summary>
public enum AssetOpenKind
{
	GenericFile,
	WorkflowJson,
	ModelFile,
}

/// <summary>
/// Origin mode for an asset interaction. This controls how the web bridge interprets the payload.
/// </summary>
public enum AssetInteractionMode
{
	File,
	Image,
	Video,
	Folder,
	Workflow,
	Model,
	Node,
}

/// <summary>
/// User action that produced the asset payload.
/// </summary>
public enum AssetInteractionAction
{
	Open,
	Insert,
	DragStart,
	Drop,
}

/// <summary>
/// Payload sent from native asset/node UI to the WebView bridge.
/// </summary>
/// <param name="FullPath">Absolute path for file and folder assets, or a stable identifier for node-style interactions.</param>
/// <param name="Kind">File classification used by the web bridge.</param>
/// <param name="Name">Raw file, folder, model, or node name.</param>
/// <param name="Extension">File extension including the dot, or an empty value when not applicable.</param>
/// <param name="SourceRoot">Root/profile path that produced the request.</param>
/// <param name="DisplayName">Optional UI-friendly name when it differs from <paramref name="Name"/>.</param>
/// <param name="ModelDirectory">ComfyUI model directory/category for model assets.</param>
/// <param name="ModelPathIndex">Index of the configured model search path that contains the asset.</param>
/// <param name="Mode">Interaction origin mode such as file, workflow, model, or node.</param>
/// <param name="Action">Specific action, for example open, drag start, or drop.</param>
/// <param name="NodeType">ComfyUI node type when the payload represents a node.</param>
/// <param name="DragId">Stable drag session id used to correlate drag start and drop.</param>
/// <param name="DropClientX">Browser client X coordinate for drop placement, if known.</param>
/// <param name="DropClientY">Browser client Y coordinate for drop placement, if known.</param>
/// <param name="RailWidth">Current rail width used by the web bridge as a native rail visual-origin hint.</param>
public sealed record AssetOpenRequest(
	string FullPath,
	AssetOpenKind Kind,
	string Name,
	string Extension,
	string SourceRoot,
	string? DisplayName = null,
	string? ModelDirectory = null,
	int? ModelPathIndex = null,
	AssetInteractionMode Mode = AssetInteractionMode.File,
	AssetInteractionAction Action = AssetInteractionAction.Open,
	string? NodeType = null,
	string? DragId = null,
	double? DropClientX = null,
	double? DropClientY = null,
	double? RailWidth = null);
