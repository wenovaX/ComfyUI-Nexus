namespace ComfyUI_Nexus.Platform;

public sealed class UnsupportedPlatformCursorService : IPlatformCursorService
{
	public void SetCursor(NexusCursorShape shape)
	{
	}

	public void SetCursor(VisualElement? element, NexusCursorShape shape)
	{
	}

	public void SetCssCursor(string cssCursor)
	{
	}

	public bool IsPointerOver(VisualElement? element) => false;

	public Point? GetPointerPositionRelativeTo(VisualElement? element) => null;

	public bool IsPrimaryPointerPressed() => false;

	public void AttachResizeHandleCursor(
		VisualElement handleElement,
		Action pointerEntered,
		Action pointerExited,
		Action pointerPressed)
	{
	}

	public void AttachDynamicCursorSurface(
		VisualElement element,
		Func<NexusCursorShape> cursorShapeProvider,
		Action pointerEntered,
		Action pointerMoved,
		Action pointerExited,
		Action pointerPressed,
		Action pointerReleased)
	{
	}

	void IPlatformCursorService.SetCssCursor(VisualElement? element, string cssCursor)
	{
	}
}
