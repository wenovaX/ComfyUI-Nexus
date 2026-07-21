using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Platform;
using Microsoft.Maui.Controls;

namespace ComfyUI_Nexus.Ui;

/// <summary>
/// A: File Type -> B: Validation -> C: Action Dispatcher
/// </summary>
internal static class AssetActionDispatcher
{
	internal static async Task DispatchAsync(NexusWebViewBridge? bridge, INexusBrowserSurface browserSurface, AssetOpenRequest request)
	{
		if (request == null) return;

		// Nodes and model API entries can be virtual rail items, so they are validated by the bridge flow.
		if (request.Mode is not (AssetInteractionMode.Node or AssetInteractionMode.Model))
		{
			if (string.IsNullOrWhiteSpace(request.FullPath) || !File.Exists(request.FullPath))
			{
				return;
			}
		}

		switch (request.Mode)
		{
			case AssetInteractionMode.Workflow:
				if (request.Action == AssetInteractionAction.Insert)
				{
					await InsertWorkflowAsync(bridge, request);
					return;
				}

				await browserSurface.SimulateFileDropAsync(request.FullPath);
				return;

			case AssetInteractionMode.Model:
			case AssetInteractionMode.Node:
				await NotifyBridgeAssetOpenAsync(bridge, request);
				return;

			case AssetInteractionMode.Image:
				await OpenInOsAsync(request.FullPath);
				return;

			case AssetInteractionMode.Video:
				await OpenInOsAsync(request.FullPath);
				return;

			case AssetInteractionMode.Folder:
				await OpenInOsAsync(request.FullPath);
				return;

			case AssetInteractionMode.File:
			default:
				if (IsLocalWorkflowFile(request.FullPath))
				{
					await browserSurface.SimulateFileDropAsync(request.FullPath);
					return;
				}

				await OpenInOsAsync(request.FullPath);
				return;
		}
	}

	private static Task NotifyBridgeAssetOpenAsync(NexusWebViewBridge? bridge, AssetOpenRequest request)
		=> bridge?.NotifyAssetOpenAsync(request) ?? Task.CompletedTask;

	private static async Task InsertWorkflowAsync(NexusWebViewBridge? bridge, AssetOpenRequest request)
	{
		if (bridge == null)
		{
			return;
		}

		string? userDataPath = string.Equals(request.SourceRoot, "workflows", StringComparison.OrdinalIgnoreCase)
			? request.DisplayName
			: null;
		var decision = AssetInsertPolicy.Evaluate(request.FullPath, userDataPath);

		if (decision.Route == AssetInsertRoute.WorkflowFromUserData)
		{
			await bridge.InsertWorkflowFromUserDataAsync(decision.UserDataPath!);
			return;
		}

		if (decision.Route != AssetInsertRoute.WorkflowFromLocalFile)
		{
			return;
		}

		try
		{
			await using var stream = new FileStream(request.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
			using var document = await JsonDocument.ParseAsync(stream);
			if (!AssetInsertPolicy.ValidatePayload(decision.Route, document.RootElement))
			{
				return;
			}

			await bridge.InsertWorkflowFromJsonAsync(document.RootElement.GetRawText());
		}
		catch (JsonException ex)
		{
			NexusLog.Warning($"Workflow insert validation failed: {ex.Message}");
		}
		catch (IOException ex)
		{
			NexusLog.Warning($"Workflow insert file access failed: {ex.Message}");
		}
		catch (UnauthorizedAccessException ex)
		{
			NexusLog.Warning($"Workflow insert file access denied: {ex.Message}");
		}
	}

	private static bool IsLocalWorkflowFile(string filePath)
		=> AssetInsertPolicy.Evaluate(filePath).Route == AssetInsertRoute.WorkflowFromLocalFile;

	private static async Task OpenInOsAsync(string path)
	{
		var result = await PlatformManager.Current.Shell.OpenPathAsync(path);
		if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.Message))
		{
			NexusLog.Warning($"Failed to open in OS: {result.Message}");
		}
	}
}
