namespace ComfyUI_Nexus.Setup.Diagnostics;

internal interface IRuntimeDiagnosticNode
{
	string NodeId { get; }
	string DisplayName { get; }
	string Description { get; }
	bool IsCritical { get; }

	// Lightweight state check used by startup/setup readiness.
	Task<HealthState> CheckHealthAsync(CancellationToken cancellationToken);

	// Recovery action used when a node is missing or misconfigured.
	Task<RecoveryResult> RecoverAsync(IProgress<double>? progress, CancellationToken cancellationToken);
}
