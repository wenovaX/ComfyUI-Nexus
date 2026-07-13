using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Ui;

/// <summary>
/// Centralized service for managing ComfyUI workflow bookmarks (.index.json)
/// </summary>
internal static class WorkflowBookmarkService
{
	private const string IndexFileName = ".index.json";

	internal static string GetIndexPath(string workflowsPath)
	{
		if (string.IsNullOrWhiteSpace(workflowsPath)) return string.Empty;
		return Path.Combine(workflowsPath, IndexFileName);
	}

	internal static async Task<HashSet<string>> SyncAndLoadAsync(string workflowsPath)
	{
		var bookmarks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string indexPath = GetIndexPath(workflowsPath);

		if (string.IsNullOrEmpty(indexPath) || !File.Exists(indexPath))
		{
			return bookmarks;
		}

		try
		{
			string json = await File.ReadAllTextAsync(indexPath);
			using var doc = JsonDocument.Parse(json);

			if (doc.RootElement.TryGetProperty("favorites", out var favArray))
			{
				foreach (var item in favArray.EnumerateArray())
				{
					string favPath = item.GetString() ?? string.Empty;
					if (string.IsNullOrEmpty(favPath)) continue;

					string relativePath = WorkflowTabController.NormalizeWorkflowRelativePath(favPath);
					if (string.IsNullOrEmpty(relativePath)) continue;

					bookmarks.Add(relativePath);
				}
			}

			// Cleanup: Verify if each bookmarked file actually exists
			bool dirty = false;
			var currentList = bookmarks.ToList();
			foreach (var relativePath in currentList)
			{
				string fullFilePath = ResolveFullPath(workflowsPath, relativePath);
				if (!File.Exists(fullFilePath))
				{
					bookmarks.Remove(relativePath);
					dirty = true;
				}
			}

			// If we removed non-existent files, sync back to disk
			if (dirty)
			{
				await SaveAsync(workflowsPath, bookmarks);
			}
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[BOOKMARK SERVICE] Load Error");
		}

		return bookmarks;
	}

	internal static async Task SaveAsync(string workflowsPath, IEnumerable<string> bookmarks)
	{
		string indexPath = GetIndexPath(workflowsPath);
		if (string.IsNullOrEmpty(indexPath)) return;

		try
		{
			// ComfyUI expects paths under the "workflows/" userdata root.
			var denormalized = bookmarks
				.Select(WorkflowTabController.NormalizeWorkflowRelativePath)
				.Where(path => !string.IsNullOrWhiteSpace(path))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();

			var payload = new { favorites = denormalized };
			string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

			await File.WriteAllTextAsync(indexPath, json);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[BOOKMARK SERVICE] Save Error");
		}
	}

	private static string ResolveFullPath(string workflowsPath, string relativePath)
	{
		string stripped = WorkflowTabController.StripWorkflowPrefix(relativePath);
		string[] segments = stripped.Split('/', StringSplitOptions.RemoveEmptyEntries);
		return Path.Combine([workflowsPath, .. segments]);
	}
}
