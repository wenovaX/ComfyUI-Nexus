#if WINDOWS
namespace ComfyUI_Nexus.Platform.Windows;

public sealed class WindowsInteractionAddon : UnsupportedPlatformInteractionAddon
{
	public override void AttachDoubleTap(View element, Func<Task> handler)
	{
		base.AttachDoubleTap(element, handler);
	}
}
#endif
