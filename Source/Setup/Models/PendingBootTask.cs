namespace ComfyUI_Nexus.Setup.Models;

using System.Text.Json.Serialization;

internal sealed class PendingBootTask
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("state")]
	public string State { get; set; } = PendingBootTaskStates.Pending;

	[JsonPropertyName("created_at_utc")]
	public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

	[JsonPropertyName("started_at_utc")]
	public DateTime? StartedAtUtc { get; set; }

	[JsonPropertyName("last_error")]
	public string LastError { get; set; } = string.Empty;

	[JsonPropertyName("origin")]
	public string Origin { get; set; } = string.Empty;

	[JsonPropertyName("target_folders")]
	public List<string> TargetFolders { get; set; } = new();

	[JsonPropertyName("action")]
	public string Action { get; set; } = string.Empty;
}

internal static class PendingBootTaskStates
{
	internal const string Pending = "pending";
	internal const string InProgress = "in_progress";
}

internal static class PendingBootTaskOrigins
{
	internal const string VenvModeSelection = "venv-mode-selection";
	internal const string ComfyCoreSourceSelection = "comfy-core-source-selection";
}

internal static class PendingBootTaskActions
{
	internal const string ExtensionSync = "extension-sync";
	internal const string ExtensionReinstall = "extension-reinstall";
}

internal static class PendingBootTaskIds
{
	internal const string RuntimePurge = "runtime-purge";
	internal const string ResetSetup = "reset-setup";
	internal const string ResetAll = "reset-all";
	internal const string ComfyUpdate = "comfy-update";
	internal const string ExtensionRepair = "extension-repair";
	internal const string VenvCreate = "venv-create";
	internal const string VenvRebuild = "venv-rebuild";
	internal const string VenvDelete = "venv-delete";
	internal const string RuntimeRepair = "runtime-repair";
}
