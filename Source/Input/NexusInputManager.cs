using ComfyUI_Nexus.Platform;

namespace ComfyUI_Nexus.Input;

/// <summary>
/// Provides the app-specific actions needed to route native key input without coupling the input manager to MainPage.
/// </summary>
/// <param name="PerformSystemRebootAsync">Runs the app-controlled F5 refresh sequence.</param>
/// <param name="OpenNexusCommandConsoleAsync">Opens the native Nexus command console.</param>
/// <param name="CloseActiveWorkflowTab">Closes the active workflow tab for Ctrl+W.</param>
/// <param name="ToggleRailAsync">Toggles the rail for Ctrl+B.</param>
/// <param name="TryHandleRailShortcut">Lets the rail consume file-browser shortcuts before web fallback.</param>
internal sealed record NexusInputContext(
	Func<Task> PerformSystemRebootAsync,
	Func<Task> OpenNexusCommandConsoleAsync,
	Action CloseActiveWorkflowTab,
	Func<Task> ToggleRailAsync,
	Func<NexusKey, bool, bool, bool> TryHandleRailShortcut);

internal static class NexusInputManager
{
	internal static bool IsGlobalAppShortcut(NexusKey key, bool ctrl, bool shift, bool alt)
		=> IsCommandConsoleShortcut(key, ctrl, shift, alt)
			|| IsRefreshShortcut(key, ctrl, shift, alt)
			|| IsCloseTabShortcut(key, ctrl, shift, alt)
			|| IsRailToggleShortcut(key, ctrl, shift, alt);

	/// <summary>
	/// Routes a native keydown event to native UI first, then to ComfyUI when appropriate.
	/// </summary>
	/// <param name="context">Callbacks describing the current shell/input state.</param>
	/// <param name="key">Platform-normalized key value.</param>
	/// <param name="ctrl">Whether Ctrl is currently pressed.</param>
	/// <param name="shift">Whether Shift is currently pressed.</param>
	/// <param name="alt">Whether Alt is currently pressed.</param>
	/// <returns>True when the key was handled by native routing.</returns>
	internal static async Task<bool> HandleGlobalKeyDown(NexusInputContext context, NexusKey key, bool ctrl, bool shift, bool alt)
	{
		if (IsCommandConsoleShortcut(key, ctrl, shift, alt))
		{
			await context.OpenNexusCommandConsoleAsync();
			return true;
		}

		if (IsRefreshShortcut(key, ctrl, shift, alt))
		{
			await context.PerformSystemRebootAsync();
			return true;
		}

		if (IsCloseTabShortcut(key, ctrl, shift, alt))
		{
			context.CloseActiveWorkflowTab();
			return true;
		}

		if (IsRailToggleShortcut(key, ctrl, shift, alt))
		{
			await context.ToggleRailAsync();
			return true;
		}

		if (context.TryHandleRailShortcut(key, ctrl, shift))
		{
			return true;
		}

		return false;
	}

	private static bool IsCommandConsoleShortcut(NexusKey key, bool ctrl, bool shift, bool alt)
		=> ctrl && alt && !shift && key == NexusKey.Space;

	private static bool IsRefreshShortcut(NexusKey key, bool ctrl, bool shift, bool alt)
		=> !ctrl && !shift && !alt && key == NexusKey.F5;

	private static bool IsCloseTabShortcut(NexusKey key, bool ctrl, bool shift, bool alt)
		=> ctrl && !shift && !alt && key == NexusKey.W;

	private static bool IsRailToggleShortcut(NexusKey key, bool ctrl, bool shift, bool alt)
		=> ctrl && !shift && !alt && key == NexusKey.B;
}
