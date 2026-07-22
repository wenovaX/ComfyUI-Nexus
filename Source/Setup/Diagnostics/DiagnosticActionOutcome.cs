namespace ComfyUI_Nexus.Setup.Diagnostics;

// A setup action always resolves to one explicit UI outcome.
internal enum DiagnosticActionOutcomeKind
{
	Completed,
	AwaitingUserChoice,
	Cancelled,
	Failed
}

internal sealed record DiagnosticActionOutcome(
	DiagnosticActionOutcomeKind Kind,
	string Details = "",
	HealthState? Health = null,
	bool RequestTailScroll = false);
