#if WINDOWS
using WinRT.Interop;

namespace ComfyUI_Nexus.Platform.Windows;

public sealed class WindowsFilePicker : IPlatformFilePicker
{
	public async Task<PlatformFeatureResult<string>> PickFolderAsync(string? title = null)
	{
		var platformWindow = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
		if (platformWindow is null)
		{
			return PlatformFeatureResult<string>.Failed("A Windows app window is required before folder selection can start.");
		}

		var folderPicker = new global::Windows.Storage.Pickers.FolderPicker
		{
			SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.Desktop,
		};
		folderPicker.FileTypeFilter.Add("*");

		var hwnd = WindowNative.GetWindowHandle(platformWindow);
		InitializeWithWindow.Initialize(folderPicker, hwnd);

		var folder = await folderPicker.PickSingleFolderAsync();
		if (folder is null || string.IsNullOrWhiteSpace(folder.Path))
		{
			return PlatformFeatureResult<string>.Canceled();
		}

		return PlatformFeatureResult<string>.Success(folder.Path);
	}

	public async Task<PlatformFeatureResult<string>> PickFileAsync(string? title = null, IReadOnlyList<string>? fileTypes = null)
	{
		var platformWindow = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
		if (platformWindow is null)
		{
			return PlatformFeatureResult<string>.Failed("A Windows app window is required before file selection can start.");
		}

		var filePicker = new global::Windows.Storage.Pickers.FileOpenPicker
		{
			SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
			ViewMode = global::Windows.Storage.Pickers.PickerViewMode.List
		};

		foreach (string fileType in fileTypes is { Count: > 0 } ? fileTypes : ["*"])
		{
			filePicker.FileTypeFilter.Add(fileType);
		}

		var hwnd = WindowNative.GetWindowHandle(platformWindow);
		InitializeWithWindow.Initialize(filePicker, hwnd);

		var file = await filePicker.PickSingleFileAsync();
		if (file is null || string.IsNullOrWhiteSpace(file.Path))
		{
			return PlatformFeatureResult<string>.Canceled();
		}

		return PlatformFeatureResult<string>.Success(file.Path);
	}
}
#endif
