using Microsoft.Maui.Controls;

namespace ComfyUI_Nexus.Ui;

internal enum NexusAnimatedWebpFinalFrameBehavior
{
	ResetToFirstFrame,
	HoldFinalFrame,
}

/// <summary>
/// Identifies a packaged animated WebP and its bounded UI decode profile.
/// </summary>
internal sealed class NexusAnimatedWebpDefinition
{
	internal NexusAnimatedWebpDefinition(
		string packageAssetName,
		int sourceFrameStride,
		uint decodePixelWidth,
		double playbackRate = 1)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(packageAssetName);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sourceFrameStride, 0);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(decodePixelWidth, 0u);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(playbackRate, 0);
		if (!double.IsFinite(playbackRate))
		{
			throw new ArgumentOutOfRangeException(nameof(playbackRate));
		}

		PackageAssetName = packageAssetName;
		SourceFrameStride = sourceFrameStride;
		DecodePixelWidth = decodePixelWidth;
		PlaybackRate = playbackRate;
	}

	internal string PackageAssetName { get; }
	internal int SourceFrameStride { get; }
	internal uint DecodePixelWidth { get; }
	internal double PlaybackRate { get; }
	internal string CacheKey => $"{PackageAssetName}|{SourceFrameStride}|{DecodePixelWidth}";
}
