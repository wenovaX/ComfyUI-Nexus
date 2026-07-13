using System.Text.Json;

namespace ComfyUI_Nexus.Ui;

internal enum AssetInsertRoute
{
	None,
	WorkflowFromUserData,
	WorkflowFromLocalFile,
}

internal readonly record struct AssetInsertDecision(
	AssetInsertRoute Route,
	string? UserDataPath = null)
{
	internal bool IsAllowed => Route != AssetInsertRoute.None;
}

/// <summary>
/// Selects an insert rule by file extension, validates the candidate, and decides its execution route.
/// </summary>
internal static class AssetInsertPolicy
{
	private static readonly IAssetInsertRule[] Rules =
	[
		new WorkflowJsonInsertRule(),
	];

	internal static AssetInsertDecision Evaluate(string fullPath, string? userDataPath = null)
	{
		if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
		{
			return default;
		}

		string extension = Path.GetExtension(fullPath);
		foreach (var rule in Rules)
		{
			if (!rule.SupportsExtension(extension))
			{
				continue;
			}

			var decision = rule.Evaluate(fullPath, userDataPath);
			if (decision.IsAllowed)
			{
				return decision;
			}
		}

		return default;
	}

	internal static bool ValidatePayload(AssetInsertRoute route, JsonElement root)
		=> route switch
		{
			AssetInsertRoute.WorkflowFromLocalFile => IsWorkflowDocument(root),
			_ => false,
		};

	private static bool IsWorkflowDocument(JsonElement root)
	{
		if (root.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		return root.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array;
	}

	private interface IAssetInsertRule
	{
		bool SupportsExtension(string extension);

		AssetInsertDecision Evaluate(string fullPath, string? userDataPath);
	}

	private sealed class WorkflowJsonInsertRule : IAssetInsertRule
	{
		private const int ProbeBufferSize = 16 * 1024;

		public bool SupportsExtension(string extension)
			=> string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase);

		public AssetInsertDecision Evaluate(string fullPath, string? userDataPath)
		{
			if (!HasWorkflowSignature(fullPath))
			{
				return default;
			}

			return string.IsNullOrWhiteSpace(userDataPath)
				? new AssetInsertDecision(AssetInsertRoute.WorkflowFromLocalFile)
				: new AssetInsertDecision(AssetInsertRoute.WorkflowFromUserData, userDataPath);
		}

		private static bool HasWorkflowSignature(string fullPath)
		{
			try
			{
				using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				using var reader = new StreamReader(stream);
				char[] buffer = new char[ProbeBufferSize];
				int read = reader.ReadBlock(buffer, 0, buffer.Length);
				var content = buffer.AsSpan(0, read);
				return content.Contains("\"nodes\"", StringComparison.Ordinal) &&
					(content.Contains("\"links\"", StringComparison.Ordinal) ||
					 content.Contains("\"last_node_id\"", StringComparison.Ordinal));
			}
			catch
			{
				return false;
			}
		}
	}
}
