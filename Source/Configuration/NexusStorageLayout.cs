namespace ComfyUI_Nexus.Configuration;

using Microsoft.Maui.Storage;

internal static class NexusStorageLayout
{
	private static readonly Lazy<Paths> CurrentPaths = new(CreatePaths);

	internal static bool IsStoreDistribution => NexusBuildProfile.IsStoreDistribution;
	internal static string PackageRoot => CurrentPaths.Value.PackageRoot;
	internal static string DataRoot => CurrentPaths.Value.DataRoot;
	internal static string SettingsPath => Path.Combine(DataRoot, "nexus_settings.json");
	internal static string RuntimePackagesRoot => Path.Combine(PackageRoot, "LocalRuntime", "Packages");
	internal static string LocalRuntimeRoot => CurrentPaths.Value.PhysicalRuntimeRoot;

	internal static bool AreEquivalentRuntimePaths(string firstPath, string secondPath)
		=> string.Equals(
			NormalizeRuntimePath(firstPath),
			NormalizeRuntimePath(secondPath),
			StringComparison.OrdinalIgnoreCase);

	internal static string GetRuntimePackagePath(string relativePath)
		=> Path.Combine(RuntimePackagesRoot, NormalizeRelativePath(relativePath));

	private static Paths CreatePaths()
	{
		string packageRoot = ResolvePackageRoot();
		string dataRoot = IsStoreDistribution
			? Path.Combine(FileSystem.AppDataDirectory, "Nexus")
			: packageRoot;
		string physicalRuntimeRoot = Path.Combine(dataRoot, "LocalRuntime");
		return new Paths(packageRoot, dataRoot, physicalRuntimeRoot);
	}

	private static string ResolvePackageRoot()
	{
		if (IsStoreDistribution)
		{
			return NormalizeRoot(AppContext.BaseDirectory);
		}

		string? portableRoot = Environment.GetEnvironmentVariable("COMFYUI_NEXUS_PORTABLE_ROOT");
		if (!string.IsNullOrWhiteSpace(portableRoot))
		{
			try
			{
				return NormalizeRoot(portableRoot);
			}
			catch
			{
				// Fall back to the executable directory when a launcher provides an invalid root.
			}
		}

		string baseDirectory = NormalizeRoot(GetExecutableDirectory());
		string[] segments = baseDirectory.Split(
			[Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
			StringSplitOptions.RemoveEmptyEntries);
		if (!segments.Contains("bin", StringComparer.OrdinalIgnoreCase))
		{
			return baseDirectory;
		}

		string? root = baseDirectory;
		while (root != null && !File.Exists(Path.Combine(root, "ComfyUI-Nexus.csproj")))
		{
			root = Path.GetDirectoryName(root);
		}

		return root is null ? baseDirectory : NormalizeRoot(root);
	}

	private static string GetExecutableDirectory()
	{
		string? processPath = Environment.ProcessPath;
		string? processDirectory = string.IsNullOrWhiteSpace(processPath)
			? null
			: Path.GetDirectoryName(processPath);
		return string.IsNullOrWhiteSpace(processDirectory) ? AppContext.BaseDirectory : processDirectory;
	}

	private static string NormalizeRoot(string path)
		=> Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

	private static string NormalizeRuntimePath(string path)
	{
		string normalizedPath = NormalizeRoot(path);
		return NexusToolingPathLeaseController.ResolvePhysicalPath(normalizedPath);
	}

	private static string NormalizeRelativePath(string relativePath)
		=> relativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

	private sealed record Paths(string PackageRoot, string DataRoot, string PhysicalRuntimeRoot);
}

/// <summary>
/// Creates writable application storage after the app runtime has claimed ownership.
/// </summary>
internal static class NexusStorageProvisioner
{
	internal static void EnsureCreated()
	{
		Directory.CreateDirectory(NexusStorageLayout.DataRoot);
		Directory.CreateDirectory(NexusStorageLayout.LocalRuntimeRoot);
	}
}
