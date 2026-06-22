namespace ComfyUI_Nexus.Platform;

public interface IPlatformKeyboardState
{
	bool IsCtrlPressed();

	bool IsShiftPressed();

	bool IsAltPressed();

	bool IsNativeTextInputFocused(Element? scope);

	NexusKey ToNexusKey(object? platformKey);
}
