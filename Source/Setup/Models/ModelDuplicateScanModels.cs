namespace ComfyUI_Nexus.Setup.Models;

internal sealed record ModelDuplicateScanResult(
	int ScannedFileCount,
	long ScannedBytes,
	IReadOnlyList<ModelDuplicateGroup> Groups,
	string ReportPath)
{
	public bool HasDuplicates => Groups.Count > 0;
}

internal sealed record ModelDuplicateGroup(
	string Sha256,
	long Length,
	IReadOnlyList<ModelDuplicateFile> Files);

internal sealed record ModelDuplicateFile(
	string SourceKind,
	string SourceLabel,
	string SourceRoot,
	string FullPath,
	string RelativePath,
	string FileName,
	long Length);

internal sealed record ModelDuplicateScanProgress(
	ModelDuplicateScanStage Stage,
	int ProcessedFiles,
	int TotalFiles,
	long ProcessedBytes,
	long TotalBytes)
{
	public double? Progress =>
		TotalBytes > 0
			? Math.Clamp((double)ProcessedBytes / TotalBytes, 0, 1)
			: TotalFiles > 0
				? Math.Clamp((double)ProcessedFiles / TotalFiles, 0, 1)
				: null;
}

internal enum ModelDuplicateScanStage
{
	DiscoveringFiles,
	PreparingHashes,
	HashingFiles,
	WritingReport
}
