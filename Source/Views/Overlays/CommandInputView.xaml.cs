namespace ComfyUI_Nexus.Views.Overlays;

using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Ui.Popups;

public partial class CommandInputView : ContentView, INexusPopupSurface
{
	private const double HiddenInputOffsetY = 20;
	private const double HideInputOffsetY = 15;
	private const double HiddenInputScale = 0.92;
	private const double HideInputScale = 0.95;
	private const uint BackdropAnimationLength = 150;
	private const uint ShowInputAnimationLength = 200;
	private const uint HideInputAnimationLength = 150;

	internal event EventHandler<TappedEventArgs>? BackdropTapped;
	internal event EventHandler? Completed;

	public CommandInputView()
	{
		InitializeComponent();
	}

	internal bool IsOverlayVisible => CommandInputOverlayGrid.IsVisible;
	internal bool IsOverlayAtState(bool isVisible)
		=> CommandInputOverlayGrid.IsVisible == isVisible
		   && Math.Abs(CommandInputOverlayGrid.Opacity - (isVisible ? 1 : 0)) < 0.01;

	public string PopupKey => "CommandInput";
	public string PopupGroup => "SmallMenu";
	public VisualElement PopupRoot => CommandInputOverlayGrid;

	public bool IsShown(bool visible)
		=> IsOverlayAtState(visible);

	internal string GetCommandText()
		=> CommandInputEntry.Text ?? string.Empty;

	public void PrepareShowShell(NexusPopupOpenContext context)
	{
		CommandInputOverlayGrid.IsVisible = true;
		CommandInputOverlayGrid.Opacity = 0;
		CommandInputOverlayGrid.InputTransparent = true;
		InputTransparent = true;
		CommandInputBorder.TranslationY = HiddenInputOffsetY;
		CommandInputBorder.Scale = HiddenInputScale;
		CommandInputEntry.Text = string.Empty;
	}

	public void ActivateInput(NexusPopupOpenContext context)
	{
		CommandInputOverlayGrid.InputTransparent = false;
		InputTransparent = false;
	}

	public void PrepareHide()
	{
		CommandInputOverlayGrid.InputTransparent = true;
		InputTransparent = true;
	}

	public void ResetHiddenState()
	{
		CommandInputOverlayGrid.IsVisible = false;
		CommandInputOverlayGrid.Opacity = 0;
		CommandInputBorder.TranslationY = 0;
		CommandInputBorder.Scale = 1;
	}

	internal void FocusInput()
	{
		CommandInputEntry.Focus();
	}

	public Task AnimateShowAsync(NexusPopupOpenContext context)
		=> AnimateInputAsync(1, 0, 1, ShowInputAnimationLength, Easing.CubicOut);

	public Task RefreshContentAsync(NexusPopupOpenContext context)
	{
		FocusInput();
		return Task.CompletedTask;
	}

	public Task AnimateHideAsync(NexusPopupOpenContext context)
		=> AnimateInputAsync(0, HideInputOffsetY, HideInputScale, HideInputAnimationLength, Easing.CubicIn);

	private Task AnimateInputAsync(double backdropOpacity, double offsetY, double scale, uint transformLength, Easing easing)
	{
		SafeAnimation.AbortAnimation(CommandInputOverlayGrid, "CommandInput.Backdrop", "CommandInput");
		SafeAnimation.AbortAnimation(CommandInputBorder, "CommandInput.Transform", "CommandInput");
		return Task.WhenAll(
			SafeAnimation.TweenAsync(
				CommandInputOverlayGrid,
				"CommandInput.Backdrop",
				value => CommandInputOverlayGrid.Opacity = value,
				CommandInputOverlayGrid.Opacity,
				backdropOpacity,
				16,
				BackdropAnimationLength,
				easing,
				source: "CommandInput"),
			SafeAnimation.FadeTranslateScaleToAsync(CommandInputBorder, "CommandInput.Transform", CommandInputBorder.Opacity, offsetY, scale, transformLength, easing, "CommandInput"));
	}

	private void OnBackdropTapped(object? sender, TappedEventArgs e) => BackdropTapped?.Invoke(sender, e);

	private void OnEntryCompleted(object? sender, EventArgs e) => Completed?.Invoke(sender, e);
}
