namespace ComfyUI_Nexus.Views.Overlays;

using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Ui;

public partial class AboutOverlayView : ContentView
{
	private const double HiddenPanelScale = 0.98;
	private const double HiddenPanelOffsetY = 12;
	private const double HidePanelOffsetY = 10;
	private const double ShownGlowOpacity = 0.18;
	private const uint ShowAnimationLength = 170;
	private const uint ShowGlowAnimationLength = 260;
	private const uint HideAnimationLength = 120;
	private const uint HideGlowAnimationLength = 150;
	private const int PanelGlowAnimationRate = 16;
	private const string PanelGlowAnimationName = "AboutPanelGlow";
	private static readonly Color ServerUrlHoverColor = NexusColors.AccentHover;
	private static readonly Color ServerUrlNormalColor = NexusColors.Accent;

	private string _serverUrl = string.Empty;
	private bool _isLayoutPrewarmed;

	internal event EventHandler? CloseRequested;

	public AboutOverlayView()
	{
		InitializeComponent();
	}

	internal bool IsShown(bool isVisible)
		=> IsVisible == isVisible && Math.Abs(Opacity - (isVisible ? 1 : 0)) < 0.01;

	internal async Task PrewarmLayoutAsync()
	{
		if (_isLayoutPrewarmed)
		{
			return;
		}

		bool wasVisible = IsVisible;
		bool wasInputTransparent = InputTransparent;
		double previousOpacity = Opacity;
		double previousScale = Scale;
		double previousTranslationY = TranslationY;
		float previousGlowOpacity = AboutPanelGlow.Opacity;

		try
		{
			PrepareToShow();
			InputTransparent = true;
			Opacity = 0;
			Scale = 1;
			TranslationY = 0;
			await Task.Yield();
			InvalidateMeasure();
			await Task.Yield();
			ResetHiddenState();
			_isLayoutPrewarmed = true;
		}
		finally
		{
			IsVisible = wasVisible;
			InputTransparent = wasInputTransparent;
			Opacity = previousOpacity;
			Scale = previousScale;
			TranslationY = previousTranslationY;
			AboutPanelGlow.Opacity = previousGlowOpacity;
		}
	}

	internal void SetDetails(string version, string comfyPath, string serverUrl, string pythonMode)
	{
		_serverUrl = serverUrl;
		VersionValueLabel.Text = version;
		ComfyPathValueLabel.Text = comfyPath;
		ServerUrlValueLabel.Text = serverUrl;
		PythonModeValueLabel.Text = pythonMode;
	}

	internal void PrepareToShow()
	{
		IsVisible = true;
		InputTransparent = false;
		AboutPanelGlow.Opacity = 0;
		Scale = HiddenPanelScale;
		TranslationY = HiddenPanelOffsetY;
	}

	internal void PrepareToHide()
	{
		InputTransparent = true;
	}

	internal void ResetHiddenState()
	{
		IsVisible = false;
		Scale = 1;
		TranslationY = 0;
		AboutPanelGlow.Opacity = 0;
	}

	internal Task AnimateShowAsync()
		=> AnimateShowCoreAsync();

	private Task AnimateShowCoreAsync()
		=> AnimatePanelShowAsync();

	private async Task AnimatePanelShowAsync()
	{
		await Task.WhenAll(
			this.FadeToAsync(1, ShowAnimationLength, Easing.CubicOut),
			this.TranslateToAsync(0, 0, ShowAnimationLength, Easing.CubicOut),
			this.ScaleToAsync(1, ShowAnimationLength, Easing.CubicOut));
		await AnimatePanelGlowAsync(ShownGlowOpacity, ShowGlowAnimationLength, Easing.CubicOut);
	}

	internal Task AnimateHideAsync()
	{
		return Task.WhenAll(
			AnimatePanelGlowAsync(0, HideGlowAnimationLength, Easing.CubicIn),
			this.FadeToAsync(0, HideAnimationLength, Easing.CubicIn),
			this.TranslateToAsync(0, HidePanelOffsetY, HideAnimationLength, Easing.CubicIn),
			this.ScaleToAsync(HiddenPanelScale, HideAnimationLength, Easing.CubicIn));
	}

	private Task AnimatePanelGlowAsync(double targetOpacity, uint length, Easing easing)
	{
		var completion = new TaskCompletionSource();
		new Animation(value => AboutPanelGlow.Opacity = (float)value, AboutPanelGlow.Opacity, targetOpacity, easing)
			.Commit(this, PanelGlowAnimationName, PanelGlowAnimationRate, length, finished: (_, _) => completion.TrySetResult());
		return completion.Task;
	}

	private void OnCloseClicked(object? sender, EventArgs e) => CloseRequested?.Invoke(this, e);

	private async void OnServerUrlTapped(object? sender, TappedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(_serverUrl))
		{
			return;
		}

		try
		{
			await Launcher.Default.OpenAsync(_serverUrl);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, $"[ABOUT] Failed to open server URL: {_serverUrl}");
		}
	}

	private void OnServerUrlPointerEntered(object? sender, PointerEventArgs e)
	{
		ServerUrlValueLabel.TextColor = ServerUrlHoverColor;
	}

	private void OnServerUrlPointerExited(object? sender, PointerEventArgs e)
	{
		ServerUrlValueLabel.TextColor = ServerUrlNormalColor;
	}
}
