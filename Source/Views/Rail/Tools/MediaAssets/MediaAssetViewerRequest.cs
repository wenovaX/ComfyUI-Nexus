using ComfyUI_Nexus.Views.Overlays;

namespace ComfyUI_Nexus.Views.Rail.Tools.MediaAssets;

internal sealed class MediaAssetViewerRequest : EventArgs
{
	internal MediaAssetViewerRequest(IReadOnlyList<MediaViewerItem> items, int startIndex)
	{
		Items = items;
		StartIndex = startIndex;
	}

	internal IReadOnlyList<MediaViewerItem> Items { get; }
	internal int StartIndex { get; }
}
