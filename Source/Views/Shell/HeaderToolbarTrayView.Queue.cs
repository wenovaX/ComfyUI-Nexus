using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Ui;
using Microsoft.Maui.Controls;

namespace ComfyUI_Nexus.Views;

public partial class HeaderToolbarTrayView
{
	private void OnQueueCountPanUpdated(object? sender, PanUpdatedEventArgs e)
	{
		if (_isUnloaded) return;

		switch (e.StatusType)
		{
			case GestureStatus.Started:
				_panStartValue = GetQueueCount();
				_panAccumulator = 0;
				break;
			case GestureStatus.Running:
				_panAccumulator = e.TotalX;
				int delta = (int)(_panAccumulator / QueuePanStepPixels);
				SetQueueCount(Math.Clamp((int)_panStartValue + delta, QueueCountMin, QueueCountMax));
				break;
			case GestureStatus.Completed:
			case GestureStatus.Canceled:
				ResetQueueCountDragState(sender as VisualElement);
				break;
		}
	}

	private void OnQueueAdjustPointerPressed(object? sender, PointerEventArgs e)
	{
		if (_isUnloaded) return;

		int delta = sender == QueueIncrementButton ? 1 : -1;
		StopQueueAdjustHold();
		_queueAdjustHoldDelta = delta;
		_queueAdjustRepeatCount = 0;
		ApplyQueueStep(delta);

		_queueAdjustHoldTimer = Dispatcher.CreateTimer();
		_queueAdjustHoldTimer.Interval = TimeSpan.FromMilliseconds(QueueHoldInitialDelayMs);
		_queueAdjustHoldTimer.Tick += OnQueueAdjustHoldTimerTick;
		_queueAdjustHoldTimer.Start();
	}

	private void OnQueueAdjustPointerReleased(object? sender, PointerEventArgs e)
	{
		StopQueueAdjustHold();
	}

	private void OnQueueAdjustHoldTimerTick(object? sender, EventArgs e)
	{
		if (_isUnloaded || _queueAdjustHoldTimer is null)
		{
			StopQueueAdjustHold();
			return;
		}

		ApplyQueueStep(_queueAdjustHoldDelta);
		int intervalMs = Math.Max(
			QueueHoldMinIntervalMs,
			QueueHoldStartIntervalMs - (_queueAdjustRepeatCount * QueueHoldAccelerationStepMs));
		_queueAdjustRepeatCount++;
		_queueAdjustHoldTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
	}

	private void StopQueueAdjustHold()
	{
		if (_queueAdjustHoldTimer is null)
		{
			return;
		}

		_queueAdjustHoldTimer.Stop();
		_queueAdjustHoldTimer.Tick -= OnQueueAdjustHoldTimerTick;
		_queueAdjustHoldTimer = null;
	}

	private void ApplyQueueStep(int delta)
	{
		int currentCount = GetQueueCount();
		int nextCount = Math.Clamp(currentCount + delta, QueueCountMin, QueueCountMax);
		if (nextCount == currentCount)
		{
			return;
		}

		SetQueueCount(nextCount);
	}

	private void OnQueueCountTextChanged(object? sender, TextChangedEventArgs e)
	{
		if (int.TryParse(e.NewTextValue, out int count) && count is >= QueueCountMin and <= QueueCountMax)
		{
			QueueCountLabel.Text = count.ToString();
		}
	}

	private void OnQueueCountPointerPressed(object? sender, PointerEventArgs e)
	{
		if (_isUnloaded) return;

		_isDraggingQueueCount = true;
		PlatformManager.Current.Cursor.SetCursor(sender as VisualElement, NexusCursorShape.SizeWestEast);
	}

	private void OnQueueCountPointerReleased(object? sender, PointerEventArgs e)
	{
		if (_isUnloaded) return;

		ResetQueueCountDragState(sender as VisualElement, hideGlow: false);
	}

	private void OnQueueCountPointerEntered(object? sender, PointerEventArgs e)
	{
		if (_isUnloaded) return;

		_ = SafeAnimation.FadeToAsync(QueueCountHoverGlow, 1, QueueHoverLength, Easing.CubicOut, "HeaderToolbar.QueueCount");
		PlatformManager.Current.Cursor.SetCursor(sender as VisualElement, NexusCursorShape.SizeWestEast);
	}

	private void OnQueueCountPointerExited(object? sender, PointerEventArgs e)
	{
		if (_isUnloaded) return;

		if (_isDraggingQueueCount) return;

		_ = SafeAnimation.FadeToAsync(QueueCountHoverGlow, 0, ViewQueueHoverOutLength, Easing.CubicIn, "HeaderToolbar.QueueCount");
		PlatformManager.Current.Cursor.SetCursor(sender as VisualElement, NexusCursorShape.Arrow);
	}

	private void ResetQueueCountDragState(VisualElement? target, bool hideGlow = true)
	{
		_isDraggingQueueCount = false;
		PlatformManager.Current.Cursor.SetCursor(target, NexusCursorShape.Arrow);

		if (hideGlow)
		{
			_ = SafeAnimation.FadeToAsync(QueueCountHoverGlow, 0, ViewQueueHoverOutLength, Easing.CubicIn, "HeaderToolbar.QueueCount");
		}
	}
}
