using ComfyUI_Nexus.Platform;

namespace ComfyUI_Nexus.Input;

/// <summary>
/// Maintains a priority-ordered list of native UI surfaces that can temporarily own keyboard input.
/// </summary>
internal sealed class NexusUiSurfaceManager
{
	private readonly List<NexusUiSurfaceRegistration> _surfaces = new();

	/// <summary>
	/// Adds or replaces a surface registration. Sorting happens only when registrations change, not per key event.
	/// </summary>
	internal void Register(NexusUiSurfaceRegistration surface)
	{
		_surfaces.RemoveAll(item => item.Id == surface.Id);
		_surfaces.Add(surface);
		_surfaces.Sort((left, right) => right.Priority.CompareTo(left.Priority));
	}

	internal bool BlocksWebViewKeyboard
		=> _surfaces.Any(IsBlockingOpenSurface);

	/// <summary>
	/// Returns the first active surface decision without performing async work.
	/// </summary>
	internal NexusKeyRouteDecision PreviewKey(
		NexusKeyboardInputSource source,
		NexusKey key,
		NexusKeyModifiers modifiers,
		bool isNativeTextInputFocused)
	{
		foreach (var surface in _surfaces)
		{
			if (!IsOpenInteractiveSurface(surface))
			{
				continue;
			}

			var decision = surface.PreviewKey?.Invoke(key, modifiers) ?? NexusKeyRouteDecision.Pass;
			if (decision.Kind != NexusKeyRouteDecisionKind.Pass)
			{
				return decision;
			}

			if (surface.AcceptsTextInput &&
				source == NexusKeyboardInputSource.NativeWindow &&
				isNativeTextInputFocused &&
				key != NexusKey.Escape)
			{
				return NexusKeyRouteDecision.PassToNativeInput;
			}

			if (surface.BlocksUnhandledKeys)
			{
				return NexusKeyRouteDecision.Consume;
			}
		}

		return NexusKeyRouteDecision.Pass;
	}

	private static bool IsBlockingOpenSurface(NexusUiSurfaceRegistration surface)
		=> IsOpenInteractiveSurface(surface) && (surface.IsModal || surface.BlocksUnhandledKeys || surface.AcceptsTextInput);

	private static bool IsOpenInteractiveSurface(NexusUiSurfaceRegistration surface)
		=> surface.IsOpen() && (surface.IsInteractive?.Invoke() ?? true);
}
