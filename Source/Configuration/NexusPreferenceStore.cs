using System.Text.Json;

namespace ComfyUI_Nexus.Configuration;

/// <summary>
/// Owns the small app preference document that is separate from setup settings.
/// The store is app-lifetime state and is created only by <see cref="NexusAppManager"/>.
/// </summary>
internal sealed class NexusPreferenceStore
{
	private static readonly JsonSerializerOptions WriteIndentedJsonOptions = new()
	{
		WriteIndented = true,
	};

	private readonly object _sync = new();
	private readonly string _filePath;
	private Dictionary<string, JsonElement>? _values;

	internal NexusPreferenceStore()
	{
		_filePath = Path.Combine(NexusStorageLayout.LocalRuntimeRoot, "State", "preferences.json");
	}

	internal string Get(string key, string defaultValue)
		=> GetValue(key, defaultValue);

	internal int Get(string key, int defaultValue)
		=> GetValue(key, defaultValue);

	internal double Get(string key, double defaultValue)
		=> GetValue(key, defaultValue);

	internal bool Get(string key, bool defaultValue)
		=> GetValue(key, defaultValue);

	internal void Set(string key, string value)
		=> SetValue(key, value);

	internal void Set(string key, int value)
		=> SetValue(key, value);

	internal void Set(string key, double value)
		=> SetValue(key, value);

	internal void Set(string key, bool value)
		=> SetValue(key, value);

	internal void Remove(string key)
	{
		lock (_sync)
		{
			try
			{
				EnsureLoaded();
				if (_values!.Remove(key))
				{
					Save();
				}
			}
			catch
			{
			}
		}
	}

	private T GetValue<T>(string key, T defaultValue)
	{
		lock (_sync)
		{
			EnsureLoaded();
			if (!_values!.TryGetValue(key, out JsonElement value))
			{
				return defaultValue;
			}

			try
			{
				return value.Deserialize<T>() ?? defaultValue;
			}
			catch
			{
				return defaultValue;
			}
		}
	}

	private void SetValue<T>(string key, T value)
	{
		lock (_sync)
		{
			try
			{
				EnsureLoaded();
				_values![key] = JsonSerializer.SerializeToElement(value);
				Save();
			}
			catch
			{
			}
		}
	}

	private void EnsureLoaded()
	{
		if (_values != null)
		{
			return;
		}

		try
		{
			if (File.Exists(_filePath))
			{
				_values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(_filePath));
			}
		}
		catch
		{
		}

		_values ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
	}

	private void Save()
	{
		string directory = Path.GetDirectoryName(_filePath)!;
		string temporaryPath = _filePath + ".tmp";
		Directory.CreateDirectory(directory);

		string json = JsonSerializer.Serialize(_values, WriteIndentedJsonOptions);
		File.WriteAllText(temporaryPath, json);
		File.Move(temporaryPath, _filePath, overwrite: true);
	}
}
