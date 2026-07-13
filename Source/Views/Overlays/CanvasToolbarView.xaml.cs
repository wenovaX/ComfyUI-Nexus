namespace ComfyUI_Nexus.Views.Overlays;

using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Ui;
using System.Windows.Input;

public partial class CanvasToolbarView : ContentView
{
	private static Color ToggleActiveBackgroundColor => ResourceColor("CanvasToolbarHoverColor", "#1d2a3d");
	private static readonly Color ToggleActiveTextColor = NexusColors.White;
	private static Color ToggleInactiveTextColor => ResourceColor("CanvasToolbarTextColor", "#88e4ff");

	public static readonly BindableProperty HelpCommandProperty = BindableProperty.Create(nameof(HelpCommand), typeof(ICommand), typeof(CanvasToolbarView));
	public static readonly BindableProperty TerminalCommandProperty = BindableProperty.Create(nameof(TerminalCommand), typeof(ICommand), typeof(CanvasToolbarView));
	public static readonly BindableProperty ShortcutsCommandProperty = BindableProperty.Create(nameof(ShortcutsCommand), typeof(ICommand), typeof(CanvasToolbarView));
	public static readonly BindableProperty ModeCommandProperty = BindableProperty.Create(nameof(ModeCommand), typeof(ICommand), typeof(CanvasToolbarView));
	public static readonly BindableProperty FitViewCommandProperty = BindableProperty.Create(nameof(FitViewCommand), typeof(ICommand), typeof(CanvasToolbarView));
	public static readonly BindableProperty ZoomCommandProperty = BindableProperty.Create(nameof(ZoomCommand), typeof(ICommand), typeof(CanvasToolbarView));
	public static readonly BindableProperty MinimapCommandProperty = BindableProperty.Create(nameof(MinimapCommand), typeof(ICommand), typeof(CanvasToolbarView));
	public static readonly BindableProperty LinksCommandProperty = BindableProperty.Create(nameof(LinksCommand), typeof(ICommand), typeof(CanvasToolbarView));

	private bool _isMinimapActive = false;
	private bool _isLinksHidden = false;

	public ICommand? HelpCommand
	{
		get => (ICommand?)GetValue(HelpCommandProperty);
		set => SetValue(HelpCommandProperty, value);
	}

	public ICommand? TerminalCommand
	{
		get => (ICommand?)GetValue(TerminalCommandProperty);
		set => SetValue(TerminalCommandProperty, value);
	}

	public ICommand? ShortcutsCommand
	{
		get => (ICommand?)GetValue(ShortcutsCommandProperty);
		set => SetValue(ShortcutsCommandProperty, value);
	}

	public ICommand? ModeCommand
	{
		get => (ICommand?)GetValue(ModeCommandProperty);
		set => SetValue(ModeCommandProperty, value);
	}

	public ICommand? FitViewCommand
	{
		get => (ICommand?)GetValue(FitViewCommandProperty);
		set => SetValue(FitViewCommandProperty, value);
	}

	public ICommand? ZoomCommand
	{
		get => (ICommand?)GetValue(ZoomCommandProperty);
		set => SetValue(ZoomCommandProperty, value);
	}

	public ICommand? MinimapCommand
	{
		get => (ICommand?)GetValue(MinimapCommandProperty);
		set => SetValue(MinimapCommandProperty, value);
	}

	public ICommand? LinksCommand
	{
		get => (ICommand?)GetValue(LinksCommandProperty);
		set => SetValue(LinksCommandProperty, value);
	}

	public ICommand GuardedMinimapCommand { get; }
	public ICommand GuardedLinksCommand { get; }

	public CanvasToolbarView()
	{
		GuardedMinimapCommand = new Command(ExecuteMinimapCommand);
		GuardedLinksCommand = new Command(ExecuteLinksCommand);
		InitializeComponent();
	}

	private void ExecuteMinimapCommand()
	{
		_isMinimapActive = !_isMinimapActive;
		ApplyToggleVisual(MinimapButton, _isMinimapActive);
		if (MinimapCommand?.CanExecute(null) != false)
		{
			MinimapCommand?.Execute(null);
		}
	}

	private void ExecuteLinksCommand()
	{
		_isLinksHidden = !_isLinksHidden;
		ApplyToggleVisual(LinksButton, !_isLinksHidden);
		if (LinksCommand?.CanExecute(null) != false)
		{
			LinksCommand?.Execute(null);
		}
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

	private static Color ResourceColor(string key, string fallback)
	{
		if (Application.Current?.Resources.TryGetValue(key, out object? value) == true)
		{
			return value switch
			{
				Color color => color,
				SolidColorBrush brush => brush.Color,
				_ => Color.FromArgb(fallback),
			};
		}

		return Color.FromArgb(fallback);
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

	internal void SetLinksHidden(bool isHidden)
	{
		_isLinksHidden = isHidden;
		ApplyToggleVisual(LinksButton, !isHidden);
	}
}
