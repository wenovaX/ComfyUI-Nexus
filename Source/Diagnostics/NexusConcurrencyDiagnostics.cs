namespace ComfyUI_Nexus.Diagnostics;

internal static class NexusConcurrencyDiagnostics
{
	private static readonly object Gate = new();
	private static readonly Dictionary<string, Func<string>> Owners = new(StringComparer.Ordinal);
	private static string _lastFault = "none";
	private static string _lastLifecycleStop = "none";
	private static Func<string>? _workerSnapshot;
	private static Func<string>? _uiPostSnapshot;

	internal static void ConfigureRuntimeSnapshots(
		Func<string> workerSnapshot,
		Func<string> uiPostSnapshot)
	{
		ArgumentNullException.ThrowIfNull(workerSnapshot);
		ArgumentNullException.ThrowIfNull(uiPostSnapshot);
		lock (Gate)
		{
			_workerSnapshot = workerSnapshot;
			_uiPostSnapshot = uiPostSnapshot;
		}
	}

	internal static void ClearRuntimeSnapshots()
	{
		lock (Gate)
		{
			_workerSnapshot = null;
			_uiPostSnapshot = null;
		}
	}

	internal static void RegisterOwner(string owner, Func<string> snapshot)
	{
		lock (Gate)
		{
			Owners[owner] = snapshot;
		}
	}

	internal static void UnregisterOwner(string owner)
	{
		lock (Gate)
		{
			Owners.Remove(owner);
		}
	}

	internal static void RecordFault(string owner, string key, long generation, Exception ex)
	{
		lock (Gate)
		{
			_lastFault = $"{owner}/{key} generation={generation}: {ex.GetType().Name}: {ex.Message}";
		}
	}

	internal static void RecordWorkerFault(string owner, string key, Ui.NexusBackgroundLane lane, Exception ex)
	{
		lock (Gate)
		{
			_lastFault = $"worker {lane} {owner}/{key}: {ex.GetType().Name}: {ex.Message}";
		}
	}

	internal static void RecordLifecycleStop(string owner)
	{
		lock (Gate)
		{
			_lastLifecycleStop = owner;
		}
	}

	internal static string GetSnapshot()
	{
		lock (Gate)
		{
			string owners = Owners.Count == 0
				? "none"
				: string.Join("; ", Owners.Select(pair => $"{pair.Key}=[{pair.Value()}]"));
			string workerSnapshot = _workerSnapshot?.Invoke() ?? "unavailable";
			string uiPostSnapshot = _uiPostSnapshot?.Invoke() ?? "unavailable";
			return $"owners=[{owners}]; workers=[{workerSnapshot}]; uiPosts=[{uiPostSnapshot}]; lastFault={_lastFault}; lastStop={_lastLifecycleStop}";
		}
	}
}
