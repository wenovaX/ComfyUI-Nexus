namespace ComfyUI_Nexus.Setup.Diagnostics;

internal interface IFolderSelectionDiagnosticNode : IConfigurableDiagnosticNode
{
	string FolderPickerTitle { get; }
	bool RequiresFolderSelection(string optionId);
	RecoveryResult ApplySelectedFolder(string optionId, string folderPath);
}
