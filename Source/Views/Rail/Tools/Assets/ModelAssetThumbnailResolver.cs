using System.Security.Cryptography;

namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

internal static class ModelAssetThumbnailResolver
{
	private const int GalleryNumberPadding = 3;

	private static readonly string[] ImageExtensions =
	[
		".png",
		".jpg",
		".jpeg",
		".webp"
	];

	internal static string? ResolveThumbnailPath(string modelFilePath)
		=> ResolveThumbnail(modelFilePath)?.Path;

	internal static ModelAssetThumbnail? ResolveThumbnail(string modelFilePath)
	{
		if (string.IsNullOrWhiteSpace(modelFilePath) || !File.Exists(modelFilePath))
		{
			return null;
		}

		string? directory = Path.GetDirectoryName(modelFilePath);
		if (string.IsNullOrWhiteSpace(directory))
		{
			return null;
		}

		string baseName = Path.GetFileNameWithoutExtension(modelFilePath);
		if (string.IsNullOrWhiteSpace(baseName))
		{
			return null;
		}

		foreach (string extension in ImageExtensions)
		{
			string candidate = Path.Combine(directory, baseName + extension);
			if (TryCreateThumbnail(candidate, out var thumbnail))
			{
				return thumbnail;
			}
		}

		string sidecarDirectory = Path.Combine(directory, baseName);
		if (!Directory.Exists(sidecarDirectory))
		{
			return null;
		}

		string? galleryImagePath = GetSortedGalleryImagePaths(sidecarDirectory, baseName).FirstOrDefault();
		return galleryImagePath is not null && TryCreateThumbnail(galleryImagePath, out var galleryThumbnail)
			? galleryThumbnail
			: null;
	}

	internal static ModelAssetThumbnail? ResolveImageFileThumbnail(string imageFilePath)
		=> IsSupportedImageFile(imageFilePath) && TryCreateThumbnail(imageFilePath, out var thumbnail)
			? thumbnail
			: null;

	internal static bool IsSupportedImageFile(string path)
		=> ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

	internal static ModelAssetThumbnailAddResult AddThumbnail(string modelFilePath, string sourceImagePath)
	{
		if (string.IsNullOrWhiteSpace(modelFilePath) || !File.Exists(modelFilePath))
		{
			return ModelAssetThumbnailAddResult.Failed("Model file was not found.");
		}

		if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
		{
			return ModelAssetThumbnailAddResult.Failed("Image file was not found.");
		}

		if (!IsSupportedImageFile(sourceImagePath))
		{
			return ModelAssetThumbnailAddResult.Failed("Unsupported image format.");
		}

		string? directory = Path.GetDirectoryName(modelFilePath);
		string baseName = Path.GetFileNameWithoutExtension(modelFilePath);
		if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(baseName))
		{
			return ModelAssetThumbnailAddResult.Failed("Invalid model file path.");
		}

		string extension = Path.GetExtension(sourceImagePath).ToLowerInvariant();
		string galleryDirectory = Path.Combine(directory, baseName);
		Directory.CreateDirectory(galleryDirectory);

		NormalizeGallery(modelFilePath);

		var existingImages = GetGalleryImages(modelFilePath);
		bool isFirstImage = existingImages.Count == 0;
		string targetFileName = isFirstImage
			? FormatGalleryFileName(1, extension)
			: FormatGalleryFileName(GetNextGalleryIndex(existingImages), extension);
		string targetPath = Path.Combine(galleryDirectory, targetFileName);

		File.Copy(sourceImagePath, targetPath, overwrite: false);

		if (isFirstImage)
		{
			SyncRootRepresentativeImage(modelFilePath, targetPath);
		}

		return new ModelAssetThumbnailAddResult(true, targetPath, targetFileName, string.Empty);
	}

	internal static bool SetPrimaryThumbnail(string modelFilePath, string galleryImageFileName)
	{
		if (string.IsNullOrWhiteSpace(modelFilePath) ||
			string.IsNullOrWhiteSpace(galleryImageFileName) ||
			!File.Exists(modelFilePath))
		{
			return false;
		}

		string? directory = Path.GetDirectoryName(modelFilePath);
		string baseName = Path.GetFileNameWithoutExtension(modelFilePath);
		if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(baseName))
		{
			return false;
		}

		string galleryDirectory = Path.Combine(directory, baseName);
		string selectedFileName = Path.GetFileName(galleryImageFileName);
		var normalizedFileNames = NormalizeGallery(modelFilePath);
		if (normalizedFileNames.TryGetValue(selectedFileName, out var normalizedFileName) &&
			!string.IsNullOrWhiteSpace(normalizedFileName))
		{
			selectedFileName = normalizedFileName;
		}

		string sourcePath = Path.Combine(galleryDirectory, selectedFileName);
		if (!File.Exists(sourcePath) || !IsSupportedImageFile(sourcePath))
		{
			return false;
		}

		SyncRootRepresentativeImage(modelFilePath, sourcePath);
		return true;
	}

	internal static IReadOnlyList<ModelAssetThumbnailGalleryImage> GetGalleryImages(string modelFilePath)
	{
		if (string.IsNullOrWhiteSpace(modelFilePath) || !File.Exists(modelFilePath))
		{
			return [];
		}

		string? directory = Path.GetDirectoryName(modelFilePath);
		string baseName = Path.GetFileNameWithoutExtension(modelFilePath);
		if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(baseName))
		{
			return [];
		}

		string galleryDirectory = Path.Combine(directory, baseName);
		if (!Directory.Exists(galleryDirectory))
		{
			return [];
		}

		string primaryPath = ResolveRootRepresentativePath(modelFilePath);
		string previewPath = ResolveThumbnailPath(modelFilePath) ?? string.Empty;
		return Directory.EnumerateFiles(galleryDirectory)
			.Where(IsSupportedImageFile)
			.Select(path => new ModelAssetThumbnailGalleryImage(
				Path.GetFileName(path),
				path,
				IsPrimaryGalleryImage(path, primaryPath, previewPath)))
			.OrderByDescending(image => image.IsPrimary)
			.ThenBy(image => GetGallerySortKey(image.FileName, baseName))
			.ThenBy(image => image.FileName, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static bool IsPrimaryGalleryImage(string path, string primaryPath, string previewPath)
	{
		if (!string.IsNullOrWhiteSpace(previewPath) &&
			string.Equals(path, previewPath, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return IsSameFileContent(path, primaryPath);
	}

	private static int GetNextGalleryIndex(IReadOnlyList<ModelAssetThumbnailGalleryImage> existingImages)
	{
		int maxIndex = 0;
		foreach (var image in existingImages)
		{
			if (TryParseCanonicalGalleryIndex(image.FileName, out int index))
			{
				maxIndex = Math.Max(maxIndex, index);
			}
		}

		return maxIndex + 1;
	}

	private static GallerySortKey GetGallerySortKey(string fileName, string baseName)
	{
		string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
		if (TryParseCanonicalGalleryIndex(fileName, out int canonicalIndex))
		{
			return new GallerySortKey(0, canonicalIndex);
		}

		if (string.Equals(nameWithoutExtension, baseName, StringComparison.OrdinalIgnoreCase))
		{
			return new GallerySortKey(1, 0);
		}

		string prefix = baseName + ".";
		if (nameWithoutExtension.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
			int.TryParse(nameWithoutExtension[prefix.Length..], out int index) &&
			index > 0)
		{
			return new GallerySortKey(1, index);
		}

		return new GallerySortKey(2, int.MaxValue);
	}

	private static bool TryParseCanonicalGalleryIndex(string fileName, out int index)
	{
		string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
		if (nameWithoutExtension.All(char.IsDigit) &&
			int.TryParse(nameWithoutExtension, out index) &&
			index > 0)
		{
			return true;
		}

		index = 0;
		return false;
	}

	private static string FormatGalleryFileName(int index, string extension)
	{
		string normalizedExtension = extension.StartsWith('.')
			? extension.ToLowerInvariant()
			: "." + extension.ToLowerInvariant();
		string number = index <= 999 ? index.ToString("D3") : index.ToString();
		return number + normalizedExtension;
	}

	private static IReadOnlyDictionary<string, string> NormalizeGallery(string modelFilePath)
	{
		string? directory = Path.GetDirectoryName(modelFilePath);
		string baseName = Path.GetFileNameWithoutExtension(modelFilePath);
		if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(baseName))
		{
			return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		}

		string galleryDirectory = Path.Combine(directory, baseName);
		if (!Directory.Exists(galleryDirectory))
		{
			return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		}

		var files = Directory.EnumerateFiles(galleryDirectory)
			.Where(IsSupportedImageFile)
			.OrderBy(path => GetGallerySortKey(Path.GetFileName(path), baseName))
			.ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
			.Select((path, index) => new GalleryNormalizeEntry(
				path,
				Path.GetFileName(path),
				Path.Combine(galleryDirectory, FormatGalleryFileName(index + 1, Path.GetExtension(path)))))
			.ToList();

		var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var file in files)
		{
			mapping[file.OriginalFileName] = Path.GetFileName(file.TargetPath);
		}

		bool alreadyCanonical = files.All(file => string.Equals(file.OriginalPath, file.TargetPath, StringComparison.OrdinalIgnoreCase));
		if (alreadyCanonical)
		{
			return mapping;
		}

		var tempEntries = new List<GalleryNormalizeTempEntry>(files.Count);
		try
		{
			for (int i = 0; i < files.Count; i++)
			{
				var file = files[i];
				string tempPath = Path.Combine(
					galleryDirectory,
					$".__nexus_thumb_tmp_{Guid.NewGuid():N}_{i}{Path.GetExtension(file.OriginalPath).ToLowerInvariant()}");
				File.Move(file.OriginalPath, tempPath);
				tempEntries.Add(new GalleryNormalizeTempEntry(file.OriginalPath, tempPath, file.TargetPath));
			}

			foreach (var entry in tempEntries)
			{
				File.Move(entry.TempPath, entry.TargetPath);
			}
		}
		catch
		{
			foreach (var entry in tempEntries)
			{
				try
				{
					if (File.Exists(entry.TempPath) && !File.Exists(entry.OriginalPath))
					{
						File.Move(entry.TempPath, entry.OriginalPath);
					}
				}
				catch
				{
					// Best-effort rollback only; callers will surface the original failure.
				}
			}

			throw;
		}

		return mapping;
	}

	private static IEnumerable<string> GetSortedGalleryImagePaths(string galleryDirectory, string baseName)
	{
		if (!Directory.Exists(galleryDirectory))
		{
			return [];
		}

		return Directory.EnumerateFiles(galleryDirectory)
			.Where(IsSupportedImageFile)
			.OrderBy(path => GetGallerySortKey(Path.GetFileName(path), baseName))
			.ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static void SyncRootRepresentativeImage(string modelFilePath, string sourceImagePath)
	{
		string? directory = Path.GetDirectoryName(modelFilePath);
		string baseName = Path.GetFileNameWithoutExtension(modelFilePath);
		if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(baseName))
		{
			return;
		}

		foreach (string extension in ImageExtensions)
		{
			string candidate = Path.Combine(directory, baseName + extension);
			if (File.Exists(candidate))
			{
				File.Delete(candidate);
			}
		}

		string targetPath = Path.Combine(directory, baseName + Path.GetExtension(sourceImagePath).ToLowerInvariant());
		File.Copy(sourceImagePath, targetPath, overwrite: true);
	}

	private static string ResolveRootRepresentativePath(string modelFilePath)
	{
		string? directory = Path.GetDirectoryName(modelFilePath);
		string baseName = Path.GetFileNameWithoutExtension(modelFilePath);
		if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(baseName))
		{
			return string.Empty;
		}

		foreach (string extension in ImageExtensions)
		{
			string candidate = Path.Combine(directory, baseName + extension);
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		return string.Empty;
	}

	private static bool IsSameFileContent(string path, string otherPath)
	{
		if (string.IsNullOrWhiteSpace(path) ||
			string.IsNullOrWhiteSpace(otherPath) ||
			!File.Exists(path) ||
			!File.Exists(otherPath))
		{
			return false;
		}

		try
		{
			var left = new FileInfo(path);
			var right = new FileInfo(otherPath);
			if (left.Length != right.Length)
			{
				return false;
			}

			using var leftStream = File.OpenRead(path);
			using var rightStream = File.OpenRead(otherPath);
			byte[] leftHash = SHA256.HashData(leftStream);
			byte[] rightHash = SHA256.HashData(rightStream);
			return leftHash.SequenceEqual(rightHash);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static bool TryCreateThumbnail(string path, out ModelAssetThumbnail thumbnail)
	{
		thumbnail = null!;
		if (!File.Exists(path))
		{
			return false;
		}

		if (!TryReadImageSize(path, out int width, out int height))
		{
			width = 1;
			height = 1;
		}

		thumbnail = new ModelAssetThumbnail(path, width, height);
		return true;
	}

	private static bool TryReadImageSize(string path, out int width, out int height)
	{
		width = 0;
		height = 0;

		try
		{
			using var stream = File.OpenRead(path);
			Span<byte> header = stackalloc byte[32];
			int read = stream.Read(header);
			if (read < 12)
			{
				return false;
			}

			if (TryReadPngSize(header[..read], out width, out height) ||
				TryReadWebpSize(header[..read], out width, out height))
			{
				return true;
			}

			stream.Position = 0;
			return TryReadJpegSize(stream, out width, out height);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static bool TryReadPngSize(ReadOnlySpan<byte> header, out int width, out int height)
	{
		width = 0;
		height = 0;
		if (header.Length < 24 ||
			header[0] != 0x89 ||
			header[1] != (byte)'P' ||
			header[2] != (byte)'N' ||
			header[3] != (byte)'G')
		{
			return false;
		}

		width = ReadBigEndianInt32(header[16..20]);
		height = ReadBigEndianInt32(header[20..24]);
		return width > 0 && height > 0;
	}

	private static bool TryReadWebpSize(ReadOnlySpan<byte> header, out int width, out int height)
	{
		width = 0;
		height = 0;
		if (header.Length < 30 ||
			header[0] != (byte)'R' ||
			header[1] != (byte)'I' ||
			header[2] != (byte)'F' ||
			header[3] != (byte)'F' ||
			header[8] != (byte)'W' ||
			header[9] != (byte)'E' ||
			header[10] != (byte)'B' ||
			header[11] != (byte)'P')
		{
			return false;
		}

		if (header[12] == (byte)'V' &&
			header[13] == (byte)'P' &&
			header[14] == (byte)'8' &&
			header[15] == (byte)'X')
		{
			width = 1 + ReadLittleEndianUInt24(header[24..27]);
			height = 1 + ReadLittleEndianUInt24(header[27..30]);
			return width > 0 && height > 0;
		}

		if (header[12] == (byte)'V' &&
			header[13] == (byte)'P' &&
			header[14] == (byte)'8' &&
			header[15] == (byte)'L' &&
			header[20] == 0x2F)
		{
			width = 1 + header[21] + ((header[22] & 0x3F) << 8);
			height = 1 + ((header[22] & 0xC0) >> 6) + (header[23] << 2) + ((header[24] & 0x0F) << 10);
			return width > 0 && height > 0;
		}

		if (header[12] == (byte)'V' &&
			header[13] == (byte)'P' &&
			header[14] == (byte)'8' &&
			header[15] == (byte)' ' &&
			header[23] == 0x9D &&
			header[24] == 0x01 &&
			header[25] == 0x2A)
		{
			width = (header[26] | (header[27] << 8)) & 0x3FFF;
			height = (header[28] | (header[29] << 8)) & 0x3FFF;
			return width > 0 && height > 0;
		}

		return false;
	}

	private static bool TryReadJpegSize(Stream stream, out int width, out int height)
	{
		width = 0;
		height = 0;
		if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
		{
			return false;
		}

		while (stream.Position < stream.Length)
		{
			int markerPrefix = stream.ReadByte();
			if (markerPrefix != 0xFF)
			{
				continue;
			}

			int marker = stream.ReadByte();
			while (marker == 0xFF)
			{
				marker = stream.ReadByte();
			}

			if (marker < 0)
			{
				return false;
			}

			if (marker is 0xD8 or 0xD9)
			{
				continue;
			}

			int segmentLength = ReadBigEndianUInt16(stream);
			if (segmentLength < 2)
			{
				return false;
			}

			if (IsJpegStartOfFrame(marker))
			{
				stream.ReadByte();
				height = ReadBigEndianUInt16(stream);
				width = ReadBigEndianUInt16(stream);
				return width > 0 && height > 0;
			}

			stream.Seek(segmentLength - 2, SeekOrigin.Current);
		}

		return false;
	}

	private static bool IsJpegStartOfFrame(int marker)
		=> marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

	private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes)
		=> (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

	private static int ReadLittleEndianUInt24(ReadOnlySpan<byte> bytes)
		=> bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);

	private static int ReadBigEndianUInt16(Stream stream)
	{
		int high = stream.ReadByte();
		int low = stream.ReadByte();
		if (high < 0 || low < 0)
		{
			return 0;
		}

		return (high << 8) | low;
	}
}

internal sealed record ModelAssetThumbnail(string Path, int Width, int Height);

internal sealed record ModelAssetThumbnailGalleryImage(string FileName, string Path, bool IsPrimary);

internal readonly record struct GallerySortKey(int Group, int Index) : IComparable<GallerySortKey>
{
	public int CompareTo(GallerySortKey other)
	{
		int group = Group.CompareTo(other.Group);
		return group != 0 ? group : Index.CompareTo(other.Index);
	}
}

internal sealed record GalleryNormalizeEntry(string OriginalPath, string OriginalFileName, string TargetPath);

internal sealed record GalleryNormalizeTempEntry(string OriginalPath, string TempPath, string TargetPath);

internal sealed record ModelAssetThumbnailAddResult(bool Success, string Path, string FileName, string Error)
{
	internal static ModelAssetThumbnailAddResult Failed(string error)
		=> new(false, string.Empty, string.Empty, error);
}

public sealed record ModelAssetThumbnailPreviewRequest(string ThumbnailPath, double Width, double Height);
