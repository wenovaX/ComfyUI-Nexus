namespace ComfyUI_Nexus.Ui.Popups;

internal interface INexusPopupSurface
{
	string PopupKey { get; }
	string PopupGroup { get; }
	VisualElement PopupRoot { get; }
	bool IsShown(bool visible);
	void PrepareShowShell(NexusPopupOpenContext context);
	void ActivateInput(NexusPopupOpenContext context);
	Task AnimateShowAsync(NexusPopupOpenContext context);
	Task RefreshContentAsync(NexusPopupOpenContext context);
	void PrepareHide();
	Task AnimateHideAsync(NexusPopupOpenContext context);
	void ResetHiddenState();
}
