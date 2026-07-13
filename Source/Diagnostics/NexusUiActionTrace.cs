namespace ComfyUI_Nexus.Diagnostics;

internal static class NexusUiActionTrace
{
	private const int Capacity = 48;
	private static readonly UiActionEntry?[] Entries = new UiActionEntry[Capacity];
	private static long _nextSequence;

	internal static void Record(string owner, string action, string? detail = null)
	{
		if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(action))
		{
			return;
		}

		long sequence = Interlocked.Increment(ref _nextSequence);
		string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" - {detail}";
		var entry = new UiActionEntry(
			sequence,
			$"{DateTimeOffset.Now:HH:mm:ss.fff} {owner}.{action}{suffix}");
		Volatile.Write(ref Entries[(int)((sequence - 1) % Capacity)], entry);
	}

	internal static string GetSnapshot()
	{
		long lastSequence = Volatile.Read(ref _nextSequence);
		if (lastSequence == 0)
		{
			return "none";
		}

		long firstSequence = Math.Max(1, lastSequence - Capacity + 1);
		var actions = new List<string>(Capacity);
		for (long sequence = firstSequence; sequence <= lastSequence; sequence++)
		{
			UiActionEntry? entry = Volatile.Read(ref Entries[(int)((sequence - 1) % Capacity)]);
			if (entry?.Sequence == sequence)
			{
				actions.Add(entry.Text);
			}
		}

		return actions.Count == 0 ? "none" : string.Join(" -> ", actions);
	}

	internal static void WriteSnapshot(string reason)
		=> NexusLog.Info($"[UI_TRACE] {reason}; actions=[{GetSnapshot()}]");

	private sealed record UiActionEntry(long Sequence, string Text);
}
