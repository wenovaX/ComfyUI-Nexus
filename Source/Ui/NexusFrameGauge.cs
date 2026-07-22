using System.Runtime.InteropServices.WindowsRuntime;
using ComfyUI_Nexus.Diagnostics;
using Microsoft.Maui.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ComfyUI_Nexus.Ui;

/// <summary>
/// Displays a stateful WebP frame atlas as a gauge. New values only replace the
/// pending target; an active transition advances one frame per dispatcher tick.
/// </summary>
internal sealed class NexusFrameGauge
{
	private static readonly TimeSpan FrameStepInterval = TimeSpan.FromMilliseconds(16);
	private readonly NexusMotionController _motion;
	private readonly NexusAnimatedWebpFrameCache _frameCache;
	private readonly Image _target;
	private readonly string _motionName;
	private readonly NexusAnimatedWebpDefinition _definition;
	private NexusAnimatedWebpFrameSet? _frames;
	private WriteableBitmap? _surface;
	private Microsoft.UI.Xaml.Controls.Image? _nativeImage;
	private Task<bool>? _prepareTask;
	private int _currentFrame;
	private int _targetFrame;
	private bool _isPrepared;
	private bool _isStepping;

	internal NexusFrameGauge(
		NexusMotionController motion,
		NexusAnimatedWebpFrameCache frameCache,
		Image target,
		string motionName,
		NexusAnimatedWebpDefinition definition)
	{
		_motion = motion ?? throw new ArgumentNullException(nameof(motion));
		_frameCache = frameCache ?? throw new ArgumentNullException(nameof(frameCache));
		_target = target ?? throw new ArgumentNullException(nameof(target));
		ArgumentException.ThrowIfNullOrWhiteSpace(motionName);
		_motionName = motionName;
		_definition = definition ?? throw new ArgumentNullException(nameof(definition));
	}

	internal async Task<bool> PrepareAsync()
	{
		Task<bool> prepareTask = _prepareTask ??= PrepareCoreAsync();
		bool prepared = await prepareTask;
		if (!prepared && ReferenceEquals(_prepareTask, prepareTask))
		{
			_prepareTask = null;
		}

		return prepared;
	}

	internal void SetTarget(double normalizedValue, bool animate = true)
	{
		_targetFrame = GetFrameIndex(normalizedValue);
		if (!_isPrepared)
		{
			return;
		}

		if (!animate)
		{
			SetFrameImmediately(_targetFrame);
			return;
		}

		if (_currentFrame == _targetFrame)
		{
			RenderFrame(_currentFrame);
			return;
		}

		StartStepping();
	}

	internal void SetVisible(bool isVisible)
	{
		_target.Opacity = isVisible ? 1 : 0;
		if (!isVisible)
		{
			_motion.Stop(_motionName);
			_isStepping = false;
		}
	}

	internal void RefreshSurfaceAttachment()
	{
		if (!_isPrepared || _frames is null || !TryAttachSurface(_frames))
		{
			return;
		}

		RenderFrame(_currentFrame);
	}

	internal void Reset()
	{
		_motion.Stop(_motionName);
		_isStepping = false;
		_currentFrame = 0;
		_targetFrame = 0;
		if (_isPrepared)
		{
			RenderFrame(0);
		}
	}

	private void SetFrameImmediately(int frame)
	{
		int clampedFrame = ClampFrame(frame);
		_targetFrame = clampedFrame;
		_currentFrame = clampedFrame;
		_motion.Stop(_motionName);
		_isStepping = false;
		if (_isPrepared)
		{
			RenderFrame(_currentFrame);
		}
	}

	private async Task<bool> PrepareCoreAsync()
	{
		try
		{
			_frames ??= await _frameCache.GetAsync(_definition);
			if (!TryAttachSurface(_frames))
			{
				return false;
			}

			_currentFrame = ClampFrame(_targetFrame);
			_targetFrame = _currentFrame;
			_isPrepared = true;
			RenderFrame(_currentFrame);
			NexusLog.Trace($"[FRAME_GAUGE] Prepared. asset='{_definition.PackageAssetName}', frames={_frames.Count}.");
			return true;
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"[FRAME_GAUGE] Prepare failed. asset='{_definition.PackageAssetName}', reason={ex.Message}");
			return false;
		}
	}

	private void StartStepping()
	{
		if (_isStepping)
		{
			return;
		}

		_isStepping = true;
		_motion.StartFrameLoop(
			_motionName,
			() => FrameStepInterval,
			CanStep,
			AdvanceFrame,
			() => _isStepping = false);
	}

	private bool CanStep()
		=> _isPrepared
			&& _currentFrame != _targetFrame
			&& _nativeImage is not null
			&& ReferenceEquals(_target.Handler?.PlatformView, _nativeImage);

	private void AdvanceFrame()
	{
		if (_currentFrame == _targetFrame)
		{
			_motion.Stop(_motionName);
			return;
		}

		_currentFrame += Math.Sign(_targetFrame - _currentFrame);
		RenderFrame(_currentFrame);
		if (_currentFrame == _targetFrame)
		{
			_motion.Stop(_motionName);
		}
	}

	private int GetFrameIndex(double normalizedValue)
	{
		int lastFrame = Math.Max((_frames?.Count ?? 32) - 1, 0);
		return (int)Math.Round(Math.Clamp(normalizedValue, 0, 1) * lastFrame, MidpointRounding.AwayFromZero);
	}

	private int ClampFrame(int frame)
		=> Math.Clamp(frame, 0, Math.Max((_frames?.Count ?? 32) - 1, 0));

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
		}

		return true;
	}

	private void RenderFrame(int frameIndex)
	{
		if (_frames is null || _surface is null)
		{
			return;
		}

		using Stream pixelStream = _surface.PixelBuffer.AsStream();
		pixelStream.Position = 0;
		byte[] frame = _frames.Buffers[ClampFrame(frameIndex)];
		pixelStream.Write(frame, 0, frame.Length);
		_surface.Invalidate();
	}
}
