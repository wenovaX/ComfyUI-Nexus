namespace ComfyUI_Nexus.Setup.Diagnostics;

internal sealed class DiagnosticOption
{
	public string Id { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string WorkingHint { get; set; } = string.Empty;
	public bool IsRecommended { get; set; }
}

internal interface IConfigurableDiagnosticNode : IRuntimeDiagnosticNode
{
	// Human-readable environment summary shown in setup cards.
	string EnvironmentDetails { get; }

	// Install or executable path related to this diagnostic node.
	string EnvironmentPath { get; }

	// User-selectable recovery or configuration options.
	IReadOnlyList<DiagnosticOption> AvailableOptions { get; }

	// Currently selected option id.
	string SelectedOptionId { get; }

	// Performs a deeper environment probe before showing configuration actions.
	Task ProbeEnvironmentAsync(CancellationToken cancellationToken);

	// Applies the selected option to settings or node state.
	void SelectOption(string optionId);
}
