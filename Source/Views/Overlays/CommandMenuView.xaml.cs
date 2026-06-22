using ComfyUI_Nexus.Platform;

namespace ComfyUI_Nexus.Views.Overlays;

public partial class CommandMenuView : ContentView
{
	private const double HiddenMenuScale = 0.94;
	private const double HiddenMenuOffsetY = -10;
	private const double HideMenuScale = 0.96;
	private const double HideMenuOffsetY = -6;
	private const double ShownGlowOpacity = 0.18;
	private const uint ShowAnimationLength = 150;
	private const uint ShowGlowAnimationLength = 220;
	private const uint HideAnimationLength = 100;
	private const uint HideGlowAnimationLength = 130;
	private const string GlowAnimationName = "CommandMenuGlow";

	internal event EventHandler? RestartServerRequested;
	internal event EventHandler? SettingsRequested;
	internal event EventHandler? HelpRequested;
	internal event EventHandler? AboutRequested;

	public CommandMenuView()
	{
		InitializeComponent();
	}

	internal bool IsMenuVisible => IsVisible;

	internal bool IsPointerOverMenuBody()
		=> PlatformManager.Current.Cursor.IsPointerOver(CommandMenuBorder);

	internal bool IsShown(bool isVisible)
		=> IsVisible == isVisible && Math.Abs(Opacity - (isVisible ? 1 : 0)) < 0.01;

	internal void PrepareToShow()
	{
		IsVisible = true;
		InputTransparent = false;
		CommandMenuGlow.Opacity = 0;
		Scale = HiddenMenuScale;
		TranslationY = HiddenMenuOffsetY;
	}

	internal void PrepareToHide()
	{
		InputTransparent = true;
	}

	internal void ResetHiddenState()
	{
		IsVisible = false;
		Scale = 1;
		CommandMenuGlow.Opacity = 0;
	}

	internal Task AnimateShowAsync()
		=> AnimateShowCoreAsync();

	private async Task AnimateShowCoreAsync()
	{
		await AnimateMenuAsync(1, 0, 1, ShowAnimationLength, Easing.CubicOut);
		await AnimateMenuGlowAsync(ShownGlowOpacity, ShowGlowAnimationLength, Easing.CubicOut);
	}

	internal Task AnimateHideAsync()
	{
		return Task.WhenAll(
			AnimateMenuGlowAsync(0, HideGlowAnimationLength, Easing.CubicIn),
			AnimateMenuAsync(0, HideMenuOffsetY, HideMenuScale, HideAnimationLength, Easing.CubicIn));
	}

	private Task AnimateMenuAsync(double opacity, double offsetY, double scale, uint length, Easing easing)
		=> Task.WhenAll(
			this.FadeToAsync(opacity, length, easing),
			this.TranslateToAsync(0, offsetY, length, easing),
			this.ScaleToAsync(scale, length, easing));

	private Task AnimateMenuGlowAsync(double targetOpacity, uint length, Easing easing)
	{
		var completion = new TaskCompletionSource();
		new Animation(value => CommandMenuGlow.Opacity = (float)value, CommandMenuGlow.Opacity, targetOpacity, easing)
			.Commit(this, GlowAnimationName, 16, length, finished: (_, _) => completion.TrySetResult());
		return completion.Task;
	}

	private void OnRestartServerClicked(object? sender, EventArgs e) => RestartServerRequested?.Invoke(sender, e);
	private void OnSettingsClicked(object? sender, EventArgs e) => SettingsRequested?.Invoke(sender, e);
	private void OnHelpClicked(object? sender, EventArgs e) => HelpRequested?.Invoke(sender, e);
	private void OnAboutClicked(object? sender, EventArgs e) => AboutRequested?.Invoke(sender, e);
}
