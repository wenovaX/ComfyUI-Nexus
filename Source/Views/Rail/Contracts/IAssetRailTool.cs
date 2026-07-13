using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus.Views.Rail.Contracts;

internal interface IAssetRailTool : IRailToolView
{
	event EventHandler<AssetOpenRequest>? FileOpenRequested;

	event EventHandler<AssetOpenRequest>? AssetInteractionRequested;

	void SetRootPath(string rootPath);

	void RefreshTree();

	string FixedWorkflowsPath { get; set; }

	bool CanHandleKeyboardShortcut(NexusKey key, bool ctrl, bool shift);

	bool TryHandleKeyboardShortcut(NexusKey key, bool ctrl, bool shift);
}
