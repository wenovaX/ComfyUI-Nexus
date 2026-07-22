using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using ComfyUI_Nexus.Diagnostics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ComfyUI_Nexus.Ui;

/// <summary>
/// App-wide single-flight decode cache. Frame buffers are immutable and can be reused by
/// multiple clips; each clip still owns its own native WriteableBitmap surface.
/// </summary>
internal sealed class NexusAnimatedWebpFrameCache : IDisposable
{
	private const long MaxCachedBytes = 160L * 1024 * 1024;
	private sealed class CacheEntry
	{
		internal CacheEntry(Func<Task<NexusAnimatedWebpFrameSet>> decode)
		{
			Frames = new Lazy<Task<NexusAnimatedWebpFrameSet>>(decode, LazyThreadSafetyMode.ExecutionAndPublication);
		}

		internal Lazy<Task<NexusAnimatedWebpFrameSet>> Frames { get; }
		internal long LastAccess { get; set; }
	}

	private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
	private readonly Dictionary<string, int> _retainedKeys = new(StringComparer.Ordinal);
	private readonly object _trimGate = new();
	private long _accessSequence;
	private bool _disposed;

	internal async Task<NexusAnimatedWebpCacheLease> AcquireAsync(NexusAnimatedWebpCacheGroup group)
	{
		IReadOnlyList<NexusAnimatedWebpDefinition> definitions = NexusAnimatedWebpCacheCatalog.GetDefinitions(group);
		return await AcquireAsync(group.ToString(), definitions).ConfigureAwait(false);
	}

	internal async Task<NexusAnimatedWebpCacheLease> AcquireAsync(
		string scope,
		IReadOnlyList<NexusAnimatedWebpDefinition> definitions)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		ArgumentException.ThrowIfNullOrWhiteSpace(scope);
		ArgumentNullException.ThrowIfNull(definitions);
		IReadOnlyList<NexusAnimatedWebpDefinition> distinctDefinitions = definitions
			.GroupBy(definition => definition.CacheKey, StringComparer.Ordinal)
			.Select(group => group.First())
			.ToArray();

		Retain(distinctDefinitions);
		bool isReady = true;
		try
		{
			foreach (NexusAnimatedWebpDefinition definition in distinctDefinitions)
			{
				await GetAsync(definition).ConfigureAwait(false);
			}
		}
		catch (Exception ex)
		{
			isReady = false;
			NexusLog.Warning($"[ANIMATED_WEBP] Cache scope prewarm incomplete. scope={scope}, reason={ex.Message}");
		}

		NexusLog.Trace($"[ANIMATED_WEBP] Cache scope prepared. scope={scope}, ready={isReady}, assets={distinctDefinitions.Count}.");
		return new NexusAnimatedWebpCacheLease(this, scope, distinctDefinitions);
	}

	internal async Task<NexusAnimatedWebpFrameSet> GetAsync(NexusAnimatedWebpDefinition definition)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		ArgumentNullException.ThrowIfNull(definition);
		CacheEntry entry = _entries.GetOrAdd(
			definition.CacheKey,
			_ => new CacheEntry(() => DecodeAsync(definition)));

		try
		{
			NexusAnimatedWebpFrameSet frameSet = await entry.Frames.Value.ConfigureAwait(false);
			entry.LastAccess = Interlocked.Increment(ref _accessSequence);
			Trim(definition.CacheKey);
			return frameSet;
		}
		catch
		{
			if (_entries.TryGetValue(definition.CacheKey, out CacheEntry? current)
				&& ReferenceEquals(current, entry))
			{
				_entries.TryRemove(definition.CacheKey, out _);
			}
			throw;
		}
	}

	private void Retain(IReadOnlyList<NexusAnimatedWebpDefinition> definitions)
	{
		lock (_trimGate)
		{
			foreach (NexusAnimatedWebpDefinition definition in definitions)
			{
				_retainedKeys.TryGetValue(definition.CacheKey, out int count);
				_retainedKeys[definition.CacheKey] = count + 1;
			}
		}
	}

	internal void Release(NexusAnimatedWebpCacheLease lease)
	{
		ArgumentNullException.ThrowIfNull(lease);
		lock (_trimGate)
		{
			foreach (NexusAnimatedWebpDefinition definition in lease.Definitions)
			{
				if (!_retainedKeys.TryGetValue(definition.CacheKey, out int count))
				{
					continue;
				}

				if (count > 1)
				{
					_retainedKeys[definition.CacheKey] = count - 1;
					continue;
				}

				_retainedKeys.Remove(definition.CacheKey);
				_entries.TryRemove(definition.CacheKey, out _);
			}
		}

		NexusLog.Trace($"[ANIMATED_WEBP] Cache scope released. scope={lease.Scope}, assets={lease.Definitions.Count}.");
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		lock (_trimGate)
		{
			_retainedKeys.Clear();
			_entries.Clear();
		}
	}

	private void Trim(string protectedKey)
	{
		lock (_trimGate)
		{
			var completed = _entries
				.Where(pair => pair.Value.Frames.IsValueCreated && pair.Value.Frames.Value.IsCompletedSuccessfully)
				.Select(pair => (pair.Key, Entry: pair.Value, Frames: pair.Value.Frames.Value.Result))
				.ToList();
			long totalBytes = completed.Sum(item => item.Frames.ByteSize);

			foreach (var item in completed.OrderBy(item => item.Entry.LastAccess))
			{
				if (totalBytes <= MaxCachedBytes)
				{
					break;
				}

				if (string.Equals(item.Key, protectedKey, StringComparison.Ordinal))
				{
					continue;
				}

				if (_retainedKeys.ContainsKey(item.Key))
				{
					continue;
				}

				if (_entries.TryGetValue(item.Key, out CacheEntry? current) && ReferenceEquals(current, item.Entry))
				{
					_entries.TryRemove(item.Key, out _);
					totalBytes -= item.Frames.ByteSize;
					NexusLog.Trace($"[ANIMATED_WEBP] Frames evicted. asset='{item.Key}', cachedBytes={totalBytes}.");
				}
			}
		}
	}

	private static async Task<NexusAnimatedWebpFrameSet> DecodeAsync(NexusAnimatedWebpDefinition definition)
	{
		await using Stream packageStream = await FileSystem.OpenAppPackageFileAsync(definition.PackageAssetName).ConfigureAwait(false);
		using var sourceBytes = new MemoryStream();
		await packageStream.CopyToAsync(sourceBytes).ConfigureAwait(false);

		byte[] packageBytes = sourceBytes.ToArray();
		using var randomAccessStream = new InMemoryRandomAccessStream();
		using (var writer = new DataWriter(randomAccessStream))
		{
			writer.WriteBytes(packageBytes);
			await writer.StoreAsync().AsTask().ConfigureAwait(false);
			writer.DetachStream();
		}
		randomAccessStream.Seek(0);

		BitmapDecoder decoder = await BitmapDecoder.CreateAsync(randomAccessStream).AsTask().ConfigureAwait(false);
		uint targetWidth = Math.Min(decoder.PixelWidth, definition.DecodePixelWidth);
		uint targetHeight = Math.Max(1, (uint)Math.Round(decoder.PixelHeight * (targetWidth / (double)decoder.PixelWidth)));
		var transform = new BitmapTransform
		{
			ScaledWidth = targetWidth,
			ScaledHeight = targetHeight,
			InterpolationMode = BitmapInterpolationMode.Fant,
		};

		IReadOnlyList<TimeSpan> sourceFrameDurations = ReadFrameDurations(packageBytes, checked((int)decoder.FrameCount));
		var buffers = new List<byte[]>();
		var frameDurations = new List<TimeSpan>();
		for (uint frameIndex = 0; frameIndex < decoder.FrameCount; frameIndex += (uint)definition.SourceFrameStride)
		{
			BitmapFrame frame = await decoder.GetFrameAsync(frameIndex).AsTask().ConfigureAwait(false);
			PixelDataProvider provider = await frame.GetPixelDataAsync(
				BitmapPixelFormat.Bgra8,
				BitmapAlphaMode.Premultiplied,
				transform,
				ExifOrientationMode.RespectExifOrientation,
				ColorManagementMode.ColorManageToSRgb).AsTask().ConfigureAwait(false);
			buffers.Add(provider.DetachPixelData());
			frameDurations.Add(SumFrameDurations(sourceFrameDurations, checked((int)frameIndex), definition.SourceFrameStride));
		}

		if (buffers.Count == 0)
		{
			throw new InvalidOperationException("The animated WebP did not contain any decodable frames.");
		}

		var frameSet = new NexusAnimatedWebpFrameSet(targetWidth, targetHeight, buffers, frameDurations);
		NexusLog.Trace($"[ANIMATED_WEBP] Frames cached. asset='{definition.PackageAssetName}', frames={frameSet.Count}, size={frameSet.Width}x{frameSet.Height}.");
		return frameSet;
	}

	private static IReadOnlyList<TimeSpan> ReadFrameDurations(byte[] packageBytes, int expectedFrameCount)
	{
		const int RiffHeaderLength = 12;
		const int ChunkHeaderLength = 8;
		const int AnimationFrameHeaderLength = 16;
		var durations = new List<TimeSpan>();

		for (int offset = RiffHeaderLength; offset + ChunkHeaderLength <= packageBytes.Length;)
		{
			string chunkName = System.Text.Encoding.ASCII.GetString(packageBytes, offset, 4);
			int chunkLength = checked((int)BitConverter.ToUInt32(packageBytes, offset + 4));
			int chunkDataOffset = offset + ChunkHeaderLength;
			if (chunkLength < 0 || chunkDataOffset + chunkLength > packageBytes.Length)
			{
				break;
			}

			if (chunkName == "ANMF" && chunkLength >= AnimationFrameHeaderLength)
			{
				int durationMilliseconds = packageBytes[chunkDataOffset + 12]
					| (packageBytes[chunkDataOffset + 13] << 8)
					| (packageBytes[chunkDataOffset + 14] << 16);
				durations.Add(TimeSpan.FromMilliseconds(Math.Max(durationMilliseconds, 1)));
			}

			offset = chunkDataOffset + chunkLength + (chunkLength & 1);
		}

		if (durations.Count == expectedFrameCount)
		{
			return durations;
		}

		return Enumerable.Repeat(TimeSpan.FromMilliseconds(100), Math.Max(expectedFrameCount, 1)).ToArray();
	}

	private static TimeSpan SumFrameDurations(IReadOnlyList<TimeSpan> sourceFrameDurations, int startIndex, int stride)
	{
		TimeSpan duration = TimeSpan.Zero;
		int endIndex = Math.Min(startIndex + stride, sourceFrameDurations.Count);
		for (int index = startIndex; index < endIndex; index++)
		{
			duration += sourceFrameDurations[index];
		}

		return duration > TimeSpan.Zero ? duration : TimeSpan.FromMilliseconds(1);
	}
}

internal sealed class NexusAnimatedWebpCacheLease : IDisposable
{
	private readonly NexusAnimatedWebpFrameCache _owner;
	private int _isReleased;

	internal NexusAnimatedWebpCacheLease(NexusAnimatedWebpFrameCache owner, string scope, IReadOnlyList<NexusAnimatedWebpDefinition> definitions)
	{
		_owner = owner;
		Scope = scope;
		Definitions = definitions;
	}

	internal string Scope { get; }
	internal IReadOnlyList<NexusAnimatedWebpDefinition> Definitions { get; }

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isReleased, 1) == 0)
		{
			_owner.Release(this);
		}
	}
}

internal sealed class NexusAnimatedWebpFrameSet
{
	internal NexusAnimatedWebpFrameSet(uint width, uint height, IReadOnlyList<byte[]> buffers, IReadOnlyList<TimeSpan> frameDurations)
	{
		if (buffers.Count != frameDurations.Count)
		{
			throw new ArgumentException("Every decoded frame must have a matching duration.", nameof(frameDurations));
		}

		Width = width;
		Height = height;
		Buffers = buffers;
		FrameDurations = frameDurations;
	}

	internal uint Width { get; }
	internal uint Height { get; }
	internal IReadOnlyList<byte[]> Buffers { get; }
	internal IReadOnlyList<TimeSpan> FrameDurations { get; }
	internal int Count => Buffers.Count;
	internal long ByteSize => Buffers.Sum(buffer => (long)buffer.Length);

	internal TimeSpan GetFrameInterval(int frameIndex, int frameStep, bool wraps, double playbackRate)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(frameStep, 1);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(playbackRate, 0);
		if (!double.IsFinite(playbackRate))
		{
			throw new ArgumentOutOfRangeException(nameof(playbackRate));
		}

		TimeSpan duration = TimeSpan.Zero;
		for (int offset = 0; offset < frameStep; offset++)
		{
			int index = frameIndex + offset;
			if (!wraps && index >= FrameDurations.Count)
			{
				break;
			}

			duration += FrameDurations[index % FrameDurations.Count];
		}

		return TimeSpan.FromTicks(Math.Max(1, (long)Math.Round(duration.Ticks / playbackRate)));
	}
}
