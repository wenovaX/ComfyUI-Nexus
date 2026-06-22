using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui;

namespace ComfyUI_Nexus.Views.Components;

public class FileSystemItem
{
	public string Name { get; set; } = string.Empty;
	public string FullPath { get; set; } = string.Empty;
	public string IconSource { get; set; } = "status_folder.png";
	public string IconColor { get; set; } = "#c78bff";
	public bool IsDrive { get; set; }
}

public partial class FolderBrowserView : ContentView
{
	public ObservableCollection<FileSystemItem> Items { get; } = new();
	private string _currentPath = string.Empty;

	public event EventHandler<string>? PathConfirmed;

	public FolderBrowserView()
	{
		InitializeComponent();
		FolderList.ItemsSource = Items;

		// Remove default Windows Entry border/underline
#if WINDOWS
		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoBorder", (handler, view) =>
		{
			if (view is Entry && handler.PlatformView is Microsoft.UI.Xaml.Controls.TextBox textBox)
			{
				textBox.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
				textBox.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
				textBox.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
				textBox.SelectionHighlightColor = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

				// Final attempt to break inheritance
				textBox.Style = null;
			}
		});
#endif

		// Start at drives by default or C:\
		LoadDrives();
	}

	public void InitializePath(string path)
	{
		if (Directory.Exists(path))
		{
			LoadDirectory(path);
		}
		else
		{
			LoadDrives();
		}
	}

	private void LoadDrives()
	{
		Items.Clear();
		_currentPath = "";
		PathEntry.Text = "This PC";

		foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
		{
			Items.Add(new FileSystemItem
			{
				Name = $"{drive.Name} ({drive.VolumeLabel})",
				FullPath = drive.Name,
				IconSource = "status_drive.png",
				IconColor = "#31d8ff",
				IsDrive = true
			});
		}
	}

	private void LoadDirectory(string path)
	{
		try
		{
			var dirInfo = new DirectoryInfo(path);
			var dirs = dirInfo.GetDirectories();

			Items.Clear();
			_currentPath = path;
			PathEntry.Text = path;

			foreach (var d in dirs)
			{
				// Skip hidden/system folders to keep it clean
				if ((d.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden) continue;

				Items.Add(new FileSystemItem
				{
					Name = d.Name,
					FullPath = d.FullName,
					IconSource = "status_folder.png",
					IconColor = "#c78bff"
				});
			}
		}
		catch (UnauthorizedAccessException) { }
		catch (Exception) { }
	}

	private void OnPathEntryCompleted(object? sender, EventArgs e)
	{
		var path = PathEntry.Text?.Trim();
		if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
		{
			LoadDirectory(path);
			PathConfirmed?.Invoke(this, path);
		}
	}

	private async void OnNativeBrowseClicked(object? sender, TappedEventArgs e)
	{
#if WINDOWS
		var folderPicker = new Windows.Storage.Pickers.FolderPicker();
		folderPicker.FileTypeFilter.Add("*");
		var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler.PlatformView as MauiWinUIWindow;
		if (window != null)
		{
			var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
			WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
			var folder = await folderPicker.PickSingleFolderAsync();
			if (folder != null)
			{
				LoadDirectory(folder.Path);
				PathConfirmed?.Invoke(this, folder.Path);
			}
		}
#endif
	}

	private void OnUpClicked(object? sender, TappedEventArgs e)
	{
		if (string.IsNullOrEmpty(_currentPath)) return;

		var parent = Directory.GetParent(_currentPath);
		if (parent != null)
		{
			LoadDirectory(parent.FullName);
			PathConfirmed?.Invoke(this, parent.FullName);
		}
		else
		{
			LoadDrives();
		}
	}

	private void OnDrivesClicked(object? sender, TappedEventArgs e)
	{
		LoadDrives();
	}

	private void OnFolderSelected(object? sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection.FirstOrDefault() is FileSystemItem item)
		{
			FolderList.SelectedItem = null; // Reset selection

			if (item.IsDrive)
			{
				LoadDirectory(item.FullPath);
			}
			else
			{
				// Navigate into the folder
				LoadDirectory(item.FullPath);

				// Automatically trigger confirmation for the new path
				PathConfirmed?.Invoke(this, item.FullPath);
			}
		}
	}

	private void OnUpHovered(object? sender, PointerEventArgs e) => VisualStateManager.GoToState(UpButtonBorder, "UpPointerOver");
	private void OnUpUnhovered(object? sender, PointerEventArgs e) => VisualStateManager.GoToState(UpButtonBorder, "UpNormal");
	private void OnDrivesHovered(object? sender, PointerEventArgs e) => VisualStateManager.GoToState(DrivesButtonBorder, "DrivesPointerOver");
	private void OnDrivesUnhovered(object? sender, PointerEventArgs e) => VisualStateManager.GoToState(DrivesButtonBorder, "DrivesNormal");
	private void OnNativeBrowseHovered(object? sender, PointerEventArgs e) => VisualStateManager.GoToState(NativeBrowseButtonBorder, "NativeBrowsePointerOver");
	private void OnNativeBrowseUnhovered(object? sender, PointerEventArgs e) => VisualStateManager.GoToState(NativeBrowseButtonBorder, "NativeBrowseNormal");
}
