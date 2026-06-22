using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

internal static class ModelAssetPathResolver
{
	internal static IReadOnlyList<ModelAssetPathMatch> ResolveMatches(
		string syntheticPath,
		string modelsRoot,
		IEnumerable<string> externalModelRoots)
	{
		if (string.IsNullOrWhiteSpace(syntheticPath) ||
			string.IsNullOrWhiteSpace(modelsRoot) ||
			!TryGetModelRelativePath(syntheticPath, modelsRoot, out string relativePath))
		{
			return [];
		}

		var matches = new List<ModelAssetPathMatch>();
		var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddRootMatch(matches, seenRoots, modelsRoot, relativePath, rootIndex: 0, rootLabel: "Internal models");

		int externalIndex = 1;
		foreach (string root in externalModelRoots)
		{
			if (string.IsNullOrWhiteSpace(root))
			{
				continue;
			}

			AddRootMatch(
				matches,
				seenRoots,
				root,
				relativePath,
				externalIndex,
				$"External library {externalIndex}");
			externalIndex++;
		}

		if (matches.Count > 1)
		{
			NexusLog.Trace($"[MODEL ASSETS] Multiple filesystem matches for '{relativePath}': {matches.Count}");
		}

		return matches;
	}

	private static bool TryGetModelRelativePath(string syntheticPath, string modelsRoot, out string relativePath)
	{
		relativePath = string.Empty;
		try
		{
			string fullSyntheticPath = Path.GetFullPath(syntheticPath);
			string fullModelsRoot = NormalizeRoot(modelsRoot);
			if (!IsPathWithinRoot(fullSyntheticPath, fullModelsRoot))
			{
				return false;
			}

			string candidate = Path.GetRelativePath(fullModelsRoot, fullSyntheticPath);
			if (string.IsNullOrWhiteSpace(candidate) ||
				candidate.StartsWith("..", StringComparison.Ordinal) ||
				Path.IsPathRooted(candidate))
			{
				return false;
			}

			relativePath = candidate;
			return true;
		}
		catch (Exception ex)
		{
			NexusLog.Trace($"[MODEL ASSETS] Unable to resolve synthetic model path '{syntheticPath}': {ex.Message}");
			return false;
		}
	}

	private static void AddRootMatch(
		List<ModelAssetPathMatch> matches,
		HashSet<string> seenRoots,
		string root,
		string relativePath,
		int rootIndex,
		string rootLabel)
	{
		try
		{
			string normalizedRoot = NormalizeRoot(root);
			if (!Directory.Exists(normalizedRoot) || !seenRoots.Add(normalizedRoot))
			{
				return;
			}

			string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
			if (!IsPathWithinRoot(candidate, normalizedRoot) ||
				(!File.Exists(candidate) && !Directory.Exists(candidate)))
			{
				return;
			}

			matches.Add(new ModelAssetPathMatch(rootIndex, rootLabel, normalizedRoot, candidate));
		}
		catch (Exception ex)
		{
			NexusLog.Trace($"[MODEL ASSETS] Unable to test model root '{root}': {ex.Message}");
		}
	}

	private static string NormalizeRoot(string root)
		=> Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

	private static bool IsPathWithinRoot(string path, string root)
	{
		return string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
			path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
			path.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}
}

internal sealed record ModelAssetPathMatch(
	int RootIndex,
	string RootLabel,
	string RootPath,
	string FullPath);
