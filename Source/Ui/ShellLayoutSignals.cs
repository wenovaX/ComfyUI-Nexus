namespace ComfyUI_Nexus.Ui;

/// <summary>
/// Reason code for shell layout invalidation events.
/// </summary>
internal enum ShellLayoutInvalidationReason
{
	Unknown,
	WindowSizeChanged,
	RailStateChanged,
	RailResized,
}

/// <summary>
/// Event payload sent through <see cref="ShellLayoutSignals"/>.
/// </summary>
internal sealed class ShellLayoutInvalidatedEventArgs : EventArgs
{
	/// <summary>
	/// Creates a layout invalidation event.
	/// </summary>
	/// <param name="reason">Reason that triggered the layout recalculation.</param>
	internal ShellLayoutInvalidatedEventArgs(ShellLayoutInvalidationReason reason)
	{
		Reason = reason;
	}

	internal ShellLayoutInvalidationReason Reason { get; }
}

internal sealed class ShellLayoutSignals
{
	internal event EventHandler<ShellLayoutInvalidatedEventArgs>? LayoutInvalidated;

	/// <summary>
	/// Notifies shell components that responsive layout should be recalculated.
	/// </summary>
	/// <param name="reason">Reason for invalidation, used for diagnostics and future filtering.</param>
	internal void InvalidateLayout(ShellLayoutInvalidationReason reason = ShellLayoutInvalidationReason.Unknown)
		=> LayoutInvalidated?.Invoke(this, new ShellLayoutInvalidatedEventArgs(reason));
}
