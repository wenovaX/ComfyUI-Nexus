namespace ComfyUI_Nexus.Setup.Services;

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ComfyUI_Nexus.Setup.Models;

internal sealed record RuntimeBackupAnalysis(
	bool IsSuccess,
	string Message,
	IReadOnlyList<string> Targets,
	long SourceBytes,
	long FileCount,
	long RequiredBytes,
	long AvailableBytes,
	string BackupRoot,
	string ComfyPath);

internal sealed record RuntimeBackupEntry(string Path, string Name, string Format, bool IsComplete);

internal sealed class RuntimeBackupService
{
	private const string RuntimeTag = "[Runtime]";
	private const string BackupNamePrefix = "comfyui-nexus-runtime-backup-";
	private const string LegacyBackupNamePrefix = "runtime-backup-";
	internal const string BackupCompleteMarkerFileName = ".nexus-backup-complete.json";
	private const int CopyProgressReportIntervalMs = 250;
	private const long MinimumSafetyBytes = 1024L * 1024 * 1024;
	private const string RestoreTempMarker = ".nexus-restore-";
	private const string RestoreJournalFileName = "runtime-restore-journal.json";
	private const string RestorePreviewFileName = "runtime-restore-preview.txt";
	private static readonly JsonSerializerOptions ManifestJsonOptions = new()
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	private readonly Action<string> _log;
	private readonly Action<double, string> _progress;
	private readonly object _progressLock = new();
	private readonly Stopwatch _progressClock = new();
	private double _lastProgress = -1;

	internal RuntimeBackupService(Action<string> log, Action<double, string> progress)
	{
		_log = log;
		_progress = progress;
	}

	internal static string GetConfiguredBackupRoot(SetupSettings? settings = null)
	{
		settings ??= SetupSettingsService.Instance.Settings;
		return string.IsNullOrWhiteSpace(settings.RuntimeBackupPath)
			? Path.Combine(ComfyInstallService.RootPath, "Backups")
			: Path.GetFullPath(settings.RuntimeBackupPath);
	}

	internal static bool TryGetAvailableSpace(string path, out long availableBytes, out string error)
		=> TryGetAvailableBytes(path, out availableBytes, out error);

	internal void CleanupPendingRestoreTemps()
	{
		try
		{
			CleanupRestoreTempsAsync(CancellationToken.None).GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			_log($"{RuntimeTag} [ERROR] Startup restore temp cleanup failed: {ex.Message}");
		}
	}

	internal async Task<RuntimeBackupAnalysis> AnalyzeBackupAsync(
		IEnumerable<string> backupTargets,
		CancellationToken cancellationToken)
	{
		try
		{
			var targets = NormalizeTargets(backupTargets);
			string comfyPath = GetActiveComfyPath();
			if (targets.Count == 0)
			{
				return Failure("Select at least one runtime folder to back up.", targets);
			}

			var sources = ResolveBackupSources(targets, comfyPath);
			if (sources.Count == 0)
			{
				return Failure("No selected runtime folders were available to back up.", targets);
			}
			var availableTargets = sources.Select(source => source.Target).ToList();

			var totals = await ScanPathsAsync(sources.Select(source => source.Path), cancellationToken).ConfigureAwait(false);
			long safetyBytes = Math.Max(MinimumSafetyBytes, CalculatePercent(totals.Bytes, 2));
			long requiredBytes = AddWithoutOverflow(totals.Bytes, safetyBytes);
			string backupRoot = GetConfiguredBackupRoot();
			if (sources.Any(source => IsSameOrDescendantPath(backupRoot, source.Path)))
			{
				return new RuntimeBackupAnalysis(
					false,
					"The backup destination cannot be inside a selected source folder.",
					availableTargets,
					totals.Bytes,
					totals.FileCount,
					requiredBytes,
					-1,
					backupRoot,
					comfyPath);
			}

			if (!TryGetAvailableBytes(backupRoot, out long availableBytes, out string error))
			{
				return new RuntimeBackupAnalysis(
					false,
					error,
					availableTargets,
					totals.Bytes,
					totals.FileCount,
					requiredBytes,
					-1,
					backupRoot,
					comfyPath);
			}

			if (availableBytes < requiredBytes)
			{
				return new RuntimeBackupAnalysis(
					false,
					"Backup destination does not have enough free space.",
					availableTargets,
					totals.Bytes,
					totals.FileCount,
					requiredBytes,
					availableBytes,
					backupRoot,
					comfyPath);
			}

			return new RuntimeBackupAnalysis(
				true,
				"Backup space is ready.",
				availableTargets,
				totals.Bytes,
				totals.FileCount,
				requiredBytes,
				availableBytes,
				backupRoot,
				comfyPath);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_log($"{RuntimeTag} [ERROR] Backup analysis failed: {ex.Message}");
			return Failure($"Backup analysis failed: {ex.Message}", NormalizeTargets(backupTargets));
		}
	}

	internal RuntimeBackupAnalysis RefreshAvailableSpace(RuntimeBackupAnalysis analysis)
	{
		if (!TryGetAvailableBytes(analysis.BackupRoot, out long availableBytes, out string error))
		{
			return analysis with { IsSuccess = false, Message = error, AvailableBytes = -1 };
		}

		return analysis with
		{
			IsSuccess = availableBytes >= analysis.RequiredBytes,
			Message = availableBytes >= analysis.RequiredBytes
				? "Backup space is ready."
				: "Backup destination does not have enough free space.",
			AvailableBytes = availableBytes
		};
	}

	internal async Task<SetupStepResult> BackupRuntimeDataAsync(
		IEnumerable<string> backupTargets,
		string format,
		CancellationToken cancellationToken)
	{
		RuntimeBackupAnalysis analysis = await AnalyzeBackupAsync(backupTargets, cancellationToken).ConfigureAwait(false);
		return await BackupRuntimeDataAsync(analysis, format, cancellationToken).ConfigureAwait(false);
	}

	internal async Task<SetupStepResult> BackupRuntimeDataAsync(
		RuntimeBackupAnalysis analysis,
		string format,
		CancellationToken cancellationToken)
	{
		if (!analysis.IsSuccess)
		{
			return new SetupStepResult(false, analysis.Message, 0);
		}

		string normalizedFormat = RuntimeBackupFormats.IsKnown(format) ? format : RuntimeBackupFormats.Folder;
		string finalPath = GetUniqueBackupPath(analysis.BackupRoot, normalizedFormat);
		string partialPath = finalPath + ".partial";
		try
		{
			Directory.CreateDirectory(analysis.BackupRoot);
			ResetProgressThrottle();
			var sources = ResolveBackupSources(analysis.Targets, analysis.ComfyPath);
			if (sources.Count != analysis.Targets.Count)
			{
				return new SetupStepResult(false, "A selected backup source changed after analysis. Calculate the backup again.", 0);
			}

			if (normalizedFormat == RuntimeBackupFormats.Zip)
			{
				await CreateZipBackupAsync(partialPath, sources, analysis, cancellationToken).ConfigureAwait(false);
				File.Move(partialPath, finalPath);
			}
			else
			{
				Directory.CreateDirectory(partialPath);
				await CreateFolderBackupAsync(partialPath, sources, analysis, cancellationToken).ConfigureAwait(false);
				Directory.Move(partialPath, finalPath);
			}

			ReportProgress(analysis.SourceBytes, analysis.SourceBytes, "Runtime backup completed.");
			return new SetupStepResult(true, $"Runtime backup completed: {finalPath}", 1);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_log($"{RuntimeTag} [ERROR] Backup failed: {ex.Message}");
			TryDeletePartial(partialPath);
			return new SetupStepResult(false, $"Backup failed: {ex.Message}", 0);
		}
		catch
		{
			TryDeletePartial(partialPath);
			throw;
		}
	}

	internal async Task<RuntimeRestoreAnalysis> AnalyzeRestoreAsync(string backupPath, CancellationToken cancellationToken)
	{
		try
		{
			await CleanupRestoreTempsAsync(cancellationToken).ConfigureAwait(false);
			string comfyPath = GetActiveComfyPath();
			if (string.IsNullOrWhiteSpace(backupPath))
			{
				return RestoreFailure("Select a valid runtime backup.", backupPath, comfyPath: comfyPath);
			}

			string format;
			List<RestoreSourceFile> sourceFiles;
			if (File.Exists(backupPath) && string.Equals(Path.GetExtension(backupPath), ".zip", StringComparison.OrdinalIgnoreCase))
			{
				format = RuntimeBackupFormats.Zip;
				sourceFiles = await ReadZipRestoreSourcesAsync(backupPath, cancellationToken).ConfigureAwait(false);
			}
			else if (Directory.Exists(backupPath))
			{
				format = RuntimeBackupFormats.Folder;
				sourceFiles = await ReadFolderRestoreSourcesAsync(backupPath, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				return RestoreFailure("Select a valid runtime backup.", backupPath, comfyPath: comfyPath);
			}

			if (sourceFiles.Count == 0)
			{
				return RestoreFailure("The selected backup does not contain models or custom_nodes.", backupPath, format, comfyPath: comfyPath);
			}

			var items = new List<RuntimeRestoreItem>(sourceFiles.Count);
			var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			long addBytes = 0;
			long positiveGrowthBytes = 0;
			long largestReplacementBytes = 0;
			foreach (RestoreSourceFile source in sourceFiles)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!sourcePaths.Add(source.RelativePath))
				{
					return RestoreFailure($"The backup contains a duplicate runtime path: {source.RelativePath}", backupPath, format, comfyPath: comfyPath);
				}
				string destinationPath = GetSafeRestorePath(source.RelativePath, comfyPath);
				if (!File.Exists(destinationPath))
				{
					items.Add(new RuntimeRestoreItem(
						source.RelativePath,
						RuntimeRestoreAction.Add,
						source.Length,
						source.Sha256,
						-1,
						-1));
					addBytes = AddWithoutOverflow(addBytes, source.Length);
					continue;
				}

				var destination = new FileInfo(destinationPath);
				long destinationLength = destination.Length;
				long destinationWriteTicks = destination.LastWriteTimeUtc.Ticks;
				if (destinationLength != source.Length)
				{
					items.Add(new RuntimeRestoreItem(
						source.RelativePath,
						RuntimeRestoreAction.Replace,
						source.Length,
						source.Sha256,
						destinationLength,
						destinationWriteTicks));
					positiveGrowthBytes = AddWithoutOverflow(
						positiveGrowthBytes,
						Math.Max(0, source.Length - destinationLength));
					largestReplacementBytes = Math.Max(largestReplacementBytes, source.Length);
					continue;
				}

				string sourceHash = string.IsNullOrWhiteSpace(source.Sha256)
					? await ComputeRestoreSourceHashAsync(backupPath, format, source.RelativePath, cancellationToken).ConfigureAwait(false)
					: source.Sha256;
				string destinationHash = await ComputeFileHashAsync(destinationPath, cancellationToken).ConfigureAwait(false);
				RuntimeRestoreAction action = string.Equals(sourceHash, destinationHash, StringComparison.OrdinalIgnoreCase)
					? RuntimeRestoreAction.Unchanged
					: RuntimeRestoreAction.Replace;
				items.Add(new RuntimeRestoreItem(
					source.RelativePath,
					action,
					source.Length,
					sourceHash,
					destinationLength,
					destinationWriteTicks));
				if (action == RuntimeRestoreAction.Replace)
				{
					largestReplacementBytes = Math.Max(largestReplacementBytes, source.Length);
				}
			}

			var targets = sourceFiles
				.Select(source => source.RelativePath.Split('/')[0])
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			foreach (string target in targets)
			{
				string targetRoot = Path.Combine(comfyPath, target);
				foreach (string existingPath in EnumerateFilesSafely(targetRoot, cancellationToken))
				{
					string relative = $"{target}/{Path.GetRelativePath(targetRoot, existingPath).Replace(Path.DirectorySeparatorChar, '/')}";
					if (!sourcePaths.Contains(relative))
					{
						var existing = new FileInfo(existingPath);
						items.Add(new RuntimeRestoreItem(
							relative,
							RuntimeRestoreAction.Retained,
							-1,
							string.Empty,
							existing.Length,
							existing.LastWriteTimeUtc.Ticks));
					}
				}
			}

			long copyBytes = items
				.Where(item => item.Action is RuntimeRestoreAction.Add or RuntimeRestoreAction.Replace)
				.Aggregate(0L, (total, item) => AddWithoutOverflow(total, item.SourceLength));
			long requiredBytes = copyBytes == 0
				? 0
				: AddWithoutOverflow(
					AddWithoutOverflow(addBytes, positiveGrowthBytes),
					AddWithoutOverflow(
						largestReplacementBytes,
						Math.Max(MinimumSafetyBytes, CalculatePercent(copyBytes, 2))));
			if (!TryGetAvailableBytes(comfyPath, out long availableBytes, out string spaceError))
			{
				return RestoreFailure(spaceError, backupPath, format, items, copyBytes, requiredBytes, -1, comfyPath);
			}

			string previewPath = await WriteRestorePreviewAsync(
				backupPath,
				format,
				targets,
				items,
				copyBytes,
				requiredBytes,
				availableBytes,
				comfyPath,
				cancellationToken).ConfigureAwait(false);
			bool enoughSpace = availableBytes >= requiredBytes;
			return new RuntimeRestoreAnalysis(
				enoughSpace,
				enoughSpace ? "Runtime restore analysis is ready." : "The ComfyUI destination does not have enough free space.",
				Path.GetFullPath(backupPath),
				format,
				comfyPath,
				targets,
				items,
				copyBytes,
				requiredBytes,
				availableBytes,
				previewPath);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_log($"{RuntimeTag} [ERROR] Restore analysis failed: {ex.Message}");
			return RestoreFailure($"Restore analysis failed: {ex.Message}", backupPath, comfyPath: GetActiveComfyPath());
		}
	}

	internal async Task<RuntimeRestoreResult> RestoreRuntimeBackupAsync(
		RuntimeRestoreAnalysis analysis,
		CancellationToken cancellationToken)
	{
		if (!analysis.IsSuccess)
		{
			return new RuntimeRestoreResult(false, analysis.Message, 0, 0);
		}

		var pendingItems = analysis.Items
			.Where(item => item.Action is RuntimeRestoreAction.Add or RuntimeRestoreAction.Replace)
			.ToList();
		if (!IsSamePath(GetActiveComfyPath(), analysis.ComfyPath))
		{
			return new RuntimeRestoreResult(false, "The active ComfyUI path changed after restore analysis. Analyze the backup again.", 0, pendingItems.Count);
		}

		if (!ValidateRestoreSnapshot(pendingItems, analysis.ComfyPath, out string validationError))
		{
			return new RuntimeRestoreResult(false, validationError, 0, pendingItems.Count);
		}

		if (!TryGetAvailableBytes(analysis.ComfyPath, out long availableBytes, out string spaceError)
			|| availableBytes < analysis.RequiredBytes)
		{
			string message = availableBytes < 0
				? spaceError
				: $"The ComfyUI destination does not have enough free space. Required: {FormatBytes(analysis.RequiredBytes)}. Available: {FormatBytes(availableBytes)}.";
			return new RuntimeRestoreResult(false, message, 0, pendingItems.Count);
		}

		string sessionId = Guid.NewGuid().ToString("N");
		var journal = new RestoreJournal(sessionId, analysis.BackupPath, []);
		int completed = 0;
		long restoredBytes = 0;
		ResetProgressThrottle();
		try
		{
			await SaveRestoreJournalAsync(journal, cancellationToken).ConfigureAwait(false);
			if (analysis.BackupFormat == RuntimeBackupFormats.Zip)
			{
				using var archive = ZipFile.OpenRead(analysis.BackupPath);
				var entries = archive.Entries
					.Where(entry => !string.IsNullOrEmpty(entry.Name))
					.ToDictionary(
						entry => NormalizeRelativePath(entry.FullName),
						StringComparer.OrdinalIgnoreCase);
				foreach (RuntimeRestoreItem item in pendingItems)
				{
					ZipArchiveEntry entry = entries.GetValueOrDefault(item.RelativePath)
						?? throw new InvalidDataException($"Backup entry is missing: {item.RelativePath}");
					await using Stream source = entry.Open();
					await StageAndCommitRestoreFileAsync(
						source,
						item,
						sessionId,
						journal,
						analysis.ComfyPath,
						value =>
						{
							restoredBytes += value;
							ReportProgress(restoredBytes, analysis.CopyBytes, $"Restoring {item.RelativePath}...");
						},
						cancellationToken).ConfigureAwait(false);
					completed++;
				}
			}
			else
			{
				foreach (RuntimeRestoreItem item in pendingItems)
				{
					string sourcePath = GetSafeFolderBackupPath(analysis.BackupPath, item.RelativePath);
					await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
					await StageAndCommitRestoreFileAsync(
						source,
						item,
						sessionId,
						journal,
						analysis.ComfyPath,
						value =>
						{
							restoredBytes += value;
							ReportProgress(restoredBytes, analysis.CopyBytes, $"Restoring {item.RelativePath}...");
						},
						cancellationToken).ConfigureAwait(false);
					completed++;
				}
			}

			DeleteRestoreJournal();
			ReportProgress(analysis.CopyBytes, analysis.CopyBytes, "Runtime backup restored.");
			return new RuntimeRestoreResult(
				true,
				$"Runtime restore completed. {completed} file(s) merged; {analysis.UnchangedCount} identical file(s) skipped; {analysis.RetainedCount} existing file(s) retained.",
				completed,
				0);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_log($"{RuntimeTag} [ERROR] Restore failed after {completed} file(s): {ex.Message}");
			await CleanupJournalTempsAsync(journal, analysis.ComfyPath, CancellationToken.None).ConfigureAwait(false);
			return new RuntimeRestoreResult(false, $"Restore failed: {ex.Message}", completed, pendingItems.Count - completed);
		}
		catch
		{
			await CleanupJournalTempsAsync(journal, analysis.ComfyPath, CancellationToken.None).ConfigureAwait(false);
			throw;
		}
	}

	internal Task<SetupStepResult> DeleteRuntimeBackupAsync(string backupPath, CancellationToken cancellationToken)
	{
		return Task.Run(() =>
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!IsSafeManagedBackupPath(backupPath))
				{
					return new SetupStepResult(false, $"Refusing to delete unsafe backup path: {backupPath}", 0);
				}

				if (Directory.Exists(backupPath))
				{
					ClearReadOnlyAttributes(new DirectoryInfo(backupPath));
					Directory.Delete(backupPath, recursive: true);
				}
				else if (File.Exists(backupPath))
				{
					File.SetAttributes(backupPath, FileAttributes.Normal);
					File.Delete(backupPath);
				}
				else
				{
					return new SetupStepResult(false, "Select a valid runtime backup.", 0);
				}

				return new SetupStepResult(true, "Runtime backup deleted.", 1);
			}
			catch (Exception ex)
			{
				_log($"{RuntimeTag} [ERROR] Backup delete failed: {ex.Message}");
				return new SetupStepResult(false, $"Backup delete failed: {ex.Message}", 0);
			}
		}, cancellationToken);
	}

	internal IReadOnlyList<RuntimeBackupEntry> GetManagedBackups(bool includeIncomplete)
	{
		try
		{
			string root = GetConfiguredBackupRoot();
			if (!Directory.Exists(root))
			{
				return [];
			}

			var entries = new List<RuntimeBackupEntry>();
			foreach (string directory in EnumerateManagedBackupDirectories(root))
			{
				bool complete = File.Exists(Path.Combine(directory, BackupCompleteMarkerFileName))
					&& !directory.EndsWith(".partial", StringComparison.OrdinalIgnoreCase);
				if (complete || includeIncomplete)
				{
					entries.Add(new RuntimeBackupEntry(
						directory,
						Path.GetFileName(directory),
						RuntimeBackupFormats.Folder,
						complete));
				}
			}

			foreach (string file in EnumerateManagedBackupFiles(root))
			{
				bool isZip = file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
				bool isPartialZip = file.EndsWith(".zip.partial", StringComparison.OrdinalIgnoreCase);
				if (!isZip && !isPartialZip)
				{
					continue;
				}

				bool complete = isZip;
				if (complete || includeIncomplete)
				{
					entries.Add(new RuntimeBackupEntry(
						file,
						Path.GetFileName(file),
						RuntimeBackupFormats.Zip,
						complete));
				}
			}

			return entries
				.OrderByDescending(entry => GetLastWriteTime(entry.Path))
				.ToList();
		}
		catch (Exception ex)
		{
			_log($"{RuntimeTag} [ERROR] Unable to enumerate runtime backups: {ex.Message}");
			return [];
		}
	}

	private async Task CreateFolderBackupAsync(
		string partialPath,
		IReadOnlyList<(string Target, string Path)> sources,
		RuntimeBackupAnalysis analysis,
		CancellationToken cancellationToken)
	{
		long copiedBytes = 0;
		var manifestFiles = new List<RuntimeBackupManifestFile>();
		foreach (var source in sources)
		{
			string destination = Path.Combine(partialPath, source.Target);
			Directory.CreateDirectory(destination);
			_log($"{RuntimeTag} Backing up {source.Path} to {destination}");
			foreach (string sourceFile in EnumerateFilesSafely(source.Path, cancellationToken))
			{
				string relative = Path.GetRelativePath(source.Path, sourceFile);
				string destinationFile = Path.Combine(destination, relative);
				Directory.CreateDirectory(Path.GetDirectoryName(destinationFile) ?? destination);
				await using var input = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
				await using var output = new FileStream(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
				(long length, string hash) = await CopyStreamWithHashAsync(
					input,
					output,
					value =>
					{
						copiedBytes += value;
						ReportProgress(copiedBytes, analysis.SourceBytes, $"Backing up {source.Target}...");
					},
					cancellationToken).ConfigureAwait(false);
				manifestFiles.Add(new RuntimeBackupManifestFile(
					$"{source.Target}/{relative.Replace(Path.DirectorySeparatorChar, '/')}",
					length,
					hash));
			}
		}

		await WriteManifestFileAsync(
			partialPath,
			analysis,
			RuntimeBackupFormats.Folder,
			manifestFiles,
			cancellationToken).ConfigureAwait(false);
	}

	private async Task CreateZipBackupAsync(
		string partialPath,
		IReadOnlyList<(string Target, string Path)> sources,
		RuntimeBackupAnalysis analysis,
		CancellationToken cancellationToken)
	{
		long copiedBytes = 0;
		var manifestFiles = new List<RuntimeBackupManifestFile>();
		await using var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
		using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);
		foreach (var source in sources)
		{
			archive.CreateEntry($"{source.Target}/", CompressionLevel.NoCompression);
			foreach (string filePath in EnumerateFilesSafely(source.Path, cancellationToken))
			{
				string relativePath = Path.GetRelativePath(source.Path, filePath);
				string entryName = $"{source.Target}/{relativePath.Replace(Path.DirectorySeparatorChar, '/')}";
				ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
				await using Stream entryStream = entry.Open();
				await using var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
				(long length, string hash) = await CopyStreamWithHashAsync(
					sourceStream,
					entryStream,
					value =>
					{
						copiedBytes += value;
						ReportProgress(copiedBytes, analysis.SourceBytes, $"Packing {source.Target}...");
					},
					cancellationToken).ConfigureAwait(false);
				manifestFiles.Add(new RuntimeBackupManifestFile(entryName, length, hash));
			}
		}

		ZipArchiveEntry markerEntry = archive.CreateEntry(BackupCompleteMarkerFileName, CompressionLevel.NoCompression);
		await using Stream markerStream = markerEntry.Open();
		await JsonSerializer.SerializeAsync(
			markerStream,
			CreateManifest(analysis, RuntimeBackupFormats.Zip, manifestFiles),
			ManifestJsonOptions,
			cancellationToken: cancellationToken).ConfigureAwait(false);
	}

	private static IReadOnlyList<string> NormalizeTargets(IEnumerable<string> targets)
		=> targets
			.Select(RuntimeBackupTargets.Canonicalize)
			.Where(target => !string.IsNullOrWhiteSpace(target))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

	private static List<(string Target, string Path)> ResolveBackupSources(IEnumerable<string> targets, string comfyPath)
	{
		var sources = new List<(string Target, string Path)>();
		foreach (string target in targets)
		{
			string sourcePath = ResolveRuntimeTargetPath(comfyPath, target);
			if (Directory.Exists(sourcePath))
			{
				sources.Add((target, sourcePath));
			}
		}

		return sources;
	}

	private static string ResolveRuntimeTargetPath(string comfyPath, string target)
		=> RuntimeBackupTargets.Canonicalize(target) switch
		{
			RuntimeBackupTargets.Workflows => ResolveRuntimeWorkflowsPath(comfyPath),
			RuntimeBackupTargets.Models => Path.Combine(comfyPath, RuntimeBackupTargets.Models),
			RuntimeBackupTargets.CustomNodes => Path.Combine(comfyPath, RuntimeBackupTargets.CustomNodes),
			RuntimeBackupTargets.Input => Path.Combine(comfyPath, RuntimeBackupTargets.Input),
			RuntimeBackupTargets.Output => Path.Combine(comfyPath, RuntimeBackupTargets.Output),
			_ => string.Empty
		};

	private static string ResolveRuntimeWorkflowsPath(string comfyPath)
	{
		string userRoot = Path.Combine(comfyPath, "user");
		string defaultWorkflowPath = Path.Combine(userRoot, "default", "workflows");
		if (Directory.Exists(defaultWorkflowPath) || !Directory.Exists(userRoot))
		{
			return defaultWorkflowPath;
		}

		string? userFolder = Directory
			.EnumerateDirectories(userRoot)
			.Select(Path.GetFileName)
			.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
		return string.IsNullOrWhiteSpace(userFolder)
			? defaultWorkflowPath
			: Path.Combine(userRoot, userFolder, "workflows");
	}

	private static RuntimeBackupAnalysis Failure(string message, IReadOnlyList<string> targets)
		=> new(false, message, targets, 0, 0, 0, -1, GetConfiguredBackupRoot(), GetActiveComfyPath());

	private static string GetActiveComfyPath()
	{
		string comfyPath = ComfyPathResolver.ResolveActiveComfyPath();
		return string.IsNullOrWhiteSpace(comfyPath)
			? ComfyInstallService.DefaultComfyPath
			: comfyPath;
	}

	private static bool IsSamePath(string left, string right)
	{
		if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
		{
			return false;
		}

		string normalizedLeft = Path.GetFullPath(left)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string normalizedRight = Path.GetFullPath(right)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
	}

	private static long CalculatePercent(long value, int percent)
		=> value > long.MaxValue / percent ? long.MaxValue : value * percent / 100;

	private static long AddWithoutOverflow(long left, long right)
		=> left > long.MaxValue - right ? long.MaxValue : left + right;

	private static bool TryGetAvailableBytes(string path, out long availableBytes, out string error)
	{
		try
		{
			string? root = Path.GetPathRoot(Path.GetFullPath(path));
			if (string.IsNullOrWhiteSpace(root))
			{
				throw new IOException("The backup destination volume could not be resolved.");
			}

			var drive = new DriveInfo(root);
			if (!drive.IsReady)
			{
				throw new IOException("The backup destination volume is not ready.");
			}

			availableBytes = drive.AvailableFreeSpace;
			error = string.Empty;
			return true;
		}
		catch (Exception ex)
		{
			availableBytes = -1;
			error = $"Unable to read free space for the backup destination: {ex.Message}";
			return false;
		}
	}

	private async Task<(long Bytes, long FileCount)> ScanPathsAsync(
		IEnumerable<string> sourcePaths,
		CancellationToken cancellationToken)
		=> await Task.Run(() =>
		{
			long bytes = 0;
			long fileCount = 0;
			foreach (string filePath in sourcePaths.SelectMany(path => EnumerateFilesSafely(path, cancellationToken)))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!TryGetFileInfo(filePath, out FileInfo? file))
				{
					continue;
				}

				bytes = AddWithoutOverflow(bytes, file!.Length);
				fileCount++;
			}

			return (bytes, fileCount);
		}, cancellationToken).ConfigureAwait(false);

	private IEnumerable<string> EnumerateFilesSafely(string rootPath, CancellationToken cancellationToken)
	{
		var pending = new Stack<string>();
		pending.Push(rootPath);
		while (pending.Count > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string current = pending.Pop();
			if (!TryGetDirectoryInfo(current, out DirectoryInfo? directory))
			{
				continue;
			}

			if (!directory!.Exists || directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
			{
				continue;
			}

			IReadOnlyList<FileInfo> files = EnumerateDirectoryFiles(directory);
			foreach (FileInfo file in files)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (TryGetAttributes(file.FullName, out FileAttributes attributes)
					&& !attributes.HasFlag(FileAttributes.ReparsePoint))
				{
					yield return file.FullName;
				}
			}

			IReadOnlyList<DirectoryInfo> children = EnumerateDirectoryChildren(directory);
			foreach (DirectoryInfo child in children)
			{
				if (TryGetAttributes(child.FullName, out FileAttributes attributes)
					&& !attributes.HasFlag(FileAttributes.ReparsePoint))
				{
					pending.Push(child.FullName);
				}
			}
		}
	}

	private bool TryGetDirectoryInfo(string path, out DirectoryInfo? directory)
	{
		try
		{
			directory = new DirectoryInfo(path);
			_ = directory.Attributes;
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
		{
			_log($"{RuntimeTag} Skipping inaccessible directory during runtime file scan: {path} ({ex.Message})");
			directory = null;
			return false;
		}
	}

	private bool TryGetFileInfo(string path, out FileInfo? file)
	{
		try
		{
			file = new FileInfo(path);
			_ = file.Length;
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
		{
			_log($"{RuntimeTag} Skipping inaccessible file during runtime file scan: {path} ({ex.Message})");
			file = null;
			return false;
		}
	}

	private IReadOnlyList<FileInfo> EnumerateDirectoryFiles(DirectoryInfo directory)
	{
		try
		{
			return directory.EnumerateFiles().ToList();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
		{
			_log($"{RuntimeTag} Skipping files in inaccessible directory: {directory.FullName} ({ex.Message})");
			return [];
		}
	}

	private IReadOnlyList<DirectoryInfo> EnumerateDirectoryChildren(DirectoryInfo directory)
	{
		try
		{
			return directory.EnumerateDirectories().ToList();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
		{
			_log($"{RuntimeTag} Skipping child directories in inaccessible directory: {directory.FullName} ({ex.Message})");
			return [];
		}
	}

	private bool TryGetAttributes(string path, out FileAttributes attributes)
	{
		try
		{
			attributes = File.GetAttributes(path);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
		{
			_log($"{RuntimeTag} Skipping inaccessible path during runtime file scan: {path} ({ex.Message})");
			attributes = default;
			return false;
		}
	}

	private static async Task<long> CopyStreamAsync(
		Stream source,
		Stream destination,
		Action<long> reportCurrentChunk,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[1024 * 1024];
		long copied = 0;
		while (true)
		{
			int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
			if (read == 0)
			{
				break;
			}

			await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
			copied += read;
			reportCurrentChunk(read);
		}

		return copied;
	}

	private static async Task<(long Length, string Sha256)> CopyStreamWithHashAsync(
		Stream source,
		Stream destination,
		Action<long> reportCurrentChunk,
		CancellationToken cancellationToken)
	{
		using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		var buffer = new byte[1024 * 1024];
		long copied = 0;
		while (true)
		{
			int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
			if (read == 0)
			{
				break;
			}

			hash.AppendData(buffer, 0, read);
			await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
			copied += read;
			reportCurrentChunk(read);
		}

		await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
		return (copied, Convert.ToHexString(hash.GetHashAndReset()));
	}

	private async Task<List<RestoreSourceFile>> ReadFolderRestoreSourcesAsync(
		string backupPath,
		CancellationToken cancellationToken)
	{
		string markerPath = Path.Combine(backupPath, BackupCompleteMarkerFileName);
		if (!File.Exists(markerPath))
		{
			throw new InvalidDataException("The selected folder is not a completed Nexus runtime backup.");
		}

		RuntimeBackupManifest? manifest = await ReadManifestAsync(markerPath, cancellationToken).ConfigureAwait(false);
		var hashes = manifest?.Version >= 3
			? (manifest.Files ?? []).ToDictionary(file => NormalizeRelativePath(file.RelativePath), StringComparer.OrdinalIgnoreCase)
			: new Dictionary<string, RuntimeBackupManifestFile>(StringComparer.OrdinalIgnoreCase);
		var files = new List<RestoreSourceFile>();
		foreach (string target in RuntimeBackupTargets.All)
		{
			string root = Path.Combine(backupPath, target);
			foreach (string filePath in EnumerateFilesSafely(root, cancellationToken))
			{
				string relative = NormalizeRelativePath($"{target}/{Path.GetRelativePath(root, filePath)}");
				var file = new FileInfo(filePath);
				hashes.TryGetValue(relative, out RuntimeBackupManifestFile? metadata);
				string hash = !string.IsNullOrWhiteSpace(metadata?.Sha256)
					? metadata.Sha256
					: await ComputeFileHashAsync(filePath, cancellationToken).ConfigureAwait(false);
				files.Add(new RestoreSourceFile(relative, file.Length, hash));
			}
		}

		return files;
	}

	private async Task<List<RestoreSourceFile>> ReadZipRestoreSourcesAsync(
		string backupPath,
		CancellationToken cancellationToken)
	{
		using var archive = ZipFile.OpenRead(backupPath);
		ZipArchiveEntry marker = archive.GetEntry(BackupCompleteMarkerFileName)
			?? throw new InvalidDataException("The selected ZIP is not a completed Nexus runtime backup.");
		RuntimeBackupManifest? manifest;
		await using (Stream markerStream = marker.Open())
		{
			manifest = await JsonSerializer.DeserializeAsync<RuntimeBackupManifest>(
				markerStream,
				ManifestJsonOptions,
				cancellationToken).ConfigureAwait(false);
		}

		var hashes = manifest?.Version >= 3
			? (manifest.Files ?? []).ToDictionary(file => NormalizeRelativePath(file.RelativePath), StringComparer.OrdinalIgnoreCase)
			: new Dictionary<string, RuntimeBackupManifestFile>(StringComparer.OrdinalIgnoreCase);
		var files = new List<RestoreSourceFile>();
		foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => IsRuntimeDataEntry(entry.FullName) && !string.IsNullOrEmpty(entry.Name)))
		{
			cancellationToken.ThrowIfCancellationRequested();
			string relative = NormalizeRelativePath(entry.FullName);
			hashes.TryGetValue(relative, out RuntimeBackupManifestFile? metadata);
			string hash;
			if (!string.IsNullOrWhiteSpace(metadata?.Sha256))
			{
				hash = metadata.Sha256;
			}
			else
			{
				await using Stream source = entry.Open();
				hash = await ComputeStreamHashAsync(source, cancellationToken).ConfigureAwait(false);
			}
			files.Add(new RestoreSourceFile(relative, entry.Length, hash));
		}

		return files;
	}

	private static async Task<RuntimeBackupManifest?> ReadManifestAsync(
		string path,
		CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
		return await JsonSerializer.DeserializeAsync<RuntimeBackupManifest>(
			stream,
			ManifestJsonOptions,
			cancellationToken).ConfigureAwait(false);
	}

	private static async Task<string> ComputeRestoreSourceHashAsync(
		string backupPath,
		string format,
		string relativePath,
		CancellationToken cancellationToken)
	{
		if (format == RuntimeBackupFormats.Zip)
		{
			using var archive = ZipFile.OpenRead(backupPath);
			ZipArchiveEntry entry = archive.GetEntry(relativePath)
				?? throw new InvalidDataException($"Backup entry is missing: {relativePath}");
			await using Stream stream = entry.Open();
			return await ComputeStreamHashAsync(stream, cancellationToken).ConfigureAwait(false);
		}

		return await ComputeFileHashAsync(
			GetSafeFolderBackupPath(backupPath, relativePath),
			cancellationToken).ConfigureAwait(false);
	}

	private static async Task<string> ComputeFileHashAsync(string path, CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
		return await ComputeStreamHashAsync(stream, cancellationToken).ConfigureAwait(false);
	}

	private static async Task<string> ComputeStreamHashAsync(Stream stream, CancellationToken cancellationToken)
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		var buffer = new byte[1024 * 1024];
		while (true)
		{
			int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
			if (read == 0)
			{
				break;
			}

			hash.AppendData(buffer, 0, read);
		}

		return Convert.ToHexString(hash.GetHashAndReset());
	}

	private static string GetSafeFolderBackupPath(string backupRoot, string relativePath)
	{
		string root = Path.GetFullPath(backupRoot)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
			+ Path.DirectorySeparatorChar;
		string candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
		if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException($"Backup path escapes its root: {relativePath}");
		}

		return candidate;
	}

	private static string NormalizeRelativePath(string path)
	{
		string normalized = path.Replace('\\', '/').TrimStart('/');
		int separatorIndex = normalized.IndexOf('/');
		if (separatorIndex <= 0)
		{
			return normalized;
		}

		string target = normalized[..separatorIndex];
		string canonicalTarget = RuntimeBackupTargets.Canonicalize(target);
		if (!string.IsNullOrWhiteSpace(canonicalTarget))
		{
			target = canonicalTarget;
		}

		return $"{target}/{normalized[(separatorIndex + 1)..]}";
	}

	private static RuntimeRestoreAnalysis RestoreFailure(
		string message,
		string backupPath,
		string format = "",
		IReadOnlyList<RuntimeRestoreItem>? items = null,
		long copyBytes = 0,
		long requiredBytes = 0,
		long availableBytes = -1,
		string? comfyPath = null)
		=> new(
			false,
			message,
			backupPath,
			format,
			string.IsNullOrWhiteSpace(comfyPath) ? GetActiveComfyPath() : comfyPath,
			[],
			items ?? [],
			copyBytes,
			requiredBytes,
			availableBytes,
			string.Empty);

	private static async Task<string> WriteRestorePreviewAsync(
		string backupPath,
		string format,
		IReadOnlyList<string> targets,
		IReadOnlyList<RuntimeRestoreItem> items,
		long copyBytes,
		long requiredBytes,
		long availableBytes,
		string comfyPath,
		CancellationToken cancellationToken)
	{
		string logDirectory = ComfyInstallService.GetLocalRuntimePath("Work/Logs");
		Directory.CreateDirectory(logDirectory);
		string reportPath = Path.Combine(logDirectory, RestorePreviewFileName);
		var builder = new StringBuilder();
		builder.AppendLine("ComfyUI Nexus Runtime Restore Preview");
		builder.AppendLine($"Generated: {DateTimeOffset.Now:O}");
		builder.AppendLine($"Backup: {backupPath}");
		builder.AppendLine($"Format: {format}");
		builder.AppendLine($"Destination: {comfyPath}");
		builder.AppendLine($"Targets: {string.Join(", ", targets)}");
		builder.AppendLine($"Copy bytes: {copyBytes}");
		builder.AppendLine($"Required bytes: {requiredBytes}");
		builder.AppendLine($"Available bytes: {availableBytes}");
		builder.AppendLine();
		foreach (RuntimeRestoreItem item in items.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
		{
			builder.Append('[').Append(item.Action.ToString().ToUpperInvariant()).Append("] ").AppendLine(item.RelativePath);
		}

		await File.WriteAllTextAsync(reportPath, builder.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
		return reportPath;
	}

	private static bool ValidateRestoreSnapshot(
		IReadOnlyList<RuntimeRestoreItem> items,
		string comfyPath,
		out string error)
	{
		foreach (RuntimeRestoreItem item in items)
		{
			string destinationPath = GetSafeRestorePath(item.RelativePath, comfyPath);
			if (item.Action == RuntimeRestoreAction.Add)
			{
				if (File.Exists(destinationPath))
				{
					error = $"Restore destination changed after analysis: {item.RelativePath}";
					return false;
				}
				continue;
			}

			if (!File.Exists(destinationPath))
			{
				error = $"Restore destination changed after analysis: {item.RelativePath}";
				return false;
			}

			var destination = new FileInfo(destinationPath);
			if (destination.Length != item.DestinationLength
				|| destination.LastWriteTimeUtc.Ticks != item.DestinationLastWriteUtcTicks)
			{
				error = $"Restore destination changed after analysis: {item.RelativePath}";
				return false;
			}
		}

		error = string.Empty;
		return true;
	}

	private async Task StageAndCommitRestoreFileAsync(
		Stream source,
		RuntimeRestoreItem item,
		string sessionId,
		RestoreJournal journal,
		string comfyPath,
		Action<long> reportCopiedBytes,
		CancellationToken cancellationToken)
	{
		string destinationPath = GetSafeRestorePath(item.RelativePath, comfyPath);
		string directory = Path.GetDirectoryName(destinationPath) ?? comfyPath;
		Directory.CreateDirectory(directory);
		string tempPath = Path.Combine(directory, $"{Path.GetFileName(destinationPath)}{RestoreTempMarker}{sessionId}.tmp");
		journal.TempPaths.Add(tempPath);
		await SaveRestoreJournalAsync(journal, cancellationToken).ConfigureAwait(false);
		try
		{
			await using (var destination = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true))
			{
				(long length, string hash) = await CopyStreamWithHashAsync(
					source,
					destination,
					reportCopiedBytes,
					cancellationToken).ConfigureAwait(false);
				if (length != item.SourceLength
					|| !string.Equals(hash, item.SourceSha256, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException($"Backup source changed after analysis: {item.RelativePath}");
				}
			}

			if (item.Action == RuntimeRestoreAction.Replace && File.Exists(destinationPath))
			{
				File.Move(tempPath, destinationPath, overwrite: true);
			}
			else
			{
				File.Move(tempPath, destinationPath);
			}

			journal.TempPaths.Remove(tempPath);
			await SaveRestoreJournalAsync(journal, cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			TryDeleteFile(tempPath);
			journal.TempPaths.Remove(tempPath);
			await SaveRestoreJournalAsync(journal, CancellationToken.None).ConfigureAwait(false);
			throw;
		}
	}

	private static string RestoreJournalPath
		=> ComfyInstallService.GetLocalRuntimePath($"Work/State/{RestoreJournalFileName}");

	private static async Task SaveRestoreJournalAsync(RestoreJournal journal, CancellationToken cancellationToken)
	{
		string? directory = Path.GetDirectoryName(RestoreJournalPath);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}
		await File.WriteAllTextAsync(
			RestoreJournalPath,
			JsonSerializer.Serialize(journal, ManifestJsonOptions),
			Encoding.UTF8,
			cancellationToken).ConfigureAwait(false);
	}

	private static void DeleteRestoreJournal()
	{
		TryDeleteFile(RestoreJournalPath);
	}

	private async Task CleanupRestoreTempsAsync(CancellationToken cancellationToken)
	{
		if (!File.Exists(RestoreJournalPath))
		{
			return;
		}

		try
		{
			string json = await File.ReadAllTextAsync(RestoreJournalPath, cancellationToken).ConfigureAwait(false);
			RestoreJournal? journal = JsonSerializer.Deserialize<RestoreJournal>(json, ManifestJsonOptions);
			if (journal != null)
			{
				await CleanupJournalTempsAsync(journal, GetActiveComfyPath(), cancellationToken).ConfigureAwait(false);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_log($"{RuntimeTag} [ERROR] Restore temp cleanup failed: {ex.Message}");
		}
		finally
		{
			DeleteRestoreJournal();
		}
	}

	private static Task CleanupJournalTempsAsync(RestoreJournal journal, string comfyPath, CancellationToken cancellationToken)
	{
		return Task.Run(() =>
		{
			foreach (string tempPath in journal.TempPaths)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (IsSafeRestoreTempPath(tempPath, journal.SessionId, comfyPath))
				{
					TryDeleteFile(tempPath);
				}
			}
			DeleteRestoreJournal();
		}, cancellationToken);
	}

	private static bool IsSafeRestoreTempPath(string path, string sessionId, string comfyPath)
	{
		if (string.IsNullOrWhiteSpace(path)
			|| string.IsNullOrWhiteSpace(sessionId)
			|| !Path.GetFileName(path).Contains($"{RestoreTempMarker}{sessionId}.tmp", StringComparison.Ordinal))
		{
			return false;
		}

		string fullPath = Path.GetFullPath(path);
		foreach (string target in RuntimeBackupTargets.All)
		{
			string targetPath = ResolveRuntimeTargetPath(comfyPath, target);
			if (string.IsNullOrWhiteSpace(targetPath))
			{
				continue;
			}

			string root = Path.GetFullPath(targetPath)
				.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				+ Path.DirectorySeparatorChar;
			if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static void TryDeleteFile(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.SetAttributes(path, FileAttributes.Normal);
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	private static async Task WriteManifestFileAsync(
		string backupRoot,
		RuntimeBackupAnalysis analysis,
		string format,
		IReadOnlyList<RuntimeBackupManifestFile> files,
		CancellationToken cancellationToken)
	{
		string markerPath = Path.Combine(backupRoot, BackupCompleteMarkerFileName);
		await File.WriteAllTextAsync(
			markerPath,
			JsonSerializer.Serialize(CreateManifest(analysis, format, files), ManifestJsonOptions),
			cancellationToken).ConfigureAwait(false);
	}

	private static RuntimeBackupManifest CreateManifest(
		RuntimeBackupAnalysis analysis,
		string format,
		IReadOnlyList<RuntimeBackupManifestFile> files)
		=> new(
			"complete",
			3,
			format,
			DateTimeOffset.Now,
			analysis.ComfyPath,
			analysis.Targets,
			analysis.SourceBytes,
			analysis.FileCount,
			files);

	private static string GetUniqueBackupPath(string backupRoot, string format)
	{
		string extension = format == RuntimeBackupFormats.Zip ? ".zip" : string.Empty;
		string baseName = $"{BackupNamePrefix}{DateTime.Now:yyyyMMdd-HHmmss}";
		for (int suffix = 0; ; suffix++)
		{
			string name = suffix == 0 ? baseName : $"{baseName}-{suffix}";
			string candidate = Path.Combine(backupRoot, name + extension);
			if (!File.Exists(candidate)
				&& !Directory.Exists(candidate)
				&& !File.Exists(candidate + ".partial")
				&& !Directory.Exists(candidate + ".partial"))
			{
				return candidate;
			}
		}
	}

	private static DateTime GetLastWriteTime(string path)
		=> Directory.Exists(path) ? Directory.GetLastWriteTime(path) : File.GetLastWriteTime(path);

	private static IEnumerable<string> EnumerateManagedBackupDirectories(string root)
		=> Directory
			.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
			.Where(path => IsManagedBackupName(Path.GetFileName(path)));

	private static IEnumerable<string> EnumerateManagedBackupFiles(string root)
		=> Directory
			.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
			.Where(path => IsManagedBackupName(GetBackupNameWithoutKnownExtension(Path.GetFileName(path))));

	private static string GetBackupNameWithoutKnownExtension(string fileName)
	{
		if (fileName.EndsWith(".zip.partial", StringComparison.OrdinalIgnoreCase))
		{
			return fileName[..^".zip.partial".Length];
		}

		return fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
			? fileName[..^".zip".Length]
			: fileName;
	}

	private static bool IsManagedBackupName(string name)
		=> name.StartsWith(BackupNamePrefix, StringComparison.OrdinalIgnoreCase)
			|| name.StartsWith(LegacyBackupNamePrefix, StringComparison.OrdinalIgnoreCase);

	private static bool IsRuntimeDataEntry(string entryName)
	{
		string normalized = entryName.Replace('\\', '/');
		return RuntimeBackupTargets.All
			.Any(target => normalized.StartsWith($"{target}/", StringComparison.OrdinalIgnoreCase));
	}

	private static string GetSafeRestorePath(string entryName, string comfyPath)
	{
		string normalized = entryName.Replace('\\', '/').TrimStart('/');
		int separatorIndex = normalized.IndexOf('/');
		if (separatorIndex <= 0)
		{
			throw new InvalidDataException($"Backup entry has an invalid runtime path: {entryName}");
		}

		string target = normalized[..separatorIndex];
		target = RuntimeBackupTargets.Canonicalize(target);
		if (string.IsNullOrWhiteSpace(target))
		{
			throw new InvalidDataException($"Backup entry targets an unsupported runtime folder: {entryName}");
		}

		string relativePath = normalized[(separatorIndex + 1)..]
			.Replace('/', Path.DirectorySeparatorChar);
		string targetPath = ResolveRuntimeTargetPath(comfyPath, target);
		if (string.IsNullOrWhiteSpace(targetPath))
		{
			throw new InvalidDataException($"Backup entry targets an unsupported runtime folder: {entryName}");
		}

		string targetRoot = Path.GetFullPath(targetPath)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
			+ Path.DirectorySeparatorChar;
		string destination = Path.GetFullPath(Path.Combine(targetRoot, relativePath));
		if (!destination.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException($"Backup entry escapes its runtime folder: {entryName}");
		}

		EnsureNoReparsePointInDestination(targetRoot, destination);
		return destination;
	}

	private static void EnsureNoReparsePointInDestination(string targetRoot, string destinationPath)
	{
		string? current = Path.GetDirectoryName(destinationPath);
		while (!string.IsNullOrWhiteSpace(current)
			&& current.Length >= targetRoot.Length)
		{
			if (Directory.Exists(current)
				&& new DirectoryInfo(current).Attributes.HasFlag(FileAttributes.ReparsePoint))
			{
				throw new InvalidDataException($"Restore destination contains a reparse point: {current}");
			}

			if (string.Equals(
				current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
				targetRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
				StringComparison.OrdinalIgnoreCase))
			{
				break;
			}

			current = Path.GetDirectoryName(current);
		}
	}

	private static bool IsSafeManagedBackupPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string root = Path.GetFullPath(GetConfiguredBackupRoot()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string? parent = Directory.GetParent(fullPath)?.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return string.Equals(parent, root, StringComparison.OrdinalIgnoreCase)
			&& IsManagedBackupName(GetBackupNameWithoutKnownExtension(Path.GetFileName(fullPath)));
	}

	private static bool IsSameOrDescendantPath(string candidatePath, string parentPath)
	{
		string candidate = Path.GetFullPath(candidatePath)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string parent = Path.GetFullPath(parentPath)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase)
			|| candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}

	private static void TryDeletePartial(string partialPath)
	{
		try
		{
			if (Directory.Exists(partialPath))
			{
				ClearReadOnlyAttributes(new DirectoryInfo(partialPath));
				Directory.Delete(partialPath, recursive: true);
			}
			else if (File.Exists(partialPath))
			{
				File.SetAttributes(partialPath, FileAttributes.Normal);
				File.Delete(partialPath);
			}
		}
		catch
		{
		}
	}

	private static void ClearReadOnlyAttributes(DirectoryInfo directory)
	{
		if (!directory.Exists)
		{
			return;
		}

		foreach (FileInfo file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
		{
			file.Attributes = FileAttributes.Normal;
		}
	}

	private void ResetProgressThrottle()
	{
		lock (_progressLock)
		{
			_lastProgress = -1;
			_progressClock.Restart();
		}
	}

	private void ReportProgress(long copiedBytes, long totalBytes, string message)
	{
		double progress = totalBytes <= 0
			? copiedBytes >= totalBytes ? 1 : 0
			: Math.Clamp((double)copiedBytes / totalBytes, 0, 1);
		lock (_progressLock)
		{
			bool isComplete = progress >= 1;
			bool hasMeaningfulChange = progress - _lastProgress >= 0.01;
			if (!isComplete && !hasMeaningfulChange && _progressClock.ElapsedMilliseconds < CopyProgressReportIntervalMs)
			{
				return;
			}

			_lastProgress = progress;
			_progressClock.Restart();
		}

		_progress(progress, message);
	}

	internal static string FormatBytes(long bytes)
	{
		if (bytes < 0)
		{
			return "Unavailable";
		}

		string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];
		double value = bytes;
		int unit = 0;
		while (value >= 1024 && unit < units.Length - 1)
		{
			value /= 1024;
			unit++;
		}

		return unit == 0 ? $"{bytes} {units[unit]}" : $"{value:0.##} {units[unit]}";
	}

	private sealed record RestoreSourceFile(string RelativePath, long Length, string Sha256);

	private sealed record RuntimeBackupManifest(
		[property: JsonPropertyName("status")] string Status,
		[property: JsonPropertyName("version")] int Version,
		[property: JsonPropertyName("format")] string Format,
		[property: JsonPropertyName("completed_at")] DateTimeOffset CompletedAt,
		[property: JsonPropertyName("comfy_path")] string ComfyPath,
		[property: JsonPropertyName("targets")] IReadOnlyList<string> Targets,
		[property: JsonPropertyName("source_bytes")] long SourceBytes,
		[property: JsonPropertyName("file_count")] long FileCount,
		[property: JsonPropertyName("files")] IReadOnlyList<RuntimeBackupManifestFile> Files)
	{
		public RuntimeBackupManifest()
			: this(string.Empty, 0, string.Empty, default, string.Empty, [], 0, 0, [])
		{
		}
	}

	private sealed record RuntimeBackupManifestFile(
		[property: JsonPropertyName("relative_path")] string RelativePath,
		[property: JsonPropertyName("length")] long Length,
		[property: JsonPropertyName("sha256")] string Sha256);

	private sealed record RestoreJournal(string SessionId, string BackupPath, List<string> TempPaths);
}
