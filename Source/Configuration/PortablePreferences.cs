namespace ComfyUI_Nexus.Configuration;

using System.Text.Json;
using ComfyUI_Nexus.Setup.Services;

internal static class PortablePreferences
{
	private static readonly object Sync = new();
	private static Dictionary<string, JsonElement>? _values;

	internal static string Get(string key, string defaultValue)
		=> GetValue(key, defaultValue);

	internal static int Get(string key, int defaultValue)
		=> GetValue(key, defaultValue);

	internal static double Get(string key, double defaultValue)
		=> GetValue(key, defaultValue);

	internal static void Set(string key, string value)
		=> SetValue(key, value);

	internal static void Set(string key, int value)
		=> SetValue(key, value);

	internal static void Set(string key, double value)
		=> SetValue(key, value);

	internal static void Remove(string key)
	{
		lock (Sync)
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

	private static T GetValue<T>(string key, T defaultValue)
	{
		lock (Sync)
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

	private static void SetValue<T>(string key, T value)
	{
		lock (Sync)
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

	private static void EnsureLoaded()
	{
		if (_values != null)
		{
			return;
		}

		string path = GetFilePath();
		try
		{
			if (File.Exists(path))
			{
				string json = File.ReadAllText(path);
				_values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
			}
		}
		catch
		{
		}

		_values ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
	}

	private static void Save()
	{
		string path = GetFilePath();
		string directory = Path.GetDirectoryName(path)!;
		string temporaryPath = path + ".tmp";
		Directory.CreateDirectory(directory);

		string json = JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(temporaryPath, json);
		File.Move(temporaryPath, path, overwrite: true);
	}

	private static string GetFilePath()
		=> ComfyInstallService.GetLocalRuntimePath("State/preferences.json");
}
