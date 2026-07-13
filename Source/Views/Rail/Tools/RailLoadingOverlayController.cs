using ComfyUI_Nexus.Diagnostics;

namespace ComfyUI_Nexus.Views.Rail.Tools;

internal sealed class RailLoadingOverlayController
{
	private const int MinimumVisibleMilliseconds = 350;

	private readonly VisualElement _overlay;
	private IDispatcherTimer? _hideTimer;
	private EventHandler? _hideTimerTick;
	private TaskCompletionSource<bool>? _pendingHideCompletion;
	private int _version;
	private DateTime _shownAtUtc = DateTime.MinValue;

	internal RailLoadingOverlayController(VisualElement overlay)
	{
		_overlay = overlay;
	}

	internal async Task ShowAsync()
	{
		Show();

		await Task.Yield();
	}

	internal void Show()
	{
		if (_overlay.IsVisible)
		{
			return;
		}

		StopPendingHide(completed: false);
		_shownAtUtc = DateTime.UtcNow;
		Interlocked.Increment(ref _version);
		_overlay.Opacity = 1;
		_overlay.IsVisible = true;
	}

	internal Task HideAsync()
	{
		int version = _version;
		TimeSpan elapsed = DateTime.UtcNow - _shownAtUtc;
		int remainingMilliseconds = MinimumVisibleMilliseconds - (int)elapsed.TotalMilliseconds;
		if (remainingMilliseconds <= 0 || _overlay.Dispatcher is null)
		{
			HideIfCurrent(version);
			return Task.CompletedTask;
		}

		StopPendingHide(completed: false);
		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pendingHideCompletion = completion;
		_hideTimer = _overlay.Dispatcher.CreateTimer();
		_hideTimer.Interval = TimeSpan.FromMilliseconds(remainingMilliseconds);
		_hideTimerTick = (_, _) =>
		{
			StopPendingHide(completed: true);
			HideIfCurrent(version);
		};
		_hideTimer.Tick += _hideTimerTick;
		_hideTimer.Start();
		return completion.Task;
	}

	private void HideIfCurrent(int version)
	{
		if (version != _version || !_overlay.IsVisible)
		{
			return;
		}

		try
		{
			_overlay.IsVisible = false;
			_overlay.Opacity = 1;
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"Rail loading overlay hide skipped: {ex.Message}");
		}
	}

	private void StopPendingHide(bool completed)
	{
		if (_hideTimer is not null)
		{
			_hideTimer.Stop();
			if (_hideTimerTick is not null)
			{
				_hideTimer.Tick -= _hideTimerTick;
			}
		}

		_hideTimer = null;
		_hideTimerTick = null;
		_pendingHideCompletion?.TrySetResult(completed);
		_pendingHideCompletion = null;
	}
}
