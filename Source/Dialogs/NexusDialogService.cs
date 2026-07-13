using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Views.Overlays;

namespace ComfyUI_Nexus.Dialogs;

internal static class NexusDialogService
{
	private static NexusDialogOverlayView? _host;

	internal static bool IsOpen => _host?.IsDialogOpen == true;

	internal static event Action<NexusDialogReturnFocusTarget>? Closed;

	internal static void Register(NexusDialogOverlayView host)
	{
		_host = host;
		_host.Closed += OnHostClosed;
	}

	internal static void Unregister(NexusDialogOverlayView host)
	{
		if (ReferenceEquals(_host, host))
		{
			_host.Closed -= OnHostClosed;
			_host = null;
		}
	}

	internal static Task<bool> AlertAsync(
		string title,
		string message,
		string okText = "OK",
		Func<Task<NexusDialogActionResult>>? onOk = null)
		=> ShowAsync(new NexusDialogRequest
		{
			Kind = NexusDialogKind.Alert,
			Title = title,
			Message = message,
			OkText = okText,
			OkCallback = onOk
		}).ContinueWith(task => task.Result.Accepted);

	internal static async Task<bool> ConfirmAsync(
		string title,
		string message,
		string okText = "OK",
		string cancelText = "Cancel",
		bool okIsDanger = false,
		Func<Task<NexusDialogActionResult>>? onOk = null,
		Func<Task>? onCancel = null,
		NexusDialogReturnFocusTarget returnFocusTarget = NexusDialogReturnFocusTarget.None)
	{
		var result = await ShowAsync(new NexusDialogRequest
		{
			Kind = NexusDialogKind.Confirm,
			Title = title,
			Message = message,
			OkText = okText,
			CancelText = cancelText,
			OkIsDanger = okIsDanger,
			ReturnFocusTarget = returnFocusTarget,
			OkCallback = onOk,
			CancelCallback = onCancel
		});
		return result.Accepted;
	}

	internal static async Task<string?> PromptAsync(
		string title,
		string message = "",
		string okText = "OK",
		string cancelText = "Cancel",
		string placeholder = "",
		int maxLength = -1,
		Keyboard? keyboard = null,
		string initialValue = "",
		Func<string, Task<NexusDialogActionResult>>? onOk = null,
		Func<Task>? onCancel = null)
	{
		var result = await ShowAsync(new NexusDialogRequest
		{
			Kind = NexusDialogKind.Prompt,
			Title = title,
			Message = message,
			OkText = okText,
			CancelText = cancelText,
			Placeholder = placeholder,
			MaxLength = maxLength,
			Keyboard = keyboard ?? Keyboard.Text,
			InitialValue = initialValue,
			PromptOkCallback = onOk,
			CancelCallback = onCancel
		});
		return result.Accepted ? result.Value : null;
	}

	internal static Task<NexusDialogResult> ChoiceAsync(
		string title,
		string message,
		IReadOnlyList<NexusDialogChoice> choices,
		string cancelText = "Cancel",
		Func<Task>? onCancel = null)
		=> ShowAsync(new NexusDialogRequest
		{
			Kind = NexusDialogKind.Choice,
			Title = title,
			Message = message,
			CancelText = cancelText,
			Choices = choices,
			CancelCallback = onCancel
		});

	internal static Task<NexusDialogResult> ThumbnailChoiceAsync(
		string title,
		string message,
		IReadOnlyList<NexusDialogThumbnailChoice> choices,
		string okText = "OK",
		string cancelText = "Cancel",
		Func<Task>? onCancel = null)
		=> ShowAsync(new NexusDialogRequest
		{
			Kind = NexusDialogKind.ThumbnailChoice,
			Title = title,
			Message = message,
			OkText = okText,
			CancelText = cancelText,
			ThumbnailChoices = choices,
			CancelCallback = onCancel
		});

	internal static Task<bool> TryHandleShortcutAsync(NexusKey key)
		=> _host?.TryHandleShortcutAsync(key) ?? Task.FromResult(false);

	private static Task<NexusDialogResult> ShowAsync(NexusDialogRequest request)
	{
		if (_host == null)
		{
			return Task.FromResult(NexusDialogResult.Cancelled);
		}

		return MainThread.InvokeOnMainThreadAsync(() => _host.ShowAsync(request));
	}

	private static void OnHostClosed(object? sender, NexusDialogReturnFocusTarget target)
		=> Closed?.Invoke(target);
}
