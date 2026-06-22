namespace ComfyUI_Nexus.Views.Overlays;

public partial class CanvasModeMenuView : ContentView
{
	private const double HiddenMenuOffsetY = 8;
	private const double HideMenuOffsetY = 6;
	private const double HiddenMenuScaleY = 0.92;
	private const double HideMenuScaleY = 0.96;
	private const uint ShowAnimationLength = 120;
	private const uint HideAnimationLength = 100;

	internal event EventHandler? DismissRequested;
	internal event Action<string>? ModeRequested;

	public CanvasModeMenuView()
	{
		InitializeComponent();
	}

	internal bool IsOpen => CanvasModeOverlayRoot.IsVisible;

	internal bool IsShown(bool isVisible)
		=> CanvasModeOverlayRoot.IsVisible == isVisible && Math.Abs(CanvasModeMenuBorder.Opacity - (isVisible ? 1 : 0)) < 0.01;

	internal void PrepareToShow()
	{
		CanvasModeOverlayRoot.IsVisible = true;
		CanvasModeOverlayRoot.InputTransparent = false;
		CanvasModeMenuBorder.InputTransparent = false;
		CanvasModeMenuBorder.TranslationY = HiddenMenuOffsetY;
		CanvasModeMenuBorder.ScaleY = HiddenMenuScaleY;
		CanvasModeMenuBorder.Opacity = 0;
	}

	internal void PrepareToHide()
	{
		CanvasModeOverlayRoot.InputTransparent = true;
		CanvasModeMenuBorder.InputTransparent = true;
	}

	internal void ResetHiddenState()
	{
		CanvasModeOverlayRoot.IsVisible = false;
		CanvasModeMenuBorder.ScaleY = 1;
	}

	internal Task AnimateShowAsync()
		=> AnimateMenuAsync(1, 0, 1, ShowAnimationLength, Easing.CubicOut);

	internal Task AnimateHideAsync()
		=> AnimateMenuAsync(0, HideMenuOffsetY, HideMenuScaleY, HideAnimationLength, Easing.CubicIn);

	private Task AnimateMenuAsync(double opacity, double offsetY, double scaleY, uint length, Easing easing)
		=> Task.WhenAll(
			CanvasModeMenuBorder.FadeToAsync(opacity, length, easing),
			CanvasModeMenuBorder.TranslateToAsync(0, offsetY, length, easing),
			CanvasModeMenuBorder.ScaleYToAsync(scaleY, length, easing));

	private void OnSelectClicked(object? sender, EventArgs e) => ModeRequested?.Invoke("Select");
	private void OnHandClicked(object? sender, EventArgs e) => ModeRequested?.Invoke("Hand");
	private void OnBackdropTapped(object? sender, TappedEventArgs e) => DismissRequested?.Invoke(this, EventArgs.Empty);
}
