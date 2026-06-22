namespace ComfyUI_Nexus.Platform;

public sealed class UnsupportedPlatformKeyboardState : IPlatformKeyboardState
{
	public bool IsCtrlPressed() => false;

	public bool IsShiftPressed() => false;

	public bool IsAltPressed() => false;

	public bool IsNativeTextInputFocused(Element? scope) => false;

	public NexusKey ToNexusKey(object? platformKey) => NexusKey.Unknown;
}
