using ComfyUI_Nexus.Localization;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace ComfyUI_Nexus.Ui;

internal sealed class NexusEntryTextController
{
	private readonly Entry _entry;
	private readonly View _flyoutOwner;
	private readonly TapGestureRecognizer _secondaryTap;

	internal NexusEntryTextController(Entry entry, View? flyoutOwner = null)
	{
		_entry = entry;
		_flyoutOwner = flyoutOwner ?? entry;
		_secondaryTap = new TapGestureRecognizer { Buttons = ButtonsMask.Secondary };
		_secondaryTap.Tapped += OnSecondaryTapped;
		_entry.HandlerChanged += OnEntryHandlerChanged;
		AttachFlyout();
		ApplyNativeSelectionColors();
	}

	private void AttachFlyout()
	{
		FlyoutBase.SetContextFlyout(_flyoutOwner, CreateFlyout());
		_flyoutOwner.GestureRecognizers.Add(_secondaryTap);
	}

	private MenuFlyout CreateFlyout()
	{
		var flyout = new MenuFlyout();
		flyout.Add(CreateItem(LocalizationManager.Text("common.copy"), (_, _) => CopySelection()));
		flyout.Add(CreateItem(LocalizationManager.Text("common.paste"), async (_, _) => await PasteClipboardAsync()));
		flyout.Add(new MenuFlyoutSeparator());
		flyout.Add(CreateItem(LocalizationManager.Text("common.select_all"), (_, _) => SelectAll()));
		return flyout;
	}

	private static MenuFlyoutItem CreateItem(string text, EventHandler handler)
	{
		var item = new MenuFlyoutItem { Text = text };
		item.Clicked += handler;
		return item;
	}

	private void CopySelection()
	{
		string selectedText = GetSelectedText();
		if (string.IsNullOrEmpty(selectedText))
		{
			return;
		}

		_ = Clipboard.Default.SetTextAsync(selectedText);
	}

	private async Task PasteClipboardAsync()
	{
		string? clipboardText = await Clipboard.Default.GetTextAsync();
		if (string.IsNullOrEmpty(clipboardText))
		{
			return;
		}

		string text = _entry.Text ?? string.Empty;
		int start = Math.Clamp(_entry.CursorPosition, 0, text.Length);
		int length = Math.Clamp(_entry.SelectionLength, 0, text.Length - start);
		_entry.Text = text.Remove(start, length).Insert(start, clipboardText);
		_entry.CursorPosition = start + clipboardText.Length;
		_entry.SelectionLength = 0;
		_entry.Focus();
	}

	private void SelectAll()
	{
		_entry.Focus();
		_entry.CursorPosition = 0;
		_entry.SelectionLength = _entry.Text?.Length ?? 0;
	}

	private string GetSelectedText()
	{
		string text = _entry.Text ?? string.Empty;
		int start = Math.Clamp(_entry.CursorPosition, 0, text.Length);
		int length = Math.Clamp(_entry.SelectionLength, 0, text.Length - start);
		return length <= 0 ? string.Empty : text.Substring(start, length);
	}

	internal void RefreshNativeSelectionColors()
		=> ApplyNativeSelectionColors();

	private void ApplyNativeSelectionColors()
	{
#if WINDOWS
		var platformView = _entry.Handler?.PlatformView;
		var property = platformView?.GetType().GetProperty("SelectionHighlightColor");
		if (property == null)
		{
			return;
		}

		var selectionBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
			Microsoft.UI.ColorHelper.FromArgb(255, 49, 216, 255));
		if (property.PropertyType.IsAssignableFrom(selectionBrush.GetType()))
		{
			property.SetValue(platformView, selectionBrush);
		}
#endif
	}

	private void OnEntryHandlerChanged(object? sender, EventArgs e)
		=> ApplyNativeSelectionColors();

	private void OnSecondaryTapped(object? sender, TappedEventArgs e)
		=> _entry.Focus();
}
