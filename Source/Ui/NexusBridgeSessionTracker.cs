namespace ComfyUI_Nexus.Ui;

internal enum NexusBridgeSessionState
{
	Idle,
	Navigating,
	Live,
	Disconnected,
}

internal sealed record NexusBridgeSessionSnapshot(long Generation, NexusBridgeSessionState State);

/// <summary>
/// Owns the native bridge session boundary. Navigation starts close the current
/// session; only BOOT_READY from the replacement document makes it live again.
/// </summary>
internal sealed class NexusBridgeSessionTracker
{
	private readonly object _gate = new();
	private NexusBridgeSessionSnapshot _snapshot = new(0, NexusBridgeSessionState.Idle);

	internal NexusBridgeSessionSnapshot Snapshot
	{
		get
		{
			lock (_gate)
			{
				return _snapshot;
			}
		}
	}

	internal bool BeginNavigation()
	{
		lock (_gate)
		{
			if (_snapshot.State == NexusBridgeSessionState.Navigating)
			{
				return false;
			}

			_snapshot = new NexusBridgeSessionSnapshot(_snapshot.Generation + 1, NexusBridgeSessionState.Navigating);
			return true;
		}
	}

	internal bool MarkReady()
		=> Transition(NexusBridgeSessionState.Live, incrementGeneration: false);

	internal bool MarkDisconnected()
		=> Transition(NexusBridgeSessionState.Disconnected, incrementGeneration: false);

	private bool Transition(NexusBridgeSessionState state, bool incrementGeneration)
	{
		lock (_gate)
		{
			long generation = incrementGeneration ? _snapshot.Generation + 1 : _snapshot.Generation;
			if (_snapshot.State == state && _snapshot.Generation == generation)
			{
				return false;
			}

			_snapshot = new NexusBridgeSessionSnapshot(generation, state);
			return true;
		}
	}
}
