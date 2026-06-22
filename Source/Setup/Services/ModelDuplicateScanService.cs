namespace ComfyUI_Nexus.Setup.Services;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ComfyUI_Nexus.Setup.Models;

internal static class ModelDuplicateScanService
{
	private const string InternalSourceKind = "internal";
	private const string ExternalSourceKind = "external";
	private const string ReportFileName = "model-duplicate-scan.txt";

	private static readonly HashSet<string> ModelExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".safetensors",
		".ckpt",
		".pt",
		".pth",
		".bin",
		".onnx",
		".gguf",
		".ggml",
		".zip"
	};

	internal static async Task<ModelDuplicateScanResult> ScanAsync(
		string comfyPath,
		IEnumerable<string> modelLibraryRoots,
		CancellationToken cancellationToken,
		IProgress<ModelDuplicateScanProgress>? progress = null)
	{
		var roots = BuildRoots(comfyPath, modelLibraryRoots);
		var files = new List<ModelDuplicateFile>();
		var progressClock = Stopwatch.StartNew();
		long discoveredBytes = 0;
		progress?.Report(new ModelDuplicateScanProgress(ModelDuplicateScanStage.DiscoveringFiles, 0, 0, 0, 0));
		foreach (var root in roots)
		{
			cancellationToken.ThrowIfCancellationRequested();
			foreach (ModelDuplicateFile file in EnumerateModelFiles(root, cancellationToken))
			{
				files.Add(file);
				discoveredBytes += file.Length;
				ReportProgress(
					progress,
					progressClock,
					new ModelDuplicateScanProgress(ModelDuplicateScanStage.DiscoveringFiles, files.Count, 0, discoveredBytes, 0));
			}
		}

		var duplicateLengthGroups = files
			.GroupBy(file => file.Length)
			.Where(group => group.Count() > 1)
			.Select(group => group.ToList())
			.ToList();
		var hashCandidates = duplicateLengthGroups
			.SelectMany(group => group)
			.ToList();
		long hashBytesTotal = hashCandidates.Sum(file => file.Length);
		long hashBytesProcessed = 0;
		int hashFilesProcessed = 0;
		progress?.Report(new ModelDuplicateScanProgress(
			ModelDuplicateScanStage.PreparingHashes,
			0,
			hashCandidates.Count,
			0,
			hashBytesTotal));

		var groups = new List<ModelDuplicateGroup>();
		foreach (var lengthGroup in duplicateLengthGroups)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var hashedFiles = new List<(ModelDuplicateFile File, string Sha256)>();
			foreach (ModelDuplicateFile file in lengthGroup)
			{
				string hash = await ComputeSha256Async(
					file.FullPath,
					bytesRead =>
					{
						ReportProgress(
							progress,
							progressClock,
							new ModelDuplicateScanProgress(
								ModelDuplicateScanStage.HashingFiles,
								hashFilesProcessed,
								hashCandidates.Count,
								hashBytesProcessed + bytesRead,
								hashBytesTotal));
					},
					cancellationToken).ConfigureAwait(false);
				hashedFiles.Add((file, hash));
				hashFilesProcessed++;
				hashBytesProcessed += file.Length;
				progress?.Report(new ModelDuplicateScanProgress(
					ModelDuplicateScanStage.HashingFiles,
					hashFilesProcessed,
					hashCandidates.Count,
					hashBytesProcessed,
					hashBytesTotal));
			}

			groups.AddRange(hashedFiles
				.GroupBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase)
				.Where(group => group.Count() > 1)
				.Select(group => new ModelDuplicateGroup(
					group.Key,
					lengthGroup[0].Length,
					group
						.Select(item => item.File)
						.OrderBy(file => file.SourceKind, StringComparer.OrdinalIgnoreCase)
						.ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
						.ToList())));
		}

		groups = groups
			.OrderByDescending(group => group.Length)
			.ThenBy(group => group.Files[0].FileName, StringComparer.OrdinalIgnoreCase)
			.ToList();

		progress?.Report(new ModelDuplicateScanProgress(ModelDuplicateScanStage.WritingReport, files.Count, files.Count, discoveredBytes, discoveredBytes));
		string reportPath = await WriteReportAsync(files, groups, roots, cancellationToken).ConfigureAwait(false);
		return new ModelDuplicateScanResult(
			files.Count,
			files.Sum(file => file.Length),
			groups,
			reportPath);
	}

	private static IReadOnlyList<ModelRoot> BuildRoots(string comfyPath, IEnumerable<string> modelLibraryRoots)
	{
		var roots = new List<ModelRoot>();
		string internalModelsPath = Path.Combine(comfyPath, "models");
		if (Directory.Exists(internalModelsPath))
		{
			roots.Add(new ModelRoot(InternalSourceKind, "Internal models", internalModelsPath));
		}

		int index = 1;
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		seen.Add(Path.GetFullPath(internalModelsPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		foreach (string rawRoot in modelLibraryRoots)
		{
			if (string.IsNullOrWhiteSpace(rawRoot))
			{
				continue;
			}

			string root = Path.GetFullPath(rawRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			if (!Directory.Exists(root) || !seen.Add(root))
			{
				continue;
			}

			roots.Add(new ModelRoot(ExternalSourceKind, $"External library {index}", root));
			index++;
		}

		return roots;
	}

	private static IEnumerable<ModelDuplicateFile> EnumerateModelFiles(ModelRoot root, CancellationToken cancellationToken)
	{
		var pending = new Stack<string>();
		pending.Push(root.Path);
		while (pending.Count > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string directory = pending.Pop();
			IEnumerable<string> childDirectories;
			try
			{
				childDirectories = Directory.EnumerateDirectories(directory);
			}
			catch
			{
				continue;
			}

			foreach (string childDirectory in childDirectories)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (IsReparsePoint(childDirectory))
				{
					continue;
				}

				pending.Push(childDirectory);
			}

			IEnumerable<string> childFiles;
			try
			{
				childFiles = Directory.EnumerateFiles(directory);
			}
			catch
			{
				continue;
			}

			foreach (string file in childFiles)
			{
				cancellationToken.ThrowIfCancellationRequested();
				FileInfo info;
				try
				{
					info = new FileInfo(file);
				}
				catch
				{
					continue;
				}

				if (info.Length <= 0 || !ModelExtensions.Contains(info.Extension))
				{
					continue;
				}

				string relativePath = Path.GetRelativePath(root.Path, info.FullName);
				yield return new ModelDuplicateFile(
					root.Kind,
					root.Label,
					root.Path,
					info.FullName,
					relativePath,
					info.Name,
					info.Length);
			}
		}
	}

	private static async Task<string> ComputeSha256Async(
		string path,
		Action<long>? progress,
		CancellationToken cancellationToken)
	{
		await using FileStream stream = new(
			path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete,
			bufferSize: 1024 * 1024,
			useAsync: true);
		using HashAlgorithm hashAlgorithm = SHA256.Create();
		byte[] buffer = new byte[1024 * 1024 * 4];
		long bytesReadTotal = 0;
		int bytesRead;
		while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
		{
			hashAlgorithm.TransformBlock(buffer, 0, bytesRead, buffer, 0);
			bytesReadTotal += bytesRead;
			progress?.Invoke(bytesReadTotal);
		}

		hashAlgorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
		return Convert.ToHexString(hashAlgorithm.Hash ?? Array.Empty<byte>());
	}

	private static void ReportProgress(
		IProgress<ModelDuplicateScanProgress>? progress,
		Stopwatch progressClock,
		ModelDuplicateScanProgress value)
	{
		if (progress is null || progressClock.ElapsedMilliseconds < 120)
		{
			return;
		}

		progressClock.Restart();
		progress.Report(value);
	}

	private static async Task<string> WriteReportAsync(
		IReadOnlyList<ModelDuplicateFile> files,
		IReadOnlyList<ModelDuplicateGroup> groups,
		IReadOnlyList<ModelRoot> roots,
		CancellationToken cancellationToken)
	{
		string logDirectory = ComfyInstallService.GetLocalRuntimePath("Work/Logs");
		Directory.CreateDirectory(logDirectory);
		string reportPath = Path.Combine(logDirectory, ReportFileName);
		var builder = new StringBuilder();
		builder.AppendLine("ComfyUI Nexus Model Duplicate Scan");
		builder.AppendLine($"Generated: {DateTimeOffset.Now:O}");
		builder.AppendLine();
		builder.AppendLine("Roots:");
		foreach (ModelRoot root in roots)
		{
			builder.AppendLine($"- {root.Label}: {root.Path}");
		}

		builder.AppendLine();
		builder.AppendLine($"Scanned files: {files.Count}");
		builder.AppendLine($"Scanned size: {RuntimeBackupService.FormatBytes(files.Sum(file => file.Length))}");
		builder.AppendLine($"Duplicate groups: {groups.Count}");
		builder.AppendLine();
		for (int index = 0; index < groups.Count; index++)
		{
			ModelDuplicateGroup group = groups[index];
			builder.AppendLine($"[{index + 1}] {group.Files[0].FileName}");
			builder.AppendLine($"    Size: {RuntimeBackupService.FormatBytes(group.Length)}");
			builder.AppendLine($"    SHA-256: {group.Sha256}");
			foreach (ModelDuplicateFile file in group.Files)
			{
				builder.AppendLine($"    - {file.SourceLabel}: {file.FullPath}");
			}

			builder.AppendLine();
		}

		await File.WriteAllTextAsync(reportPath, builder.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
		return reportPath;
	}

	private static bool IsReparsePoint(string path)
	{
		try
		{
			return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
		}
		catch
		{
			return true;
		}
	}

	private sealed record ModelRoot(string Kind, string Label, string Path);
}
