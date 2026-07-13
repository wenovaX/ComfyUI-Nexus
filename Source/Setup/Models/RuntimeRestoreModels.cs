namespace ComfyUI_Nexus.Setup.Models;

internal enum RuntimeRestoreAction
{
	Add,
	Replace,
	Unchanged,
	Retained
}

internal sealed record RuntimeRestoreItem(
	string RelativePath,
	RuntimeRestoreAction Action,
	long SourceLength,
	string SourceSha256,
	long DestinationLength,
	long DestinationLastWriteUtcTicks);

internal sealed record RuntimeRestoreAnalysis(
	bool IsSuccess,
	string Message,
	string BackupPath,
	string BackupFormat,
	string ComfyPath,
	IReadOnlyList<string> Targets,
	IReadOnlyList<RuntimeRestoreItem> Items,
	long CopyBytes,
	long RequiredBytes,
	long AvailableBytes,
	string PreviewReportPath)
{
	internal int AddCount => Items.Count(item => item.Action == RuntimeRestoreAction.Add);
	internal int ReplaceCount => Items.Count(item => item.Action == RuntimeRestoreAction.Replace);
	internal int UnchangedCount => Items.Count(item => item.Action == RuntimeRestoreAction.Unchanged);
	internal int RetainedCount => Items.Count(item => item.Action == RuntimeRestoreAction.Retained);
}

internal sealed record RuntimeRestoreRequest(RuntimeRestoreAnalysis Analysis, bool ServerWasRunning);

internal sealed record RuntimeRestoreResult(bool IsSuccess, string Message, int CompletedFiles, int PendingFiles);
