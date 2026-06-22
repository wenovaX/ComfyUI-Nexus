namespace ComfyUI_Nexus.Views.Overlays;

using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Ui;

public partial class CanvasToolbarView : ContentView
{
	private static readonly Color ToggleActiveBackgroundColor = Color.FromArgb("#1d2a3d");
	private static readonly Color ToggleActiveTextColor = NexusColors.White;
	private static readonly Color ToggleInactiveTextColor = NexusColors.AccentText;

	internal event EventHandler? ModeToggled;
	internal event EventHandler? FitViewRequested;
	internal event EventHandler? ZoomRequested;
	internal event EventHandler? MinimapToggled;
	internal event EventHandler? LinksToggled;
	internal event EventHandler? HelpRequested;
	internal event EventHandler? TerminalRequested;
	internal event EventHandler? ShortcutsRequested;

	private bool _isMinimapActive = false;
	private bool _isLinksHidden = false;

	public CanvasToolbarView()
	{
		InitializeComponent();
	}

	private void OnModeClicked(object? sender, EventArgs e)
	{
		ModeToggled?.Invoke(this, e);
	}

	private void OnFitViewClicked(object? sender, EventArgs e) => FitViewRequested?.Invoke(this, e);
	private void OnZoomClicked(object? sender, EventArgs e) => ZoomRequested?.Invoke(this, e);

	private void OnHelpClicked(object? sender, EventArgs e) => HelpRequested?.Invoke(this, e);
	private void OnTerminalClicked(object? sender, EventArgs e) => TerminalRequested?.Invoke(this, e);
	private void OnShortcutsClicked(object? sender, EventArgs e) => ShortcutsRequested?.Invoke(this, e);

	private void OnMinimapClicked(object? sender, EventArgs e)
	{
		_isMinimapActive = !_isMinimapActive;
		ApplyToggleVisual(MinimapButton, _isMinimapActive);
		MinimapToggled?.Invoke(this, e);
	}

	private void OnLinksClicked(object? sender, EventArgs e)
	{
		_isLinksHidden = !_isLinksHidden;
		ApplyToggleVisual(LinksButton, _isLinksHidden);
		LinksToggled?.Invoke(this, e);
	}

	private void ApplyToggleVisual(Button btn, bool isActive)
	{
		if (isActive)
		{
			btn.BackgroundColor = ToggleActiveBackgroundColor;
			btn.TextColor = ToggleActiveTextColor;
		}
		else
		{
			btn.BackgroundColor = Colors.Transparent;
			btn.TextColor = ToggleInactiveTextColor;
		}
	}

	// Optional helpers to update text based on state from external source
	internal void SetMode(bool isHand)
	{
		ModeButton.Text = LocalizationManager.Text(isHand
			? "common.hand"
			: "common.select");
	}

	internal void SetZoom(string percentText)
	{
		ZoomButton.Text = percentText;
	}

	internal void SetTerminalActive(bool isActive)
	{
		ApplyToggleVisual(TerminalButton, isActive);
	}

	internal void SetShortcutsActive(bool isActive)
	{
		ApplyToggleVisual(ShortcutsButton, isActive);
	}

	internal void SetMinimapActive(bool isActive)
	{
		_isMinimapActive = isActive;
		ApplyToggleVisual(MinimapButton, isActive);
	}
}
