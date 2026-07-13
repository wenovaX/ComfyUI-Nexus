#if WINDOWS
using Windows.Storage;
using WinClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;
using WinDataPackage = Windows.ApplicationModel.DataTransfer.DataPackage;
using WinDataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;

namespace ComfyUI_Nexus.Platform.Windows;

public sealed class WindowsFileClipboardService : IPlatformFileClipboardService
{
	public async Task<PlatformFeatureResult<bool>> SetFilesAsync(IReadOnlyList<string> paths, bool cut)
	{
		if (paths.Count == 0)
		{
			return PlatformFeatureResult<bool>.Failed("No files were selected.");
		}

		try
		{
			var storageItems = new List<IStorageItem>(paths.Count);
			foreach (string path in paths)
			{
				if (File.Exists(path))
				{
					storageItems.Add(await StorageFile.GetFileFromPathAsync(path));
				}
				else if (Directory.Exists(path))
				{
					storageItems.Add(await StorageFolder.GetFolderFromPathAsync(path));
				}
			}

			if (storageItems.Count == 0)
			{
				return PlatformFeatureResult<bool>.Failed("Selected files no longer exist.");
			}

			var package = new WinDataPackage
			{
				RequestedOperation = cut ? WinDataPackageOperation.Move : WinDataPackageOperation.Copy
			};
			package.SetStorageItems(storageItems);
			WinClipboard.SetContent(package);
			WinClipboard.Flush();
			return PlatformFeatureResult<bool>.Success(true);
		}
		catch (Exception ex)
		{
			return PlatformFeatureResult<bool>.Failed(ex.Message);
		}
	}
}
#endif
