namespace ComfyUI_Nexus.Views.Controls.Buttons;

internal interface IRailHoverParticipant
{
	void ResetRailHover();
}

/// <summary>
/// Clears transient rail hover visuals when the pointer leaves the native window
/// before an individual MAUI control receives its PointerExited event.
/// </summary>
internal sealed class NexusRailHoverRegistry
{
	private readonly HashSet<IRailHoverParticipant> _participants = [];

	internal void Register(IRailHoverParticipant participant)
	{
		ArgumentNullException.ThrowIfNull(participant);
		_participants.Add(participant);
	}

	internal void Unregister(IRailHoverParticipant participant)
	{
		ArgumentNullException.ThrowIfNull(participant);
		_participants.Remove(participant);
	}

	internal void ResetAll()
	{
		foreach (IRailHoverParticipant participant in _participants.ToArray())
		{
			participant.ResetRailHover();
		}
	}
}
