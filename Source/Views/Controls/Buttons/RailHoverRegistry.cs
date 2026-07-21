namespace ComfyUI_Nexus.Views.Controls.Buttons;

internal interface IRailHoverParticipant
{
	void ResetRailHover();
}

/// <summary>
/// Clears transient rail hover visuals when the pointer leaves the native window
/// before an individual MAUI control receives its PointerExited event.
/// </summary>
internal static class RailHoverRegistry
{
	private static readonly HashSet<IRailHoverParticipant> Participants = [];

	internal static void Register(IRailHoverParticipant participant)
	{
		ArgumentNullException.ThrowIfNull(participant);
		Participants.Add(participant);
	}

	internal static void Unregister(IRailHoverParticipant participant)
	{
		ArgumentNullException.ThrowIfNull(participant);
		Participants.Remove(participant);
	}

	internal static void ResetAll()
	{
		foreach (IRailHoverParticipant participant in Participants.ToArray())
		{
			participant.ResetRailHover();
		}
	}
}
