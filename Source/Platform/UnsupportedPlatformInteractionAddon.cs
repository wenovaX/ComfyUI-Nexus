namespace ComfyUI_Nexus.Platform;

public class UnsupportedPlatformInteractionAddon : IPlatformInteractionAddon
{
	public virtual void AttachDoubleTap(View element, Func<Task> handler)
	{
		var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
		doubleTap.Tapped += async (sender, args) => await handler();
		element.GestureRecognizers.Add(doubleTap);
	}
}
