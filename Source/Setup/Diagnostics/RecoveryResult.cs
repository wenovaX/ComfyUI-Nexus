namespace ComfyUI_Nexus.Setup.Diagnostics;

internal sealed record RecoveryResult(
	bool IsSuccess,
	string Message);
