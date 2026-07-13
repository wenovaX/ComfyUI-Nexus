namespace ComfyUI_Nexus.Setup.Models;

internal static class RuntimeBackupTargets
{
	internal const string Models = "models";
	internal const string CustomNodes = "custom_nodes";
	internal const string Input = "input";
	internal const string Output = "output";
	internal const string Workflows = "workflows";

	internal static readonly IReadOnlyList<string> All =
	[
		Models,
		CustomNodes,
		Input,
		Output,
		Workflows
	];

	internal static string Canonicalize(string target)
	{
		if (string.Equals(target, Models, StringComparison.OrdinalIgnoreCase))
		{
			return Models;
		}

		if (string.Equals(target, CustomNodes, StringComparison.OrdinalIgnoreCase))
		{
			return CustomNodes;
		}

		if (string.Equals(target, Input, StringComparison.OrdinalIgnoreCase))
		{
			return Input;
		}

		if (string.Equals(target, Output, StringComparison.OrdinalIgnoreCase))
		{
			return Output;
		}

		if (string.Equals(target, Workflows, StringComparison.OrdinalIgnoreCase))
		{
			return Workflows;
		}

		return string.Empty;
	}
}
