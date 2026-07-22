using ComfyUI_Nexus.Diagnostics;
using Microsoft.Maui.Controls;

namespace ComfyUI_Nexus.Ui;

internal enum NexusVisualPlaybackKind
{
	Loop,
	OneShot,
}

internal enum NexusVisualTransitionResult
{
	Completed,
	Superseded,
	Unavailable,
}

internal sealed class NexusVisualStateTransition<TState>
	where TState : notnull
{
	internal NexusVisualStateTransition(TState state, long generation, Task<NexusVisualTransitionResult> completion)
	{
		State = state;
		Generation = generation;
		Completion = completion;
	}

	internal TState State { get; }
	internal long Generation { get; }
	internal Task<NexusVisualTransitionResult> Completion { get; }
}

internal sealed record NexusVisualStateAnimation(
	string Name,
	Image Target,
	NexusAnimatedWebpDefinition Definition,
	NexusVisualPlaybackKind PlaybackKind,
	double? PlaybackRate = null,
	int FrameStep = 1,
	NexusAnimatedWebpFinalFrameBehavior FinalFrameBehavior = NexusAnimatedWebpFinalFrameBehavior.ResetToFirstFrame,
	bool Preload = false);

/// <summary>
/// Maps visual states to one or more animated WebP clips while keeping cache, generation,
/// and lifecycle ownership inside one surface-scoped controller.
/// </summary>
internal sealed class NexusVisualStateAnimator<TState> : IDisposable
	where TState : notnull
{
	private sealed class ClipEntry
	{
		internal required NexusVisualStateAnimation Definition { get; init; }
		internal required NexusAnimatedWebpClip Clip { get; init; }
	}

	private readonly object _gate = new();
	private readonly string _owner;
	private readonly NexusMotionController _motion;
	private readonly NexusAnimatedWebpFrameCache _frameCache;
	private readonly Func<bool> _canRun;
	private readonly IReadOnlyDictionary<TState, IReadOnlySet<string>> _stateMap;
	private readonly Dictionary<string, ClipEntry> _clips;
	private readonly HashSet<string> _activeNames = new(StringComparer.Ordinal);
	private readonly IReadOnlyList<NexusAnimatedWebpDefinition> _preloadDefinitions;
	private NexusAnimatedWebpCacheLease? _cacheLease;
	private Task? _initializationTask;
	private NexusVisualStateTransition<TState>? _activeTransition;
	private TState _currentState = default!;
	private bool _hasCurrentState;
	private long _generation;
	private long _cacheGeneration;
	private bool _isDisposed;

	internal NexusVisualStateAnimator(
		string owner,
		NexusMotionController motion,
		NexusAnimatedWebpFrameCache frameCache,
		Func<bool> canRun,
		IEnumerable<NexusVisualStateAnimation> animations,
		IReadOnlyDictionary<TState, IReadOnlyCollection<string>> stateMap)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(owner);
		ArgumentNullException.ThrowIfNull(motion);
		ArgumentNullException.ThrowIfNull(frameCache);
		ArgumentNullException.ThrowIfNull(canRun);
		ArgumentNullException.ThrowIfNull(animations);
		ArgumentNullException.ThrowIfNull(stateMap);

		_owner = owner;
		_motion = motion;
		_frameCache = frameCache;
		_canRun = canRun;
		_clips = animations.ToDictionary(
			animation => animation.Name,
			animation => CreateClipEntry(animation),
			StringComparer.Ordinal);
		if (_clips.Count == 0)
		{
			throw new ArgumentException("At least one visual animation is required.", nameof(animations));
		}

		_stateMap = stateMap.ToDictionary(
			pair => pair.Key,
			pair => (IReadOnlySet<string>)new HashSet<string>(pair.Value, StringComparer.Ordinal));
		foreach (IReadOnlySet<string> animationNames in _stateMap.Values)
		{
			foreach (string animationName in animationNames)
			{
				if (!_clips.ContainsKey(animationName))
				{
					throw new ArgumentException($"State map references an unregistered animation: {animationName}.", nameof(stateMap));
				}
			}
		}

		_preloadDefinitions = _clips.Values
			.Where(entry => entry.Definition.Preload)
			.Select(entry => entry.Definition.Definition)
			.GroupBy(definition => definition.CacheKey, StringComparer.Ordinal)
			.Select(group => group.First())
			.ToArray();
	}

	internal Task InitializeAsync()
	{
		lock (_gate)
		{
			if (_isDisposed || _preloadDefinitions.Count == 0)
			{
				return Task.CompletedTask;
			}

			return _initializationTask ??= InitializeCoreAsync(++_cacheGeneration);
		}
	}

	internal NexusVisualStateTransition<TState> TransitionTo(TState state)
	{
		lock (_gate)
		{
			ThrowIfDisposed();
			if (!_stateMap.TryGetValue(state, out IReadOnlySet<string>? requiredNames))
			{
				throw new ArgumentOutOfRangeException(nameof(state), state, "The visual state is not registered.");
			}

			if (_activeTransition is not null && EqualityComparer<TState>.Default.Equals(_activeTransition.State, state))
			{
				return _activeTransition;
			}

			long generation = ++_generation;
			foreach (string activeName in _activeNames.Where(name => !requiredNames.Contains(name)).ToArray())
			{
				_clips[activeName].Clip.Stop();
				_activeNames.Remove(activeName);
			}

			_currentState = state;
			_hasCurrentState = true;
			Task<NexusVisualTransitionResult> completion = RunTransitionAsync(state, generation, requiredNames);
			_activeTransition = new NexusVisualStateTransition<TState>(state, generation, completion);
			return _activeTransition;
		}
	}

	internal void StopAll()
	{
		lock (_gate)
		{
			_generation++;
			_currentState = default!;
			_hasCurrentState = false;
			_activeTransition = null;
			foreach (ClipEntry entry in _clips.Values)
			{
				entry.Clip.Stop();
			}

			_activeNames.Clear();
			ReleaseCacheLease();
		}
	}

	internal void Reset()
	{
		lock (_gate)
		{
			_generation++;
			_currentState = default!;
			_hasCurrentState = false;
			_activeTransition = null;
			foreach (ClipEntry entry in _clips.Values)
			{
				entry.Clip.Reset();
			}

			_activeNames.Clear();
			ReleaseCacheLease();
		}
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		StopAll();
		foreach (ClipEntry entry in _clips.Values)
		{
			entry.Clip.Dispose();
		}
	}

	private ClipEntry CreateClipEntry(NexusVisualStateAnimation animation)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(animation.Name);
		ArgumentNullException.ThrowIfNull(animation.Target);
		ArgumentNullException.ThrowIfNull(animation.Definition);
		ArgumentOutOfRangeException.ThrowIfLessThan(animation.FrameStep, 1);
		if (animation.PlaybackRate is double playbackRate && (!double.IsFinite(playbackRate) || playbackRate <= 0))
		{
			throw new ArgumentOutOfRangeException(nameof(animation), "PlaybackRate must be a positive finite value.");
		}

		return new ClipEntry
		{
			Definition = animation,
			Clip = new NexusAnimatedWebpClip(_motion, _frameCache, animation.Target, $"{_owner}.{animation.Name}", animation.Definition),
		};
	}

	private async Task InitializeCoreAsync(long cacheGeneration)
	{
		NexusAnimatedWebpCacheLease? lease = null;
		try
		{
			lease = await _frameCache.AcquireAsync(_owner, _preloadDefinitions);
			lock (_gate)
			{
				if (_isDisposed || cacheGeneration != _cacheGeneration)
				{
					lease.Dispose();
					return;
				}

				_cacheLease = lease;
			}
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"[VISUAL_STATE] Cache initialization failed. owner={_owner}, reason={ex.Message}");
			lease?.Dispose();
		}
	}

	private async Task<NexusVisualTransitionResult> RunTransitionAsync(
		TState state,
		long generation,
		IReadOnlySet<string> requiredNames)
	{
		try
		{
			await InitializeAsync();
			if (!IsCurrent(state, generation))
			{
				return NexusVisualTransitionResult.Superseded;
			}

			var oneShots = new List<Task<bool>>();
			foreach (string name in requiredNames)
			{
				if (!IsCurrent(state, generation))
				{
					return NexusVisualTransitionResult.Superseded;
				}

				ClipEntry entry = _clips[name];
				if (_activeNames.Contains(name))
				{
					continue;
				}

				bool prepared = await entry.Clip.PrepareAsync();
				if (!IsCurrent(state, generation))
				{
					return NexusVisualTransitionResult.Superseded;
				}

				if (!prepared)
				{
					return NexusVisualTransitionResult.Unavailable;
				}

				_activeNames.Add(name);
				if (entry.Definition.PlaybackKind == NexusVisualPlaybackKind.Loop)
				{
					entry.Clip.PlayLoop(_canRun);
					continue;
				}

				oneShots.Add(entry.Clip.PlayOnceAsync(
					_canRun,
					entry.Definition.PlaybackRate,
					entry.Definition.FrameStep,
					entry.Definition.FinalFrameBehavior));
			}

			if (oneShots.Count == 0)
			{
				return NexusVisualTransitionResult.Completed;
			}

			bool[] results = await Task.WhenAll(oneShots);
			if (!IsCurrent(state, generation))
			{
				return NexusVisualTransitionResult.Superseded;
			}

			return results.All(result => result)
				? NexusVisualTransitionResult.Completed
				: NexusVisualTransitionResult.Unavailable;
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"[VISUAL_STATE] Transition unavailable. owner={_owner}, state={state}, generation={generation}, reason={ex.Message}");
			return IsCurrent(state, generation)
				? NexusVisualTransitionResult.Unavailable
				: NexusVisualTransitionResult.Superseded;
		}
	}

	private bool IsCurrent(TState state, long generation)
		=> !_isDisposed
			&& generation == Volatile.Read(ref _generation)
			&& _hasCurrentState
			&& EqualityComparer<TState>.Default.Equals(_currentState, state);

	private void ReleaseCacheLease()
	{
		_cacheGeneration++;
		_cacheLease?.Dispose();
		_cacheLease = null;
		_initializationTask = null;
	}

	private void ThrowIfDisposed()
	{
		if (_isDisposed)
		{
			throw new ObjectDisposedException(nameof(NexusVisualStateAnimator<TState>));
		}
	}
}
