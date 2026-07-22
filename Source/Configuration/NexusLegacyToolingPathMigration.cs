namespace ComfyUI_Nexus.Configuration;

using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Setup.Services;

/// <summary>
/// Removes only marker-owned junction containers created by pre-lease builds.
/// </summary>
internal static class NexusLegacyToolingPathMigration
{
	private const string LegacyPrefix = "nx";
	private const string LegacyMarkerFileName = "README_NEXUS_TEMP.txt";
	private const string LegacyComfyLinkName = "ComfyUI";

	internal static void RunOnce(NexusComfyRuntimePaths paths, NexusPreferenceStore preferences)
	{
		ArgumentNullException.ThrowIfNull(paths);
		ArgumentNullException.ThrowIfNull(preferences);
#if !WINDOWS
		return;
#else
		if (preferences.Get(PreferenceKeys.LegacyToolingPathMigrationCompleted, false))
		{
			return;
		}

		foreach (string root in GetCandidateRoots(paths.ActiveComfyPath))
		{
			try
			{
				foreach (string candidate in Directory.EnumerateDirectories(root, $"{LegacyPrefix}????????", SearchOption.TopDirectoryOnly))
				{
					if (!IsOwnedLegacyContainer(candidate))
					{
						continue;
					}

					DeleteOwnedLegacyContainer(candidate);
					NexusLog.Info($"[TOOLING_PATH] Removed a legacy temporary tooling junction: {candidate}");
				}
			}
			catch (Exception ex)
			{
				NexusLog.Warning($"[TOOLING_PATH] Legacy migration scan skipped for {root}: {ex.Message}");
			}
		}

		preferences.Set(PreferenceKeys.LegacyToolingPathMigrationCompleted, true);
#endif
	}

	private static IEnumerable<string> GetCandidateRoots(string comfyRoot)
	{
		var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			Path.GetTempPath()
		};

		string? driveRoot = Path.GetPathRoot(comfyRoot);
		if (!string.IsNullOrWhiteSpace(driveRoot))
		{
			roots.Add(driveRoot);

			string relative = Path.GetRelativePath(driveRoot, comfyRoot);
			string? firstSegment = relative
				.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
				.FirstOrDefault();
			if (!string.IsNullOrWhiteSpace(firstSegment) && firstSegment is not "." and not "..")
			{
				roots.Add(Path.Combine(driveRoot, firstSegment));
			}
		}

		return roots.Where(Directory.Exists);
	}

	private static bool IsOwnedLegacyContainer(string candidate)
	{
		string markerPath = Path.Combine(candidate, LegacyMarkerFileName);
		string comfyLinkPath = Path.Combine(candidate, LegacyComfyLinkName);
		if (!File.Exists(markerPath)
			|| !Directory.Exists(comfyLinkPath)
			|| (File.GetAttributes(comfyLinkPath) & FileAttributes.ReparsePoint) == 0)
		{
			return false;
		}

		return Directory.EnumerateFiles(candidate, "*", SearchOption.TopDirectoryOnly)
			.All(path => string.Equals(Path.GetFileName(path), LegacyMarkerFileName, StringComparison.OrdinalIgnoreCase))
			&& Directory.EnumerateDirectories(candidate, "*", SearchOption.TopDirectoryOnly)
				.All(path => string.Equals(Path.GetFileName(path), LegacyComfyLinkName, StringComparison.OrdinalIgnoreCase));
	}

	private static void DeleteOwnedLegacyContainer(string candidate)
	{
		Directory.Delete(Path.Combine(candidate, LegacyComfyLinkName));
		File.Delete(Path.Combine(candidate, LegacyMarkerFileName));
		Directory.Delete(candidate);
	}
}
