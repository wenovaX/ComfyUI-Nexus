namespace ComfyUI_Nexus.Setup.Models;

internal sealed record SetupStepResult(
	bool IsSuccess,
	string Message,
	double Progress = 1,
	bool RequiresSetupHandoff = false);
