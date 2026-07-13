namespace ComfyUI_Nexus.Ui.Popups;

public sealed record NexusPopupOpenContext(
	double? TopOffset = null,
	bool CaptureFocus = true,
	bool RefocusAfterShow = true,
	bool RestoreFocusOnClose = true);
