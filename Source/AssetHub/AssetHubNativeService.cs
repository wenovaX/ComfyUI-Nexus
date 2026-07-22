using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Platform;

namespace ComfyUI_Nexus.AssetHub;

internal sealed class AssetHubNativeService
{
	private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff", "svg",
	};

	private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".mp4", ".webm", ".mov", ".avi", ".mkv", ".wmv", ".m4v",
	};

	private static readonly HashSet<string> ModelExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".safetensors", ".ckpt", ".pt", ".pth", ".bin", ".onnx", ".gguf", ".ggml",
	};

	private readonly AssetHubBookmarkStore _bookmarkStore;

	public AssetHubNativeService(AssetHubBookmarkStore? bookmarkStore = null)
	{
		_bookmarkStore = bookmarkStore ?? new AssetHubBookmarkStore();
	}

	public IReadOnlyList<(string Label, string Path)> GetDefaultRoots(string comfyRoot)
	{
		var roots = new List<(string Label, string Path)>();
		if (string.IsNullOrWhiteSpace(comfyRoot) || !Directory.Exists(comfyRoot))
		{
			return roots;
		}

		var inputPath = Path.Combine(comfyRoot, "input");
		var outputPath = Path.Combine(comfyRoot, "output");

		if (Directory.Exists(inputPath))
		{
			roots.Add(("Input", inputPath));
		}

		if (Directory.Exists(outputPath))
		{
			roots.Add(("Output", outputPath));
		}

		var modelsPath = Path.Combine(comfyRoot, "models");
		if (Directory.Exists(modelsPath))
		{
			roots.Add(("Models", modelsPath));
		}

		roots.Add(("ComfyUI Root", comfyRoot));
		return roots;
	}

	public Task<IReadOnlyList<string>> LoadBookmarksAsync(CancellationToken cancellationToken = default)
		=> _bookmarkStore.LoadAsync(cancellationToken);

	public Task SaveBookmarksAsync(IEnumerable<string> bookmarks, CancellationToken cancellationToken = default)
		=> _bookmarkStore.SaveAsync(bookmarks, cancellationToken);

	public async Task AddBookmarkAsync(string path, CancellationToken cancellationToken = default)
	{
		var bookmarks = (await _bookmarkStore.LoadAsync(cancellationToken)).ToList();
		if (!bookmarks.Contains(path, StringComparer.OrdinalIgnoreCase))
		{
			bookmarks.Add(path);
			await _bookmarkStore.SaveAsync(bookmarks, cancellationToken);
		}
	}

	public async Task RemoveBookmarkAsync(string path, CancellationToken cancellationToken = default)
	{
		var bookmarks = (await _bookmarkStore.LoadAsync(cancellationToken))
			.Where(bookmark => !string.Equals(bookmark, path, StringComparison.OrdinalIgnoreCase))
			.ToArray();

		await _bookmarkStore.SaveAsync(bookmarks, cancellationToken);
	}

	public Task<IReadOnlyList<AssetHubItem>> ListAsync(string path, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Path is required.", nameof(path));
		}

		if (!Directory.Exists(path))
		{
			throw new DirectoryNotFoundException(path);
		}

		return Task.Run<IReadOnlyList<AssetHubItem>>(() =>
		{
			cancellationToken.ThrowIfCancellationRequested();

			var entries = Directory.EnumerateFileSystemEntries(path)
				.Select(entry => BuildItem(entry, path))
				.OrderByDescending(item => item.Type == AssetHubItemType.Directory)
				.ThenByDescending(item => item.ModifiedAtUtc)
				.ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
				.ToArray();

			return entries;
		}, cancellationToken);
	}

	public Task<IReadOnlyList<AssetHubItem>> SearchAsync(
		string rootPath,
		string query,
		bool recursive = true,
		bool filterModelFilesOnly = false,
		bool includeDirectories = true,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(rootPath))
		{
			throw new ArgumentException("Root path is required.", nameof(rootPath));
		}

		if (!Directory.Exists(rootPath))
		{
			throw new DirectoryNotFoundException(rootPath);
		}

		string trimmedQuery = query?.Trim() ?? string.Empty;
		if (trimmedQuery.Length == 0)
		{
			return Task.FromResult<IReadOnlyList<AssetHubItem>>([]);
		}

		return Task.Run<IReadOnlyList<AssetHubItem>>(() =>
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return [];
			}

			var results = new List<AssetHubItem>();
			var comparison = StringComparison.OrdinalIgnoreCase;
			var enumerationOptions = new EnumerationOptions
			{
				IgnoreInaccessible = true,
				RecurseSubdirectories = recursive,
				ReturnSpecialDirectories = false,
				AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
			};

			if (includeDirectories)
			{
				foreach (string directory in Directory.EnumerateDirectories(rootPath, "*", enumerationOptions))
				{
					if (cancellationToken.IsCancellationRequested)
					{
						return [];
					}

					string name = Path.GetFileName(directory);
					if (name.Contains(trimmedQuery, comparison))
					{
						results.Add(BuildItem(directory, rootPath));
					}
				}
			}

			foreach (string file in Directory.EnumerateFiles(rootPath, "*", enumerationOptions))
			{
				if (cancellationToken.IsCancellationRequested)
				{
					return [];
				}

				string fileName = Path.GetFileName(file);
				if (string.Equals(fileName, ".index.json", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				if (filterModelFilesOnly && !IsModelFile(file))
				{
					continue;
				}

				if (fileName.Contains(trimmedQuery, comparison))
				{
					results.Add(BuildItem(file, rootPath));
				}
			}

			return results
				.OrderByDescending(item => item.Type == AssetHubItemType.Directory)
				.ThenBy(item => item.RelativePath?.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar) ?? 0)
				.ThenByDescending(item => item.ModifiedAtUtc)
				.ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}, cancellationToken);
	}

	public Task CreateFolderAsync(string parentDirectory, string folderName, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(folderName))
		{
			throw new ArgumentException("Folder name is required.", nameof(folderName));
		}

		var targetPath = Path.Combine(parentDirectory, folderName);
		Directory.CreateDirectory(targetPath);
		return Task.CompletedTask;
	}

	public Task RenameAsync(string path, string newName, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(newName))
		{
			throw new ArgumentException("New name is required.", nameof(newName));
		}

		var parentDirectory = Path.GetDirectoryName(path);
		if (string.IsNullOrWhiteSpace(parentDirectory))
		{
			throw new InvalidOperationException($"Cannot rename path without a parent directory: {path}");
		}

		var destinationPath = Path.Combine(parentDirectory, newName);
		if (Directory.Exists(path))
		{
			Directory.Move(path, destinationPath);
		}
		else if (File.Exists(path))
		{
			File.Move(path, destinationPath);
		}
		else
		{
			throw new FileNotFoundException("Path not found.", path);
		}

		return Task.CompletedTask;
	}

	public Task DeleteAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
	{
		foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
			else if (File.Exists(path))
			{
				File.Delete(path);
			}
		}

		return Task.CompletedTask;
	}

	public Task CopyAsync(IEnumerable<string> sourcePaths, string destinationDirectory, CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(destinationDirectory);

		foreach (var sourcePath in sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
		{
			cancellationToken.ThrowIfCancellationRequested();
			CopyEntry(sourcePath, destinationDirectory);
		}

		return Task.CompletedTask;
	}

	public Task MoveAsync(IEnumerable<string> sourcePaths, string destinationDirectory, CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(destinationDirectory);

		foreach (var sourcePath in sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
		{
			cancellationToken.ThrowIfCancellationRequested();
			MoveEntry(sourcePath, destinationDirectory);
		}

		return Task.CompletedTask;
	}

	public async Task OpenInOsAsync(string path, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		var result = await NexusAppManager.Instance.Platform.Shell.OpenPathAsync(path);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			NexusLog.Warning($"Failed to open in OS: {result.Message}");
		}
	}

	public bool IsModelFile(string path)
		=> IsModelFileCore(path);

	private static AssetHubItem BuildItem(string fullPath, string? rootPath = null)
	{
		if (Directory.Exists(fullPath))
		{
			var info = new DirectoryInfo(fullPath);
			return new AssetHubItem(
				info.Name,
				info.FullName,
				AssetHubItemType.Directory,
				0,
				info.Exists ? info.LastWriteTimeUtc : null,
				false,
				false,
				ComputeRelativePath(rootPath, info.FullName));
		}

		var fileInfo = new FileInfo(fullPath);
		var extension = fileInfo.Extension;
		return new AssetHubItem(
			fileInfo.Name,
			fileInfo.FullName,
			AssetHubItemType.File,
			fileInfo.Exists ? fileInfo.Length : 0,
			fileInfo.Exists ? fileInfo.LastWriteTimeUtc : null,
			ImageExtensions.Contains(extension),
			VideoExtensions.Contains(extension),
			ComputeRelativePath(rootPath, fileInfo.FullName));
	}

	private static bool IsModelFileCore(string path)
		=> ModelExtensions.Contains(Path.GetExtension(path));

	private static string? ComputeRelativePath(string? rootPath, string fullPath)
	{
		if (string.IsNullOrWhiteSpace(rootPath))
		{
			return null;
		}

		try
		{
			return Path.GetRelativePath(rootPath, fullPath);
		}
		catch
		{
			return null;
		}
	}

	private static void CopyEntry(string sourcePath, string destinationDirectory)
	{
		if (Directory.Exists(sourcePath))
		{
			var directoryName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			var destinationPath = EnsureUniquePath(Path.Combine(destinationDirectory, directoryName));
			CopyDirectoryRecursive(sourcePath, destinationPath);
			return;
		}

		if (File.Exists(sourcePath))
		{
			var destinationPath = EnsureUniquePath(Path.Combine(destinationDirectory, Path.GetFileName(sourcePath)));
			File.Copy(sourcePath, destinationPath);
		}
	}

	private static void MoveEntry(string sourcePath, string destinationDirectory)
	{
		if (Directory.Exists(sourcePath))
		{
			var directoryName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			var destinationPath = EnsureUniquePath(Path.Combine(destinationDirectory, directoryName));
			Directory.Move(sourcePath, destinationPath);
			return;
		}

		if (File.Exists(sourcePath))
		{
			var destinationPath = EnsureUniquePath(Path.Combine(destinationDirectory, Path.GetFileName(sourcePath)));
			File.Move(sourcePath, destinationPath);
		}
	}

	private static void CopyDirectoryRecursive(string sourceDirectory, string destinationDirectory)
	{
		var pending = new Stack<(string Source, string Destination)>();
		pending.Push((sourceDirectory, destinationDirectory));
		var options = new EnumerationOptions
		{
			IgnoreInaccessible = false,
			RecurseSubdirectories = false,
			ReturnSpecialDirectories = false,
			AttributesToSkip = FileAttributes.ReparsePoint,
		};

		while (pending.Count > 0)
		{
			var current = pending.Pop();
			Directory.CreateDirectory(current.Destination);

			foreach (string file in Directory.EnumerateFiles(current.Source, "*", options))
			{
				File.Copy(file, Path.Combine(current.Destination, Path.GetFileName(file)));
			}

			foreach (string subdirectory in Directory.EnumerateDirectories(current.Source, "*", options))
			{
				pending.Push((subdirectory, Path.Combine(current.Destination, Path.GetFileName(subdirectory))));
			}
		}
	}

	private static string EnsureUniquePath(string targetPath)
	{
		if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
		{
			return targetPath;
		}

		var directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
		var name = Path.GetFileNameWithoutExtension(targetPath);
		var extension = Path.GetExtension(targetPath);

		for (int index = 1; index < int.MaxValue; index++)
		{
			var candidate = Path.Combine(directory, $"{name}_{index}{extension}");
			if (!File.Exists(candidate) && !Directory.Exists(candidate))
			{
				return candidate;
			}
		}

		throw new IOException($"Unable to find a unique destination for '{targetPath}'.");
	}
}
