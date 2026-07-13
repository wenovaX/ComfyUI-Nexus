namespace ComfyUI_Nexus.Setup.Services;

using System.Text;
using System.Text.RegularExpressions;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Models;
using YamlDotNet.Serialization;

internal sealed record ExtraModelPathsResult(bool IsSuccess, string Message);

internal sealed class ExtraModelPathsTransaction
{
	private readonly string _yamlPath;
	private readonly bool _originalExisted;
	private readonly string _originalText;
	private readonly bool _originalHadUtf8Bom;
	private bool _completed;

	internal ExtraModelPathsTransaction(
		string yamlPath,
		bool originalExisted,
		string originalText,
		bool originalHadUtf8Bom)
	{
		_yamlPath = yamlPath;
		_originalExisted = originalExisted;
		_originalText = originalText;
		_originalHadUtf8Bom = originalHadUtf8Bom;
	}

	internal void Commit()
		=> _completed = true;

	internal void Rollback()
	{
		if (_completed)
		{
			return;
		}

		try
		{
			if (_originalExisted)
			{
				ExtraModelPathsService.WriteAtomic(_yamlPath, _originalText, _originalHadUtf8Bom);
			}
			else if (File.Exists(_yamlPath))
			{
				File.Delete(_yamlPath);
			}
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[MODEL PATHS] Failed to roll back extra_model_paths.yaml");
		}
	}
}

internal static class ExtraModelPathsService
{
	internal const string BeginMarker = "# BEGIN COMFYUI NEXUS MODEL PATHS";
	internal const string EndMarker = "# END COMFYUI NEXUS MODEL PATHS";
	private const string YamlFileName = "extra_model_paths.yaml";
	private const string BackupSuffix = ".nexus.bak";
	private static readonly (string Key, string[] Paths)[] CorePathAliases =
	[
		("text_encoders", ["text_encoders", "clip"]),
		("diffusion_models", ["unet", "diffusion_models"]),
		("controlnet", ["controlnet", "t2i_adapter"]),
		("ultralytics_bbox", ["ultralytics/bbox"]),
		("ultralytics_segm", ["ultralytics/segm"])
	];
	private static readonly HashSet<string> ReservedYamlKeys = new(
		["base_path", "is_default"],
		StringComparer.OrdinalIgnoreCase);

	private sealed record ModelPathMapping(string Key, IReadOnlyList<string> RelativePaths);
	private sealed record ModelLibraryPathSet(string Root, IReadOnlyList<ModelPathMapping> Mappings);

	internal static IReadOnlyList<string> NormalizeRoots(SetupSettings settings)
	{
		var roots = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (string root in settings.ModelLibraryRoots ?? [])
		{
			AddRoot(root);
		}

		return roots;

		void AddRoot(string? root)
		{
			string normalized = NormalizeFileSystemPath(root);
			if (normalized.Length > 0 && seen.Add(normalized))
			{
				roots.Add(normalized);
			}
		}
	}

	internal static string NormalizeFileSystemPath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return string.Empty;
		}

		try
		{
			return Path.GetFullPath(path.Trim())
				.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}
		catch
		{
			return path.Trim()
				.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}
	}

	internal static ExtraModelPathsResult ValidateSettings(SetupSettings settings)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string root in settings.ModelLibraryRoots ?? [])
		{
			string normalized = NormalizeFileSystemPath(root);
			if (normalized.Length == 0)
			{
				continue;
			}

			if (!seen.Add(normalized))
			{
				return new ExtraModelPathsResult(
					false,
					LocalizationManager.Format("model_libraries.service.duplicate_path", normalized));
			}
		}

		return new ExtraModelPathsResult(true, string.Empty);
	}

	internal static ExtraModelPathsResult Inspect(SetupSettings settings, string comfyPath)
	{
		ExtraModelPathsResult validation = ValidateSettings(settings);
		if (!validation.IsSuccess)
		{
			return validation;
		}

		IReadOnlyList<string> roots = NormalizeRoots(settings);
		if (roots.Count == 0)
		{
			return new ExtraModelPathsResult(true, LocalizationManager.Text("model_libraries.service.none_connected"));
		}

		string? unavailable = roots.FirstOrDefault(root => !Directory.Exists(root));
		if (unavailable != null)
		{
			return new ExtraModelPathsResult(false, LocalizationManager.Format("model_libraries.service.unavailable", unavailable));
		}

		string yamlPath = Path.Combine(comfyPath, YamlFileName);
		if (!File.Exists(yamlPath))
		{
			return new ExtraModelPathsResult(false, LocalizationManager.Text("model_libraries.service.yaml_missing"));
		}

		string text = File.ReadAllText(yamlPath, Encoding.UTF8);
		if (!TrySplitManagedSection(text, out _, out string managed, out _, out string error))
		{
			return new ExtraModelPathsResult(false, error);
		}

		IReadOnlyList<ModelLibraryPathSet> libraries = ResolveModelLibraries(roots);
		string expected = BuildManagedSection(libraries);
		return string.Equals(NormalizeLineEndings(managed).Trim(), NormalizeLineEndings(expected).Trim(), StringComparison.Ordinal)
			? new ExtraModelPathsResult(true, LocalizationManager.Format("model_libraries.service.connected_count", roots.Count))
			: new ExtraModelPathsResult(false, LocalizationManager.Text("model_libraries.service.out_of_sync"));
	}

	internal static bool NeedsSynchronization(SetupSettings settings, string comfyPath)
	{
		IReadOnlyList<string> roots = NormalizeRoots(settings);
		string yamlPath = Path.Combine(comfyPath, YamlFileName);
		if (!File.Exists(yamlPath))
		{
			return roots.Count > 0;
		}

		try
		{
			string text = File.ReadAllText(yamlPath, Encoding.UTF8);
			if (!TrySplitManagedSection(text, out _, out string managed, out _, out _))
			{
				return true;
			}

			if (roots.Count == 0)
			{
				return managed.Length > 0;
			}

			IReadOnlyList<ModelLibraryPathSet> libraries = ResolveModelLibraries(roots);
			string expected = BuildManagedSection(libraries);
			return !string.Equals(
				NormalizeLineEndings(managed).Trim(),
				NormalizeLineEndings(expected).Trim(),
				StringComparison.Ordinal);
		}
		catch
		{
			return true;
		}
	}

	internal static ExtraModelPathsResult TryApply(
		SetupSettings settings,
		string comfyPath,
		out ExtraModelPathsTransaction? transaction)
	{
		transaction = null;
		ExtraModelPathsResult validation = ValidateSettings(settings);
		if (!validation.IsSuccess)
		{
			return validation;
		}

		if (string.IsNullOrWhiteSpace(comfyPath) || !Directory.Exists(comfyPath))
		{
			return new ExtraModelPathsResult(false, LocalizationManager.Text("model_libraries.service.comfy_unavailable"));
		}

		string yamlPath = Path.Combine(comfyPath, YamlFileName);
		bool originalExisted = File.Exists(yamlPath);
		bool originalHadUtf8Bom = originalExisted && HasUtf8Bom(yamlPath);
		string originalText = originalExisted ? File.ReadAllText(yamlPath, Encoding.UTF8) : string.Empty;
		string newline = originalText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

		try
		{
			if (originalText.Length > 0)
			{
				ValidateYaml(originalText);
			}

			if (!TrySplitManagedSection(originalText, out string before, out _, out string after, out string markerError))
			{
				return new ExtraModelPathsResult(false, markerError);
			}

			string unmanagedText = JoinOutsideSections(before, after);
			ValidateUnmanagedKeys(unmanagedText);

			IReadOnlyList<string> roots = NormalizeRoots(settings);
			IReadOnlyList<ModelLibraryPathSet> libraries = ResolveModelLibraries(roots);
			string finalText = BuildFinalText(unmanagedText, libraries, newline);
			if (finalText.Length > 0)
			{
				ValidateYaml(finalText);
			}

			string backupPath = yamlPath + BackupSuffix;
			if (originalExisted)
			{
				File.Copy(yamlPath, backupPath, overwrite: true);
			}

			transaction = new ExtraModelPathsTransaction(
				yamlPath,
				originalExisted,
				originalText,
				originalHadUtf8Bom);
			if (finalText.Length == 0)
			{
				if (File.Exists(yamlPath))
				{
					File.Delete(yamlPath);
				}
			}
			else
			{
				WriteAtomic(yamlPath, finalText, originalHadUtf8Bom);
			}

			return new ExtraModelPathsResult(true, roots.Count == 0
				? LocalizationManager.Text("model_libraries.service.removed")
				: LocalizationManager.Format("model_libraries.service.applied_count", roots.Count));
		}
		catch (Exception ex)
		{
			transaction?.Rollback();
			transaction = null;
			return new ExtraModelPathsResult(
				false,
				LocalizationManager.Format("model_libraries.service.update_failed", ex.Message));
		}
	}

	internal static bool ContainsRoot(SetupSettings settings, string path)
	{
		string normalized = NormalizeFileSystemPath(path);
		return NormalizeRoots(settings).Any(root =>
			string.Equals(root, normalized, StringComparison.OrdinalIgnoreCase));
	}

	internal static void WriteAtomic(string path, string text, bool emitUtf8Bom = false)
	{
		string? directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}

		string tempPath = path + $".{Guid.NewGuid():N}.tmp";
		try
		{
			File.WriteAllText(tempPath, text, new UTF8Encoding(emitUtf8Bom));
			File.Move(tempPath, path, overwrite: true);
		}
		finally
		{
			if (File.Exists(tempPath))
			{
				File.Delete(tempPath);
			}
		}
	}

	private static string BuildFinalText(
		string unmanagedText,
		IReadOnlyList<ModelLibraryPathSet> libraries,
		string newline)
	{
		if (libraries.Count == 0)
		{
			return string.IsNullOrWhiteSpace(unmanagedText) ? string.Empty : unmanagedText;
		}

		string separator = unmanagedText.Length == 0
			? string.Empty
			: unmanagedText.EndsWith('\n') || unmanagedText.EndsWith('\r')
				? newline
				: newline + newline;
		return unmanagedText
			+ separator
			+ BuildManagedSection(libraries).Replace(Environment.NewLine, newline, StringComparison.Ordinal)
			+ newline;
	}

	private static string BuildManagedSection(IReadOnlyList<ModelLibraryPathSet> libraries)
	{
		var builder = new StringBuilder();
		builder.AppendLine(BeginMarker);
		for (int index = 0; index < libraries.Count; index++)
		{
			ModelLibraryPathSet library = libraries[index];
			string key = $"nexus_model_library_{index + 1}";
			builder.AppendLine($"{key}:");
			builder.AppendLine($"  base_path: {QuoteYamlScalar(library.Root.Replace('\\', '/'))}");

			foreach (ModelPathMapping mapping in library.Mappings)
			{
				if (mapping.RelativePaths.Count == 1)
				{
					builder.AppendLine($"  {FormatYamlKey(mapping.Key)}: {QuoteYamlScalar(mapping.RelativePaths[0])}");
					continue;
				}

				builder.AppendLine($"  {FormatYamlKey(mapping.Key)}: |");
				foreach (string relativePath in mapping.RelativePaths)
				{
					builder.AppendLine($"    {relativePath}");
				}
			}

			if (index < libraries.Count - 1)
			{
				builder.AppendLine();
			}
		}

		builder.Append(EndMarker);
		return builder.ToString();
	}

	private static string QuoteYamlScalar(string value)
		=> $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

	private static string FormatYamlKey(string value)
		=> Regex.IsMatch(value, @"^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)
			? value
			: QuoteYamlScalar(value);

	private static IReadOnlyList<ModelLibraryPathSet> ResolveModelLibraries(IReadOnlyList<string> roots)
		=> roots
			.Select(root => new ModelLibraryPathSet(root, ResolveModelPathMappings(root)))
			.ToList();

	private static IReadOnlyList<ModelPathMapping> ResolveModelPathMappings(string rootPath)
	{
		var topLevelSubdirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (Directory.Exists(rootPath))
		{
			foreach (string directory in Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly))
			{
				var info = new DirectoryInfo(directory);
				if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
				{
					continue;
				}

				if (!ReservedYamlKeys.Contains(info.Name))
				{
					topLevelSubdirectories.Add(info.Name);
				}
			}
		}

		var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var mappings = new List<ModelPathMapping>();
		foreach ((string key, string[] paths) in CorePathAliases)
		{
			List<string> availablePaths = paths
				.Where(path => PathExistsInModelStructure(rootPath, topLevelSubdirectories, path))
				.ToList();
			if (availablePaths.Count == 0)
			{
				continue;
			}

			mappings.Add(new ModelPathMapping(key, availablePaths));
			consumed.UnionWith(availablePaths);
		}

		foreach (string subdirectory in topLevelSubdirectories
			.Where(path => !consumed.Contains(path))
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
		{
			mappings.Add(new ModelPathMapping(subdirectory, [subdirectory]));
		}

		return mappings;
	}

	private static bool PathExistsInModelStructure(
		string rootPath,
		IReadOnlySet<string> topLevelSubdirectories,
		string relativePath)
	{
		if (topLevelSubdirectories.Contains(relativePath))
		{
			return true;
		}

		if (!relativePath.Contains('/', StringComparison.Ordinal))
		{
			return topLevelSubdirectories.Contains(relativePath);
		}

		return Directory.Exists(Path.Combine(
			rootPath,
			relativePath.Replace('/', Path.DirectorySeparatorChar)));
	}

	private static bool TrySplitManagedSection(
		string text,
		out string before,
		out string managed,
		out string after,
		out string error)
	{
		before = text;
		managed = string.Empty;
		after = string.Empty;
		error = string.Empty;

		List<Match> beginMatches = FindMarkerMatches(text, BeginMarker);
		List<Match> endMatches = FindMarkerMatches(text, EndMarker);
		if (beginMatches.Count == 0 && endMatches.Count == 0)
		{
			return true;
		}

		if (beginMatches.Count != 1 || endMatches.Count != 1 || beginMatches[0].Index >= endMatches[0].Index)
		{
			error = LocalizationManager.Text("model_libraries.service.markers_invalid");
			return false;
		}

		int begin = beginMatches[0].Index;
		int end = endMatches[0].Index + endMatches[0].Length;
		before = text[..begin];
		managed = text[begin..end];
		after = text[end..];
		return true;
	}

	private static List<Match> FindMarkerMatches(string text, string marker)
		=> Regex.Matches(
				text,
				$@"(?m)^[ \t]*{Regex.Escape(marker)}[ \t]*(?:\r?\n|$)",
				RegexOptions.CultureInvariant)
			.Cast<Match>()
			.ToList();

	private static string JoinOutsideSections(string before, string after)
		=> before + after;

	private static void ValidateUnmanagedKeys(string unmanagedText)
	{
		if (string.IsNullOrWhiteSpace(unmanagedText))
		{
			return;
		}

		object? document = new DeserializerBuilder().Build().Deserialize(new StringReader(unmanagedText));
		if (document is not IDictionary<object, object> mapping)
		{
			throw new InvalidDataException(LocalizationManager.Text("model_libraries.service.top_level_mapping"));
		}

		foreach (object key in mapping.Keys)
		{
			if (key?.ToString()?.StartsWith("nexus_", StringComparison.OrdinalIgnoreCase) == true)
			{
				throw new InvalidDataException(
					LocalizationManager.Format("model_libraries.service.reserved_key", key));
			}
		}
	}

	private static void ValidateYaml(string yaml)
		=> _ = new DeserializerBuilder().Build().Deserialize(new StringReader(yaml));

	private static string NormalizeLineEndings(string value)
		=> value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

	private static bool HasUtf8Bom(string path)
	{
		using var stream = File.OpenRead(path);
		return stream.Length >= 3
			&& stream.ReadByte() == 0xEF
			&& stream.ReadByte() == 0xBB
			&& stream.ReadByte() == 0xBF;
	}
}
