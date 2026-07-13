namespace ComfyUI_Nexus.Setup.Diagnostics;

internal interface IFolderSelectionDiagnosticNode : IConfigurableDiagnosticNode
{
	bool RequiresFolderSelection(string optionId);
	RecoveryResult ApplySelectedFolder(string optionId, string folderPath);
}
