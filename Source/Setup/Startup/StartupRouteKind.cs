namespace ComfyUI_Nexus.Setup.Startup;

internal enum StartupRouteKind
{
	FullSetup,
	MaintenanceRecovery,
	ServerLaunchOnly,
	ServerStartupPending,
	DirectLoading
}
