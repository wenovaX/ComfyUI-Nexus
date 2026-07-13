namespace ComfyUI_Nexus.Setup.Runtime;

internal sealed record ComfyServerProcessInfo(
	int ProcessId,
	string ProcessName,
	string Source,
	string? LogPath = null);
