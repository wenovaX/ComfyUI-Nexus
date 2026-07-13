namespace ComfyUI_Nexus.Setup.Diagnostics;

internal sealed class RuntimeDiagnosticCatalog
{
	internal RuntimeDiagnosticCatalog(IEnumerable<IRuntimeDiagnosticNode> nodes)
	{
		Nodes = nodes.ToList();
	}

	internal IReadOnlyList<IRuntimeDiagnosticNode> Nodes { get; }
}
