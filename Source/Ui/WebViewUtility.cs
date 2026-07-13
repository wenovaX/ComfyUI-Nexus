using System.IO;
using System.Threading.Tasks;
using ComfyUI_Nexus.Configuration;
using Microsoft.Maui.Controls;

namespace ComfyUI_Nexus.Ui;

internal static class WebViewUtility
{
	internal static async Task SimulateFileDropAsync(WebView webView, string filePath, string? workflowRelativePath = null)
	{
		if (webView == null || string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

		try
		{
			var fileInfo = new FileInfo(filePath);
			byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
			string base64Data = Convert.ToBase64String(fileBytes);
			string? normalizedWorkflowRelativePath = NormalizeWorkflowRelativePath(workflowRelativePath);

			var payload = new
			{
				name = fileInfo.Name,
				size = fileInfo.Length,
				data = base64Data,
				type = GetMimeType(fileInfo.Extension),
				workflowRelativePath = normalizedWorkflowRelativePath
			};

			string json = System.Text.Json.JsonSerializer.Serialize(payload);
			await webView.EvaluateJavaScriptAsync($"window.NexusAction('{BridgeActions.SimulateDrop}', {json})");
		}
		catch (Exception) { /* Logging omitted */ }
	}

	private static string GetMimeType(string ext) => ext.ToLower() switch
	{
		".json" => "application/json",
		".png" => "image/png",
		".jpg" or ".jpeg" => "image/jpeg",
		".webp" => "image/webp",
		_ => "application/octet-stream"
	};

	private static string? NormalizeWorkflowRelativePath(string? relativePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath))
		{
			return null;
		}

		string normalized = relativePath.Replace('\\', '/').Trim().TrimStart('/');
		if (normalized.StartsWith("workflows/", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized["workflows/".Length..];
		}

		return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
	}
}
