#if WINDOWS
namespace ComfyUI_Nexus.Platform.Windows;

public sealed class WindowsDragDropService : UnsupportedPlatformDragDropService
{
	public override async Task<bool> ContainsFolderAsync(DragEventArgs e)
	{
		if (e.PlatformArgs?.DragEventArgs?.DataView is not { } dataView)
		{
			return false;
		}

		try
		{
			var items = await dataView.GetStorageItemsAsync();
			return items != null && items.Any(item => item is global::Windows.Storage.IStorageFolder);
		}
		catch
		{
			return false;
		}
	}

	public override async Task<IReadOnlyList<string>> GetDroppedPathsAsync(DragEventArgs e)
	{
		var propertyPaths = ReadDataProperties(e.Data);
		if (propertyPaths.Count > 0)
		{
			return propertyPaths;
		}

		if (e.PlatformArgs?.DragEventArgs?.DataView is global::Windows.ApplicationModel.DataTransfer.DataPackageView dataView)
		{
			return await ReadStorageItemPathsAsync(dataView);
		}

		return propertyPaths;
	}

	public override async Task<IReadOnlyList<string>> GetDroppedPathsAsync(DropEventArgs e)
	{
		var propertyPaths = ReadDataProperties(e.Data);
		if (propertyPaths.Count > 0)
		{
			return propertyPaths;
		}

		if (e.PlatformArgs?.DragEventArgs?.DataView is global::Windows.ApplicationModel.DataTransfer.DataPackageView dataView)
		{
			return await ReadStorageItemPathsAsync(dataView);
		}

		return propertyPaths;
	}

	public override async Task SetDragStartingPathsAsync(DragStartingEventArgs e, IReadOnlyList<string> paths)
	{
		if (e.PlatformArgs is null)
		{
			return;
		}

		try
		{
			var items = new List<global::Windows.Storage.IStorageItem>();
			foreach (string path in paths)
			{
				if (Directory.Exists(path))
				{
					items.Add(await global::Windows.Storage.StorageFolder.GetFolderFromPathAsync(path));
				}
				else if (File.Exists(path))
				{
					items.Add(await global::Windows.Storage.StorageFile.GetFileFromPathAsync(path));
				}
			}

			e.PlatformArgs.DragStartingEventArgs.Data.SetStorageItems(items);
			e.PlatformArgs.DragStartingEventArgs.AllowedOperations =
				global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy |
				global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
			e.PlatformArgs.Handled = true;
		}
		catch
		{
		}
	}

	public override Task SetDragStartingTextAsync(DragStartingEventArgs e, string text)
	{
		if (e.PlatformArgs is null)
		{
			return Task.CompletedTask;
		}

		try
		{
			e.PlatformArgs.DragStartingEventArgs.Data.SetText(text);
			e.PlatformArgs.DragStartingEventArgs.Data.SetData("application/x-nexus-asset-intent", text);
			e.PlatformArgs.DragStartingEventArgs.Data.RequestedOperation =
				global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
			e.PlatformArgs.DragStartingEventArgs.AllowedOperations =
				global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
			e.PlatformArgs.Handled = true;
		}
		catch
		{
		}

		return Task.CompletedTask;
	}

	private static async Task<IReadOnlyList<string>> ReadStorageItemPathsAsync(global::Windows.ApplicationModel.DataTransfer.DataPackageView dataView)
	{
		var results = new List<string>();
		try
		{
			if (dataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
			{
				var items = await dataView.GetStorageItemsAsync();
				foreach (var item in items)
				{
					if (!string.IsNullOrWhiteSpace(item.Path))
					{
						results.Add(item.Path);
					}
				}
			}
		}
		catch
		{
		}

		return results;
	}
}
#endif
