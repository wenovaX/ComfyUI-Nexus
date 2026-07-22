namespace ComfyUI_Nexus.Views.Overlays;

using ComfyUI_Nexus.Ui;
#if WINDOWS
using Microsoft.UI.Xaml.Input;
#endif

public partial class ProductSetupView
{
#if WINDOWS
	private void AttachNativeInitiationScrollDragHandlers()
	{
		if (_nativeScrollDragAttached) return;

		AttachNativeInitiationScrollDragHandler(VanguardInitiationScrollView);
		AttachNativeInitiationScrollDragHandler(ArchitectInitiationScrollView);
		_nativeScrollDragAttached = true;
	}

	private void AttachNativeInitiationScrollDragHandler(ScrollView scrollView)
	{
		if (scrollView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.ScrollViewer nativeScrollViewer)
		{
			scrollView.HandlerChanged += OnInitiationScrollViewHandlerChanged;
			return;
		}

		nativeScrollViewer.PointerPressed -= OnNativeInitiationScrollPointerPressed;
		nativeScrollViewer.PointerMoved -= OnNativeInitiationScrollPointerMoved;
		nativeScrollViewer.PointerReleased -= OnNativeInitiationScrollPointerReleased;
		nativeScrollViewer.PointerCanceled -= OnNativeInitiationScrollPointerReleased;

		nativeScrollViewer.PointerPressed += OnNativeInitiationScrollPointerPressed;
		nativeScrollViewer.PointerMoved += OnNativeInitiationScrollPointerMoved;
		nativeScrollViewer.PointerReleased += OnNativeInitiationScrollPointerReleased;
		nativeScrollViewer.PointerCanceled += OnNativeInitiationScrollPointerReleased;
		nativeScrollViewer.VerticalScrollMode = _isInitiationUserScrollBlocked
			? Microsoft.UI.Xaml.Controls.ScrollMode.Disabled
			: Microsoft.UI.Xaml.Controls.ScrollMode.Enabled;
	}

	private void OnInitiationScrollViewHandlerChanged(object? sender, EventArgs e)
	{
		if (sender is not ScrollView scrollView) return;

		scrollView.HandlerChanged -= OnInitiationScrollViewHandlerChanged;
		AttachNativeInitiationScrollDragHandler(scrollView);
	}

	private void DetachNativeInitiationScrollDragHandlers()
	{
		DetachNativeInitiationScrollDragHandler(VanguardInitiationScrollView);
		DetachNativeInitiationScrollDragHandler(ArchitectInitiationScrollView);
		_nativeScrollDragAttached = false;
		_isNativeInitiationScrollDragging = false;
		_nativeDraggedInitiationScrollViewer = null;
	}

	private void DetachNativeInitiationScrollDragHandler(ScrollView scrollView)
	{
		scrollView.HandlerChanged -= OnInitiationScrollViewHandlerChanged;

		if (scrollView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.ScrollViewer nativeScrollViewer)
		{
			return;
		}

		nativeScrollViewer.PointerPressed -= OnNativeInitiationScrollPointerPressed;
		nativeScrollViewer.PointerMoved -= OnNativeInitiationScrollPointerMoved;
		nativeScrollViewer.PointerReleased -= OnNativeInitiationScrollPointerReleased;
		nativeScrollViewer.PointerCanceled -= OnNativeInitiationScrollPointerReleased;
	}

	private void OnNativeInitiationScrollPointerPressed(object sender, PointerRoutedEventArgs e)
	{
		if (_isInitiationUserScrollBlocked)
		{
			e.Handled = true;
			return;
		}

		if (sender is not Microsoft.UI.Xaml.Controls.ScrollViewer scrollViewer) return;

		var point = e.GetCurrentPoint(scrollViewer);
		if (!point.Properties.IsLeftButtonPressed) return;

		_isNativeInitiationScrollDragging = true;
		_nativeDraggedInitiationScrollViewer = scrollViewer;
		_nativeDraggedInitiationScrollContent = GetNativeInitiationScrollContent(scrollViewer);
		_nativeDraggedInitiationScrollContent?.CancelAnimations();
		_nativeInitiationDragStartY = point.Position.Y;
		_nativeInitiationDragStartOffsetY = scrollViewer.VerticalOffset;
		scrollViewer.CapturePointer(e.Pointer);
		e.Handled = true;
	}

	private void OnNativeInitiationScrollPointerMoved(object sender, PointerRoutedEventArgs e)
	{
		if (!_isNativeInitiationScrollDragging) return;
		if (sender is not Microsoft.UI.Xaml.Controls.ScrollViewer scrollViewer) return;
		if (!ReferenceEquals(_nativeDraggedInitiationScrollViewer, scrollViewer)) return;

		var point = e.GetCurrentPoint(scrollViewer);
		if (!point.Properties.IsLeftButtonPressed)
		{
			EndNativeInitiationScrollDrag(scrollViewer, e);
			return;
		}

		double dragDeltaY = point.Position.Y - _nativeInitiationDragStartY;
		double rawTargetOffsetY = _nativeInitiationDragStartOffsetY - dragDeltaY;
		double targetOffsetY = Math.Clamp(
			rawTargetOffsetY,
			0,
			Math.Max(0, scrollViewer.ScrollableHeight));

		scrollViewer.ChangeView(null, targetOffsetY, null, disableAnimation: true);
		ApplyNativeInitiationOverscroll(rawTargetOffsetY, scrollViewer.ScrollableHeight);
		e.Handled = true;
	}

	private void OnNativeInitiationScrollPointerReleased(object sender, PointerRoutedEventArgs e)
	{
		if (sender is Microsoft.UI.Xaml.Controls.ScrollViewer scrollViewer)
		{
			EndNativeInitiationScrollDrag(scrollViewer, e);
		}
	}

	private void EndNativeInitiationScrollDrag(Microsoft.UI.Xaml.Controls.ScrollViewer scrollViewer, PointerRoutedEventArgs e)
	{
		_isNativeInitiationScrollDragging = false;
		_nativeDraggedInitiationScrollViewer = null;
		_ = ReturnNativeInitiationOverscrollAsync(_nativeDraggedInitiationScrollContent);
		_nativeDraggedInitiationScrollContent = null;
		scrollViewer.ReleasePointerCapture(e.Pointer);
		e.Handled = true;
	}

	private View? GetNativeInitiationScrollContent(Microsoft.UI.Xaml.Controls.ScrollViewer nativeScrollViewer)
	{
		if (ReferenceEquals(VanguardInitiationScrollView.Handler?.PlatformView, nativeScrollViewer))
		{
			return VanguardInitiationScrollView.Content;
		}

		if (ReferenceEquals(ArchitectInitiationScrollView.Handler?.PlatformView, nativeScrollViewer))
		{
			return ArchitectInitiationScrollView.Content;
		}

		return null;
	}

	private void ApplyNativeInitiationOverscroll(double rawTargetOffsetY, double maxOffsetY)
	{
		if (_nativeDraggedInitiationScrollContent == null) return;

		double overscroll = 0;
		if (rawTargetOffsetY < 0)
		{
			overscroll = GetRubberBandOffset(-rawTargetOffsetY);
		}
		else if (rawTargetOffsetY > maxOffsetY)
		{
			overscroll = -GetRubberBandOffset(rawTargetOffsetY - maxOffsetY);
		}

		_nativeDraggedInitiationScrollContent.TranslationY = overscroll;
	}

	private static double GetRubberBandOffset(double overscroll)
		=> Math.Min(InitiationOverscrollMaxOffset, overscroll * InitiationOverscrollResistance);

	private static async Task ReturnNativeInitiationOverscrollAsync(View? content)
	{
		if (content == null || Math.Abs(content.TranslationY) < 0.5)
		{
			return;
		}

		try
		{
			await SafeAnimation.TranslateToAsync(content, 0, 0, InitiationOverscrollReturnLength, Easing.SpringOut, "Setup.InitiationScroll");
		}
		catch
		{
			content.TranslationY = 0;
		}
	}
#else
	private void AttachNativeInitiationScrollDragHandlers()
	{
	}

	private void DetachNativeInitiationScrollDragHandlers()
	{
	}
#endif
}
