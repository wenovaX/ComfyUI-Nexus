namespace ComfyUI_Nexus.Platform;

public interface IPlatformCursorService
{
	void SetCursor(NexusCursorShape shape);

	void SetCursor(VisualElement? element, NexusCursorShape shape);

	void SetCssCursor(string cssCursor);

	void SetCssCursor(VisualElement? element, string cssCursor);

	bool IsPointerOver(VisualElement? element);

	Point? GetPointerPositionRelativeTo(VisualElement? element);

	bool IsPrimaryPointerPressed();

	void AttachResizeHandleCursor(
		VisualElement handleElement,
		Action pointerEntered,
		Action pointerExited,
		Action pointerPressed);

	void AttachDynamicCursorSurface(
		VisualElement element,
		Func<NexusCursorShape> cursorShapeProvider,
		Action pointerEntered,
		Action pointerMoved,
		Action pointerExited,
		Action pointerPressed,
		Action pointerReleased);
}
