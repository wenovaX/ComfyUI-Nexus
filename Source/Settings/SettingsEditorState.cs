namespace ComfyUI_Nexus.Settings;

internal sealed record SettingsEditorState(
	bool HasUnsavedChanges,
	bool RequiresServerRestart,
	IReadOnlyList<string> RestartReasons);
