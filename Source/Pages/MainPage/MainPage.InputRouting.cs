using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Input;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	/// <summary>
	/// Registers native surfaces that should temporarily own keyboard input before global shortcuts or WebView relay.
	/// </summary>
	private void InitializeKeyboardSurfaces()
	{
		RegisterModalSurface("CommandInput", 800, () => CommandInputControl.IsOverlayVisible, () => SetCommandInputVisibleAsync(false), acceptsTextInput: true);
		RegisterModalSurface("Settings", 700, () => SettingsOverlayControl.IsVisible, () => SetSettingsOverlayVisible(false));
		RegisterModalSurface("Help", 700, () => HelpOverlayControl.IsVisible, () => SetHelpOverlayVisible(false));
		RegisterModalSurface("About", 700, () => AboutOverlayControl.IsVisible, () => SetAboutOverlayVisible(false));
		RegisterModalSurface("WorkflowDropdown", 600, () => WorkflowDropdownControl.IsOpen, HideWorkflowDropdownAsync);
		RegisterModalSurface("CanvasModeMenu", 600, () => CanvasModeMenuControl.IsOpen, () => SetCanvasModeMenuVisible(false));
		RegisterModalSurface("WorkflowActionsMenu", 600, () => WorkflowActionsMenuControl.IsOpen, () => SetWorkflowActionsMenuVisible(false));
		RegisterModalSurface("CommandMenu", 600, () => CommandMenuControl.IsMenuVisible, () => SetCommandMenuVisible(false));
	}

	private void RegisterModalSurface(
		string id,
		int priority,
		Func<bool> isOpen,
		Func<Task> closeAsync,
		bool acceptsTextInput = false)
	{
		_uiSurfaceManager.Register(new NexusUiSurfaceRegistration(
			id,
			priority,
			isOpen,
			IsModal: true,
			AcceptsTextInput: acceptsTextInput,
			BlocksUnhandledKeys: true,
			PreviewKey: (key, modifiers) => IsPlainEscape(key, modifiers)
				? NexusKeyRouteDecision.Run(closeAsync)
				: NexusKeyRouteDecision.Pass));
	}

	private bool IsNativeInputFocused()
	{
#if WINDOWS
		if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
		{
			return false;
		}
#endif
		return NexusAppManager.Instance.Platform.Keyboard.IsNativeTextInputFocused(this);
	}

	private void CloseActiveWorkflowTab()
	{
		if (_tabController.ActiveTabIndex >= 0)
		{
			Log($"INPUT: Closing active tab [{_tabController.ActiveTabIndex}].");
			_ = _tabController.CloseWorkflowsAsync(new System.Collections.Generic.List<int> { _tabController.ActiveTabIndex });
		}
	}

	private void OnCommandInputCompleted(object? sender, EventArgs e)
	{
		_ = ExecuteCommandInputAsync();
	}

	private void OnCommandInputBackdropTapped(object? sender, EventArgs e)
	{
		_ = SetCommandInputVisibleAsync(false);
	}

	private static bool IsPlainEscape(NexusKey key, NexusKeyModifiers modifiers)
		=> key == NexusKey.Escape && !modifiers.Ctrl && !modifiers.Shift && !modifiers.Alt;
}
