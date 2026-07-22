namespace ComfyUI_Nexus.Setup.Diagnostics;

using System.Threading;
using System.Threading.Tasks;

internal interface IExecutableSelectionDiagnosticNode : IConfigurableDiagnosticNode
{
	bool RequiresExecutableSelection(string optionId);
	Task<RecoveryResult> ApplySelectedExecutableAsync(string optionId, string executablePath, CancellationToken cancellationToken);
}
