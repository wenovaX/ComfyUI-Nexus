using System.Runtime.InteropServices.WindowsRuntime;
using ComfyUI_Nexus.Diagnostics;
using Microsoft.Maui.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ComfyUI_Nexus.Ui;

/// <summary>
/// Owns one animated WebP surface. The MAUI Image stays mounted; this class attaches a
/// WriteableBitmap once and only updates its frame buffer for subsequent playback.
/// </summary>
internal sealed class NexusAnimatedWebpClip : IDisposable
{
	private readonly NexusMotionController _motion;
	private readonly Image _target;
	private readonly string _motionName;
	private readonly NexusAnimatedWebpDefinition _definition;
	private NexusAnimatedWebpFrameSet? _frames;
	private WriteableBitmap? _surface;
	private Microsoft.UI.Xaml.Controls.Image? _nativeImage;
	private TaskCompletionSource<bool>? _oneShotCompletion;
	private Func<bool>? _canRun;
	private int _frameIndex;
	private int _oneShotFrameStep = 1;
	private double _playbackRate = 1;
	private long _generation;
	private long _lastBlockedGeneration = -1;
	private NexusAnimatedWebpFinalFrameBehavior _finalFrameBehavior;
	private bool _isHoldingFinalFrame;
	private bool _isOneShot;
	private bool _isDisposed;

	internal NexusAnimatedWebpClip(
		NexusMotionController motion,
		Image target,
		string motionName,
		NexusAnimatedWebpDefinition definition)
	{
		_motion = motion ?? throw new ArgumentNullException(nameof(motion));
		_target = target ?? throw new ArgumentNullException(nameof(target));
		ArgumentException.ThrowIfNullOrWhiteSpace(motionName);
		_motionName = motionName;
		_definition = definition ?? throw new ArgumentNullException(nameof(definition));
	}

	internal async Task<bool> PrepareAsync()
	{
		if (_isDisposed)
		{
			NexusLog.Warning($"[ANIMATED_WEBP] Prepare skipped because the clip is disposed. asset='{_definition.PackageAssetName}'.");
			return false;
		}

		try
		{
			NexusLog.Trace($"[ANIMATED_WEBP] Prepare requested. asset='{_definition.PackageAssetName}', targetHandler={_target.Handler is not null}.");
			_frames ??= await NexusAnimatedWebpFrameCache.GetAsync(_definition);
			if (_isDisposed)
			{
				NexusLog.Warning($"[ANIMATED_WEBP] Prepare abandoned after decode because the clip was disposed. asset='{_definition.PackageAssetName}'.");
				return false;
			}

			if (!TryAttachSurface(_frames))
			{
				NexusLog.Warning($"[ANIMATED_WEBP] Prepare could not attach a native image surface. asset='{_definition.PackageAssetName}', targetHandler={_target.Handler is not null}, platformView='{_target.Handler?.PlatformView?.GetType().FullName ?? "none"}'.");
				return false;
			}

			RenderFrame(0);
			NexusLog.Trace($"[ANIMATED_WEBP] Prepare completed. asset='{_definition.PackageAssetName}', frames={_frames.Count}, surface={_surface?.PixelWidth}x{_surface?.PixelHeight}.");
			return true;
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"[ANIMATED_WEBP] Prepare failed. asset='{_definition.PackageAssetName}', reason={ex.Message}");
			return false;
		}
	}

	internal void PlayLoop(Func<bool> canRun)
	{
		ArgumentNullException.ThrowIfNull(canRun);
		_ = PlayLoopCoreAsync(canRun);
	}

	internal void Rewind()
	{
		Interlocked.Increment(ref _generation);
		_motion.Stop(_motionName);
		CompleteOneShot(false);
		_frameIndex = 0;
		ResetSurface();
	}

	internal async Task<bool> PlayOnceAsync(
		Func<bool> canRun,
		double? playbackRate = null,
		int frameStep = 1,
		NexusAnimatedWebpFinalFrameBehavior finalFrameBehavior = NexusAnimatedWebpFinalFrameBehavior.ResetToFirstFrame)
	{
		ArgumentNullException.ThrowIfNull(canRun);
		ArgumentOutOfRangeException.ThrowIfLessThan(frameStep, 1);
		if (playbackRate is double requestedRate && (!double.IsFinite(requestedRate) || requestedRate <= 0))
		{
			throw new ArgumentOutOfRangeException(nameof(playbackRate));
		}
		StopPlayback(releaseFrames: false);
		long generation = Interlocked.Increment(ref _generation);
		_canRun = canRun;
		_isOneShot = true;
		_oneShotFrameStep = frameStep;
		_playbackRate = playbackRate ?? _definition.PlaybackRate;
		_finalFrameBehavior = finalFrameBehavior;
		_isHoldingFinalFrame = false;
		_oneShotCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		NexusLog.Trace($"[ANIMATED_WEBP] One-shot requested. asset='{_definition.PackageAssetName}', generation={generation}.");

		if (!await PrepareAsync())
		{
			NexusLog.Warning($"[ANIMATED_WEBP] One-shot unavailable before playback. asset='{_definition.PackageAssetName}', generation={generation}.");
			CompleteOneShot(false);
			return false;
		}

		if (!CanRun(generation))
		{
			NexusLog.Warning($"[ANIMATED_WEBP] One-shot stopped before its first frame. asset='{_definition.PackageAssetName}', generation={generation}, reason='{GetRunBlockReason(generation)}'.");
			CompleteOneShot(false);
			return false;
		}

		_frameIndex = 0;
		RenderFrame(_frameIndex);
		if (_frames!.Count == 1)
		{
			CompleteOneShot(true);
			return true;
		}

		NexusLog.Trace($"[ANIMATED_WEBP] One-shot started. asset='{_definition.PackageAssetName}', generation={generation}, frames={_frames.Count}.");
		_motion.StartFrameLoop(
			_motionName,
			GetCurrentFrameInterval,
			() => CanRun(generation),
			AdvanceOneShotFrame,
			HandleOneShotStopped);

		return await _oneShotCompletion.Task;
	}

	internal void Stop()
		=> StopPlayback(releaseFrames: true);

	private void StopPlayback(bool releaseFrames)
	{
		Interlocked.Increment(ref _generation);
		_motion.Stop(_motionName);
		CompleteOneShot(false);
		_isHoldingFinalFrame = false;
		ResetSurface();
		if (releaseFrames)
		{
			ReleaseFrames();
		}
	}

	internal void Reset()
	{
		StopPlayback(releaseFrames: false);
		ResetSurface();
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		Stop();
		_frames = null;
		_surface = null;
		_nativeImage = null;
	}

	private async Task PlayLoopCoreAsync(Func<bool> canRun)
	{
		Stop();
		long generation = Interlocked.Increment(ref _generation);
		_canRun = canRun;
		_isOneShot = false;
		_playbackRate = _definition.PlaybackRate;

		if (!await PrepareAsync() || !CanRun(generation))
		{
			return;
		}

		_frameIndex = 0;
		RenderFrame(_frameIndex);
		NexusLog.Trace($"[ANIMATED_WEBP] Loop started. asset='{_definition.PackageAssetName}', generation={generation}, frames={_frames!.Count}.");
		_motion.StartFrameLoop(
			_motionName,
			GetCurrentFrameInterval,
			() => CanRun(generation),
			AdvanceLoopFrame,
			() =>
			{
				ResetSurface();
				ReleaseFrames();
			});
	}

	private TimeSpan GetCurrentFrameInterval()
		=> _frames?.GetFrameInterval(_frameIndex, _isOneShot ? _oneShotFrameStep : 1, wraps: !_isOneShot, playbackRate: _playbackRate)
			?? TimeSpan.FromMilliseconds(1);

	private bool TryAttachSurface(NexusAnimatedWebpFrameSet frames)
	{
		if (_target.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.Image nativeImage)
		{
			return false;
		}

		if (_surface is null || _surface.PixelWidth != frames.Width || _surface.PixelHeight != frames.Height)
		{
			_surface = new WriteableBitmap((int)frames.Width, (int)frames.Height);
		}

		if (!ReferenceEquals(_nativeImage, nativeImage) || !ReferenceEquals(nativeImage.Source, _surface))
		{
			_nativeImage = nativeImage;
			_nativeImage.Source = _surface;
			NexusLog.Trace($"[ANIMATED_WEBP] Native surface attached or restored. asset='{_definition.PackageAssetName}', surface={frames.Width}x{frames.Height}.");
		}

		return true;
	}

	private void AdvanceLoopFrame()
	{
		if (_frames is null)
		{
			return;
		}

		_frameIndex = (_frameIndex + 1) % _frames.Count;
		RenderFrame(_frameIndex);
	}

	private void AdvanceOneShotFrame()
	{
		if (_frames is null)
		{
			CompleteOneShot(false);
			return;
		}

		_frameIndex += _oneShotFrameStep;
		if (_frameIndex >= _frames.Count)
		{
			NexusLog.Trace($"[ANIMATED_WEBP] One-shot completed. asset='{_definition.PackageAssetName}', generation={Volatile.Read(ref _generation)}.");
			_isHoldingFinalFrame = _finalFrameBehavior == NexusAnimatedWebpFinalFrameBehavior.HoldFinalFrame;
			CompleteOneShot(true);
			_motion.Stop(_motionName);
			return;
		}

		RenderFrame(_frameIndex);
	}

	private void RenderFrame(int index)
	{
		if (_frames is null || _surface is null)
		{
			return;
		}

		using Stream pixelStream = _surface.PixelBuffer.AsStream();
		pixelStream.Position = 0;
		byte[] frame = _frames.Buffers[index];
		pixelStream.Write(frame, 0, frame.Length);
		_surface.Invalidate();
	}

	private void ResetSurface()
	{
		if (_frames is not null && _surface is not null)
		{
			RenderFrame(0);
		}
	}

	private void ReleaseFrames()
		=> _frames = null;

	private void HandleOneShotStopped()
	{
		bool holdFinalFrame = _isHoldingFinalFrame;
		_isHoldingFinalFrame = false;
		CompleteOneShot(false);
		if (!holdFinalFrame)
		{
			ResetSurface();
		}

		ReleaseFrames();
	}

	private bool CanRun(long generation)
	{
		string? reason = GetRunBlockReason(generation);
		if (reason is null)
		{
			return true;
		}

		if (Interlocked.Exchange(ref _lastBlockedGeneration, generation) != generation)
		{
			NexusLog.Trace($"[ANIMATED_WEBP] Playback stopped. asset='{_definition.PackageAssetName}', generation={generation}, reason='{reason}'.");
		}

		return false;
	}

	private string? GetRunBlockReason(long generation)
	{
		if (_isDisposed)
		{
			return "disposed";
		}

		if (generation != Volatile.Read(ref _generation))
		{
			return "superseded";
		}

		if (_nativeImage is null)
		{
			return "native-image-unavailable";
		}

		if (!ReferenceEquals(_target.Handler?.PlatformView, _nativeImage))
		{
			return "native-image-detached";
		}

		return _canRun?.Invoke() == true ? null : "owner-not-runnable";
	}

	private void CompleteOneShot(bool completed)
	{
		if (!_isOneShot)
		{
			return;
		}

		_isOneShot = false;
		_oneShotCompletion?.TrySetResult(completed);
		_oneShotCompletion = null;
	}
}
