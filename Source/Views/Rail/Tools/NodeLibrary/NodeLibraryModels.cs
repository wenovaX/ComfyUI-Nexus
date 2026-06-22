using System.Collections.Generic;

namespace ComfyUI_Nexus.Views.Rail.Tools.NodeLibrary;

public enum NodeGroupKind
{
	Bookmarked,
	Blueprint,
	Partner,
	Comfy,
	Extension,
	Uncategorized
}

public sealed record NodeLibraryEntry(
	string Type,
	string DisplayName,
	string Description,
	string Category,
	string PythonModule,
	NodeGroupKind GroupKind,
	string ColorHex = "#8de7ff"
);

public sealed class NodeCategoryNode
{
	public string Name { get; init; }
	public NodeCategoryNode? Parent { get; init; }
	public List<NodeCategoryNode> SubCategories { get; } = new();
	public List<NodeLibraryEntry> Nodes { get; } = new();
	public bool IsExpanded { get; set; }
	public bool IsBookmarkExpanded { get; set; }

	public NodeCategoryNode(string name, NodeCategoryNode? parent = null)
	{
		Name = name;
		Parent = parent;
	}

	public string FullPath => Parent == null ? Name : $"{Parent.FullPath}/{Name}";
}

public enum SectionKind
{
	NexusFamily,
	Bookmarked,
	Blueprints,
	Partner,
	Comfy,
	Extensions
}

public sealed class NodeLibraryRoot
{
	public HashSet<string> BookmarkedTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
	public HashSet<string> BookmarkedCategoryPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
	public NodeCategoryNode NexusFamilyRoot { get; } = new("ComfyUI-HUD");
	public List<NodeLibraryEntry> Bookmarked { get; } = new();
	public List<NodeCategoryNode> BookmarkedCategories { get; } = new();
	public bool IsBookmarkedExpanded { get; set; } = true;
	public Dictionary<string, BlueprintItem> BlueprintsById { get; } = new(StringComparer.OrdinalIgnoreCase);
	public NodeCategoryNode BlueprintRoot { get; } = new("Subgraph Blueprints");
	public NodeCategoryNode PartnerRoot { get; } = new("Partner Nodes");
	public NodeCategoryNode ComfyRoot { get; } = new("Comfy Nodes");
	public NodeCategoryNode ExtensionRoot { get; } = new("Extensions");
}

public sealed class BlueprintItem
{
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Category { get; set; } = "Uncategorized";
	public System.Text.Json.JsonElement? Source { get; set; }
	public System.Text.Json.JsonElement? Info { get; set; }
	public string? Error { get; set; }
	public System.Text.Json.JsonElement? Workflow { get; set; }
	public System.Text.Json.JsonElement? Subgraph { get; set; }
}
