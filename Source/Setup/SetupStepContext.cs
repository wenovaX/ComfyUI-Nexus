using ComfyUI_Nexus.Setup.Services;

namespace ComfyUI_Nexus.Setup;

internal sealed record SetupStepContext(
	CoreLinkDetector CoreLinkDetector,
	ComfyInstallService ComfyInstallService,
	ComfyServerProcessService ComfyServerProcessService,
	NexusAppEntryService NexusAppEntryService);
