namespace ComfyUI_Nexus.Views.Overlays;

public partial class CommandInputView : ContentView
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

	internal string GetCommandText()
		=> CommandInputEntry.Text ?? string.Empty;

	internal void PrepareToShow()
	{
		CommandInputOverlayGrid.IsVisible = true;
		CommandInputOverlayGrid.InputTransparent = false;
		InputTransparent = false;
		CommandInputBorder.TranslationY = HiddenInputOffsetY;
		CommandInputBorder.Scale = HiddenInputScale;
		CommandInputEntry.Text = string.Empty;
	}

	internal void PrepareToHide()
	{
		CommandInputOverlayGrid.InputTransparent = true;
		InputTransparent = true;
	}

	internal void ResetHiddenState()
	{
		CommandInputOverlayGrid.IsVisible = false;
		CommandInputBorder.TranslationY = 0;
		CommandInputBorder.Scale = 1;
	}

	internal void FocusInput()
	{
		CommandInputEntry.Focus();
	}

	internal Task AnimateShowAsync()
		=> AnimateInputAsync(1, 0, 1, ShowInputAnimationLength, Easing.CubicOut);

	internal Task AnimateHideAsync()
		=> AnimateInputAsync(0, HideInputOffsetY, HideInputScale, HideInputAnimationLength, Easing.CubicIn);

	private Task AnimateInputAsync(double backdropOpacity, double offsetY, double scale, uint transformLength, Easing easing)
		=> Task.WhenAll(
			CommandInputOverlayGrid.FadeToAsync(backdropOpacity, BackdropAnimationLength, easing),
			CommandInputBorder.TranslateToAsync(0, offsetY, transformLength, easing),
			CommandInputBorder.ScaleToAsync(scale, transformLength, easing));

	private void OnBackdropTapped(object? sender, TappedEventArgs e) => BackdropTapped?.Invoke(sender, e);

	private void OnEntryCompleted(object? sender, EventArgs e) => Completed?.Invoke(sender, e);
}
