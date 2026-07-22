using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Platform;

namespace ComfyUI_Nexus.Input;

/// <summary>
/// Merges native and WebView keyboard entry points into one priority-ordered decision path.
/// </summary>
internal sealed class NexusInputRouter
{
	private readonly NexusUiSurfaceManager _surfaceManager;
	private readonly NexusDialogService _dialogs;

	internal NexusInputRouter(NexusUiSurfaceManager surfaceManager, NexusDialogService dialogs)
	{
		_surfaceManager = surfaceManager;
		_dialogs = dialogs;
	}

	/// <summary>
	/// WebView2 accelerator events are synchronous, so capture only keys native routing can certainly consume.
	/// </summary>
	internal bool ShouldCaptureWebViewAccelerator(NexusKey key, NexusKeyModifiers modifiers, bool mediaViewerOpen)
		=> _dialogs.IsOpen
			|| mediaViewerOpen
			|| _surfaceManager.BlocksWebViewKeyboard
			|| NexusInputManager.IsGlobalAppShortcut(key, modifiers.Ctrl, modifiers.Shift, modifiers.Alt);

	/// <summary>
	/// Routes a key event through modal surfaces, app shortcuts, rail shortcuts, then web relay/pass-through.
	/// </summary>
	internal async Task<NexusInputRouteResult> RouteAsync(
		NexusKeyboardInputSource source,
		NexusInputContext inputContext,
		NexusKey key,
		NexusKeyModifiers modifiers,
		bool isNativeTextInputFocused,
		bool mediaViewerOpen,
		Func<NexusKey, bool, bool, bool, Task<bool>> tryHandleMediaViewerShortcutAsync)
	{
		if (await _dialogs.TryHandleShortcutAsync(key))
		{
			return Handled("dialog-shortcut");
		}

		if (_dialogs.IsOpen)
		{
			return Handled("dialog-block");
		}

		if (await tryHandleMediaViewerShortcutAsync(key, modifiers.Ctrl, modifiers.Shift, modifiers.Alt))
		{
			return Handled("media-viewer-shortcut");
		}

		if (mediaViewerOpen)
		{
			return Handled("media-viewer-block");
		}

		var surfaceDecision = _surfaceManager.PreviewKey(source, key, modifiers, isNativeTextInputFocused);
		if (surfaceDecision.Kind == NexusKeyRouteDecisionKind.ConsumeAndRun && surfaceDecision.Action != null)
		{
			await surfaceDecision.Action();
			return Handled("surface-action");
		}

		if (surfaceDecision.Kind != NexusKeyRouteDecisionKind.Pass)
		{
			return new NexusInputRouteResult(surfaceDecision.Kind, "surface");
		}

		if (await NexusInputManager.HandleGlobalKeyDown(inputContext, key, modifiers.Ctrl, modifiers.Shift, modifiers.Alt))
		{
			return Handled("global");
		}

		return source == NexusKeyboardInputSource.NativeWindow
			? new NexusInputRouteResult(NexusKeyRouteDecisionKind.RelayToWeb, "web-relay")
			: new NexusInputRouteResult(NexusKeyRouteDecisionKind.Pass, "web-pass-through");
	}

	private static NexusInputRouteResult Handled(string stage)
		=> new(NexusKeyRouteDecisionKind.Consume, stage);
}
