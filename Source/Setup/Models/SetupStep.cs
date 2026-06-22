namespace ComfyUI_Nexus.Setup.Models;

internal sealed record SetupStep(
	string Id,
	string Title,
	string Description,
	Func<SetupStepContext, CancellationToken, Task<SetupStepResult>> ExecuteAsync,
	bool IsRequired = true);
