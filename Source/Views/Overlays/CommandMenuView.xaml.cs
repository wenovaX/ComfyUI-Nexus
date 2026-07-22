using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Ui.Popups;
using System.Windows.Input;

namespace ComfyUI_Nexus.Views.Overlays;

public partial class CommandMenuView : ContentView, INexusPopupSurface
{
	public static readonly BindableProperty RestartServerCommandProperty = BindableProperty.Create(nameof(RestartServerCommand), typeof(ICommand), typeof(CommandMenuView));
	public static readonly BindableProperty SettingsCommandProperty = BindableProperty.Create(nameof(SettingsCommand), typeof(ICommand), typeof(CommandMenuView));
	public static readonly BindableProperty HelpCommandProperty = BindableProperty.Create(nameof(HelpCommand), typeof(ICommand), typeof(CommandMenuView));
	public static readonly BindableProperty AboutCommandProperty = BindableProperty.Create(nameof(AboutCommand), typeof(ICommand), typeof(CommandMenuView));
	public static readonly BindableProperty ExitCommandProperty = BindableProperty.Create(nameof(ExitCommand), typeof(ICommand), typeof(CommandMenuView));

	private const double HiddenMenuScale = 0.94;
	private const double HiddenMenuOffsetY = -10;
	private const double HideMenuScale = 0.96;
	private const double HideMenuOffsetY = -6;
	private const uint ShowAnimationLength = 150;
	private const uint HideAnimationLength = 100;

	public CommandMenuView()
	{
		InitializeComponent();
	}

	public ICommand? RestartServerCommand
	{
		get => (ICommand?)GetValue(RestartServerCommandProperty);
		set => SetValue(RestartServerCommandProperty, value);
	}

	public ICommand? SettingsCommand
	{
		get => (ICommand?)GetValue(SettingsCommandProperty);
		set => SetValue(SettingsCommandProperty, value);
	}

	public ICommand? HelpCommand
	{
		get => (ICommand?)GetValue(HelpCommandProperty);
		set => SetValue(HelpCommandProperty, value);
	}

	public ICommand? AboutCommand
	{
		get => (ICommand?)GetValue(AboutCommandProperty);
		set => SetValue(AboutCommandProperty, value);
	}

	public ICommand? ExitCommand
	{
		get => (ICommand?)GetValue(ExitCommandProperty);
		set => SetValue(ExitCommandProperty, value);
	}

	internal bool IsMenuVisible => IsVisible;

	public string PopupKey => "CommandMenu";
	public string PopupGroup => "SmallMenu";
	public VisualElement PopupRoot => this;

	internal bool IsPointerOverMenuBody()
		=> NexusAppManager.Instance.Platform.Cursor.IsPointerOver(CommandMenuBorder);

	public bool IsShown(bool isVisible)
		=> IsVisible == isVisible && Math.Abs(Opacity - (isVisible ? 1 : 0)) < 0.01;

	public void PrepareShowShell(NexusPopupOpenContext context)
	{
		IsVisible = true;
		Opacity = 0;
		InputTransparent = true;
		Scale = HiddenMenuScale;
		TranslationY = HiddenMenuOffsetY;
	}

	public void ActivateInput(NexusPopupOpenContext context)
	{
		InputTransparent = false;
	}

	public void PrepareHide()
	{
		InputTransparent = true;
	}

	public void ResetHiddenState()
	{
		IsVisible = false;
		Opacity = 0;
		Scale = 1;
		TranslationY = 0;
	}

	public Task AnimateShowAsync(NexusPopupOpenContext context)
		=> AnimateShowCoreAsync();

	public Task RefreshContentAsync(NexusPopupOpenContext context)
		=> Task.CompletedTask;

	private Task AnimateShowCoreAsync()
		=> AnimateMenuAsync(1, 0, 1, ShowAnimationLength, Easing.CubicOut);

	public Task AnimateHideAsync(NexusPopupOpenContext context)
	{
		return AnimateMenuAsync(0, HideMenuOffsetY, HideMenuScale, HideAnimationLength, Easing.CubicIn);
	}

	private Task AnimateMenuAsync(double opacity, double offsetY, double scale, uint length, Easing easing)
		=> SafeAnimation.FadeTranslateScaleToAsync(this, "CommandMenu.Transform", opacity, offsetY, scale, length, easing, "CommandMenu");

}
