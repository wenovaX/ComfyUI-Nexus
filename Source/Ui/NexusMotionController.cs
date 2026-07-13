using System.Runtime.InteropServices;
using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Ui;

/// <summary>
/// Owns repeating UI motion for one visual surface. All ticks stay on the MAUI
/// animation or dispatcher timeline; this type only centralizes lifetime and fault handling.
/// </summary>
internal sealed class NexusMotionController
{
	private sealed class MotionEntry
	{
		internal required long Generation { get; init; }
		internal required Action Reset { get; init; }
		internal IAnimatable? AnimationOwner { get; init; }
		internal string? AnimationName { get; init; }
		internal IDispatcherTimer? Timer { get; set; }
		internal EventHandler? TimerTickHandler { get; set; }
	}

	private readonly object _gate = new();
	private readonly Dictionary<string, MotionEntry> _entries = new(StringComparer.Ordinal);
	private readonly string _owner;
	private readonly string _source;
	private readonly IDispatcher? _dispatcher;
	private long _generation;

	internal NexusMotionController(string owner, string source, IDispatcher? dispatcher = null)
	{
		_owner = owner;
		_source = source;
		_dispatcher = dispatcher;
	}

	internal void StartTimeline(
		string name,
		IAnimatable animationOwner,
		uint rate,
		uint length,
		Easing? easing,
		Func<bool> canRun,
		Action reset,
		params SafeAnimation.TimelineSegment[] segments)
	{
		Stop(name);

		long generation = NextGeneration();
		if (XamlLifetimeDiagnostics.AreTransformAnimationsDisabled)
		{
			RunReset(name, generation, reset, "disabled");
			return;
		}

		var entry = new MotionEntry
		{
			Generation = generation,
			Reset = reset,
			AnimationOwner = animationOwner,
			AnimationName = name,
		};
		Register(name, entry, "running");

		SafeAnimation.TimelineSegment[] guardedSegments = segments
			.Select(segment => new SafeAnimation.TimelineSegment(
				segment.Begin,
				segment.End,
				value => ApplyTimelineSegment(name, generation, segment.Apply, value),
				segment.From,
				segment.To,
				segment.Easing))
			.ToArray();

		bool committed = SafeAnimation.Timeline(
			animationOwner,
			name,
			rate,
			length,
			easing,
			() => CanRepeat(name, generation, canRun),
			_source,
			guardedSegments);
		if (!committed)
		{
			Stop(name);
		}
	}

	internal void StartFrameLoop(
		string name,
		TimeSpan interval,
		Func<bool> canRun,
		Action tick,
		Action reset)
	{
		Stop(name);

		long generation = NextGeneration();
		if (XamlLifetimeDiagnostics.AreTransformAnimationsDisabled)
		{
			RunReset(name, generation, reset, "disabled");
			return;
		}

		var entry = new MotionEntry
		{
			Generation = generation,
			Reset = reset,
		};
		if (_dispatcher is null)
		{
			RunReset(name, generation, reset, "dispatcher-unavailable");
			return;
		}

		IDispatcherTimer timer = _dispatcher.CreateTimer();
		timer.Interval = interval;
		EventHandler tickHandler = (_, _) => RunFrameTick(name, generation, canRun, tick);
		entry.Timer = timer;
		entry.TimerTickHandler = tickHandler;
		Register(name, entry, "running");
		timer.Tick += tickHandler;
		timer.Start();
	}

	internal void Stop(string name)
	{
		MotionEntry? entry = RemoveEntry(name);
		if (entry is null)
		{
			return;
		}

		StopEntry(name, entry, removeDiagnostic: true, state: "stopped");
	}

	internal void StopAll()
	{
		string[] names;
		lock (_gate)
		{
			names = _entries.Keys.ToArray();
		}

		foreach (string name in names)
		{
			Stop(name);
		}
	}

	private long NextGeneration()
		=> Interlocked.Increment(ref _generation);

	private void Register(string name, MotionEntry entry, string state)
	{
		lock (_gate)
		{
			_entries[name] = entry;
		}

		XamlLifetimeDiagnostics.RegisterMotion(_owner, name, entry.Generation, state);
	}

	private MotionEntry? RemoveEntry(string name)
	{
		lock (_gate)
		{
			if (!_entries.Remove(name, out MotionEntry? entry))
			{
				return null;
			}

			return entry;
		}
	}

	private bool IsCurrent(string name, long generation)
	{
		lock (_gate)
		{
			return _entries.TryGetValue(name, out MotionEntry? entry)
				&& entry.Generation == generation;
		}
	}

	private bool CanRepeat(string name, long generation, Func<bool> canRun)
	{
		if (!IsCurrent(name, generation))
		{
			return false;
		}

		try
		{
			if (canRun())
			{
				return true;
			}
		}
		catch (Exception ex) when (IsManagedMotionFault(ex))
		{
			Fail(name, generation, ex);
			return false;
		}

		Stop(name);
		return false;
	}

	private void ApplyTimelineSegment(string name, long generation, Action<double> apply, double value)
	{
		if (!IsCurrent(name, generation))
		{
			return;
		}

		try
		{
			apply(value);
		}
		catch (Exception ex) when (IsManagedMotionFault(ex))
		{
			Fail(name, generation, ex);
		}
	}

	private void RunFrameTick(string name, long generation, Func<bool> canRun, Action tick)
	{
		if (!CanRepeat(name, generation, canRun))
		{
			return;
		}

		try
		{
			tick();
		}
		catch (Exception ex) when (IsManagedMotionFault(ex))
		{
			Fail(name, generation, ex);
		}
	}

	private void Fail(string name, long generation, Exception exception)
	{
		MotionEntry? entry = RemoveEntryIfCurrent(name, generation);
		if (entry is null)
		{
			return;
		}

		StopEntry(name, entry, removeDiagnostic: false, state: "faulted");
		XamlLifetimeDiagnostics.RecordMotionFault(_owner, name, generation, exception);
		NexusLog.Warning($"[MOTION] {_owner}/{name} stopped after {exception.GetType().Name}: {exception.Message}");
		XamlLifetimeDiagnostics.WriteSnapshot($"motion-fault:{_owner}/{name}");
	}

	private MotionEntry? RemoveEntryIfCurrent(string name, long generation)
	{
		lock (_gate)
		{
			if (!_entries.TryGetValue(name, out MotionEntry? entry) || entry.Generation != generation)
			{
				return null;
			}

			_entries.Remove(name);
			return entry;
		}
	}

	private void StopEntry(string name, MotionEntry entry, bool removeDiagnostic, string state)
	{
		if (entry.Timer is not null)
		{
			if (entry.TimerTickHandler is not null)
			{
				entry.Timer.Tick -= entry.TimerTickHandler;
			}

			entry.Timer.Stop();
		}

		if (entry.AnimationOwner is not null && entry.AnimationName is not null)
		{
			SafeAnimation.AbortAnimation(entry.AnimationOwner, entry.AnimationName, _source);
		}

		RunReset(name, entry.Generation, entry.Reset, state);
		if (removeDiagnostic)
		{
			XamlLifetimeDiagnostics.RemoveMotion(_owner, name);
		}
	}

	private void RunReset(string name, long generation, Action reset, string state)
	{
		try
		{
			reset();
			XamlLifetimeDiagnostics.RecordMotionState(_owner, name, generation, state);
		}
		catch (Exception ex) when (IsManagedMotionFault(ex))
		{
			XamlLifetimeDiagnostics.RecordMotionFault(_owner, name, generation, ex);
			NexusLog.Warning($"[MOTION] {_owner}/{name} reset skipped after {ex.GetType().Name}: {ex.Message}");
		}
	}

	private static bool IsManagedMotionFault(Exception exception)
		=> exception is ObjectDisposedException
			or InvalidOperationException
			or COMException
			or ArgumentException;
}
