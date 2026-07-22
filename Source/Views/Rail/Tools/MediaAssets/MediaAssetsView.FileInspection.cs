using System.Buffers.Binary;
using System.Text.RegularExpressions;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Setup.Services;
using Path = System.IO.Path;

namespace ComfyUI_Nexus.Views.Rail.Tools.MediaAssets;

public partial class MediaAssetsView
{	private static IReadOnlyList<MediaAssetSourceEntry> ScanMediaFiles(
		string rootPath,
		MediaAssetSortDirection sortDirection,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
		{
			return [];
		}

		var entries = new List<MediaAssetSourceEntry>();
		SearchOption searchOption = rootPath.EndsWith(
			$"{Path.DirectorySeparatorChar}{ComfyPathOptions.InputDirectoryName}",
			StringComparison.OrdinalIgnoreCase)
			? SearchOption.TopDirectoryOnly
			: SearchOption.AllDirectories;

		foreach (string filePath in Directory.EnumerateFiles(rootPath, "*", searchOption))
		{
			if (cancellationToken.IsCancellationRequested)
			{
				break;
			}

			if (!SupportedImageExtensions.Contains(Path.GetExtension(filePath)) || !File.Exists(filePath))
			{
				continue;
			}

			try
			{
				var info = new FileInfo(filePath);
				if (!info.Exists || info.Length > MaxPreviewFileBytes)
				{
					continue;
				}

				entries.Add(new MediaAssetSourceEntry(
					info.Name,
					info.FullName,
					info.CreationTime,
					info.LastWriteTime,
					TryReadImageDimensions(info.FullName, out int pixelWidth, out int pixelHeight) ? pixelWidth : null,
					pixelWidth > 0 && pixelHeight > 0 ? pixelHeight : null,
					null,
					false,
					"output",
					string.Empty,
					info.Length));
			}
			catch (IOException)
			{
				// Files may still be writing; the watcher will schedule another refresh shortly.
			}
			catch (UnauthorizedAccessException)
			{
				// Keep the browser resilient when a generated file or folder is locked.
			}
		}

		IOrderedEnumerable<MediaAssetSourceEntry> orderedEntries = sortDirection == MediaAssetSortDirection.RecentFirst
			? entries
				.OrderByDescending(entry => entry.CreatedAt)
				.ThenByDescending(entry => entry.ModifiedAt)
				.ThenByDescending(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
			: entries
				.OrderBy(entry => entry.CreatedAt)
				.ThenBy(entry => entry.ModifiedAt)
				.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

		return orderedEntries.Take(MaxVisibleItems).ToList();
	}

	private static OutputJobBuildResult BuildSourceEntriesFromJobs(
		string outputRootPath,
		IReadOnlyList<MediaAssetJobPreview> jobs,
		MediaAssetSortDirection sortDirection,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(outputRootPath) || !Directory.Exists(outputRootPath))
		{
			return new OutputJobBuildResult([], []);
		}

		var entries = new List<MediaAssetSourceEntry>();
		var missingJobIds = new List<string>();
		var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var jobFileKeys = jobs
			.Where(job => IsOutputJob(job))
			.Select(GetJobFileKey)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		foreach (var job in jobs.Where(IsOutputJob))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!AddJobFileIfExists(outputRootPath, job, entries, addedPaths, isBatchInferred: false) &&
				!string.IsNullOrWhiteSpace(job.JobId))
			{
				missingJobIds.Add(job.JobId);
				continue;
			}

			foreach (var batchJob in ExpandForwardBatch(outputRootPath, job, jobFileKeys, cancellationToken))
			{
				AddJobFileIfExists(outputRootPath, batchJob, entries, addedPaths, isBatchInferred: true);
			}
		}

		IOrderedEnumerable<MediaAssetSourceEntry> orderedEntries = sortDirection == MediaAssetSortDirection.RecentFirst
			? entries
				.OrderByDescending(entry => entry.CreatedAt)
				.ThenByDescending(entry => entry.ModifiedAt)
				.ThenByDescending(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
			: entries
				.OrderBy(entry => entry.CreatedAt)
				.ThenBy(entry => entry.ModifiedAt)
				.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

		return new OutputJobBuildResult(
			orderedEntries.Take(MaxVisibleItems).ToList(),
			missingJobIds.Distinct(StringComparer.Ordinal).ToList());
	}

	private static bool IsOutputJob(MediaAssetJobPreview job)
		=> string.Equals(job.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
			string.Equals(job.Type, "output", StringComparison.OrdinalIgnoreCase) &&
			!string.IsNullOrWhiteSpace(job.Filename);

	private static IEnumerable<MediaAssetJobPreview> ExpandForwardBatch(
		string outputRootPath,
		MediaAssetJobPreview job,
		HashSet<string> jobFileKeys,
		CancellationToken cancellationToken)
	{
		var parsed = ParseSequenceFilename(job.Filename);
		if (parsed == null)
		{
			yield break;
		}

		for (int offset = 1; offset <= MediaJobBatchForwardLimit; offset++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int nextNumber = parsed.Value.Number + offset;
			string filename = $"{parsed.Value.Prefix}{nextNumber.ToString().PadLeft(parsed.Value.Width, '0')}{parsed.Value.Suffix}";
			var candidate = job with { JobId = string.Empty, Filename = filename };
			if (jobFileKeys.Contains(GetJobFileKey(candidate)))
			{
				continue;
			}

			string? fullPath = ResolveJobOutputPath(outputRootPath, candidate);
			if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
			{
				continue;
			}

			yield return candidate;
		}
	}

	private static string GetJobFileKey(MediaAssetJobPreview job)
		=> $"{job.Type}|{job.Subfolder}|{job.Filename}";

	private static SequenceFilename? ParseSequenceFilename(string filename)
	{
		var match = Regex.Match(filename, @"^(.*?)(\d+)(_?\.[^.]+)$", RegexOptions.CultureInvariant);
		if (!match.Success || !int.TryParse(match.Groups[2].Value, out int number))
		{
			return null;
		}

		return new SequenceFilename(
			match.Groups[1].Value,
			number,
			match.Groups[2].Value.Length,
			match.Groups[3].Value);
	}

	private static bool AddJobFileIfExists(
		string outputRootPath,
		MediaAssetJobPreview job,
		List<MediaAssetSourceEntry> entries,
		HashSet<string> addedPaths,
		bool isBatchInferred)
	{
		string? fullPath = ResolveJobOutputPath(outputRootPath, job);
		if (string.IsNullOrWhiteSpace(fullPath) || !addedPaths.Add(fullPath))
		{
			return false;
		}

		if (TryCreateSourceEntry(fullPath, out var sourceEntry))
		{
			entries.Add(sourceEntry with
			{
				JobId = isBatchInferred ? null : job.JobId,
				IsBatchInferred = isBatchInferred,
				Type = job.Type,
				Subfolder = job.Subfolder
			});
			return true;
		}

		return false;
	}

	private static string? ResolveJobOutputPath(string outputRootPath, MediaAssetJobPreview job)
	{
		try
		{
			string relativePath = string.IsNullOrWhiteSpace(job.Subfolder)
				? job.Filename
				: Path.Combine(job.Subfolder, job.Filename);
			string fullPath = Path.GetFullPath(Path.Combine(outputRootPath, relativePath));
			string rootPath = Path.GetFullPath(outputRootPath);
			return fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			return null;
		}
	}

	private static bool TryCreateSourceEntry(string filePath, out MediaAssetSourceEntry sourceEntry)
	{
		sourceEntry = new MediaAssetSourceEntry(string.Empty, filePath, DateTime.MinValue, DateTime.MinValue, null, null, null, false, "output", string.Empty, 0);
		try
		{
			var info = new FileInfo(filePath);
			if (!info.Exists || info.Length > MaxPreviewFileBytes || !SupportedImageExtensions.Contains(info.Extension))
			{
				return false;
			}

			sourceEntry = new MediaAssetSourceEntry(
				info.Name,
				info.FullName,
				info.CreationTime,
				info.LastWriteTime,
				TryReadImageDimensions(info.FullName, out int pixelWidth, out int pixelHeight) ? pixelWidth : null,
				pixelWidth > 0 && pixelHeight > 0 ? pixelHeight : null,
				null,
				false,
				"output",
				string.Empty,
				info.Length);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static bool TryReadImageDimensions(string path, out int width, out int height)
	{
		width = 0;
		height = 0;

		try
		{
			Span<byte> header = stackalloc byte[32];
			using var stream = File.OpenRead(path);
			int read = stream.Read(header);
			if (read < 24)
			{
				return false;
			}

			if (header[0] == 0x89 && header[1] == (byte)'P' && header[2] == (byte)'N' && header[3] == (byte)'G')
			{
				width = BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4));
				height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4));
				return width > 0 && height > 0;
			}

			if (header[0] == 0xFF && header[1] == 0xD8)
			{
				return TryReadJpegDimensions(stream, out width, out height);
			}

			if (header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
				header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
			{
				return TryReadWebpDimensions(header, stream, out width, out height);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}

		return false;
	}

	private static bool TryReadJpegDimensions(Stream stream, out int width, out int height)
	{
		width = 0;
		height = 0;
		stream.Position = 2;
		byte[] lengthBuffer = new byte[2];

		while (stream.Position + 9 < stream.Length)
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

			if (stream.Read(lengthBuffer) != 2)
			{
				return false;
			}

			int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(lengthBuffer);
			if (segmentLength < 2 || stream.Position + segmentLength - 2 > stream.Length)
			{
				return false;
			}

			bool isStartOfFrame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
			if (isStartOfFrame)
			{
				Span<byte> frame = stackalloc byte[5];
				if (stream.Read(frame) != 5)
				{
					return false;
				}

				height = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(1, 2));
				width = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(3, 2));
				return width > 0 && height > 0;
			}

			stream.Position += segmentLength - 2;
		}

		return false;
	}

	private static bool TryReadWebpDimensions(ReadOnlySpan<byte> header, Stream stream, out int width, out int height)
	{
		width = 0;
		height = 0;
		string chunkType = $"{(char)header[12]}{(char)header[13]}{(char)header[14]}{(char)header[15]}";
		if (chunkType == "VP8X" && header.Length >= 30)
		{
			width = 1 + header[24] + (header[25] << 8) + (header[26] << 16);
			height = 1 + header[27] + (header[28] << 8) + (header[29] << 16);
			return width > 0 && height > 0;
		}

		if (chunkType == "VP8L" && header.Length >= 25)
		{
			uint bits = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(21, 4));
			width = (int)((bits & 0x3FFF) + 1);
			height = (int)(((bits >> 14) & 0x3FFF) + 1);
			return width > 0 && height > 0;
		}

		if (chunkType == "VP8 ")
		{
			stream.Position = 26;
			Span<byte> size = stackalloc byte[4];
			if (stream.Read(size) != 4)
			{
				return false;
			}

			width = BinaryPrimitives.ReadUInt16LittleEndian(size.Slice(0, 2)) & 0x3FFF;
			height = BinaryPrimitives.ReadUInt16LittleEndian(size.Slice(2, 2)) & 0x3FFF;
			return width > 0 && height > 0;
		}

		return false;
	}

	private IReadOnlyList<MediaAssetEntry> SortEntries(IReadOnlyList<MediaAssetEntry> entries)
		=> _sortDirection == MediaAssetSortDirection.RecentFirst
			? entries
				.OrderByDescending(entry => entry.CreatedAt)
				.ThenByDescending(entry => entry.ModifiedAt)
				.ThenByDescending(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
				.ToList()
			: entries
				.OrderBy(entry => entry.CreatedAt)
				.ThenBy(entry => entry.ModifiedAt)
				.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();

	private IReadOnlyList<MediaAssetSourceEntry> SortSourceEntries(IReadOnlyList<MediaAssetSourceEntry> entries)
		=> _sortDirection == MediaAssetSortDirection.RecentFirst
			? entries
				.OrderByDescending(entry => entry.CreatedAt)
				.ThenByDescending(entry => entry.ModifiedAt)
				.ThenByDescending(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
				.ToList()
			: entries
				.OrderBy(entry => entry.CreatedAt)
				.ThenBy(entry => entry.ModifiedAt)
				.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();

}
