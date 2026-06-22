namespace ComfyUI_Nexus.Views.Overlays;

public partial class WorkflowDropdownView : ContentView
{
	private const double PrimaryRowAnchorY = 34;
	private const double SecondaryRowAnchorY = 62;
	private const double DropRevealOffset = 10;
	private const double HideRevealOffset = -10;
	private const uint RevealAnimationLength = 150;

	public WorkflowDropdownView()
	{
		InitializeComponent();
	}

	internal bool IsOpen => CustomDropdownOverlayBorder.IsVisible;

	internal void ClearItems()
	{
		CustomDropdownListStack.Children.Clear();
	}

	internal void AddItem(View item)
	{
		CustomDropdownListStack.Children.Add(item);
	}

	internal void PrepareToShow(bool isRow2)
	{
		IsVisible = true;
		InputTransparent = false;
		CustomDropdownOverlayBorder.TranslationY = GetAnchorY(isRow2);
		CustomDropdownOverlayBorder.InputTransparent = false;
		CustomDropdownOverlayBorder.IsVisible = true;
	}

	internal Task AnimateShowAsync(bool isRow2)
	{
		double target = GetAnchorY(isRow2) + DropRevealOffset;
		return AnimateDropdownAsync(1, target, Easing.CubicOut);
	}

	internal Task AnimateHideAsync()
		=> AnimateDropdownAsync(0, CustomDropdownOverlayBorder.TranslationY + HideRevealOffset, Easing.CubicIn);

	internal void CompleteHide()
	{
		CustomDropdownOverlayBorder.IsVisible = false;
		CustomDropdownOverlayBorder.InputTransparent = true;
		InputTransparent = true;
		IsVisible = false;
	}

	private static double GetAnchorY(bool isRow2)
		=> isRow2 ? SecondaryRowAnchorY : PrimaryRowAnchorY;

	private Task AnimateDropdownAsync(double opacity, double offsetY, Easing easing)
		=> Task.WhenAll(
			CustomDropdownOverlayBorder.FadeToAsync(opacity, RevealAnimationLength),
			CustomDropdownOverlayBorder.TranslateToAsync(0, offsetY, RevealAnimationLength, easing));
}
