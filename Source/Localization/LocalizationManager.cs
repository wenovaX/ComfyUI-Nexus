namespace ComfyUI_Nexus.Localization;

using System.Globalization;
using System.Reflection;
using System.Text;

internal static class LocalizationManager
{
	private const string FallbackLanguage = "en";
	private const string ResourcePrefix = "ComfyUI_Nexus.Resources.Strings";

	// Keep this list aligned with Resources/Strings/*.csv and help article folders.
	private static readonly HashSet<string> SupportedLanguages = new(StringComparer.Ordinal)
	{
		FallbackLanguage,
		"ko",
		"zh-Hans",
		"zh-Hant"
	};

	private static readonly object SyncRoot = new();
	private static readonly Dictionary<string, string> FallbackStrings = new(StringComparer.Ordinal);
	private static readonly Dictionary<string, string> ActiveStrings = new(StringComparer.Ordinal);
	private static string _activeLanguage = FallbackLanguage;
	private static bool _isInitialized;

	internal static event EventHandler? LanguageChanged;

	internal static string ActiveLanguage
	{
		get
		{
			EnsureInitialized();
			return _activeLanguage;
		}
	}

	internal static void Initialize(string? languageCode = null)
	{
		lock (SyncRoot)
		{
			FallbackStrings.Clear();
			ActiveStrings.Clear();
			LoadLanguageInto(FallbackLanguage, FallbackStrings, required: true);

			_activeLanguage = NormalizeLanguageCodeCore(languageCode)
				?? NormalizeLanguageCodeCore(GetDefaultLanguageCode())
				?? FallbackLanguage;
			if (!string.Equals(_activeLanguage, FallbackLanguage, StringComparison.Ordinal))
			{
				LoadLanguageInto(_activeLanguage, ActiveStrings, required: false);
			}

			_isInitialized = true;
		}
	}

	internal static void SetLanguage(string? languageCode)
	{
		string nextLanguage = NormalizeLanguageCode(languageCode);
		EnsureInitialized();

		lock (SyncRoot)
		{
			if (string.Equals(_activeLanguage, nextLanguage, StringComparison.Ordinal))
			{
				return;
			}

			ActiveStrings.Clear();
			_activeLanguage = nextLanguage;
			if (!string.Equals(_activeLanguage, FallbackLanguage, StringComparison.Ordinal))
			{
				LoadLanguageInto(_activeLanguage, ActiveStrings, required: false);
			}
		}

		LanguageChanged?.Invoke(null, EventArgs.Empty);
	}

	internal static string Text(string key)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return string.Empty;
		}

		EnsureInitialized();
		lock (SyncRoot)
		{
			if (ActiveStrings.TryGetValue(key, out string? activeValue))
			{
				return activeValue;
			}

			return FallbackStrings.TryGetValue(key, out string? fallbackValue)
				? fallbackValue
				: $"!{key}!";
		}
	}

	internal static string Format(string key, params object?[] args)
		=> string.Format(CultureInfo.CurrentCulture, Text(key), args);

	internal static string NormalizeLanguageCode(string? languageCode)
		=> NormalizeLanguageCodeCore(languageCode) ?? FallbackLanguage;

	internal static IEnumerable<string> GetLanguageCandidates(string? languageCode)
	{
		string? normalized = NormalizeLanguageCodeCore(languageCode);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			yield break;
		}

		yield return normalized;

		if (normalized.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase)
			|| normalized.StartsWith("zh-SG", StringComparison.OrdinalIgnoreCase))
		{
			yield return "zh-Hans";
		}
		else if (normalized.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase)
			|| normalized.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
			|| normalized.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase))
		{
			yield return "zh-Hant";
		}

		int separatorIndex = normalized.IndexOf('-', StringComparison.Ordinal);
		if (separatorIndex > 0)
		{
			yield return normalized[..separatorIndex];
		}
	}

	private static void EnsureInitialized()
	{
		if (_isInitialized)
		{
			return;
		}

		Initialize();
	}

	private static string GetDefaultLanguageCode()
	{
		CultureInfo culture = CultureInfo.CurrentUICulture;
		if (!string.IsNullOrWhiteSpace(culture.Name))
		{
			return culture.Name;
		}

		return string.IsNullOrWhiteSpace(culture.TwoLetterISOLanguageName)
			? FallbackLanguage
			: culture.TwoLetterISOLanguageName;
	}

	private static string? NormalizeLanguageCodeCore(string? languageCode)
	{
		if (string.IsNullOrWhiteSpace(languageCode))
		{
			return null;
		}

		string normalized = languageCode.Trim().Replace('_', '-');
		string normalizedLower = normalized.ToLowerInvariant();
		string? mapped = normalizedLower switch
		{
			string value when value.StartsWith("en-", StringComparison.Ordinal) => FallbackLanguage,
			"ko" => "ko",
			string value when value.StartsWith("ko-", StringComparison.Ordinal) => "ko",
			string value when value.StartsWith("zh-cn", StringComparison.Ordinal) => "zh-Hans",
			string value when value.StartsWith("zh-sg", StringComparison.Ordinal) => "zh-Hans",
			string value when value.StartsWith("zh-tw", StringComparison.Ordinal) => "zh-Hant",
			string value when value.StartsWith("zh-hk", StringComparison.Ordinal) => "zh-Hant",
			string value when value.StartsWith("zh-mo", StringComparison.Ordinal) => "zh-Hant",
			"zh-hans" => "zh-Hans",
			"zh-hant" => "zh-Hant",
			_ => normalized,
		};

		return SupportedLanguages.Contains(mapped) ? mapped : null;
	}

	private static void LoadLanguageInto(
		string languageCode,
		Dictionary<string, string> target,
		bool required)
	{
		foreach (string candidate in GetLanguageCandidates(languageCode))
		{
			if (TryLoadLanguage(candidate, target))
			{
				return;
			}
		}

		if (required)
		{
			throw new InvalidOperationException($"Missing localization fallback resource: {languageCode}.csv");
		}
	}

	private static bool TryLoadLanguage(string languageCode, Dictionary<string, string> target)
	{
		Assembly assembly = typeof(LocalizationManager).Assembly;
		string resourceName = $"{ResourcePrefix}.{languageCode}.csv";
		using Stream? stream = assembly.GetManifestResourceStream(resourceName);
		if (stream == null)
		{
			return false;
		}

		using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
		foreach (var row in ParseCsv(reader.ReadToEnd()))
		{
			if (row.Count < 2 || string.Equals(row[0], "key", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string key = row[0].Trim();
			if (key.Length == 0)
			{
				continue;
			}

			string text = row[1].Replace("\\n", Environment.NewLine, StringComparison.Ordinal);
			if (text.Length == 0)
			{
				continue;
			}

			target[key] = text;
		}

		return true;
	}

	private static IEnumerable<IReadOnlyList<string>> ParseCsv(string csv)
	{
		var row = new List<string>();
		var field = new StringBuilder();
		bool inQuotes = false;

		for (int i = 0; i < csv.Length; i++)
		{
			char current = csv[i];
			if (current == '"')
			{
				if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"')
				{
					field.Append('"');
					i++;
				}
				else
				{
					inQuotes = !inQuotes;
				}
				continue;
			}

			if (current == ',' && !inQuotes)
			{
				row.Add(field.ToString());
				field.Clear();
				continue;
			}

			if ((current == '\r' || current == '\n') && !inQuotes)
			{
				if (current == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
				{
					i++;
				}

				row.Add(field.ToString());
				field.Clear();
				if (row.Count > 1 || row.Any(value => value.Length > 0))
				{
					yield return row;
				}

				row = new List<string>();
				continue;
			}

			field.Append(current);
		}

		row.Add(field.ToString());
		if (row.Count > 1 || row.Any(value => value.Length > 0))
		{
			yield return row;
		}
	}
}
