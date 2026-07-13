namespace ComfyUI_Nexus.Views.Overlays;

using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Ui.Popups;

public partial class AboutOverlayView : ContentView, INexusPopupSurface
{
	private const double HiddenPanelScale = 0.98;
	private const double HiddenPanelOffsetY = 12;
	private const double HidePanelOffsetY = 10;
	private const uint ShowAnimationLength = 170;
	private const uint HideAnimationLength = 120;
	private static readonly Color ServerUrlHoverColor = NexusColors.AccentHover;
	private static readonly Color ServerUrlNormalColor = NexusColors.Accent;

	private string _serverUrl = string.Empty;
	private string _version = string.Empty;
	private string _comfyPath = string.Empty;
	private string _pythonMode = string.Empty;
	private bool _isLayoutPrewarmed;

	internal event EventHandler? CloseRequested;

	public string PopupKey => "About";
	public string PopupGroup => "Overlay";
	public VisualElement PopupRoot => this;

	public AboutOverlayView()
	{
		InitializeComponent();
	}

	public bool IsShown(bool isVisible)
		=> IsVisible == isVisible && Math.Abs(Opacity - (isVisible ? 1 : 0)) < 0.01;

	internal async Task PrewarmLayoutAsync()
	{
		if (_isLayoutPrewarmed)
		{
			return;
		}

		try
		{
			InvalidateMeasure();
			await NexusUiFrame.AwaitDispatcherTurnAsync(this, "ABOUT:Prewarm");
			_isLayoutPrewarmed = true;
		}
		catch (Exception ex)
		{
			NexusLog.Trace($"[ABOUT] Layout prewarm skipped: {ex.Message}");
		}
	}

	internal void SetDetails(string version, string comfyPath, string serverUrl, string pythonMode)
	{
		if (string.Equals(_version, version, StringComparison.Ordinal)
			&& string.Equals(_comfyPath, comfyPath, StringComparison.Ordinal)
			&& string.Equals(_serverUrl, serverUrl, StringComparison.Ordinal)
			&& string.Equals(_pythonMode, pythonMode, StringComparison.Ordinal))
		{
			return;
		}

		_version = version;
		_comfyPath = comfyPath;
		_serverUrl = serverUrl;
		_pythonMode = pythonMode;
		VersionValueLabel.Text = version;
		ComfyPathValueLabel.Text = comfyPath;
		ServerUrlValueLabel.Text = serverUrl;
		PythonModeValueLabel.Text = pythonMode;
	}

	public void PrepareShowShell(NexusPopupOpenContext context)
	{
		IsVisible = true;
		Opacity = 0;
		InputTransparent = true;
		Scale = HiddenPanelScale;
		TranslationY = HiddenPanelOffsetY;
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
		=> AnimatePanelShowAsync();

	private Task AnimatePanelShowAsync()
		=> SafeAnimation.FadeTranslateScaleToAsync(this, "About.Show", 1, 0, 1, ShowAnimationLength, Easing.CubicOut, "About.Show");

	public Task AnimateHideAsync(NexusPopupOpenContext context)
	{
		return SafeAnimation.FadeTranslateScaleToAsync(this, "About.Hide", 0, HidePanelOffsetY, HiddenPanelScale, HideAnimationLength, Easing.CubicIn, "About.Hide");
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
