using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Ui.Popups;
using System.Windows.Input;

namespace ComfyUI_Nexus.Views.Overlays;

public partial class CanvasModeMenuView : ContentView, INexusPopupSurface
{
	public static readonly BindableProperty DismissCommandProperty = BindableProperty.Create(nameof(DismissCommand), typeof(ICommand), typeof(CanvasModeMenuView));
	public static readonly BindableProperty SelectCommandProperty = BindableProperty.Create(nameof(SelectCommand), typeof(ICommand), typeof(CanvasModeMenuView));
	public static readonly BindableProperty HandCommandProperty = BindableProperty.Create(nameof(HandCommand), typeof(ICommand), typeof(CanvasModeMenuView));

	private const double HiddenMenuOffsetY = 8;
	private const double HideMenuOffsetY = 6;
	private const double HiddenMenuScaleY = 0.92;
	private const double HideMenuScaleY = 0.96;
	private const uint ShowAnimationLength = 120;
	private const uint HideAnimationLength = 100;

	public CanvasModeMenuView()
	{
		InitializeComponent();
	}

	public ICommand? DismissCommand
	{
		get => (ICommand?)GetValue(DismissCommandProperty);
		set => SetValue(DismissCommandProperty, value);
	}

	public ICommand? SelectCommand
	{
		get => (ICommand?)GetValue(SelectCommandProperty);
		set => SetValue(SelectCommandProperty, value);
	}

	public ICommand? HandCommand
	{
		get => (ICommand?)GetValue(HandCommandProperty);
		set => SetValue(HandCommandProperty, value);
	}

	internal bool IsOpen => CanvasModeOverlayRoot.IsVisible;

	public string PopupKey => "CanvasMode";
	public string PopupGroup => "SmallMenu";
	public VisualElement PopupRoot => CanvasModeOverlayRoot;

	public bool IsShown(bool isVisible)
		=> CanvasModeOverlayRoot.IsVisible == isVisible && Math.Abs(CanvasModeMenuBorder.Opacity - (isVisible ? 1 : 0)) < 0.01;

	public void PrepareShowShell(NexusPopupOpenContext context)
	{
		CanvasModeOverlayRoot.IsVisible = true;
		CanvasModeOverlayRoot.InputTransparent = true;
		CanvasModeMenuBorder.InputTransparent = true;
		CanvasModeMenuBorder.TranslationY = HiddenMenuOffsetY;
		CanvasModeMenuBorder.ScaleY = HiddenMenuScaleY;
		CanvasModeMenuBorder.Opacity = 0;
	}

	public void ActivateInput(NexusPopupOpenContext context)
	{
		CanvasModeOverlayRoot.InputTransparent = false;
		CanvasModeMenuBorder.InputTransparent = false;
	}

	public void PrepareHide()
	{
		CanvasModeOverlayRoot.InputTransparent = true;
		CanvasModeMenuBorder.InputTransparent = true;
	}

	public void ResetHiddenState()
	{
		CanvasModeOverlayRoot.IsVisible = false;
		CanvasModeMenuBorder.Opacity = 0;
		CanvasModeMenuBorder.TranslationY = 0;
		CanvasModeMenuBorder.ScaleY = 1;
	}

	public Task AnimateShowAsync(NexusPopupOpenContext context)
		=> AnimateMenuAsync(1, 0, 1, ShowAnimationLength, Easing.CubicOut);

	public Task RefreshContentAsync(NexusPopupOpenContext context)
		=> Task.CompletedTask;

	public Task AnimateHideAsync(NexusPopupOpenContext context)
		=> AnimateMenuAsync(0, HideMenuOffsetY, HideMenuScaleY, HideAnimationLength, Easing.CubicIn);

	private Task AnimateMenuAsync(double opacity, double offsetY, double scaleY, uint length, Easing easing)
		=> SafeAnimation.FadeTranslateScaleYToAsync(CanvasModeMenuBorder, "CanvasModeMenu.Transform", opacity, offsetY, scaleY, length, easing, "CanvasModeMenu");
}
