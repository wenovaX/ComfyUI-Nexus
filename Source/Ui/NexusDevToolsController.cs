using ComfyUI_Nexus.Platform;

namespace ComfyUI_Nexus.Ui;

/// <summary>
/// Owns the DevTools opt-in and keeps every browser host on the same setting.
/// The Control Deck changes the opt-in; keyboard routing requests the actual window.
/// </summary>
internal sealed class NexusDevToolsController
{
	internal bool IsEnabled { get; private set; }

	internal bool Toggle()
	{
		IsEnabled = !IsEnabled;
		return IsEnabled;
	}

	internal void Apply(INexusBrowserSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);
		surface.SetDevToolsEnabled(IsEnabled);
	}

	internal bool TryOpen(INexusBrowserSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);
		if (!IsEnabled)
		{
			return false;
		}

		surface.OpenDevTools();
		return true;
	}
}
