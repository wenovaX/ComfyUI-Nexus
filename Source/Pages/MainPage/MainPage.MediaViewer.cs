using System.Text.Json;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Views.Overlays;
using ComfyUI_Nexus.Views.Rail.Tools.MediaAssets;

namespace ComfyUI_Nexus;

public partial class MainPage
{
	private static readonly HashSet<string> AssetViewerImageExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff",
	};

	private static readonly HashSet<string> AssetViewerVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".mp4", ".mkv",
	};

	private sealed record MediaAssetDeleteJobPayload(
		string JobId,
		string Filename,
		string FullPath,
		string Type,
		string Subfolder,
		bool IsBatchInferred);

	private async Task<bool> TryOpenAssetMediaViewerAsync(AssetOpenRequest request)
	{
		if (request.Mode is not (AssetInteractionMode.Image or AssetInteractionMode.Video) || !File.Exists(request.FullPath))
		{
			return false;
		}

		if (RailControl.TryCreateMediaAssetViewerRequest(request.FullPath, out var mediaAssetRequest))
		{
			try
			{
				UpdateMediaViewerOverlayLayout();
				CaptureAppKeyboardFocus();
				await MediaViewerOverlayControl.ShowAsync(
					mediaAssetRequest.Items,
					mediaAssetRequest.StartIndex,
					deleteEnabled: true,
					deleteHandler: DeleteMediaViewerItemAsync,
					hideCallback: RestoreWebViewKeyboardFocus);
				CaptureAppKeyboardFocus();
				return true;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				NexusLog.Warning($"Media asset viewer failed to open from asset browser: {ex.GetType().Name} - {ex.Message}");
				return false;
			}
		}

		string? directory = Path.GetDirectoryName(request.FullPath);
		if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
		{
			return false;
		}

		var items = Directory.EnumerateFiles(directory)
			.Where(IsAssetViewerMediaFile)
			.OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
			.Select(path => new MediaViewerItem(
				Path.GetFileName(path),
				path,
				IsImage: AssetViewerImageExtensions.Contains(Path.GetExtension(path)),
				IsVideo: AssetViewerVideoExtensions.Contains(Path.GetExtension(path))))
			.ToList();

		int startIndex = items.FindIndex(item => string.Equals(item.FullPath, request.FullPath, StringComparison.OrdinalIgnoreCase));
		if (items.Count == 0 || startIndex < 0)
		{
			return false;
		}

		try
		{
			UpdateMediaViewerOverlayLayout();
			CaptureAppKeyboardFocus();
			await MediaViewerOverlayControl.ShowAsync(
				items,
				startIndex,
				deleteEnabled: true,
				deleteHandler: DeleteAssetViewerItemAsync,
				hideCallback: RestoreWebViewKeyboardFocus);
			CaptureAppKeyboardFocus();
			return true;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Warning($"Asset image viewer failed to open: {ex.GetType().Name} - {ex.Message}");
			return false;
		}
	}

	private static bool IsAssetViewerMediaFile(string path)
	{
		string extension = Path.GetExtension(path);
		return AssetViewerImageExtensions.Contains(extension) || AssetViewerVideoExtensions.Contains(extension);
	}

	private async Task<bool> DeleteAssetViewerItemAsync(MediaViewerItem item)
	{
		try
		{
			await Task.Run(() => DeleteFileIfPresent(item.FullPath));
			RailControl.RefreshMediaAssets();
			return true;
		}
		catch (FileNotFoundException)
		{
			RailControl.RefreshMediaAssets();
			return true;
		}
		catch (DirectoryNotFoundException)
		{
			RailControl.RefreshMediaAssets();
			return true;
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"Asset viewer delete failed: {ex.GetType().Name} - {ex.Message}");
			return false;
		}
	}

	private async void OnMediaAssetViewerRequested(object? sender, MediaAssetViewerRequest request)
	{
		try
		{
			UpdateMediaViewerOverlayLayout();
			CaptureAppKeyboardFocus();
			await MediaViewerOverlayControl.ShowAsync(
				request.Items,
				request.StartIndex,
				deleteEnabled: true,
				deleteHandler: DeleteMediaViewerItemAsync,
				hideCallback: RestoreWebViewKeyboardFocus);
			CaptureAppKeyboardFocus();
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"Media viewer failed to open: {ex.GetType().Name} - {ex.Message}");
		}
	}

	private async Task<bool> DeleteMediaViewerItemAsync(MediaViewerItem item)
		=> await DeleteMediaAssetItemsAsync([item]);

	private async Task<bool> DeleteMediaAssetItemsAsync(IReadOnlyList<MediaViewerItem> items)
	{
		try
		{
			var filePaths = items
				.Where(item => File.Exists(item.FullPath))
				.Select(item => item.FullPath)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			var jobPayloads = PrepareMediaAssetDeleteJobPayloads(items);
			if (filePaths.Count == 0 && jobPayloads.Count == 0)
			{
				return true;
			}

			try
			{
				await DeleteMediaAssetJobsFromWebAsync(items.Count, jobPayloads);
			}
			catch (Exception ex)
			{
				NexusLog.Warning($"Media asset web history delete failed: {ex.GetType().Name} - {ex.Message}");
			}

			await Task.Run(() =>
			{
				foreach (string path in filePaths)
				{
					DeleteFileIfPresent(path);
				}
			});
			RailControl.RefreshMediaAssets();
			return true;
		}
		catch (FileNotFoundException)
		{
			RailControl.RefreshMediaAssets();
			return true;
		}
		catch (DirectoryNotFoundException)
		{
			RailControl.RefreshMediaAssets();
			return true;
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"Media viewer delete failed: {ex.GetType().Name} - {ex.Message}");
			return false;
		}
	}

	private static void DeleteFileIfPresent(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (FileNotFoundException)
		{
		}
		catch (DirectoryNotFoundException)
		{
		}
	}

	private static List<MediaAssetDeleteJobPayload> PrepareMediaAssetDeleteJobPayloads(IReadOnlyList<MediaViewerItem> items)
		=> items
			.Where(item => !string.IsNullOrWhiteSpace(item.JobId))
			.GroupBy(item => item.JobId!, StringComparer.Ordinal)
			.Select(group => group.First())
			.Select(item => new MediaAssetDeleteJobPayload(
				item.JobId!,
				item.Name,
				item.FullPath,
				item.Type,
				item.Subfolder,
				item.IsBatchInferred))
			.ToList();

	private async Task DeleteMediaAssetJobsFromWebAsync(int selectedCount, IReadOnlyList<MediaAssetDeleteJobPayload> jobPayloads)
	{
		if (jobPayloads.Count == 0)
		{
			NexusLog.Trace($"[MEDIA_ASSETS] Delete web history skipped. selected={selectedCount}, jobIds=0");
			return;
		}

		NexusLog.Trace($"[MEDIA_ASSETS] Delete web history requested. selected={selectedCount}, jobIds={jobPayloads.Count}");
		string payloadJson = JsonSerializer.Serialize(new
		{
			items = jobPayloads.Select(item => new
			{
				jobId = item.JobId,
				filename = item.Filename,
				fullPath = item.FullPath,
				type = item.Type,
				subfolder = item.Subfolder,
				isBatchInferred = item.IsBatchInferred,
				exists = File.Exists(item.FullPath),
			}),
		});
		await _webViewBridge.DeleteMediaAssetJobsAsync(payloadJson);
	}

	private async Task<bool> TryHandleMediaViewerShortcutAsync(NexusKey key, bool ctrl, bool shift, bool alt)
	{
		if (MediaViewerOverlayControl == null)
		{
			return false;
		}

		try
		{
			return await MediaViewerOverlayControl.TryHandleShortcutAsync(key, ctrl, shift, alt);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Exception(ex, "[MEDIA_VIEWER] MainPage shortcut bridge failed");
			return true;
		}
	}

	private void UpdateMediaViewerOverlayLayout()
	{
		if (MediaViewerOverlayControl is not { IsVisible: true })
		{
			return;
		}

		MediaViewerOverlayControl.Margin = Thickness.Zero;
	}
}
