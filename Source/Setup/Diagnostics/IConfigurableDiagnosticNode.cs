namespace ComfyUI_Nexus.Setup.Diagnostics;

internal enum DiagnosticActionCompletionPolicy
{
	VerifyHealth,
	AssumeHealthy,
	AssumeOptionalMissing
}

internal sealed class DiagnosticOption
{
	public string Id { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string WorkingHint { get; set; } = string.Empty;
	public bool IsRecommended { get; set; }
	public bool RequiresRecovery { get; set; } = true;
	public bool RequiresToolingLease { get; set; }
	public bool CanCancel { get; set; }
	public string CancellationWorkingHint { get; set; } = string.Empty;
	public string CancellationResultDetails { get; set; } = string.Empty;
	public DiagnosticActionCompletionPolicy CompletionPolicy { get; set; } = DiagnosticActionCompletionPolicy.VerifyHealth;
}

internal interface IConfigurableDiagnosticNode : IRuntimeDiagnosticNode
{
	// Human-readable environment summary shown in setup cards.
	string EnvironmentDetails { get; }

	// Install or executable path related to this diagnostic node.
	string EnvironmentPath { get; }

	// Optional secondary path or URL shown beside the primary environment link.
	string SecondaryEnvironmentPath => string.Empty;

	// User-selectable recovery or configuration options.
	IReadOnlyList<DiagnosticOption> AvailableOptions { get; }

	// Currently selected option id.
	string SelectedOptionId { get; }

	// Keeps configuration actions visible after a healthy or skipped state.
	bool KeepInlineActionsVisibleAfterCompletion => false;

	// Performs a deeper environment probe before showing configuration actions.
	Task ProbeEnvironmentAsync(CancellationToken cancellationToken);

	// Applies the selected option to settings or node state.
	void SelectOption(string optionId);
}
