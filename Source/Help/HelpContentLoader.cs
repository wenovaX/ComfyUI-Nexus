namespace ComfyUI_Nexus.Help;

using System.Reflection;
using System.Text;
using System.Text.Json;
using ComfyUI_Nexus.Localization;

internal static class HelpContentLoader
{
	private const string FallbackLanguage = "en";
	private const string HelpFileName = "help.catalog.json";
	private const string HelpResourceName = "ComfyUI_Nexus.Resources.Help.help.catalog.json";
	private const string ArticleResourcePrefix = "ComfyUI_Nexus.Resources.Help.Articles";

	private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

	internal static HelpContent Load(bool preferFileSystem = false)
	{
		if (TryLoad(preferFileSystem, out HelpContent? content))
		{
			return content ?? new HelpContent();
		}

		return new HelpContent();
	}

	private static bool TryLoad(bool preferFileSystem, out HelpContent? content)
	{
		content = null;
		if (preferFileSystem && TryLoadHelpFile(out string json))
		{
			content = JsonSerializer.Deserialize<HelpContent>(json, SerializerOptions);
			if (content != null)
			{
				ApplyLocalizedText(content);
				HydrateArticleBodies(content, preferFileSystem);
			}

			return content != null;
		}

		Assembly assembly = typeof(HelpContentLoader).Assembly;
		using Stream? stream = assembly.GetManifestResourceStream(HelpResourceName);
		if (stream == null)
		{
			return false;
		}

		using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
		content = JsonSerializer.Deserialize<HelpContent>(reader.ReadToEnd(), SerializerOptions);
		if (content != null)
		{
			ApplyLocalizedText(content);
			HydrateArticleBodies(content, preferFileSystem);
		}

		return content != null;
	}

	private static void ApplyLocalizedText(HelpContent content)
	{
		content.DisplayTitle = GetLocalizedValue(content.Title);
		content.DisplaySubtitle = GetLocalizedValue(content.Subtitle);
		foreach (HelpSection section in content.Sections)
		{
			section.DisplayTitle = GetLocalizedValue(section.Title);
			section.DisplayDescription = GetLocalizedValue(section.Description);
			foreach (HelpItem item in section.Items)
			{
				item.DisplayTitle = GetLocalizedValue(item.Title);
				item.DisplayHint = GetLocalizedValue(item.Hint);
			}
		}
	}

	private static string GetLocalizedValue(LocalizedHelpText text)
	{
		foreach (string languageCode in LocalizationManager.GetLanguageCandidates(LocalizationManager.ActiveLanguage))
		{
			if (text.TryGetValue(languageCode, out string? value) && !string.IsNullOrWhiteSpace(value))
			{
				return value;
			}
		}

		if (text.TryGetValue(FallbackLanguage, out string? fallback) && !string.IsNullOrWhiteSpace(fallback))
		{
			return fallback;
		}

		return text.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
	}

	private static void HydrateArticleBodies(HelpContent content, bool preferFileSystem)
	{
		foreach (HelpItem item in content.Sections.SelectMany(section => section.Items))
		{
			if (!string.IsNullOrWhiteSpace(item.Body) ||
				string.IsNullOrWhiteSpace(item.Slug))
			{
				continue;
			}

			if (TryLoadArticle(item.Slug, LocalizationManager.ActiveLanguage, preferFileSystem, out string? body))
			{
				item.Body = body ?? string.Empty;
			}
		}
	}

	private static bool TryLoadArticle(string article, string languageCode, bool preferFileSystem, out string? body)
	{
		body = null;
		if (article.IndexOfAny(['/', '\\']) >= 0)
		{
			return false;
		}

		foreach (string candidate in GetArticleFileCandidates(article, languageCode))
		{
			if (preferFileSystem && TryLoadArticleFile(candidate, out body))
			{
				return true;
			}

			if (TryLoadArticleResource(candidate, out body))
			{
				return true;
			}
		}

		return false;
	}

	private static bool TryLoadHelpFile(out string json)
	{
		json = string.Empty;
		foreach (string root in GetWorkspaceRoots())
		{
			string path = Path.Combine(root, "Resources", "Help", HelpFileName);
			if (File.Exists(path))
			{
				json = File.ReadAllText(path, Encoding.UTF8);
				return true;
			}
		}

		return false;
	}

	private static bool TryLoadArticleFile(string fileName, out string? body)
	{
		body = null;
		foreach (string root in GetWorkspaceRoots())
		{
			string path = Path.Combine(root, "Resources", "Help", "Articles", fileName);
			if (File.Exists(path))
			{
				body = File.ReadAllText(path, Encoding.UTF8);
				return true;
			}
		}

		return false;
	}

	private static IEnumerable<string> GetWorkspaceRoots()
	{
		for (string? root = Directory.GetCurrentDirectory(); root != null; root = Path.GetDirectoryName(root))
		{
			if (File.Exists(Path.Combine(root, "ComfyUI-Nexus.csproj")))
			{
				yield return root;
				yield break;
			}
		}

		yield return AppContext.BaseDirectory;
	}

	private static IEnumerable<string> GetArticleFileCandidates(string article, string languageCode)
	{
		foreach (string candidateLanguage in LocalizationManager.GetLanguageCandidates(languageCode))
		{
			yield return Path.Combine(article, $"{candidateLanguage}.txt");
		}

		yield return Path.Combine(article, $"{FallbackLanguage}.txt");
	}

	private static bool TryLoadArticleResource(string fileName, out string? body)
	{
		body = null;
		Assembly assembly = typeof(HelpContentLoader).Assembly;
		string resourceSuffix = $"{ArticleResourcePrefix}.{fileName.Replace('\\', '.').Replace('/', '.')}";
		string? resourceName = assembly.GetManifestResourceNames()
			.FirstOrDefault(name => string.Equals(NormalizeResourceName(name), resourceSuffix, StringComparison.Ordinal));
		if (resourceName == null)
		{
			return false;
		}

		using Stream? stream = assembly.GetManifestResourceStream(resourceName);
		if (stream == null) return false;

		using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
		body = reader.ReadToEnd();
		return true;
	}

	private static string NormalizeResourceName(string resourceName)
		=> resourceName.Replace('\\', '.').Replace('/', '.');
}
