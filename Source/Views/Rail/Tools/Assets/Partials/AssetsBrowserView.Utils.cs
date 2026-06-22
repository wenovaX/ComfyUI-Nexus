namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

public partial class AssetsBrowserView
{
	private string GetIconForEntry(RailTreeEntry entry)
	{
		if (entry.IsDirectory)
		{
			return "folder";
		}

		string ext = Path.GetExtension(entry.FullPath).ToLowerInvariant();
		if (_assetHubService.IsModelFile(entry.FullPath))
		{
			return "model";
		}

		return ext switch
		{
			".json" => "json",
			".png" or ".jpg" or ".jpeg" or ".webp" => "image",
			".mp4" or ".mov" or ".avi" => "video",
			_ => "file"
		};
	}

	private void UpdateCachedChevron(RailTreeNode node)
	{
		if (_rowMap.TryGetValue(node.FullPath, out var row) && row.Children.Count > 0 && row.Children[0] is Label chevron)
		{
			chevron.Text = node.IsDirectory ? (node.IsExpanded ? "v" : ">") : " ";
		}
	}

	private Task ExpandRowsAsync(RailTreeNode parent, List<RailTreeNode> descendants)
		=> RefreshVirtualTreeRowsAsync();

	private Task CollapseRowsAsync(List<RailTreeNode> descendants)
		=> RefreshVirtualTreeRowsAsync();

	private Task RefreshVirtualTreeRowsAsync()
	{
		RenderTree();
		return Task.CompletedTask;
	}
}
