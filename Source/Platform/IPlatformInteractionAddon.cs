namespace ComfyUI_Nexus.Platform;

public interface IPlatformInteractionAddon
{
	void AttachDoubleTap(View element, Func<Task> handler);
}
