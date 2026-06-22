namespace ComfyUI_Nexus.Settings;

using System.Text.Json;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Services;

internal sealed class SettingsEditorService
{
	private static readonly JsonSerializerOptions CloneJsonOptions = new()
	{
		WriteIndented = false
	};

	private SetupSettings _saved;
	private SetupSettings _draft;

	internal SettingsEditorService()
	{
		_saved = CloneSettings(SetupSettingsService.Instance.Settings);
		_draft = CloneSettings(_saved);
	}

	internal SetupSettings Draft => _draft;

	internal void Reload()
	{
		SetupSettingsService.Instance.Reload();
		_saved = CloneSettings(SetupSettingsService.Instance.Settings);
		_draft = CloneSettings(_saved);
	}

	internal void Discard()
	{
		_draft = CloneSettings(_saved);
	}

	internal bool Save()
	{
		CopySettings(_draft, SetupSettingsService.Instance.Settings);
		if (!SetupSettingsService.Instance.TrySave())
		{
			CopySettings(_saved, SetupSettingsService.Instance.Settings);
			return false;
		}

		_saved = CloneSettings(SetupSettingsService.Instance.Settings);
		_draft = CloneSettings(_saved);
		return true;
	}

	internal bool SaveRuntimeBackupPreferences(string path, string format)
	{
		string normalizedPath;
		try
		{
			normalizedPath = string.IsNullOrWhiteSpace(path)
				? string.Empty
				: Path.GetFullPath(path.Trim())
					.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}
		catch
		{
			return false;
		}

		string normalizedFormat = RuntimeBackupFormats.IsKnown(format)
			? format
			: RuntimeBackupFormats.Folder;
		SetupSettings live = SetupSettingsService.Instance.Settings;
		string previousPath = live.RuntimeBackupPath;
		string previousFormat = live.RuntimeBackupFormat;
		live.RuntimeBackupPath = normalizedPath;
		live.RuntimeBackupFormat = normalizedFormat;
		if (!SetupSettingsService.Instance.TrySave())
		{
			live.RuntimeBackupPath = previousPath;
			live.RuntimeBackupFormat = previousFormat;
			return false;
		}

		_saved.RuntimeBackupPath = normalizedPath;
		_saved.RuntimeBackupFormat = normalizedFormat;
		_draft.RuntimeBackupPath = normalizedPath;
		_draft.RuntimeBackupFormat = normalizedFormat;
		return true;
	}

	internal bool SavePipCacheSettings(string mode, string path)
	{
		string normalizedMode = PipCacheModes.IsKnown(mode) ? mode : PipCacheModes.NexusDefault;
		string normalizedPath;
		try
		{
			normalizedPath = !string.Equals(normalizedMode, PipCacheModes.Custom, StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(path)
				? string.Empty
				: Path.IsPathFullyQualified(path.Trim())
					? Path.GetFullPath(path.Trim())
						.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
					: string.Empty;
		}
		catch
		{
			return false;
		}

		SetupSettings live = SetupSettingsService.Instance.Settings;
		string previousMode = live.PipCacheMode;
		string previousPath = live.PipCachePath;
		live.PipCacheMode = normalizedMode;
		live.PipCachePath = normalizedPath;
		if (!SetupSettingsService.Instance.TrySave())
		{
			live.PipCacheMode = previousMode;
			live.PipCachePath = previousPath;
			return false;
		}

		_saved = CloneSettings(live);
		_draft = CloneSettings(live);
		return true;
	}

	internal SettingsEditorState Evaluate()
	{
		bool hasUnsavedChanges = !SettingsJsonEquals(_saved, _draft);
		var restartReasons = GetServerRestartReasons();
		return new SettingsEditorState(
			hasUnsavedChanges,
			restartReasons.Count > 0,
			restartReasons);
	}

	internal string? ValidateDraft()
	{
		if (string.IsNullOrWhiteSpace(_draft.ListenAddress) || _draft.ListenAddress.Any(char.IsWhiteSpace))
		{
			return "Host must be a compact value without spaces.";
		}

		if (_draft.ServerPort is < 1 or > 65535)
		{
			return "Port must be between 1 and 65535.";
		}

		if (string.IsNullOrWhiteSpace(_draft.GpuId))
		{
			return "GPU id cannot be empty.";
		}

		if (!PythonExecutionModes.IsKnown(_draft.ServerPythonMode))
		{
			return "Python launch mode is invalid.";
		}

		if (!SetupInstallModes.IsKnown(_draft.InstallMode))
		{
			return "ComfyUI install mode is invalid.";
		}

		if (string.Equals(_draft.InstallMode, SetupInstallModes.ExistingComfyPath, StringComparison.Ordinal))
		{
			string comfyPath = GetEffectiveComfyPath(_draft);
			if (!File.Exists(Path.Combine(comfyPath, "main.py")))
			{
				return "Selected ComfyUI folder must contain main.py.";
			}
		}

		ExtraModelPathsResult modelValidation = ExtraModelPathsService.ValidateSettings(_draft);
		if (!modelValidation.IsSuccess)
		{
			return modelValidation.Message;
		}

		return null;
	}

	private List<string> GetServerRestartReasons()
	{
		ServerLaunchSettingsSnapshot? active = _saved.ActiveServerLaunchSettings;
		if (active == null && _saved.LastLaunchSuccessful)
		{
			active = ServerLaunchSettingsSnapshot.FromSettings(_saved, GetEffectiveComfyPath(_saved));
		}

		if (active == null)
		{
			return [];
		}

		var draft = ServerLaunchSettingsSnapshot.FromSettings(_draft, GetEffectiveComfyPath(_draft));
		var reasons = new List<string>();
		if (HasDirectPendingVenvDelete(_saved) || HasDirectPendingVenvDelete(_draft))
		{
			reasons.Add("Scheduled .venv delete");
		}

		AddIfChanged(reasons, "ComfyUI path", active.ComfyPath, draft.ComfyPath);
		AddIfChanged(reasons, "Python path", active.PythonPath, draft.PythonPath);
		AddIfChanged(reasons, "Python launch mode", active.ServerPythonMode, draft.ServerPythonMode);
		AddIfChanged(reasons, "Host", active.ListenAddress, draft.ListenAddress);
		if (active.ServerPort != draft.ServerPort)
		{
			reasons.Add("Port");
		}

		AddIfChanged(reasons, "GPU", active.GpuId, draft.GpuId);
		if (!ModelRootsEqual(_saved, _draft)
			|| ExtraModelPathsService.NeedsSynchronization(_draft, GetEffectiveComfyPath(_draft)))
		{
			reasons.Add(LocalizationManager.Text("settings.model_libraries.restart_reason"));
		}

		return reasons;
	}

	internal bool RequiresModelLibraryApply()
		=> !ModelRootsEqual(_saved, _draft)
			|| !string.Equals(
				ExtraModelPathsService.NormalizeFileSystemPath(GetEffectiveComfyPath(_saved)),
				ExtraModelPathsService.NormalizeFileSystemPath(GetEffectiveComfyPath(_draft)),
				StringComparison.OrdinalIgnoreCase)
			|| ExtraModelPathsService.NeedsSynchronization(_draft, GetEffectiveComfyPath(_draft));

	private static bool ModelRootsEqual(SetupSettings left, SetupSettings right)
	{
		IReadOnlyList<string> leftRoots = ExtraModelPathsService.NormalizeRoots(left);
		IReadOnlyList<string> rightRoots = ExtraModelPathsService.NormalizeRoots(right);
		return leftRoots.Count == rightRoots.Count
			&& leftRoots.Zip(rightRoots).All(pair =>
				string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));
	}

	private static void AddIfChanged(List<string> reasons, string label, string oldValue, string newValue)
	{
		if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
		{
			reasons.Add(label);
		}
	}

	private static string GetEffectiveComfyPath(SetupSettings settings)
		=> string.IsNullOrWhiteSpace(settings.ComfyPath) ? ComfyInstallService.DefaultComfyPath : settings.ComfyPath;

	private static bool HasDirectPendingVenvDelete(SetupSettings settings)
		=> settings.PendingVenvDelete
			&& string.Equals(settings.ServerPythonMode, PythonExecutionModes.ConfiguredPython, StringComparison.Ordinal);

	private static bool SettingsJsonEquals(SetupSettings left, SetupSettings right)
		=> string.Equals(ToJson(left), ToJson(right), StringComparison.Ordinal);

	private static SetupSettings CloneSettings(SetupSettings settings)
		=> JsonSerializer.Deserialize<SetupSettings>(ToJson(settings)) ?? new SetupSettings();

	private static void CopySettings(SetupSettings source, SetupSettings target)
	{
		SetupSettings copy = CloneSettings(source);
		foreach (var property in typeof(SetupSettings).GetProperties())
		{
			if (!property.CanWrite) continue;

			property.SetValue(target, property.GetValue(copy));
		}
	}

	private static string ToJson(SetupSettings settings)
		=> JsonSerializer.Serialize(settings, CloneJsonOptions);
}
