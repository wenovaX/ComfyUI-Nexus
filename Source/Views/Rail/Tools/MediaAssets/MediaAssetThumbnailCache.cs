using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Setup.Services;

#if WINDOWS
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
#endif

namespace ComfyUI_Nexus.Views.Rail.Tools.MediaAssets;

internal sealed class MediaAssetThumbnailCache
{
	private const uint MaxThumbnailPixelSize = 360;
	private readonly object _manifestGate = new();
	private MediaThumbnailManifest? _manifest;

	private static string CacheRootPath
		=> ComfyInstallService.GetLocalRuntimePath("Cache/MediaAssets");

	private static string ManifestPath
		=> Path.Combine(CacheRootPath, "manifest.json");

	internal Task<IReadOnlyList<MediaAssetsView.MediaAssetEntry>> BuildEntriesAsync(
		IReadOnlyList<MediaAssetsView.MediaAssetSourceEntry> sourceEntries,
		CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(CacheRootPath);

		var entries = new List<MediaAssetsView.MediaAssetEntry>(sourceEntries.Count);

		foreach (var sourceEntry in sourceEntries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			entries.Add(CreateEntry(sourceEntry));
		}

		return Task.FromResult<IReadOnlyList<MediaAssetsView.MediaAssetEntry>>(entries);
	}

	internal MediaAssetsView.MediaAssetEntry CreateEntry(MediaAssetsView.MediaAssetSourceEntry sourceEntry)
		=> new(
			sourceEntry.Name,
			sourceEntry.FullPath,
			GetExistingThumbnailPath(sourceEntry),
			sourceEntry.CreatedAt,
			sourceEntry.ModifiedAt,
			sourceEntry.PixelWidth,
			sourceEntry.PixelHeight,
			sourceEntry.JobId,
			sourceEntry.IsBatchInferred,
			sourceEntry.Type,
			sourceEntry.Subfolder,
			sourceEntry.SizeBytes);

	internal string? GetExistingThumbnailPath(MediaAssetsView.MediaAssetSourceEntry sourceEntry)
	{
		Directory.CreateDirectory(CacheRootPath);
		string signature = GetSignature(sourceEntry);
		string thumbnailPath = GetThumbnailPath(sourceEntry);
		bool shouldSave = false;
		string? reusableThumbnailPath = null;

		lock (_manifestGate)
		{
			var manifest = GetManifestUnsafe();
			if (manifest.Files.TryGetValue(sourceEntry.FullPath, out var record))
			{
				if (string.Equals(record.Signature, signature, StringComparison.Ordinal) &&
					File.Exists(record.ThumbnailPath))
				{
					return record.ThumbnailPath;
				}

				TryDeleteFile(record.ThumbnailPath);
				manifest.Files.Remove(sourceEntry.FullPath);
				shouldSave = true;
			}

			if (File.Exists(thumbnailPath))
			{
				manifest.Files[sourceEntry.FullPath] = new MediaThumbnailManifestRecord
				{
					Signature = signature,
					ThumbnailPath = thumbnailPath,
				};
				shouldSave = true;
				reusableThumbnailPath = thumbnailPath;
			}
		}

		if (shouldSave)
		{
			SaveManifest();
		}

		return reusableThumbnailPath;
	}

	internal async Task<string?> EnsureThumbnailAsync(MediaAssetsView.MediaAssetSourceEntry sourceEntry, CancellationToken cancellationToken)
	{
		string? existingThumbnailPath = GetExistingThumbnailPath(sourceEntry);
		if (!string.IsNullOrWhiteSpace(existingThumbnailPath))
		{
			return existingThumbnailPath;
		}

		string thumbnailPath = GetThumbnailPath(sourceEntry);

#if WINDOWS
		try
		{
			string? parent = Path.GetDirectoryName(thumbnailPath);
			if (!string.IsNullOrWhiteSpace(parent))
			{
				Directory.CreateDirectory(parent);
			}

			await CreateWindowsThumbnailAsync(sourceEntry.FullPath, thumbnailPath, cancellationToken);
			if (!File.Exists(thumbnailPath))
			{
				return null;
			}

			UpdateManifest(sourceEntry, thumbnailPath);
			return thumbnailPath;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Warning($"Media thumbnail generation skipped: {ex.Message}");
			return null;
		}
#else
		await Task.CompletedTask;
		return null;
#endif
	}

	internal Task CleanupStaleThumbnailsAsync(CancellationToken cancellationToken)
		=> Task.Run(() =>
		{
			try
			{
				if (!Directory.Exists(CacheRootPath))
				{
					return;
				}

				cancellationToken.ThrowIfCancellationRequested();
				var activeThumbnails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				lock (_manifestGate)
				{
					var manifest = GetManifestUnsafe();
					bool shouldSave = false;
					foreach (var pair in manifest.Files.ToArray())
					{
						cancellationToken.ThrowIfCancellationRequested();
						string sourcePath = pair.Key;
						var record = pair.Value;
						bool isValid = TryCreateSourceEntry(sourcePath, out var sourceEntry) &&
							string.Equals(record.Signature, GetSignature(sourceEntry), StringComparison.Ordinal) &&
							File.Exists(record.ThumbnailPath);

						if (isValid)
						{
							activeThumbnails.Add(record.ThumbnailPath);
							continue;
						}

						TryDeleteFile(record.ThumbnailPath);
						manifest.Files.Remove(sourcePath);
						shouldSave = true;
					}

					if (shouldSave)
					{
						SaveManifestUnsafe();
					}
				}

				foreach (string cacheFile in Directory.EnumerateFiles(CacheRootPath, "*.png", SearchOption.TopDirectoryOnly))
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (!activeThumbnails.Contains(cacheFile))
					{
						File.Delete(cacheFile);
					}
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				NexusLog.Warning($"Media thumbnail cleanup skipped: {ex.Message}");
			}
		}, cancellationToken);

#if WINDOWS
	private static async Task CreateWindowsThumbnailAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		StorageFile sourceFile = await StorageFile.GetFileFromPathAsync(sourcePath);
		using IRandomAccessStream sourceStream = await sourceFile.OpenReadAsync();
		BitmapDecoder decoder = await BitmapDecoder.CreateAsync(sourceStream);

		uint width = decoder.PixelWidth;
		uint height = decoder.PixelHeight;
		double scale = Math.Min(1d, MaxThumbnailPixelSize / (double)Math.Max(width, height));
		uint scaledWidth = Math.Max(1, (uint)Math.Round(width * scale));
		uint scaledHeight = Math.Max(1, (uint)Math.Round(height * scale));

		var transform = new BitmapTransform
		{
			ScaledWidth = scaledWidth,
			ScaledHeight = scaledHeight,
			InterpolationMode = BitmapInterpolationMode.Fant,
		};

		PixelDataProvider provider = await decoder.GetPixelDataAsync(
			BitmapPixelFormat.Bgra8,
			BitmapAlphaMode.Premultiplied,
			transform,
			ExifOrientationMode.RespectExifOrientation,
			ColorManagementMode.ColorManageToSRgb);

		cancellationToken.ThrowIfCancellationRequested();
		byte[] pixels = provider.DetachPixelData();

		string temporaryPath = $"{targetPath}.tmp";
		if (File.Exists(temporaryPath))
		{
			File.Delete(temporaryPath);
		}

		string targetDirectory = Path.GetDirectoryName(temporaryPath) ?? CacheRootPath;
		string targetFileName = Path.GetFileName(temporaryPath);
		StorageFolder targetFolder = await StorageFolder.GetFolderFromPathAsync(targetDirectory);
		StorageFile targetFile = await targetFolder.CreateFileAsync(targetFileName, CreationCollisionOption.ReplaceExisting);
		using IRandomAccessStream targetStream = await targetFile.OpenAsync(FileAccessMode.ReadWrite);
		BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, targetStream);
		encoder.SetPixelData(
			BitmapPixelFormat.Bgra8,
			BitmapAlphaMode.Premultiplied,
			scaledWidth,
			scaledHeight,
			96,
			96,
			pixels);
		await encoder.FlushAsync();
		targetStream.Dispose();

		cancellationToken.ThrowIfCancellationRequested();
		if (File.Exists(targetPath))
		{
			File.Delete(targetPath);
		}

		File.Move(temporaryPath, targetPath);
	}
#endif

	private static string GetThumbnailPath(MediaAssetsView.MediaAssetSourceEntry sourceEntry)
	{
		string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(GetSignature(sourceEntry)))).ToLowerInvariant();
		return Path.Combine(CacheRootPath, $"{hash}.png");
	}

	private static string GetSignature(MediaAssetsView.MediaAssetSourceEntry sourceEntry)
		=> $"{Path.GetFullPath(sourceEntry.FullPath)}|{sourceEntry.ModifiedAt.Ticks}|{sourceEntry.SizeBytes}";

	private void UpdateManifest(MediaAssetsView.MediaAssetSourceEntry sourceEntry, string thumbnailPath)
	{
		lock (_manifestGate)
		{
			GetManifestUnsafe().Files[sourceEntry.FullPath] = new MediaThumbnailManifestRecord
			{
				Signature = GetSignature(sourceEntry),
				ThumbnailPath = thumbnailPath,
			};
		}

		SaveManifest();
	}

	private MediaThumbnailManifest GetManifestUnsafe()
	{
		if (_manifest != null)
		{
			return _manifest;
		}

		_manifest = LoadManifest();
		return _manifest;
	}

	private static MediaThumbnailManifest LoadManifest()
	{
		try
		{
			if (!File.Exists(ManifestPath))
			{
				return new MediaThumbnailManifest();
			}

			string json = File.ReadAllText(ManifestPath, Encoding.UTF8);
			var manifest = JsonSerializer.Deserialize<MediaThumbnailManifest>(json) ?? new MediaThumbnailManifest();
			manifest.Files = new Dictionary<string, MediaThumbnailManifestRecord>(manifest.Files, StringComparer.OrdinalIgnoreCase);
			return manifest;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			NexusLog.Warning($"Media thumbnail manifest reset: {ex.Message}");
			return new MediaThumbnailManifest();
		}
	}

	private void SaveManifest()
	{
		try
		{
			Directory.CreateDirectory(CacheRootPath);
			lock (_manifestGate)
			{
				SaveManifestUnsafe();
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			NexusLog.Warning($"Media thumbnail manifest save skipped: {ex.Message}");
		}
	}

	private void SaveManifestUnsafe()
	{
		string json = JsonSerializer.Serialize(GetManifestUnsafe(), new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(ManifestPath, json, Encoding.UTF8);
	}

	private static bool TryCreateSourceEntry(string sourcePath, out MediaAssetsView.MediaAssetSourceEntry sourceEntry)
	{
		sourceEntry = new MediaAssetsView.MediaAssetSourceEntry(string.Empty, sourcePath, DateTime.MinValue, DateTime.MinValue, null, null, null, false, "output", string.Empty, 0);
		try
		{
			var info = new FileInfo(sourcePath);
			if (!info.Exists)
			{
				return false;
			}

			sourceEntry = new MediaAssetsView.MediaAssetSourceEntry(info.Name, info.FullName, info.CreationTime, info.LastWriteTime, null, null, null, false, "output", string.Empty, info.Length);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			NexusLog.Warning($"Media thumbnail source stat skipped: {ex.Message}");
			return false;
		}
	}

	private static void TryDeleteFile(string? path)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			NexusLog.Warning($"Media thumbnail delete skipped: {ex.Message}");
		}
	}

	private sealed class MediaThumbnailManifest
	{
		public Dictionary<string, MediaThumbnailManifestRecord> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	}

	private sealed class MediaThumbnailManifestRecord
	{
		public string Signature { get; set; } = string.Empty;
		public string ThumbnailPath { get; set; } = string.Empty;
	}
}
