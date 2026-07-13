using System.Collections.Concurrent;

namespace ComfyUI_Nexus.Diagnostics;

internal static class XamlLifetimeDiagnostics
{
	private const string DisableTransformAnimationsVariable = "NEXUS_XAML_DISABLE_TRANSFORM_ANIMATIONS";
	private static readonly ConcurrentDictionary<string, string> SurfaceStates = new(StringComparer.Ordinal);
	private static readonly ConcurrentDictionary<string, string> ActiveAnimations = new(StringComparer.Ordinal);
	private static readonly ConcurrentDictionary<string, string> MotionStates = new(StringComparer.Ordinal);

	internal static bool AreTransformAnimationsDisabled
		=> string.Equals(Environment.GetEnvironmentVariable(DisableTransformAnimationsVariable), "1", StringComparison.Ordinal);

	internal static void RecordSurface(string owner, string state)
	{
		if (SurfaceStates.TryGetValue(owner, out string? previous) && string.Equals(previous, state, StringComparison.Ordinal))
		{
			return;
		}

		SurfaceStates[owner] = state;
		NexusUiActionTrace.Record("Surface", owner, state);
		WriteSnapshot($"surface:{owner}");
	}

	internal static void RemoveSurface(string owner)
	{
		if (SurfaceStates.TryRemove(owner, out _))
		{
			NexusUiActionTrace.Record("Surface", owner, "closed");
			WriteSnapshot($"surface-closed:{owner}");
		}
	}

	internal static void RegisterAnimation(IAnimatable owner, string name)
	{
		ActiveAnimations[GetAnimationKey(owner, name)] = $"{owner.GetType().Name}:{name}";
	}

	internal static void RemoveAnimation(IAnimatable owner, string name)
	{
		ActiveAnimations.TryRemove(GetAnimationKey(owner, name), out _);
	}

	internal static void RegisterMotion(string owner, string name, long generation, string state)
		=> RecordMotionState(owner, name, generation, state);

	internal static void RecordMotionState(string owner, string name, long generation, string state)
	{
		string key = GetMotionKey(owner, name);
		string value = $"{owner}:{name}; generation={generation}; state={state}";
		MotionStates.AddOrUpdate(key, value, (_, previous) => string.Equals(previous, value, StringComparison.Ordinal) ? previous : value);
	}

	internal static void RecordMotionFault(string owner, string name, long generation, Exception exception)
	{
		MotionStates[GetMotionKey(owner, name)] = $"{owner}:{name}; generation={generation}; state=faulted; lastFault={exception.GetType().Name}: {exception.Message}";
		NexusUiActionTrace.Record("Motion", $"{owner}.{name}", $"faulted: {exception.GetType().Name}");
	}

	internal static void RemoveMotion(string owner, string name)
	{
		MotionStates.TryRemove(GetMotionKey(owner, name), out _);
	}

	internal static void WriteSnapshot(string reason)
	{
		NexusLog.Info($"[XAML_LIFETIME] {reason}; {GetSnapshot()}");
	}

	internal static string GetSnapshot()
	{
		string surfaces = SurfaceStates.Count == 0
			? "none"
			: string.Join(", ", SurfaceStates.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));
		string animations = ActiveAnimations.Count == 0
			? "none"
			: string.Join(", ", ActiveAnimations.Values.OrderBy(value => value));
		string motions = MotionStates.Count == 0
			? "none"
			: string.Join(", ", MotionStates.Values.OrderBy(value => value));
		return $"transformsDisabled={AreTransformAnimationsDisabled}; surfaces=[{surfaces}]; animations=[{animations}]; motions=[{motions}]";
	}

	private static string GetAnimationKey(IAnimatable owner, string name)
		=> $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(owner):X8}:{name}";

	private static string GetMotionKey(string owner, string name)
		=> $"{owner}:{name}";
}
