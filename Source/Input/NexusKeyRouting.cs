using ComfyUI_Nexus.Platform;

namespace ComfyUI_Nexus.Input;

/// <summary>
/// Identifies which platform key hook produced a keyboard event so the router can choose relay vs pass-through behavior.
/// </summary>
internal enum NexusKeyboardInputSource
{
	NativeWindow,
	WebViewAccelerator,
}

/// <summary>
/// Lightweight modifier snapshot used on the keyboard hot path.
/// </summary>
internal readonly record struct NexusKeyModifiers(bool Ctrl, bool Shift, bool Alt);

/// <summary>
/// Describes the synchronous routing decision before any optional async side effect is executed.
/// </summary>
internal enum NexusKeyRouteDecisionKind
{
	Pass,
	PassToNativeInput,
	Consume,
	ConsumeAndRun,
	RelayToWeb,
}

/// <summary>
/// A surface or router decision. Keep this cheap to create because it runs for every routed keydown.
/// </summary>
internal sealed record NexusKeyRouteDecision(
	NexusKeyRouteDecisionKind Kind,
	Func<Task>? Action = null)
{
	internal static NexusKeyRouteDecision Pass { get; } = new(NexusKeyRouteDecisionKind.Pass);
	internal static NexusKeyRouteDecision PassToNativeInput { get; } = new(NexusKeyRouteDecisionKind.PassToNativeInput);
	internal static NexusKeyRouteDecision Consume { get; } = new(NexusKeyRouteDecisionKind.Consume);
	internal static NexusKeyRouteDecision RelayToWeb { get; } = new(NexusKeyRouteDecisionKind.RelayToWeb);

	internal static NexusKeyRouteDecision Run(Func<Task> action)
		=> new(NexusKeyRouteDecisionKind.ConsumeAndRun, action);
}

internal sealed record NexusInputRouteResult(
	NexusKeyRouteDecisionKind Kind,
	string Stage);

/// <summary>
/// Registers a native UI surface with enough state for centralized keyboard routing without querying visual trees.
/// </summary>
internal sealed record NexusUiSurfaceRegistration(
	string Id,
	int Priority,
	Func<bool> IsOpen,
	bool IsModal,
	bool AcceptsTextInput,
	bool BlocksUnhandledKeys,
	Func<NexusKey, NexusKeyModifiers, NexusKeyRouteDecision>? PreviewKey = null,
	Func<bool>? IsInteractive = null);
