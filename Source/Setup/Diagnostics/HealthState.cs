namespace ComfyUI_Nexus.Setup.Diagnostics;

internal enum HealthState
{
	Pending,
	Healthy,
	OptionalMissing,
	NeedsRecovery,
	CriticalError
}
